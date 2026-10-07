// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import Foundation
import KanaKanjiConverterModuleWithDefaultDictionary

/// azooKey の変換エンジン (AzooKeyKanaKanjiConverter) で、ひらがなを漢字かな混じりに変換する。
/// azooKey の API が変わったときは、このファイルだけ直せばよいようにしてある。
final class MeltypeConverter {
    static let shared = MeltypeConverter()

    // 同梱の辞書 (KanaKanjiConverterModuleWithDefaultDictionary) を使う変換エンジン。
    // azooKey の withDefaultDictionary() は辞書を Bundle.module で探すが、Bundle.module は Meltype.app の直下か
    // ビルドしたマシンのフォルダーしか見ない。辞書のバンドルは Contents/Resources に入れているので、見つからずに落ちていた (#26)。
    // (Meltype.app の直下には置けない: 署名が通らない)。なので、辞書の場所をこちらで渡す。
    private let converter = KanaKanjiConverter(dicdataStore: DicdataStore(dictionaryURL: MeltypeConverter.resource("Dictionary")))
    private let options: ConvertRequestOptions

    /// 同梱の辞書のバンドル (Contents/Resources に入れている) の中のフォルダー。
    private static func resource(_ name: String) -> URL {
        let bundleName = "AzooKeyKanaKanjiConverter_KanaKanjiConverterModuleWithDefaultDictionary.bundle"
        let url = (Bundle.main.resourceURL ?? Bundle.main.bundleURL).appendingPathComponent(bundleName, isDirectory: true)
        return (Bundle(url: url)?.resourceURL ?? url).appendingPathComponent(name, isDirectory: true)
    }

    /// 絵文字の辞書 (azooKey の withDefaultEmojiDictionary() と同じ選び方。こちらも Bundle.module を使わない)。
    private static func emojiDictionary() -> URL {
        let directory = resource("EmojiDictionary")
        if #available(macOS 15.3, *) { return directory.appendingPathComponent("emoji_all_E16.0.txt", isDirectory: false) }
        if #available(macOS 14.4, *) { return directory.appendingPathComponent("emoji_all_E15.1.txt", isDirectory: false) }
        return directory.appendingPathComponent("emoji_all_E15.0.txt", isDirectory: false)
    }

    private init() {
        // azooKey の学習データ・ユーザー辞書の置き場所 (学習は確定のたびに learn で書き、再起動しても残る)。
        let directory = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Meltype/azooKey", isDirectory: true)
        try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        options = ConvertRequestOptions(
            requireJapanesePrediction: .disabled,
            requireEnglishPrediction: .disabled,
            keyboardLanguage: .ja_JP,
            learningType: .inputAndOutput,
            memoryDirectoryURL: directory,
            sharedContainerURL: directory,
            textReplacer: TextReplacer(emojiDataProvider: { MeltypeConverter.emojiDictionary() }),
            specialCandidateProviders: KanaKanjiConverter.defaultSpecialCandidateProviders,
            metadata: .init(versionString: "Meltype 1.0.1")
        )
    }

    /// 直近に変換した読み → 候補。確定した文字列に対応する Candidate を、学習のときに取り出すために覚えておく。
    private var recentCandidates: [String: [Candidate]] = [:]
    /// recentCandidates に入れた順 (古いものから捨てる)。
    private var recentOrder: [String] = []
    private static let recentLimit = 10

    private func remember(_ candidates: [Candidate], for reading: String) {
        if recentCandidates[reading] == nil {
            recentOrder.append(reading)
            if recentOrder.count > MeltypeConverter.recentLimit {
                recentCandidates.removeValue(forKey: recentOrder.removeFirst())
            }
        }
        recentCandidates[reading] = candidates
    }

    /// ひらがな全体に対する変換結果 (入力をすべて使ったものだけ、よい順)。
    private func results(for hiragana: String) -> [Candidate] {
        var composing = ComposingText()
        composing.insertAtCursorPosition(hiragana, inputStyle: .direct)
        let results = converter.requestCandidates(composing, options: options)
        converter.stopComposition()
        let count = hiragana.count
        // 入力 (ひらがな) をすべて使った候補だけ (.direct で入れたので、入力の文字数 = ひらがなの文字数)
        let filtered = results.mainResults.filter { $0.composingCount == .inputCount(count) || $0.composingCount == .surfaceCount(count) }
        remember(filtered, for: hiragana)
        return filtered
    }

    /// 確定した文節を azooKey に学習させる (メインスレッドから呼ぶ。KanaKanjiConverter はスレッドセーフではない)。
    /// キャッシュに一致する Candidate が無い文節 (ユーザー辞書など azooKey の候補以外から選んだもの) は飛ばす。
    func learn(context: String?, clauses: [(reading: String, text: String)]) {
        var learned = false
        for clause in clauses {
            // 文節ごとの読みで変換した候補が残っていなければ、文全体の候補は文節の単位が違うので使えない。
            guard let candidate = recentCandidates[clause.reading]?.first(where: { $0.text == clause.text }) else { continue }
            converter.updateLearningData(candidate)
            learned = true
        }
        // commitUpdateLearningData を呼ぶまで保存されない。
        if learned { converter.commitUpdateLearningData() }
    }

    /// 文節に区切った変換結果 (読みをつなげると元のひらがなになる)。変換できなければ空。
    func clauses(for hiragana: String, context: String?) -> [(reading: String, text: String)] {
        guard let best = results(for: hiragana).first else { return [] }
        var clauses: [(reading: String, text: String)] = []
        for element in best.data {
            let reading = element.ruby.applyingTransform(.hiraganaToKatakana, reverse: true) ?? element.ruby
            // 助詞・助動詞 (ひらがなだけの短い語) は前の文節にくっつける (今日|は → 今日は)。
            if let last = clauses.last, element.word == reading, element.word.count <= 3 {
                clauses[clauses.count - 1] = (last.reading + reading, last.text + element.word)
            } else {
                clauses.append((reading, element.word))
            }
        }
        // 読みがずれた (長音などの扱いの違い) ときは、文節に分けずに全体で返す。
        if clauses.map(\.reading).joined() != hiragana {
            return [(hiragana, best.text)]
        }
        return clauses
    }

    /// 読みに対する予測候補 (読みそのままのものは除き、重複なし、8 個まで)。
    /// 通常の変換結果が変わらないよう、予測は予測変換のときだけ .manualMix で要求する (変換のたびに予測を計算しない)。
    func predictions(for hiragana: String) -> [String] {
        var composing = ComposingText()
        composing.insertAtCursorPosition(hiragana, inputStyle: .direct)
        var predictionOptions = options
        predictionOptions.requireJapanesePrediction = .manualMix
        let results = converter.requestCandidates(composing, options: predictionOptions)
        converter.stopComposition()
        var seen = Set<String>()
        var list: [String] = []
        for candidate in results.predictionResults where candidate.text != hiragana && seen.insert(candidate.text).inserted {
            list.append(candidate.text)
            if list.count >= 8 { break }
        }
        return list
    }

    /// 読みに対する候補の一覧 (重複なし、30 個まで)。
    func candidates(for reading: String) -> [String] {
        var seen = Set<String>()
        var list: [String] = []
        for candidate in results(for: reading) where seen.insert(candidate.text).inserted {
            list.append(candidate.text)
            if list.count >= 30 { break }
        }
        return list
    }
}
