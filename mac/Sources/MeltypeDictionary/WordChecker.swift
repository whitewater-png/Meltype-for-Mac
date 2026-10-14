// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import AppKit

/// 英単語として正しい綴りか (macOS のスペルチェッカー、英語で調べる)。「判定を調べる」の答えを実際の IME に揃えるため、
/// 本体 (meltype_set_word_checker) に登録する。
/// mac/Sources/MeltypeIME/NativeCore.swift の isWordCallback と同じ判定にしておくこと (違うと、画面の答えが IME とずれる。
/// IME 側は MeltypeDictionaryKit に依存するだけで AppKit を持ち込まない設計なので、ここに複製している)。
/// 戻り値は 1 = 正しい綴り・0 = 違う・-1 = 分からない (本体は同梱の英単語表で答える)。
/// NSSpellChecker はメインスレッドで使うので、それ以外のスレッドから呼ばれたら -1 (0 にすると「英語ではない」と答えてしまう)。
let isEnglishWordCallback: @convention(c) (UnsafePointer<CChar>?) -> Int32 = { word in
    guard Thread.isMainThread else { return -1 }
    guard let word else { return 0 }
    let text = String(cString: word)
    let misspelled = NSSpellChecker.shared.checkSpelling(of: text, startingAt: 0, language: "en", wrap: false, inSpellDocumentWithTag: 0, wordCount: nil)
    return misspelled.location == NSNotFound ? 1 : 0
}
