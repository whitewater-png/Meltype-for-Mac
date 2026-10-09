// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import Carbon.HIToolbox
import Cocoa
import InputMethodKit

/// 入力欄 (クライアント) ごとの IME。キーを本体 (libMeltypeNative.dylib) に渡し、
/// 返ってきた結果 (確定する文字・変換中の表示・候補) を入力欄に反映する。
/// Info.plist の InputMethodServerControllerClass に書いた名前で、Input Method Kit が作る。
@objc(MeltypeInputController)
final class MeltypeInputController: IMKInputController {
    private var session: UnsafeMutableRawPointer?
    private var candidateList: [String] = []
    /// 入力メニューの項目 (tag = 番号) ごとの引数。menu() を作るたびに作り直す。
    /// IMK はメニュー項目の action に NSMenuItem ではなく infoDictionary を渡すので、representedObject には頼らず、tag で引く。
    private var menuPayloads: [[String]] = []
    private var menuPayloadsByTitle: [String: [String]] = [:]
    /// 同じタイトルの項目が 2 つ以上あるタイトル (タイトルでは引数を決められない)
    private var ambiguousMenuTitles: Set<String> = []
    private var hasMarkedText = false
    /// 直前に本体から受け取った表示。候補ウィンドウのクリックが変換の候補か予測かを見分けるのに使う。
    private var lastView: CompositionView?

    private static let japaneseModeID = InputSourceRegistration.modeID
    private static let romanModeID = InputSourceRegistration.romanModeID

    /// 入力メニューで今選ばれているモードの ID。IMKTextInput に「今のモードを読む」API が無いので、setValue で受け取って覚える。
    private var currentMode: String?

    /// 再変換する選択の長さの上限 (Core の MeltypeSession.MaxReconvertLength と同じ値)。
    private static let maxReconvertLength = 200

    /// ユーザー辞書に登録する読み・語の長さの上限 (Core の UserDictionary.MaxLength と同じ値)。
    private static let maxWordLength = 100

    /// 今のアプリの種類 (activateServer で bundle ID から決める)。候補ウィンドウの注釈に出す。
    private var appKind: AppKind = .general

    override init!(server: IMKServer!, delegate: Any!, client inputClient: Any!) {
        super.init(server: server, delegate: delegate, client: inputClient)
        session = NativeCore.shared.createSession()
    }

    deinit {
        NativeCore.shared.destroySession(session)
    }

    // ---- 原因調査用のトレース ----
    // `defaults write io.github.yksr-melt.inputmethod.Meltype MeltypeTraceIMK -bool true` で ON (起動時に 1 回読む)。出力は Application Support/Meltype/imk-trace.log。OFF のときは何も出さず、
    // 範囲の読み取り (selectedRange / markedRange) も呼ばない (呼び出し自体がアプリの状態を変える疑いがあるため)。

    private static let trace: Bool = {
        let on = UserDefaults.standard.bool(forKey: "MeltypeTraceIMK")
        if on {
            let version = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "?"
            appendTrace("trace start \(version) \(Bundle.main.bundleIdentifier ?? "?")")
        }
        return on
    }()

    private static func trace(_ message: @autoclosure () -> String) {
        guard trace else { return }
        appendTrace(message())
    }

    /// ~/Library/Application Support/Meltype/imk-trace.log に 1 行追記する (毎回開いて閉じる。診断用なので簡単な実装)。
    private static func appendTrace(_ message: String) {
        let formatter = DateFormatter()
        formatter.dateFormat = "HH:mm:ss.SSS"
        let line = "\(formatter.string(from: Date())) \(message)\n"
        guard let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first else { return }
        let directory = base.appendingPathComponent("Meltype")
        let file = directory.appendingPathComponent("imk-trace.log")
        try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
        guard let data = line.data(using: .utf8) else { return }
        // 打った文字が残るので、自分だけが読める権限 (0600) にする (既存のファイルにも付ける)。
        if !FileManager.default.fileExists(atPath: file.path) {
            FileManager.default.createFile(atPath: file.path, contents: nil, attributes: [.posixPermissions: 0o600])
        } else {
            try? FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: file.path)
        }
        if let handle = try? FileHandle(forWritingTo: file) {
            defer { try? handle.close() }
            _ = try? handle.seekToEnd()
            try? handle.write(contentsOf: data)
        } else {
            try? data.write(to: file)
        }
    }

    /// 40 文字で切り、改行は見える形にする。
    private static func cut(_ text: String?) -> String {
        guard let text else { return "nil" }
        let escaped = text.replacingOccurrences(of: "\n", with: "\\n").replacingOccurrences(of: "\u{00A0}", with: "\\u00A0")
        return "\"" + String(escaped.prefix(40)) + (escaped.count > 40 ? "…" : "") + "\""
    }

    /// トレース用: 今の選択範囲と変換中の範囲 (trace が ON のときだけ呼ばれる)。
    private static func ranges(_ client: IMKTextInput) -> String {
        "selected=\(NSStringFromRange(client.selectedRange())) marked=\(NSStringFromRange(client.markedRange()))"
    }

    override func recognizedEvents(_ sender: Any!) -> Int {
        // マウスの押下も受ける: キャレットが動いたことを本体に伝え、直前の語を確定し直さないようにする (下の handle)。
        // IMK が実際にマウスのイベントを渡すかはアプリ次第 (未検証)。
        Int((NSEvent.EventTypeMask.keyDown.union(.leftMouseDown).union(.rightMouseDown).union(.otherMouseDown)).rawValue)
    }

    override func handle(_ event: NSEvent!, client sender: Any!) -> Bool {
        guard let event else { return false }
        // クリックでキャレットが別の場所へ移ったかもしれない。確定し直しの記録を捨てるだけ (変換中の文字の確定・取消はしない)。
        if event.type == .leftMouseDown || event.type == .rightMouseDown || event.type == .otherMouseDown {
            Self.trace("マウス押下: forgetLastCommit を呼ぶ type=\(event.type.rawValue)")
            NativeCore.shared.forgetLastCommit(session)
            return false
        }
        guard event.type == .keyDown, let client = sender as? IMKTextInput else { return false }

        // パスワード欄など、macOS が「秘匿入力」にしているとき (IsSecureEventInputEnabled) は、キーを一切扱わずアプリに素通しする。
        // 本体 (学習・提案・ログ) にも何も渡さず、周りの文字 (surroundingText) も読まない。未確定の文字が残っていれば先に確定する。
        // 注意: この判定はシステム全体の状態なので、ほかのアプリが秘匿入力を付けたままにしていると、そのあいだ Meltype はどこでも素通しになる。
        if IsSecureEventInputEnabled() {
            if hasMarkedText { apply(NativeCore.shared.commit(session), to: client) }
            return false
        }

        // アプリ別設定で OFF・ゲームのアプリ (setApp の戻り値 .disabled) は、英数/かなキーも含めて全てアプリに素通しする。
        if appKind == .disabled { return false }

        // トレースは、秘匿入力・アプリ別 OFF のときは記録しない (ここより後)。
        Self.trace("handle keyCode=\(event.keyCode) flags=\(event.modifierFlags.rawValue) hasMarkedText=\(hasMarkedText) \(Self.ranges(client)) bundle=\(client.bundleIdentifier() ?? "nil")")

        // JIS キーボードの「英数」「かな」キー: 英数 (直接入力) ⇔ 日本語。
        switch Int(event.keyCode) {
        case kVK_JIS_Eisu:
            apply(NativeCore.shared.commit(session), to: client)
            NativeCore.shared.setDirect(session, true)
            // 入力メニューの表示を追従させる。selectInputMode のあとに setValue が呼ばれるかは未確認なので、setDirect も呼んだままにする
            client.selectMode(Self.romanModeID)
            currentMode = Self.romanModeID
            return true
        case kVK_JIS_Kana:
            NativeCore.shared.setDirect(session, false)
            client.selectMode(Self.japaneseModeID)
            currentMode = Self.japaneseModeID
            return true
        default:
            break
        }

        guard let vk = KeyMapping.virtualKey(for: event) else { return false }
        let utf16 = Array((event.characters ?? "").utf16)
        // 矢印・Home などの機能キーの characters は Unicode 私用領域の文字 (U+F700〜U+F8FF。右矢印は U+F703) なので、文字としては渡さない。
        // 渡すと英数字だけの変換ボックス (「2」など) で矢印がその文字を変換ボックスに足し、Google ドキュメントが変換中の範囲を広げて後ろの文字を消す。
        let character: Int32 = utf16.count == 1 && !(0xF700...0xF8FF).contains(utf16[0]) ? Int32(utf16[0]) : 0
        let flags = event.modifierFlags
        var modifiers: Int32 = 0
        if flags.contains(.shift) { modifiers |= 1 }
        if flags.contains(.control) { modifiers |= 2 }
        if flags.contains(.option) { modifiers |= 4 }
        if flags.contains(.command) { modifiers |= 8 }

        // 確定済みの文字列を選択して Shift + Space: 読みに戻して再変換する (選択範囲を変換中の文字に置き換える)。読みに戻せなければ通常の処理へ。
        // 選択が長いとき (段落など) は読みに戻せないので、中身を読まずに通常の処理へ (本体の MaxReconvertLength と同じ 200 文字)。
        if Int(event.keyCode) == kVK_Space, modifiers == 1, !hasMarkedText {
            let selection = client.selectedRange()
            if selection.location != NSNotFound, selection.length > 0, selection.length <= Self.maxReconvertLength,
               let selected = client.attributedSubstring(from: selection)?.string,
               let result = NativeCore.shared.reconvert(session, text: selected), result.consumed {
                apply(result, to: client, replacing: selection)
                return true
            }
        }

        let (before, after) = hasMarkedText ? (nil, nil) : surroundingText(of: client)
        Self.trace("surroundingText before=\(Self.cut(before)) after=\(Self.cut(after))")
        guard let result = NativeCore.shared.handleKey(session, vk: vk, character: character, modifiers: modifiers, before: before, after: after) else {
            Self.trace("handleKey の結果なし")
            return false
        }
        Self.trace("handleKey consumed=\(result.consumed) commits=\(result.commits.map { "(del=\($0.deleteBefore) orig=\(Self.cut($0.original)) text=\(Self.cut($0.text)))" }.joined()) view=\(result.view.map { "text=\(Self.cut($0.text)) converting=\($0.converting) clauses=\($0.clauses.map { Self.cut($0) }) selectedClause=\($0.selectedClause)" } ?? "nil")")
        apply(result, to: client)
        return result.consumed
    }

    /// フォーカスが外れた・クリックで別の場所に移ったときなど。未確定の内容をそのまま確定する。
    override func commitComposition(_ sender: Any!) {
        guard let client = (sender as? IMKTextInput) ?? (self.client() as? IMKTextInput) else { return }
        Self.trace("commitComposition が呼ばれた hasMarkedText=\(hasMarkedText) \(Self.ranges(client))")
        apply(NativeCore.shared.commit(session), to: client)
        // 確定する内容が無くても (変換ボックスが無くても) 組版の状態は終えておく。冪等。
        MeltypeConverter.shared.endComposition()
    }

    /// 入力メニューや Caps Lock でモードが切り替わったとき。英数なら直接入力、Meltype なら自動判定に合わせる。
    override func setValue(_ value: Any!, forTag tag: Int, client sender: Any!) {
        if tag == kTextServiceInputModePropertyTag, let mode = value as? String {
            applyMode(mode, client: (sender as? IMKTextInput) ?? (self.client() as? IMKTextInput))
        }
        super.setValue(value, forTag: tag, client: sender)
    }

    /// モード ID に合わせて直接入力かどうかを切り替える。英数へ切り替えるときは、未確定の文字を先に確定する。
    private func applyMode(_ mode: String, client: IMKTextInput?) {
        let direct = mode == Self.romanModeID
        guard direct || mode == Self.japaneseModeID else { return }
        currentMode = mode
        if direct, let client {
            apply(NativeCore.shared.commit(session), to: client)
        }
        NativeCore.shared.setDirect(session, direct)
    }

    /// 入力欄にフォーカスが来たとき。入力メニューで選ばれているモードに本体の状態を合わせる (別の入力欄で切り替えたあとなど)。
    override func activateServer(_ sender: Any!) {
        let mode = currentMode ?? currentInputSourceID()
        if let mode { applyMode(mode, client: nil) }
        // アプリ別設定を当てる。「コード」のアプリは英数から始まるので、入力メニューのモードに合わせたあとで呼ぶ。
        let bundleID = (sender as? IMKTextInput)?.bundleIdentifier()
        appKind = NativeCore.shared.setApp(session, bundleIdentifier: bundleID)
        super.activateServer(sender)
    }

    /// 今選ばれている入力ソースの ID。取れなければ nil。
    private func currentInputSourceID() -> String? {
        guard let source = TISCopyCurrentKeyboardInputSource()?.takeRetainedValue(),
              let pointer = TISGetInputSourceProperty(source, kTISPropertyInputSourceID) else { return nil }
        return Unmanaged<CFString>.fromOpaque(pointer).takeUnretainedValue() as String
    }

    override func deactivateServer(_ sender: Any!) {
        commitComposition(sender)
        candidatesWindow?.hide()
        super.deactivateServer(sender)
    }

    // ---- 変換の候補の一覧 ----

    override func candidates(_ sender: Any!) -> [Any]! {
        candidateList
    }

    /// updateCandidates で候補ウィンドウの選択を合わせている間か (そのときの候補ウィンドウからの通知は無視する)。
    private var isSelectingProgrammatically = false

    /// 候補ウィンドウで今選ばれている (青いバーの) 候補の位置。
    private var shownIndex = 0

    override func candidateSelected(_ candidateString: NSAttributedString!) {
        guard !isSelectingProgrammatically,
              let client = self.client() as? IMKTextInput,
              let string = candidateString?.string,
              let index = candidateList.firstIndex(of: string) else { return }
        // マウスで選んだときは、青いバーはもうその候補にある
        shownIndex = index
        // 変換中でなければ予測候補のクリック
        if lastView?.converting == false {
            apply(NativeCore.shared.selectPrediction(session, index: index), to: client)
        } else {
            apply(NativeCore.shared.selectCandidate(session, index: index), to: client)
        }
    }

    /// 候補ウィンドウの中で選択が動いた (クリックなど)。青いバーの位置だけを覚えておく。
    override func candidateSelectionChanged(_ candidateString: NSAttributedString!) {
        guard !isSelectingProgrammatically,
              let string = candidateString?.string,
              let index = candidateList.firstIndex(of: string) else { return }
        shownIndex = index
    }

    // ---- メニュー (メニューバーの入力メニュー) ----

    /// メニュー項目に引数を結びつける (tag に番号を入れ、menuPayloads に保存)。
    /// tag は 1 から数える: IMK が項目を複製するとタグが 0 に戻るので、0 は「タグが落ちた」の目印にする。
    private func attach(_ payload: [String], to item: NSMenuItem) {
        menuPayloads.append(payload)
        item.tag = menuPayloads.count
        // タイトルでも引けるようにしておく (タグが落ちたときの備え)。同じタイトルが 2 つ以上あると取り違えるので、その場合はタイトルでは引かない。
        if menuPayloadsByTitle[item.title] != nil || ambiguousMenuTitles.contains(item.title) {
            menuPayloadsByTitle[item.title] = nil
            ambiguousMenuTitles.insert(item.title)
        } else {
            menuPayloadsByTitle[item.title] = payload
        }
    }

    /// action の sender (IMK は [kIMKCommandMenuItemName: NSMenuItem] の辞書を渡す。NSMenuItem が直接来ても読む) から、項目の引数を取り出す。
    private func payload(from sender: Any?) -> [String]? {
        let item: NSMenuItem?
        if let direct = sender as? NSMenuItem {
            item = direct
        } else if let info = sender as? [AnyHashable: Any] {
            item = info[kIMKCommandMenuItemName] as? NSMenuItem
        } else {
            item = nil
        }
        guard let item else {
            NSLog("Meltype: メニュー項目を読めませんでした (sender: %@)", String(describing: sender))
            return nil
        }
        // タグを優先する (タイトルは先頭 20 文字で省略するので、別の語が同じタイトルになりうる)。
        if item.tag >= 1, item.tag <= menuPayloads.count { return menuPayloads[item.tag - 1] }
        // IMK が項目を複製してタグを落とすことがあるので、そのときだけタイトルで引く (同じタイトルが複数ある項目は、取り違えるより何もしない)
        return menuPayloadsByTitle[item.title]
    }

    override func menu() -> NSMenu! {
        let menu = NSMenu()
        menuPayloads = []
        menuPayloadsByTitle = [:]
        ambiguousMenuTitles = []
        // メニューを開いたとき、前の確認から 24 時間たっていれば裏で確認する (OFF のときは何もしない。メニューは待たせない)
        UpdateManager.shared.checkIfDue()
        // 新しい版が見つかっていれば先頭に出す (選ぶと確認のダイアログ。自動では入れない)
        if let offer = UpdateManager.shared.offer {
            menu.addItem(withTitle: "新しい版があります (v\(offer.version))…", action: #selector(startUpdate(_:)), keyEquivalent: "")
            menu.addItem(.separator())
        }
        // 何度も選び直した語の登録提案 (最大 3 件)。選ぶと辞書に登録、「登録しない」で以後提案しない。
        let suggestions = NativeCore.shared.suggestions(session)
        for suggestion in suggestions {
            // 長い語は 20 文字で省略する (メニューが画面からはみ出さないように。登録する語自体は省略しない)
            let shown = suggestion.word.count > 20 ? String(suggestion.word.prefix(20)) + "…" : suggestion.word
            let accept = menu.addItem(withTitle: "『\(shown)』を辞書に登録 (\(suggestion.reading))", action: #selector(acceptSuggestion(_:)), keyEquivalent: "")
            attach([suggestion.reading, suggestion.word], to: accept)
            let reject = menu.addItem(withTitle: "    『\(shown)』は登録しない", action: #selector(rejectSuggestion(_:)), keyEquivalent: "")
            attach([suggestion.reading, suggestion.word], to: reject)
        }
        if !suggestions.isEmpty { menu.addItem(.separator()) }
        // 既定は OFF。ON にすると、Space で変換したあとに文字を打っても確定せず、続けて編集・変換できる (設定は config.json に保存、全入力欄に反映)
        let continueToggle = menu.addItem(withTitle: "変換後も続けて入力できる", action: #selector(toggleContinueAfterConversion(_:)), keyEquivalent: "")
        continueToggle.state = NativeCore.shared.continueAfterConversion ? .on : .off
        // 専門用語集 (分野ごとに ON/OFF。既定はすべて OFF。設定は config.json に保存、全入力欄に反映)
        let domains = NativeCore.shared.termDomains
        if !domains.isEmpty {
            // IMK のメニューではサブメニューの項目が action に届かないことがあるので、メニュー直下に並べる
            for domain in domains {
                let item = menu.addItem(withTitle: "専門用語集(サンプル): \(domain.name) (\(domain.count) 語)", action: #selector(toggleTermDomain(_:)), keyEquivalent: "")
                item.state = domain.enabled ? .on : .off
                attach([domain.id, domain.enabled ? "on" : "off"], to: item)
            }
        }
        menu.addItem(.separator())
        // ユーザー辞書・専門用語集の一覧・登録・編集・削除と、専門用語集の分野の ON/OFF (別のアプリ「Meltype 辞書」で開く)
        menu.addItem(withTitle: "辞書を管理… (ユーザー辞書・専門用語集(サンプル))", action: #selector(openDictionaryManager(_:)), keyEquivalent: "")
        menu.addItem(withTitle: "選択中の文字をユーザー辞書に登録…", action: #selector(registerWord(_:)), keyEquivalent: "")
        menu.addItem(withTitle: "辞書の登録提案の履歴を消去", action: #selector(clearSuggestions(_:)), keyEquivalent: "")
        menu.addItem(withTitle: "学習データをすべて消去…", action: #selector(clearLearningData(_:)), keyEquivalent: "")
        let updateToggle = menu.addItem(withTitle: "更新を確認する", action: #selector(toggleUpdateCheck(_:)), keyEquivalent: "")
        updateToggle.state = UpdateManager.shared.isEnabled ? .on : .off
        menu.addItem(withTitle: "今すぐ更新を確認する", action: #selector(checkUpdateNow(_:)), keyEquivalent: "")
        menu.addItem(withTitle: "Meltype のデータフォルダを開く (設定・ユーザー辞書)", action: #selector(openDataFolder(_:)), keyEquivalent: "")
        menu.addItem(withTitle: "不具合の報告・提案… (Mac 版はプレビュー版です)", action: #selector(openReport(_:)), keyEquivalent: "")
        return menu
    }

    /// 「変換後も続けて入力できる」の ON/OFF。保存できなかったときは、値は変わらないので、そのことを伝える。
    @objc private func toggleContinueAfterConversion(_ sender: Any?) {
        guard !NativeCore.shared.setContinueAfterConversion(!NativeCore.shared.continueAfterConversion) else { return }
        let failure = NSAlert()
        failure.messageText = "設定を保存できませんでした"
        failure.informativeText = "Meltype のデータフォルダーの config.json を確認してください (読めない・書けないときは、設定を変えません)。"
        NSApp.activate(ignoringOtherApps: true)
        failure.runModal()
    }

    /// 専門用語集の分野の ON/OFF。保存できなかったときは、値は変わらないので、そのことを伝える。
    @objc private func toggleTermDomain(_ sender: Any?) {
        NSLog("Meltype: 専門用語集の切り替えを受け取りました (sender: %@)", String(describing: sender))
        guard let pair = payload(from: sender), pair.count == 2 else { return }
        guard !NativeCore.shared.setTermDomain(pair[0], enabled: pair[1] != "on") else { return }
        let failure = NSAlert()
        failure.messageText = "設定を保存できませんでした"
        failure.informativeText = "Meltype のデータフォルダーの config.json を確認してください (読めない・書けないときは、設定を変えません)。"
        NSApp.activate(ignoringOtherApps: true)
        failure.runModal()
    }

    /// 「辞書を管理…」: 辞書の管理画面 (別のふつうのアプリ) を開いて前に出す。変更は画面がすぐ保存し、この IME はファイルの版を見て読み直す。
    @objc private func openDictionaryManager(_ sender: Any?) {
        DictionaryApp.open { error in
            guard let error else { return }
            let failure = NSAlert()
            failure.messageText = "辞書の管理画面を開けませんでした"
            failure.informativeText = error
            NSApp.activate(ignoringOtherApps: true)
            failure.runModal()
        }
    }

    @objc private func startUpdate(_ sender: Any?) {
        UpdateManager.shared.promptUpdate()
    }

    /// 「更新を確認する」の ON/OFF。OFF のあいだは、手動の「今すぐ更新を確認する」以外では一切通信しない。
    @objc private func toggleUpdateCheck(_ sender: Any?) {
        UpdateManager.shared.setEnabled(!UpdateManager.shared.isEnabled)
    }

    @objc private func checkUpdateNow(_ sender: Any?) {
        UpdateManager.shared.checkNow()
    }

    @objc private func acceptSuggestion(_ sender: Any?) {
        guard let pair = payload(from: sender), pair.count == 2 else { return }
        if let error = NativeCore.shared.acceptSuggestion(session, reading: pair[0], word: pair[1]) {
            let failure = NSAlert()
            failure.messageText = "登録できませんでした"
            failure.informativeText = error
            NSApp.activate(ignoringOtherApps: true)
            failure.runModal()
        }
    }

    @objc private func rejectSuggestion(_ sender: Any?) {
        guard let pair = payload(from: sender), pair.count == 2 else { return }
        NativeCore.shared.rejectSuggestion(session, reading: pair[0], word: pair[1])
    }

    @objc private func clearSuggestions(_ sender: Any?) {
        NativeCore.shared.clearSuggestions(session)
    }

    /// 学習データをすべて消す (確認あり)。ユーザー辞書と設定は消さない。
    @objc private func clearLearningData(_ sender: Any?) {
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = "学習データをすべて消去しますか？"
        alert.informativeText = "変換の学習・辞書の登録提案の履歴・英語/日本語の学習・英訳の学習・ユーザーモデル・azooKey の学習を消します。ログ (meltype.log・crash.log) と、読めない・大きすぎるために退避したコピー (.broken・.oversize) も消します。元に戻せません。\n不具合を報告する予定があれば、先にログを保存してください。ユーザー辞書と設定は消えません。"
        // 取り消せない操作なので、Return で押される先頭のボタンはキャンセルにする。
        alert.addButton(withTitle: "キャンセル")
        alert.addButton(withTitle: "消去")
        // IME は背面のアプリなので、ダイアログを前に出す。
        NSApp.activate(ignoringOtherApps: true)
        guard alert.runModal() == .alertSecondButtonReturn else { return }
        let coreCleared = NativeCore.shared.clearLearning()
        MeltypeConverter.shared.resetLearning()
        let done = NSAlert()
        if NativeCore.shared.isCompatible {
            done.messageText = coreCleared ? "学習データを消去しました" : "消去できなかったものがあります"
            done.informativeText = coreCleared ? "ユーザー辞書と設定はそのままです。" : "Meltype のデータフォルダーの meltype.log (設定でログを ON にしたとき) を確認してください。"
        } else {
            // 本体 (libMeltypeNative.dylib) の版が合わず読み込んでいないので、本体側の学習データには触れていない。
            done.messageText = "azooKey の学習だけを消去しました"
            done.informativeText = "Meltype 本体の版が合わないため、変換・提案などの学習データは消せていません。Meltype を入れ直してから、もう一度実行してください。"
        }
        done.runModal()
    }

    /// 選択中の文字を語にして、読みを聞いてユーザー辞書に登録する。選択が無ければ語も空欄で開く。
    @objc private func registerWord(_ sender: Any?) {
        var selected = ""
        if let client = client() {
            let range = client.selectedRange()
            if range.location != NSNotFound, range.length > 0 {
                // 長い選択は中身を読む前に断る (段落全体が語として登録されるのを防ぐ)。
                if range.length > Self.maxWordLength {
                    showSelectionError("選択している文字が長すぎます (\(Self.maxWordLength) 文字まで)。短く選び直してください。")
                    return
                }
                selected = (client.attributedSubstring(from: range)?.string ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
                if selected.contains(where: { $0.isNewline }) {
                    showSelectionError("選択している文字に改行が含まれているので登録できません。1 行で選び直してください。")
                    return
                }
            }
        }
        // IME は背面専用のアプリ (LSBackgroundOnly) なので、自分のウィンドウは入力欄がキーボード入力を受けられない。
        // そのため、入力は別プロセスの osascript のダイアログで受ける。
        // 待っている間も、ダイアログへ打つキーをこの IME が処理する必要があるので、メインスレッドは止めない (非同期で順に聞く)。
        askText(prompt: "登録する単語を入力してください", defaultText: selected) { [weak self] word in
            guard let self, let word, !word.isEmpty else { return }
            self.askText(prompt: "「\(word)」の読み (ひらがな 2 文字以上) を入力してください", defaultText: "") { [weak self] reading in
                guard let self, let reading else { return }
                if let error = NativeCore.shared.addUserWord(self.session, reading: reading, word: word) {
                    self.showSelectionError(error)
                }
            }
        }
    }

    /// osascript のダイアログで 1 行の文字を聞く。キャンセル・失敗のときは nil。メインスレッドを止めずに、メインスレッドで completion を呼ぶ。
    /// 文字は AppleScript のソースに埋め込まず引数 (argv) で渡すので、入力した文字が命令として解釈されることはない。
    private func askText(prompt: String, defaultText: String, completion: @escaping (String?) -> Void) {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/osascript")
        process.arguments = [
            "-e", "on run argv",
            "-e", "set answer to display dialog (item 1 of argv) default answer (item 2 of argv) with title \"Meltype: ユーザー辞書に登録\" buttons {\"キャンセル\", \"登録\"} default button \"登録\" cancel button \"キャンセル\"",
            "-e", "return text returned of answer",
            "-e", "end run",
            "--", prompt, defaultText,
        ]
        let pipe = Pipe()
        process.standardOutput = pipe
        process.standardError = FileHandle.nullDevice
        process.terminationHandler = { finished in
            let data = pipe.fileHandleForReading.readDataToEndOfFile()
            let text = String(data: data, encoding: .utf8)?.trimmingCharacters(in: .whitespacesAndNewlines)
            DispatchQueue.main.async { completion(finished.terminationStatus == 0 ? text : nil) }
        }
        do {
            try process.run()
        } catch {
            NSLog("Meltype: osascript を起動できませんでした")
            completion(nil)
        }
    }

    private func showSelectionError(_ message: String) {
        let failure = NSAlert()
        failure.messageText = "登録できませんでした"
        failure.informativeText = message
        NSApp.activate(ignoringOtherApps: true)
        failure.runModal()
    }

    @objc private func openReport(_ sender: Any?) {
        guard let url = NativeCore.shared.reportUrl else { return }
        NSWorkspace.shared.open(url)
    }

    @objc private func openDataFolder(_ sender: Any?) {
        guard let directory = NativeCore.shared.dataDirectory else { return }
        NSWorkspace.shared.open(URL(fileURLWithPath: directory, isDirectory: true))
    }

    // ---- 結果を入力欄に反映する ----

    /// 入力欄の文字列と記録の比較。Chrome・Safari は行末の空白を U+00A0 で持つことがあるので、半角空白に揃える。読めなければ false。
    private static func sameText(_ actual: String?, _ expected: String) -> Bool {
        guard let actual else { return false }
        return actual.replacingOccurrences(of: "\u{00A0}", with: " ") == expected.replacingOccurrences(of: "\u{00A0}", with: " ")
    }

    /// replacing: 再変換のとき、置き換える選択範囲 (確定する文字があればそこに、無ければ変換中の文字の表示に使う)。
    private func apply(_ result: SessionResult?, to client: IMKTextInput, replacing replacement: NSRange? = nil) {
        guard let result else { return }
        var pendingReplacement = replacement
        for edit in result.commits {
            var range = NSRange(location: NSNotFound, length: NSNotFound)
            if let replacement = pendingReplacement {
                range = replacement
                pendingReplacement = nil
            } else if edit.deleteBefore > 0 {
                // 確定し直し: キャレット (変換中の文字があればその先頭) の前の文字を置き換える。
                let marked = client.markedRange()
                let caret = marked.location != NSNotFound && marked.length > 0 ? marked.location : client.selectedRange().location
                // キャレットが分からないまま入れると、消さずに足すので文字が重複する。この edit 全体を飛ばす。
                if caret == NSNotFound {
                    Self.trace("apply: 確定し直しを飛ばす (キャレット不明)")
                    NSLog("Meltype: 確定し直しを飛ばしました (キャレットが分からない。消す予定 %d 文字)", edit.deleteBefore)
                    continue
                }
                let length = min(edit.deleteBefore, caret)
                range = NSRange(location: caret - length, length: length)
                // 消す前に中身を確かめる。Google ドキュメントなどは selectedRange が不正確で (例: 「v1.1.2】Mel」の
                // 「2】Mel」が変換中の 1 文字に置き換わった)、キャレットが別の場所のまま確定し直すと
                // 確定済みの文字を消してしまう。違っていたら edit 全体を飛ばす (確定済みの文字はそのまま残る)。
                if let original = edit.original, Self.sameText(client.attributedSubstring(from: range)?.string, original) == false {
                    Self.trace("apply: 確定し直しを飛ばす (中身が違う) actual=\(Self.cut(client.attributedSubstring(from: range)?.string)) range=\(NSStringFromRange(range))")
                    NSLog("Meltype: 確定し直しを飛ばしました (入力欄の中身が記録と違う・読めない。消す予定 %d 文字)", original.count)
                    continue
                }
            }
            Self.trace("apply: insertText text=\(Self.cut(edit.text)) replacementRange=\(NSStringFromRange(range)) 直前 \(Self.ranges(client))")
            client.insertText(edit.text, replacementRange: range)
            Self.trace("apply: insertText 直後 \(Self.ranges(client))")
            if hasMarkedText { MeltypeConverter.shared.endComposition() }
            hasMarkedText = false
        }
        // 登録提案ができた直後の確定なら、1 行のヒントを出す (1 日 1 回まで。出すかどうかは本体が決める)。
        if !result.commits.isEmpty, let hint = NativeCore.shared.suggestionHint(session) {
            SuggestionHint.shared.show(hint, near: client)
        }
        if let view = result.view {
            showComposition(view, client: client, replacing: pendingReplacement)
        } else {
            hideComposition(client: client)
        }
    }

    /// 変換中の文字を入力欄に下線付きで出す (変換中は文節ごと、選んでいる文節は太い下線)。
    private func showComposition(_ view: CompositionView, client: IMKTextInput, replacing replacement: NSRange? = nil) {
        let text = NSMutableAttributedString(string: view.text)
        let length = (view.text as NSString).length
        if !view.converting && !view.clauses.isEmpty {
            // 変換後も続けて入力: 選んだ変換結果 (細い下線) + 未変換の文節 (最後の 1 つ。かなの下線)
            var location = 0
            for (index, clause) in view.clauses.enumerated() {
                let clauseLength = (clause as NSString).length
                let style = index == view.clauses.count - 1 ? kTSMHiliteRawText : kTSMHiliteConvertedText
                addMark(style, to: text, range: NSRange(location: location, length: clauseLength))
                location += clauseLength
            }
        } else if view.converting && !view.clauses.isEmpty {
            var location = 0
            for (index, clause) in view.clauses.enumerated() {
                let clauseLength = (clause as NSString).length
                let style = index == view.selectedClause ? kTSMHiliteSelectedConvertedText : kTSMHiliteConvertedText
                addMark(style, to: text, range: NSRange(location: location, length: clauseLength))
                location += clauseLength
            }
        } else {
            addMark(kTSMHiliteRawText, to: text, range: NSRange(location: 0, length: length))
        }
        Self.trace("showComposition: setMarkedText text=\(Self.cut(view.text)) selectionRange=\(NSStringFromRange(NSRange(location: length, length: 0))) replacementRange=\(NSStringFromRange(replacement ?? NSRange(location: NSNotFound, length: NSNotFound))) 直前 \(Self.ranges(client))")
        client.setMarkedText(text, selectionRange: NSRange(location: length, length: 0), replacementRange: replacement ?? NSRange(location: NSNotFound, length: NSNotFound))
        Self.trace("showComposition: setMarkedText 直後 \(Self.ranges(client))")
        hasMarkedText = length > 0
        updateCandidates(view)
    }

    private func hideComposition(client: IMKTextInput) {
        if hasMarkedText {
            Self.trace("hideComposition: setMarkedText(\"\") 直前 \(Self.ranges(client))")
            client.setMarkedText("", selectionRange: NSRange(location: 0, length: 0), replacementRange: NSRange(location: NSNotFound, length: NSNotFound))
            Self.trace("hideComposition: setMarkedText 直後 \(Self.ranges(client))")
            hasMarkedText = false
            // 変換ボックスが閉じたので、azooKey の差分変換の状態 (前回の入力) も終える。
            MeltypeConverter.shared.endComposition()
        }
        candidateList = []
        shownIndex = 0
        lastView = nil
        candidatesWindow?.hide()
    }

    private func addMark(_ style: Int, to text: NSMutableAttributedString, range: NSRange) {
        guard range.length > 0, range.location + range.length <= text.length,
              let marks = mark(forStyle: style, at: range) else { return }
        var attributes: [NSAttributedString.Key: Any] = [:]
        for (key, value) in marks {
            if let name = key as? String {
                attributes[NSAttributedString.Key(name)] = value
            } else if let name = key as? NSAttributedString.Key {
                attributes[name] = value
            }
        }
        text.addAttributes(attributes, range: range)
    }

    /// 候補で少し (1.5 秒) 止まったら、その候補の意味を候補ウィンドウの注釈に出す (Windows 版と同じ)。
    private var meaningKey: String?

    private func scheduleMeaning(_ view: CompositionView) {
        let key = view.meaning.map { "\(view.selectedIndex):\($0)" }
        guard key != meaningKey else { return }
        meaningKey = key
        guard let key, let meaning = view.meaning else { return }
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.5) { [weak self] in
            guard let self, self.meaningKey == key, let window = candidatesWindow, window.isVisible() else { return }
            window.showAnnotation(NSAttributedString(string: meaning))
        }
    }

    /// 候補ウィンドウの注釈に入力モードを出す (最小限の表示)。意味が出るまでの間だけで、意味が出たらそちらに替わる。
    /// 変換中は日本語入力なので「あ」、「コード」のアプリでは「あ (コード)」。英数のときは候補ウィンドウが出ないのでここには出ない。
    private func showModeAnnotation(_ view: CompositionView, window: IMKCandidates) {
        guard view.meaning == nil else { return }
        window.showAnnotation(NSAttributedString(string: appKind == .code ? "あ (コード)" : "あ"))
    }

    private func updateCandidates(_ view: CompositionView) {
        lastView = view
        guard let window = candidatesWindow else { return }
        if !view.converting && !view.predictions.isEmpty {
            updatePredictions(view, window: window)
        } else if view.converting && view.candidates.count > 1 {
            // IMKCandidates は標準では Space を候補確定に使い、コントローラーに渡さない。
            // Space で本体の次候補処理を呼べるよう、キーイベントを先にコントローラーへ配送する。
            window.setAttributes([IMKCandidatesSendServerKeyEventFirst as String: true])
            if view.candidates != candidateList || !window.isVisible() {
                // 候補が変わったときだけ、IMKCandidates に候補一覧を再取得させる (選択は先頭に戻る)。
                candidateList = view.candidates
                window.update()
                window.show(kIMKLocateCandidatesBelowHint)
                shownIndex = 0
            }
            // 本体側で選んでいる候補に、候補ウィンドウの選択 (青いバー) を合わせる。
            // candidateIdentifier(atLineNumber:) は 0 しか返さず使えないので、候補ウィンドウ自身の上下移動で動かす。
            // 動かすと候補ウィンドウから選択が変わった通知が来ることがあるので、その間の通知は無視する。
            let target = min(max(view.selectedIndex, 0), candidateList.count - 1)
            isSelectingProgrammatically = true
            defer { isSelectingProgrammatically = false }
            if target < shownIndex - target {
                // 先頭に近い候補へ戻るとき (最後の候補から先頭に戻ったときなど) は、上へ何十回も動かさずに一覧を読み直す
                window.update()
                shownIndex = 0
            }
            while shownIndex < target { window.moveDown(nil); shownIndex += 1 }
            while shownIndex > target { window.moveUp(nil); shownIndex -= 1 }
            scheduleMeaning(view)
            showModeAnnotation(view, window: window)
        } else {
            meaningKey = nil
            candidateList = []
            shownIndex = 0
            window.hide()
        }
    }

    /// 予測候補を候補ウィンドウに出す。Tab で入るまで (selectedPrediction == -1) は一覧を出すだけで、選択は動かさない
    /// (IMKCandidates に「非選択」は無いので先頭に青いバーが乗る)。
    private func updatePredictions(_ view: CompositionView, window: IMKCandidates) {
        meaningKey = nil
        // Tab・Enter・矢印を、候補ウィンドウより先に本体へ渡す (変換中と同じ)。
        window.setAttributes([IMKCandidatesSendServerKeyEventFirst as String: true])
        if view.predictions != candidateList || !window.isVisible() {
            candidateList = view.predictions
            window.update()
            window.show(kIMKLocateCandidatesBelowHint)
            shownIndex = 0
        }
        guard view.selectedPrediction >= 0 else { return }
        let target = min(view.selectedPrediction, candidateList.count - 1)
        isSelectingProgrammatically = true
        defer { isSelectingProgrammatically = false }
        if target < shownIndex - target {
            window.update()
            shownIndex = 0
        }
        while shownIndex < target { window.moveDown(nil); shownIndex += 1 }
        while shownIndex > target { window.moveUp(nil); shownIndex -= 1 }
    }

    /// 入力欄のキャレットの前後の文字列 (それぞれ 20 文字まで)。取れなければ nil。
    private func surroundingText(of client: IMKTextInput) -> (String?, String?) {
        let selection = client.selectedRange()
        guard selection.location != NSNotFound else { return (nil, nil) }
        let start = max(0, selection.location - 20)
        let before = client.attributedSubstring(from: NSRange(location: start, length: selection.location - start))?.string
        let after = client.attributedSubstring(from: NSRange(location: selection.location + selection.length, length: 20))?.string
        return (before, after)
    }
}
