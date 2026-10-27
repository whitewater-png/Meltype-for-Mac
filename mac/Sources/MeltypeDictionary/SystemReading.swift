// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import Foundation

/// 漢字をひらがなの読みにする (macOS の日本語解析 CFStringTokenizer の LatinTranscription → ひらがな)。
/// 辞書の管理画面には変換エンジン (azooKey) が無いので、漢字の読みはこれで引く。人名・専門語は違うことがあるので、画面では直せる前提で出す。
/// 漢字を含む語だけを読みにし、ひらがな・カタカナ・英字の語はそのまま (ローマ字・カタカナは本体の DictionaryManagement.ToReading が受け持つ)。
enum SystemReading {
    static func readKanji(_ text: String) -> String {
        guard text.unicodeScalars.contains(where: isKanji) else { return text }
        let source = text as NSString
        let tokenizer = CFStringTokenizerCreate(nil, source, CFRange(location: 0, length: source.length), kCFStringTokenizerUnitWordBoundary, Locale(identifier: "ja") as CFLocale)
        var out = ""
        var token = CFStringTokenizerAdvanceToNextToken(tokenizer)
        while !token.isEmpty {
            let range = CFStringTokenizerGetCurrentTokenRange(tokenizer)
            let word = source.substring(with: NSRange(location: range.location, length: range.length))
            if word.unicodeScalars.contains(where: isKanji),
               let latin = CFStringTokenizerCopyCurrentTokenAttribute(tokenizer, kCFStringTokenizerAttributeLatinTranscription) as? String,
               let kana = latin.applyingTransform(.latinToHiragana, reverse: false), !kana.isEmpty {
                out += kana
            } else {
                out += word
            }
            token = CFStringTokenizerAdvanceToNextToken(tokenizer)
        }
        return out
    }

    private static func isKanji(_ scalar: Unicode.Scalar) -> Bool {
        (0x4E00...0x9FFF).contains(scalar.value) || (0x3400...0x4DBF).contains(scalar.value) || scalar == "々" || scalar == "〆" || scalar == "ヶ"
    }
}
