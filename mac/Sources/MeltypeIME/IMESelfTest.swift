// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import AppKit
import Carbon.HIToolbox

/// `Meltype --self-test`: IME の Swift 側の純粋な関数を確かめる (辞書の管理画面の `MeltypeDictionary --self-test` と同じ形)。
/// コマンドライン ツールだけの環境では Swift のテスト (XCTest / swift-testing) が動かないので、実行ファイルに組み込んでおく。
/// IMKServer は立てず、本体 (libMeltypeNative.dylib) も macOS の辞書も読まず、通信もしない。
/// 利用者のデータ (保存場所のファイル) と設定 (UserDefaults) は読み書きしない。
/// 更新の確認 (Updater・UpdateSettings) と Zenzai (ZenzaiModel・ZenzaiSelfTest。こちらは `--zenzai-self-test`) は対象にしない。
///   1. キー対応 (KeyMapping): 特別なキー・英数字・記号・JIS かなの物理キー・修飾キーのビット
///   2. 候補の意味の整え (CandidateDictionary): 引く語の判定・定義からの 1 文の取り出し
///   3. 案内の置き直し (SuggestionHint): 候補ウィンドウを避ける位置 (右 → 上 → 下)
///   4. 本体の応答の読み取り (NativeCore): 結果の JSON・登録の提案・専門用語集の分野・設定の JSON
enum IMESelfTest {
    /// すべて通れば true。出力は辞書の管理画面の self-test と同じ (群ごとの件数・FAIL の行・最後に合計)。
    static func run(log: (String) -> Void) -> Bool {
        var failures: [String] = []
        var checks = 0
        func check(_ condition: Bool, _ message: String) {
            checks += 1
            if !condition { failures.append(message) }
        }
        let groups: [(String, ((Bool, String) -> Void) -> Void)] = [
            ("キー対応", checkKeyMapping),
            ("候補の意味の整え", checkCandidateDictionary),
            ("案内の置き直し", checkSuggestionHint),
            ("本体の応答の読み取り", checkNativeCore),
        ]
        for (name, body) in groups {
            let before = checks
            body(check)
            log("\(name): \(checks - before) 件")
        }
        for failure in failures { log("FAIL: \(failure)") }
        log(failures.isEmpty ? "PASS: \(checks) 件" : "FAIL: \(failures.count) / \(checks) 件")
        return failures.isEmpty
    }

    // ---- 1. キー対応 ----

    /// キーを押したときの NSEvent を作る (画面・アプリは要らない)。
    private static func key(_ keyCode: Int, _ characters: String, _ flags: NSEvent.ModifierFlags = []) -> NSEvent? {
        NSEvent.keyEvent(with: .keyDown, location: .zero, modifierFlags: flags, timestamp: 0, windowNumber: 0, context: nil,
                         characters: characters, charactersIgnoringModifiers: characters, isARepeat: false, keyCode: UInt16(keyCode))
    }

    private static func vk(_ keyCode: Int, _ characters: String, _ flags: NSEvent.ModifierFlags = [], kana: Bool = false) -> Int32? {
        guard let event = key(keyCode, characters, flags) else { return -1 }   // 作れなければ、どの期待値とも合わない値
        return KeyMapping.virtualKey(for: event, kana: kana)
    }

    static func checkKeyMapping(_ check: (Bool, String) -> Void) {
        check(key(kVK_ANSI_A, "a") != nil, "キーの NSEvent を作れる")
        // 特別なキー (文字ではなくキーコードで決まる)
        check(vk(kVK_Return, "\r") == 0x0D && vk(kVK_ANSI_KeypadEnter, "\u{3}") == 0x0D, "Return・テンキーの Enter は 0x0D")
        check(vk(kVK_Delete, "\u{7F}") == 0x08 && vk(kVK_ForwardDelete, "\u{F728}") == 0x2E && vk(kVK_Escape, "\u{1B}") == 0x1B,
              "BackSpace 0x08・Delete 0x2E・Esc 0x1B")
        check(vk(kVK_LeftArrow, "\u{F702}") == 0x25 && vk(kVK_UpArrow, "\u{F700}") == 0x26
              && vk(kVK_RightArrow, "\u{F703}") == 0x27 && vk(kVK_DownArrow, "\u{F701}") == 0x28, "矢印は 0x25〜0x28")
        check(vk(kVK_F6, "\u{F709}") == 0x75 && vk(kVK_F7, "\u{F70A}") == 0x76 && vk(kVK_F10, "\u{F70D}") == 0x79, "F6 0x75・F7 0x76・F10 0x79")
        check(vk(kVK_Space, " ") == 0x20 && vk(kVK_Tab, "\t") == 0x09, "Space 0x20・Tab 0x09")
        // 英数字 (文字で決まる。大文字も同じキー)
        check(vk(kVK_ANSI_A, "a") == 0x41 && vk(kVK_ANSI_A, "A", .shift) == 0x41 && vk(kVK_ANSI_Z, "z") == 0x5A, "英字は大文字の ASCII (A 0x41・Z 0x5A)")
        check(vk(kVK_ANSI_5, "5") == 0x35 && vk(kVK_ANSI_0, "0") == 0x30, "数字は ASCII (5 0x35・0 0x30)")
        // 記号: US 配列・JIS 配列の代表キー
        check(vk(kVK_ANSI_Comma, ",") == 0xBC && vk(kVK_ANSI_Period, ".") == 0xBE && vk(kVK_ANSI_Minus, "-") == 0xBD && vk(kVK_ANSI_Slash, "/") == 0xBF,
              ", . - / は OEM のキーコード")
        check(vk(kVK_ANSI_LeftBracket, "[") == 0xDB && vk(kVK_ANSI_RightBracket, "]") == 0xDD, "US の [ ] は 0xDB・0xDD")
        check(vk(kVK_ANSI_LeftBracket, "@") == 0x07 && vk(kVK_ANSI_Equal, "^") == 0x07 && vk(kVK_JIS_Yen, "¥") == 0x07,
              "JIS の @ ^ ¥ (ローマ字入力) は「その他の文字」0x07")
        check(vk(kVK_ANSI_Quote, "'") == 0xDE && vk(kVK_ANSI_Quote, ":") == 0xDE && vk(kVK_ANSI_Semicolon, ";") == 0xBA,
              "Quote の位置は文字によらず 0xDE (US の ' も JIS の : も)、Semicolon は 0xBA")
        check(vk(200, "\u{10}") == nil && vk(200, "") == nil, "制御文字・文字の無いキーは nil (アプリに渡す)")
        // JIS かな入力: 物理キーの位置で決まる
        check(KeyMapping.kanaVirtualKey(for: kVK_JIS_Yen) == 0xDC && KeyMapping.kanaVirtualKey(for: kVK_JIS_Underscore) == 0xE2
              && KeyMapping.kanaVirtualKey(for: kVK_ANSI_Equal) == 0xDE, "かな: ¥ (ー) 0xDC・_ (ろ) 0xE2・^ (へ) 0xDE")
        check(KeyMapping.kanaVirtualKey(for: kVK_ANSI_0) == 0x30 && KeyMapping.kanaVirtualKey(for: kVK_Return) == nil
              && KeyMapping.kanaVirtualKey(for: kVK_Space) == nil, "かな: 0 は 0x30、Return・Space はかなのキーではない")
        check(vk(kVK_JIS_Yen, "¥", kana: true) == 0xDC && vk(kVK_Return, "\r", kana: true) == 0x0D, "かな入力でも、かなのキー以外は今までどおり")
        check(vk(kVK_ANSI_Equal, "^", .command, kana: true) == 0x07 && vk(kVK_ANSI_Equal, "^", .control, kana: true) == 0x07
              && vk(kVK_ANSI_Equal, "^", .shift, kana: true) == 0xDE, "かな入力で Command・Control を押したキーは文字で引く (Shift は物理キーのまま)")
        // 修飾キーのビット
        check(KeyMapping.modifierBits([]) == 0 && KeyMapping.modifierBits(.shift) == 1 && KeyMapping.modifierBits(.control) == 2
              && KeyMapping.modifierBits(.option) == 4 && KeyMapping.modifierBits(.command) == 8, "修飾キー: Shift 1・Control 2・Option 4・Command 8")
        check(KeyMapping.modifierBits([.shift, .control, .option, .command]) == 15 && KeyMapping.modifierBits([.control, .option]) == 6,
              "修飾キーの組み合わせはビットの和")
        check(KeyMapping.modifierBits([.capsLock, .function, .numericPad]) == 0 && KeyMapping.modifierBits([.capsLock, .shift]) == 1,
              "Caps Lock・fn・テンキーの印は渡さない")
    }

    // ---- 2. 候補の意味の整え ----

    static func checkCandidateDictionary(_ check: (Bool, String) -> Void) {
        typealias D = CandidateDictionary
        check(D.normalize("Ｄｏｎ’ｔ") == "Don't", "全角英字を半角に、’ を ' に")
        check(D.isLookupWord("meeting") && D.isLookupWord("Ｍｅｅｔｉｎｇ") && D.isLookupWord("don’t") && D.isLookupWord("run-up"),
              "英字 (全角・’・- を含む) は引く")
        check(!D.isLookupWord("a") && !D.isLookupWord("x1") && !D.isLookupWord("日本") && !D.isLookupWord("-ab") && !D.isLookupWord(""),
              "1 文字・数字入り・日本語・英字で始まらない語は引かない")
        // (A) 国語辞典の外来語
        check(D.summary(of: "ミーティング 0meeting打ち合わせや連絡のための会合。", word: "meeting") == "ミーティング：打ち合わせや連絡のための会合",
              "(A) meeting → 見出し：本文 (「。」で切る)")
        check(D.summary(of: "エラー 1error（名）スル ① やり損なうこと。② 野球で", word: "error") == "エラー：（名）スル ① やり損なうこと",
              "(A) error → 「。」の手前で切る")
        check(D.summary(of: "デプロイ 3deploy →デプロイメント", word: "deploy") == "デプロイ：→デプロイメント", "(A) deploy → 矢印の参照")
        // (A′) 発音つきの英英、(B) 英和
        check(D.summary(of: "Kyoto Kyo·to | kēˈ(y)ōdō | an industrial city in central Japan", word: "Kyoto") == "Kyoto：an industrial city in central Japan",
              "(A′) Kyoto → 発音の後ろだけ")
        check(D.summary(of: "To･ky･o | tóʊkiòʊ | 名詞東京.", word: "Tokyo") == "名詞東京." && D.summary(of: "To･ky･o | tóʊkiòʊ | 名詞東京.", word: "tokyo") == "名詞東京.",
              "(B) Tokyo / tokyo → 2 つ目の | の後ろ (先頭の大文字の違いは当てる)")
        // 別の語・略語を出さない
        check(D.summary(of: "ラン 1running走ること。", word: "run") == nil && D.summary(of: "ラン 1run-up助走。", word: "run") == nil
              && D.summary(of: "ドント 1don't…するな。", word: "don") == nil, "running・run-up・don't を run・don に当てない")
        check(D.summary(of: "エイズ 1AIDS後天性免疫不全症候群。", word: "aids") == nil
              && D.summary(of: "エイズ 1AIDS後天性免疫不全症候群。", word: "AIDS") == "エイズ：後天性免疫不全症候群", "略語の項目は、すべて大文字で選んだときだけ")
        check(D.summary(of: "R. ① 〖ratio〗数学で「比」", word: "river") == nil && D.summary(of: "API 〖American Petroleum Institute〗", word: "api") == nil,
              "どちらの形にも当たらなければ nil")
        check(D.summary(of: "ロング 1long" + String(repeating: "あ", count: 60), word: "long", maxLength: 10) == "ロング：ああああああ…",
              "maxLength 文字で切って「…」")
        check(D.summary(of: "ミーティング 0meeting  打ち合わせ  や\n連絡", word: "meeting") == "ミーティング：打ち合わせ や", "改行で切り、空白をつめる")
    }

    // ---- 3. 案内の置き直し ----

    static func checkSuggestionHint(_ check: (Bool, String) -> Void) {
        let screen = NSRect(x: 0, y: 0, width: 1000, height: 800)
        let frame = NSRect(x: 100, y: 100, width: 200, height: 20)
        func place(_ frame: NSRect, _ blocks: [NSRect], _ screen: NSRect = screen) -> NSRect {
            SuggestionHint.avoided(frame, blocks: blocks, screen: screen)
        }
        check(place(frame, []) == frame, "避ける窓が無ければそのまま")
        check(place(frame, [NSRect(x: 500, y: 500, width: 100, height: 100)]) == frame, "重ならなければそのまま")
        check(place(frame, [NSRect(x: 100, y: 100, width: 0, height: 50)]) == frame, "空の枠は無視する")
        check(place(frame, [NSRect(x: 90, y: 50, width: 300, height: 100)]) == NSRect(x: 394, y: 100, width: 200, height: 20),
              "まず候補ウィンドウの右 (行の高さのまま、4pt 空ける)")
        let nearRight = NSRect(x: 750, y: 100, width: 200, height: 20)
        let window = NSRect(x: 700, y: 50, width: 250, height: 100)
        check(place(nearRight, [window]) == NSRect(x: 750, y: 154, width: 200, height: 20), "右が画面からはみ出すなら上")
        check(place(nearRight, [window], NSRect(x: 0, y: 0, width: 1000, height: 170)) == NSRect(x: 750, y: 26, width: 200, height: 20),
              "上もはみ出すなら下")
        check(place(nearRight, [window], NSRect(x: 0, y: 40, width: 1000, height: 130)) == nearRight, "どこにも置けなければ元のまま")
        check(place(frame, [NSRect(x: 90, y: 50, width: 100, height: 100), NSRect(x: 150, y: 90, width: 150, height: 300)])
              == NSRect(x: 304, y: 100, width: 200, height: 20), "窓が複数なら、全部を合わせた枠の右")
    }

    // ---- 4. 本体の応答の読み取り ----

    private static func decode(_ json: String) -> SessionResult? {
        try? NativeCore.decodeResult(Data(json.utf8))
    }

    static func checkNativeCore(_ check: (Bool, String) -> Void) {
        // 結果の JSON (src/Meltype.Core/Composition/MeltypeSession.cs の SessionResult.ToJson と同じ形)
        let closed = decode(#"{"consumed":false,"commits":[],"view":null}"#)
        check(closed?.consumed == false && closed?.commits.isEmpty == true && closed?.view == nil && closed?.toggle == nil,
              "変換ボックスを閉じる結果 (view が null、toggle は無いキー)")
        let committed = decode(#"{"consumed":true,"commits":[{"deleteBefore":0,"text":"日本語"},{"deleteBefore":3,"text":"Meltype","original":"めるたいぷ"}],"toggle":true,"view":null}"#)
        check(committed?.commits.count == 2 && committed?.commits[0].text == "日本語" && committed?.commits[0].original == nil
              && committed?.commits[1].deleteBefore == 3 && committed?.commits[1].original == "めるたいぷ" && committed?.toggle == true,
              "確定の列 (消す文字数・確定し直しの元の文字列・toggle)")
        let viewJson = #"{"consumed":true,"commits":[],"view":{"text":"へんかん","converting":true,"selectedIndex":1,"selectedClause":0,"hint":"","candidates":["変換","返還"],"clauses":["変換"],"suggestion":null,"meaning":"convert","predictions":["変換する"],"selectedPrediction":-1,"grid":true,"caret":2,"notes":[null,"return"]}}"#
        let view = decode(viewJson)?.view
        check(view?.text == "へんかん" && view?.converting == true && view?.selectedIndex == 1 && view?.candidates == ["変換", "返還"]
              && view?.clauses == ["変換"] && view?.predictions == ["変換する"] && view?.selectedPrediction == -1,
              "変換ボックスの中身 (候補・文節・予測)")
        check(view?.meaning == "convert" && view?.suggestion == nil && view?.grid == true, "意味・もしかして (null)・格子")
        let minimalView = decode(#"{"consumed":true,"commits":[],"view":{"text":"a","converting":false,"selectedIndex":-1,"selectedClause":0,"hint":"","candidates":[],"clauses":[],"meaning":null,"predictions":[],"selectedPrediction":-1}}"#)?.view
        check(minimalView != nil && minimalView?.grid == nil && minimalView?.suggestion == nil, "grid・suggestion の無い古い形も読める")
        check(decode(#"{"consumed":true,"commits":[],"view":{"text":"a","converting":false}}"#) == nil
              && decode(#"{"consumed":true"#) == nil && decode("") == nil, "欠けた・壊れた JSON は nil")
        // 登録の提案 (読み Tab 語)
        let suggestions = NativeCore.parseSuggestions("めるたいぷ\tMeltype\nTab の無い行\n\nかな\t仮\t名\n")
        check(suggestions.map { $0.reading } == ["めるたいぷ", "かな"] && suggestions.map { $0.word } == ["Meltype", "仮\t名"],
              "登録の提案: Tab の無い行・空行は飛ばし、2 つ目の Tab からは語に含める")
        check(NativeCore.parseSuggestions("").isEmpty, "提案が無ければ空")
        // 専門用語集の分野 (ID Tab 名称 Tab 語数 Tab 有効 [Tab 自作])
        let domains = NativeCore.parseTermDomains("medical\t医療\t120\t1\t0\nit\tIT\t多い\t1\nlaw\t法律\t30\t0\nshort\t欄が足りない\t3\n")
        check(domains.map(\.id) == ["medical", "law"] && domains[0].name == "医療" && domains[0].count == 120 && domains[0].enabled
              && !domains[1].enabled, "専門用語集: 語数が数でない行・欄の足りない行は飛ばす")
        // 設定の JSON から「候補の意味を表示」
        check(!NativeCore.showCandidateMeanings(settingsJson: #"{"items":[{"key":"LiveConversion","value":true},{"key":"ShowCandidateMeanings","value":false}]}"#),
              "候補の意味: OFF を読める")
        check(NativeCore.showCandidateMeanings(settingsJson: #"{"items":[{"key":"LiveConversion","value":false}]}"#)
              && NativeCore.showCandidateMeanings(settingsJson: "{ 壊れた")
              && NativeCore.showCandidateMeanings(settingsJson: #"{"items":[{"key":"ShowCandidateMeanings","value":"no"}]}"#),
              "候補の意味: 項目が無い・壊れた・型が違うときは既定 (ON)")
    }
}
