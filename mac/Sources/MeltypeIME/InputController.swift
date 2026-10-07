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
    private var hasMarkedText = false
    /// 直前に本体から受け取った表示。候補ウィンドウのクリックが変換の候補か予測かを見分けるのに使う。
    private var lastView: CompositionView?

    private static let japaneseModeID = InputSourceRegistration.modeID
    private static let romanModeID = InputSourceRegistration.romanModeID

    /// 入力メニューで今選ばれているモードの ID。IMKTextInput に「今のモードを読む」API が無いので、setValue で受け取って覚える。
    private var currentMode: String?

    /// 今のアプリの種類 (activateServer で bundle ID から決める)。候補ウィンドウの注釈に出す。
    private var appKind: AppKind = .general

    override init!(server: IMKServer!, delegate: Any!, client inputClient: Any!) {
        super.init(server: server, delegate: delegate, client: inputClient)
        session = NativeCore.shared.createSession()
    }

    deinit {
        NativeCore.shared.destroySession(session)
    }

    override func recognizedEvents(_ sender: Any!) -> Int {
        Int(NSEvent.EventTypeMask.keyDown.rawValue)
    }

    override func handle(_ event: NSEvent!, client sender: Any!) -> Bool {
        guard let event, event.type == .keyDown, let client = sender as? IMKTextInput else { return false }

        // アプリ別設定で OFF・ゲームのアプリ (setApp の戻り値 .disabled) は、英数/かなキーも含めて全てアプリに素通しする。
        if appKind == .disabled { return false }

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
        let character: Int32 = utf16.count == 1 ? Int32(utf16[0]) : 0
        let flags = event.modifierFlags
        var modifiers: Int32 = 0
        if flags.contains(.shift) { modifiers |= 1 }
        if flags.contains(.control) { modifiers |= 2 }
        if flags.contains(.option) { modifiers |= 4 }
        if flags.contains(.command) { modifiers |= 8 }

        // 確定済みの文字列を選択して Shift + Space: 読みに戻して再変換する (選択範囲を変換中の文字に置き換える)。読みに戻せなければ通常の処理へ。
        if Int(event.keyCode) == kVK_Space, modifiers == 1, !hasMarkedText {
            let selection = client.selectedRange()
            if selection.location != NSNotFound, selection.length > 0,
               let selected = client.attributedSubstring(from: selection)?.string,
               let result = NativeCore.shared.reconvert(session, text: selected), result.consumed {
                apply(result, to: client, replacing: selection)
                return true
            }
        }

        let (before, after) = hasMarkedText ? (nil, nil) : surroundingText(of: client)
        guard let result = NativeCore.shared.handleKey(session, vk: vk, character: character, modifiers: modifiers, before: before, after: after) else {
            return false
        }
        apply(result, to: client)
        return result.consumed
    }

    /// フォーカスが外れた・クリックで別の場所に移ったときなど。未確定の内容をそのまま確定する。
    override func commitComposition(_ sender: Any!) {
        guard let client = (sender as? IMKTextInput) ?? (self.client() as? IMKTextInput) else { return }
        apply(NativeCore.shared.commit(session), to: client)
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

    override func menu() -> NSMenu! {
        let menu = NSMenu()
        // 何度も選び直した語の登録提案 (最大 3 件)。選ぶと辞書に登録、「登録しない」で以後提案しない。
        let suggestions = NativeCore.shared.suggestions(session)
        for suggestion in suggestions {
            let accept = menu.addItem(withTitle: "『\(suggestion.word)』を辞書に登録 (\(suggestion.reading))", action: #selector(acceptSuggestion(_:)), keyEquivalent: "")
            accept.representedObject = [suggestion.reading, suggestion.word]
            let reject = menu.addItem(withTitle: "    『\(suggestion.word)』は登録しない", action: #selector(rejectSuggestion(_:)), keyEquivalent: "")
            reject.representedObject = [suggestion.reading, suggestion.word]
        }
        if !suggestions.isEmpty { menu.addItem(.separator()) }
        menu.addItem(withTitle: "選択中の文字をユーザー辞書に登録…", action: #selector(registerWord(_:)), keyEquivalent: "")
        menu.addItem(withTitle: "辞書の登録提案の履歴を消去", action: #selector(clearSuggestions(_:)), keyEquivalent: "")
        menu.addItem(withTitle: "Meltype のデータフォルダを開く (設定・ユーザー辞書)", action: #selector(openDataFolder(_:)), keyEquivalent: "")
        menu.addItem(withTitle: "不具合の報告・提案… (Mac 版はプレビュー版です)", action: #selector(openReport(_:)), keyEquivalent: "")
        return menu
    }

    @objc private func acceptSuggestion(_ sender: NSMenuItem) {
        guard let pair = sender.representedObject as? [String], pair.count == 2 else { return }
        if let error = NativeCore.shared.acceptSuggestion(session, reading: pair[0], word: pair[1]) {
            let failure = NSAlert()
            failure.messageText = "登録できませんでした"
            failure.informativeText = error
            NSApp.activate(ignoringOtherApps: true)
            failure.runModal()
        }
    }

    @objc private func rejectSuggestion(_ sender: NSMenuItem) {
        guard let pair = sender.representedObject as? [String], pair.count == 2 else { return }
        NativeCore.shared.rejectSuggestion(session, reading: pair[0], word: pair[1])
    }

    @objc private func clearSuggestions(_ sender: Any?) {
        NativeCore.shared.clearSuggestions(session)
    }

    /// 選択中の文字を語にして、読みを聞いてユーザー辞書に登録する。選択が無ければ語も空欄で開く。
    @objc private func registerWord(_ sender: Any?) {
        var selected = ""
        if let client = client() {
            let range = client.selectedRange()
            if range.location != NSNotFound, range.length > 0 {
                selected = client.attributedSubstring(from: range)?.string ?? ""
            }
        }
        let alert = NSAlert()
        alert.messageText = "ユーザー辞書に登録"
        alert.informativeText = "読み (ひらがな 2 文字以上) と単語を入力してください。登録すると次の変換から効きます。"
        let readingField = NSTextField(frame: NSRect(x: 0, y: 28, width: 260, height: 24))
        readingField.placeholderString = "読み (ひらがな)"
        let wordField = NSTextField(frame: NSRect(x: 0, y: 0, width: 260, height: 24))
        wordField.placeholderString = "単語"
        wordField.stringValue = selected
        let box = NSView(frame: NSRect(x: 0, y: 0, width: 260, height: 52))
        box.addSubview(readingField)
        box.addSubview(wordField)
        alert.accessoryView = box
        alert.addButton(withTitle: "登録")
        alert.addButton(withTitle: "キャンセル")
        alert.window.initialFirstResponder = readingField
        // IME は背面のアプリなので、ダイアログを前に出す。
        NSApp.activate(ignoringOtherApps: true)
        guard alert.runModal() == .alertFirstButtonReturn else { return }
        if let error = NativeCore.shared.addUserWord(session, reading: readingField.stringValue, word: wordField.stringValue) {
            let failure = NSAlert()
            failure.messageText = "登録できませんでした"
            failure.informativeText = error
            failure.runModal()
        }
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
                if caret != NSNotFound {
                    let length = min(edit.deleteBefore, caret)
                    range = NSRange(location: caret - length, length: length)
                }
            }
            client.insertText(edit.text, replacementRange: range)
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
        if view.converting && !view.clauses.isEmpty {
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
        client.setMarkedText(text, selectionRange: NSRange(location: length, length: 0), replacementRange: replacement ?? NSRange(location: NSNotFound, length: NSNotFound))
        hasMarkedText = length > 0
        updateCandidates(view)
    }

    private func hideComposition(client: IMKTextInput) {
        if hasMarkedText {
            client.setMarkedText("", selectionRange: NSRange(location: 0, length: 0), replacementRange: NSRange(location: NSNotFound, length: NSNotFound))
            hasMarkedText = false
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
