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
    private let converter: KanaKanjiConverter
    /// Zenzai を使わない (.off) 基本の変換オプション。Zenzai を使うときは、要求ごとに zenzaiMode (左文脈を含む) だけ差し替えた複製を使う (requestCandidates)。
    private let options: ConvertRequestOptions

    /// true のとき、変換のたびに azooKey の組版 (stopComposition) を終えず、差分変換 (kana2lattice_changed / no_change) を使う。
    /// 毎回 stopComposition すると、1 キーごとに読み全体を全計算してしまう (false は従来の動き。ベンチの比較用)。
    private let incremental: Bool

    /// azooKey のセッション (読みの系列ごとに previousInputData・lattice・予測キャッシュが独立する)。
    /// Meltype の Core は 1 キーのうちに、(1) 予測 (読み全体) → (2) ライブ変換 (読み全体の最後の区切り) →
    /// (3) 文節ごとの候補作成 (最後の文節の読み) と、読みの違う要求を続けて出す。1 つのセッションで受けると、(3) が前回の入力を上書きして
    /// 次のキーの (1) の差分が効かない。なので、要求の読みと共通接頭辞が最も長い前回の読みを持つセッションに振り分け、系列ごとに 1 文字ずつ伸ばす。
    private struct Slot {
        let id: KanaKanjiConverter.ConversionSessionID
        var lastReading = ""
        var used = 0
    }
    private var slots: [Slot] = []
    private var useClock = 0
    private static let sessionCount = 3

    /// 同梱の辞書のバンドル (Contents/Resources に入れている) の中のフォルダー。
    static func resource(_ name: String) -> URL {
        let bundleName = "AzooKeyKanaKanjiConverter_KanaKanjiConverterModuleWithDefaultDictionary.bundle"
        let url = (Bundle.main.resourceURL ?? Bundle.main.bundleURL).appendingPathComponent(bundleName, isDirectory: true)
        return (Bundle(url: url)?.resourceURL ?? url).appendingPathComponent(name, isDirectory: true)
    }

    /// 絵文字の辞書 (azooKey の withDefaultEmojiDictionary() と同じ選び方。こちらも Bundle.module を使わない)。
    static func emojiDictionary() -> URL {
        let directory = resource("EmojiDictionary")
        if #available(macOS 15.3, *) { return directory.appendingPathComponent("emoji_all_E16.0.txt", isDirectory: false) }
        if #available(macOS 14.4, *) { return directory.appendingPathComponent("emoji_all_E15.1.txt", isDirectory: false) }
        return directory.appendingPathComponent("emoji_all_E15.0.txt", isDirectory: false)
    }

    /// azooKey の学習データ・ユーザー辞書の既定の置き場所 (学習は確定のたびに learn で書き、再起動しても残る)。
    private static var defaultMemoryDirectory: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("Meltype/azooKey", isDirectory: true)
    }

    private convenience init() {
        self.init(dictionaryURL: MeltypeConverter.resource("Dictionary"), emojiDictionaryURL: MeltypeConverter.emojiDictionary(),
                  memoryDirectory: MeltypeConverter.defaultMemoryDirectory, incremental: true,
                  zenzai: ZenzaiModel.shared, zenzaiEnabled: { NativeCore.shared.zenzaiEnabled })
    }

    /// Zenzai (ニューラル変換) のモデルの管理と、設定が ON か。zenzai が nil なら、常に使わない (ベンチなど)。
    private let zenzai: ZenzaiModel?
    private let zenzaiEnabled: () -> Bool

    /// Zenzai の推論の上限。実験 (docs/ZENZAI_EXPERIMENT_2026-10.md) で 3 で頭打ち (5 でも増えない) だった。
    private static let zenzaiInferenceLimit = 3
    /// 1 キーの変換 (予測・ライブ変換・文節候補の呼び出しの合計) がこの時間を超えるのが、連続この回数になったら、このプロセスでは Zenzai を使わない。
    private static let zenzaiSlowSeconds = 0.3
    private static let zenzaiSlowStreakLimit = 3
    /// 前の呼び出しの終わりから、この時間以内に始まった呼び出しは、同じキーのものとして数える (Core は 1 キーのうちに続けて呼ぶ)。
    private static let zenzaiSameKeyGap = 0.1
    private var zenzaiWarmedUp = false
    private var zenzaiLastEnd: Date?
    private var zenzaiKeyTotal = 0.0
    private var zenzaiKeyIsSlow = false
    private var zenzaiSlowStreak = 0

    /// 辞書・学習データの場所と、差分変換を使うかを指定して作る (ベンチが従来・新の 2 つを比べるのに使う。通常は shared)。
    init(dictionaryURL: URL, emojiDictionaryURL: URL, memoryDirectory directory: URL, incremental: Bool,
         zenzai: ZenzaiModel? = nil, zenzaiEnabled: @escaping () -> Bool = { false }) {
        self.zenzai = zenzai
        self.zenzaiEnabled = zenzaiEnabled
        self.incremental = incremental
        let engine = KanaKanjiConverter(dicdataStore: DicdataStore(dictionaryURL: dictionaryURL))
        converter = engine
        if incremental { slots = (0..<MeltypeConverter.sessionCount).map { _ in Slot(id: engine.createSession()) } }
        // 学習データには打った語が入るので、本人だけが読める権限 (0700) にする。前の版が 0755 で作ったフォルダーも直す。
        try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
        try? FileManager.default.setAttributes([.posixPermissions: 0o700], ofItemAtPath: directory.path)
        options = ConvertRequestOptions(
            requireJapanesePrediction: .disabled,
            requireEnglishPrediction: .disabled,
            keyboardLanguage: .ja_JP,
            learningType: .inputAndOutput,
            memoryDirectoryURL: directory,
            sharedContainerURL: directory,
            textReplacer: TextReplacer(emojiDataProvider: { emojiDictionaryURL }),
            specialCandidateProviders: KanaKanjiConverter.defaultSpecialCandidateProviders,
            metadata: .init(versionString: "Meltype 1.2.1")
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

    /// 直近に変換した読み → 変換結果。同じ読みを続けて変換するとき (文脈付きと文脈なしで同じ読みを 2 度渡す、変換のあとの候補の一覧など)
    /// に azooKey を呼び直さない。azooKey の変換は読みの長さに比例して時間がかかるので、同じ読みの 2 度目を省く。
    /// 学習 (updateLearningData) や学習の消去で結果が変わるので、そのときは捨てる。
    private var resultCache: [String: [Candidate]] = [:]
    private var resultCacheOrder: [String] = []
    private static let resultCacheLimit = 16

    /// ひらがな全体に対する変換結果 (入力をすべて使ったものだけ、よい順)。
    /// Zenzai を使うときは、結果が左文脈で変わるので、左文脈もキャッシュの鍵に入れる (同じ読みを文脈ありとなしで続けて変換して、
    /// 2 度目が 1 度目の結果に当たらないように)。Zenzai を使わないときの鍵は、今までどおり読みだけ。
    private func results(for hiragana: String, context: String? = nil) -> [Candidate] {
        let leftContext = zenzaiActive ? (context ?? "") : nil
        let key = leftContext.map { "Z\u{1}" + $0 + "\u{1}" + hiragana } ?? hiragana
        if let cached = resultCache[key] {
            remember(cached, for: hiragana)
            return cached
        }
        let filtered = rawMainResults(for: hiragana, context: context)
        if resultCacheOrder.count >= MeltypeConverter.resultCacheLimit {
            resultCache.removeValue(forKey: resultCacheOrder.removeFirst())
        }
        resultCacheOrder.append(key)
        resultCache[key] = filtered
        return filtered
    }

    /// Zenzai を、今の要求に使うか: 設定が ON で、モデルが準備でき、このプロセスで止められていない。
    private var zenzaiActive: Bool {
        guard let zenzai, zenzai.isUsable else { return false }
        return zenzaiEnabled()
    }

    /// azooKey に変換を要求する。Zenzai を使うときは、この要求の左文脈を入れたオプションにする (context が空なら左文脈なし)。
    /// ConvertRequestOptions は値型で、複製は軽い (重いのは辞書や学習データの参照ではなく、Zenzai のモデルを読む最初の 1 回だけ)。
    /// prediction が true なら、予測も要求する (通常の変換結果が変わらないよう、予測は予測変換のときだけ .manualMix で要求する)。
    private func requestCandidates(_ composing: ComposingText, reading: String, context: String?, prediction: Bool = false) -> ConversionResult {
        var request = options
        if prediction { request.requireJapanesePrediction = .manualMix }
        lastRequestAt = Date()
        guard zenzaiActive, let zenzai else {
            return inSession(for: reading) { converter.requestCandidates(composing, options: request) }
        }
        request.zenzaiMode = zenzaiMode(weight: zenzai.weightURL, context: context)
        let start = Date()
        let result = inSession(for: reading) { converter.requestCandidates(composing, options: request) }
        noteZenzaiRequest(from: start)
        return result
    }

    /// Zenzai の設定。personalizationMode は既定値が無いので明記する。profile も空 (個人の情報は渡さない)。
    private func zenzaiMode(weight: URL, context: String?) -> ConvertRequestOptions.ZenzaiMode {
        .on(weight: weight, inferenceLimit: Self.zenzaiInferenceLimit, requestRichCandidates: false,
            personalizationMode: nil, versionDependentMode: .v3(.init(profile: "", leftSideContext: context ?? "")))
    }

    /// 直近の変換要求の時刻 (Zenzai の先読みが、入力中を避けるのに使う)。
    private var lastRequestAt = Date.distantPast
    /// 先読みは、最後の変換要求からこの時間 (秒) 以上たっているときだけ (入力の無いとき)。
    private static let zenzaiIdleSeconds = 1.5

    /// Zenzai の準備ができたあと、モデルの読み込み (数百 ms〜数秒、最初の 1 回) を、入力の無いときに先に済ませる。
    /// 読み込みはメインスレッドで起きる (KanaKanjiConverter はスレッドセーフではない) ので、最初のキーの変換で起きると、そのキーが待たされる。
    /// 1 回だけ、最後の変換から zenzaiIdleSeconds 以上あいているときに、ほかのセッションに触れない既定のセッションで 1 語を要求する。
    /// 先読みの要求も noteZenzaiRequest を通るので、読み込めなかったときは同じ安全弁 (このプロセスでは使わない) が働く。
    func warmUpZenzaiIfIdle() {
        guard !zenzaiWarmedUp, zenzaiActive, let zenzai,
              Date().timeIntervalSince(lastRequestAt) >= Self.zenzaiIdleSeconds else { return }
        var composing = ComposingText()
        composing.insertAtCursorPosition("あ", inputStyle: .direct)
        var request = options
        request.zenzaiMode = zenzaiMode(weight: zenzai.weightURL, context: nil)
        let start = Date()
        _ = converter.requestCandidates(composing, options: request)
        converter.stopComposition()
        noteZenzaiRequest(from: start)
    }

    /// Zenzai を使った 1 回の要求の安全弁。モデルを読み込めなかった (azooKey は黙って Zenzai なしの変換に落ちる) か、
    /// 1 キーの変換が遅いことが続いたら、このプロセスでは Zenzai を使わない (IME を起動し直すと戻る)。
    private func noteZenzaiRequest(from start: Date) {
        guard let zenzai else { return }
        let end = Date()
        // 読み込みに失敗すると、azooKey は zenzStatus に理由を足す ("load <URL>    <理由>")。成功のときは足さない。
        // これは azooKey の内部の文字列 (空白 4 つ) に頼った判定で、公開された API ではない。
        // azooKey の版 (Package.swift の revision) を上げたときは、azooKey の KanaKanjiConverter.swift の zenzStatus の組み立て ("load <URL>    <理由>" の行) を見て、今も合うか確かめること。
        if converter.zenzStatus.contains("    ") {
            zenzai.suspend(reason: "モデルを読み込めなかったため一時的に OFF", loadFailed: true)
            return
        }
        // 最初の 1 回はモデルの読み込み (数百 ms〜数秒、1 回だけ) を含むので、遅さの数には入れない
        guard zenzaiWarmedUp else {
            zenzaiWarmedUp = true
            zenzaiLastEnd = end
            return
        }
        if let last = zenzaiLastEnd, start.timeIntervalSince(last) > Self.zenzaiSameKeyGap {
            // 新しいキー。前のキーが遅くなかったら、連続を切る
            if !zenzaiKeyIsSlow { zenzaiSlowStreak = 0 }
            zenzaiKeyTotal = 0
            zenzaiKeyIsSlow = false
        }
        zenzaiKeyTotal += end.timeIntervalSince(start)
        zenzaiLastEnd = end
        if zenzaiKeyTotal > Self.zenzaiSlowSeconds, !zenzaiKeyIsSlow {
            zenzaiKeyIsSlow = true
            zenzaiSlowStreak += 1
            if zenzaiSlowStreak >= Self.zenzaiSlowStreakLimit {
                zenzai.suspend(reason: "変換が遅いため一時的に OFF")
            }
        }
    }

    /// 要求の読みに合うセッションを選んで body を実行する。読みの共通接頭辞が最長のセッション (同点は最近使った方)、
    /// 共通接頭辞が無ければ空のセッション、無ければ最も古く使ったセッション。従来モードは既定セッションで body を実行する。
    private func inSession<T>(for reading: String, _ body: () -> T) -> T {
        guard incremental else {
            defer { converter.stopComposition() }
            return body()
        }
        let target = Array(reading)
        // 直前に使ったスロットの読みの真の接頭辞 (短くて先頭が同じ) が来たら、そのスロットは避ける。
        // 同じキーの中で、読み全体の変換のあとに先頭の文節の変換が来ると、読み全体のスロットが短い読みで上書きされ、
        // 次のキーの予測で後ろ半分を計算し直すことになる。Backspace は直前に使ったのが文節のスロットなので、読み全体のスロットがそのまま選ばれる。
        let latest = slots.indices.max(by: { slots[$0].used < slots[$1].used })!
        let latestReading = Array(slots[latest].lastReading)
        let avoid: Int? = slots[latest].used > 0 && target.count < latestReading.count && Array(latestReading[0..<target.count]) == target ? latest : nil
        var best = -1
        var bestPrefix = -1
        for (index, slot) in slots.enumerated() where index != avoid {
            var common = 0
            for (a, b) in zip(target, slot.lastReading) { if a == b { common += 1 } else { break } }
            if common > bestPrefix || (common == bestPrefix && slot.used > slots[best].used) { best = index; bestPrefix = common }
        }
        if bestPrefix == 0 {
            let others = slots.indices.filter { $0 != avoid }
            best = others.first(where: { slots[$0].lastReading.isEmpty }) ?? others.min(by: { slots[$0].used < slots[$1].used })!
        }
        useClock += 1
        slots[best].lastReading = reading
        slots[best].used = useClock
        // withSession が投げるのは未知のセッションのときだけ (起きない)。そのときは既定セッションで実行する。
        do {
            return try converter.withSession(slots[best].id, operation: body)
        } catch {
            return body()
        }
    }

    /// キャッシュ (resultCache) を通さずに azooKey を呼んだ、入力をすべて使った変換結果 (よい順)。
    func rawMainResults(for hiragana: String, context: String? = nil) -> [Candidate] {
        var composing = ComposingText()
        composing.insertAtCursorPosition(hiragana, inputStyle: .direct)
        let results = requestCandidates(composing, reading: hiragana, context: context)
        let count = hiragana.count
        // 入力 (ひらがな) をすべて使った候補だけ (.direct で入れたので、入力の文字数 = ひらがなの文字数)
        let filtered = results.mainResults.filter { $0.composingCount == .inputCount(count) || $0.composingCount == .surfaceCount(count) }
        remember(filtered, for: hiragana)
        return filtered
    }

    /// azooKey の学習データ (メモリ上と学習のファイル) をすべて消す (メインスレッドから呼ぶ)。azooKey 側のユーザー辞書は消さない。
    func resetLearning() {
        converter.resetMemory()
        endComposition()
        resultCache.removeAll()
        resultCacheOrder.removeAll()
        recentCandidates.removeAll()
        recentOrder.removeAll()
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
        if learned {
            converter.commitUpdateLearningData()
            // 学習で候補の並びが変わるので、覚えていた変換結果は捨てる。
            resultCache.removeAll()
            resultCacheOrder.removeAll()
        }
        // 学習で辞書側の重みが変わるので、覚えていた lattice も捨てる (直前の endComposition のあとなので通常は空)。
        endComposition()
    }

    /// azooKey の組版の状態 (すべてのセッションの前回の入力と lattice) を捨てる。次の変換は全計算から始まる。
    /// 変換ボックスが閉じたとき・学習したとき・学習を消したときに呼ぶ (冪等)。1 キーごとには呼ばないこと: 差分変換が効かなくなる。
    func endComposition() {
        converter.stopComposition()
        for index in slots.indices {
            _ = try? converter.withSession(slots[index].id) { converter.stopComposition() }
            slots[index].lastReading = ""
        }
    }

    /// 文節に区切った変換結果 (読みをつなげると元のひらがなになる)。変換できなければ空。
    func clauses(for hiragana: String, context: String?) -> [(reading: String, text: String)] {
        guard let best = results(for: hiragana, context: context).first else { return [] }
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
    func predictions(for hiragana: String) -> [String] {
        var composing = ComposingText()
        composing.insertAtCursorPosition(hiragana, inputStyle: .direct)
        // 予測は左文脈なしで要求する (実験と同じ。1 キーの 95% 61 ms は、この呼び方で測った値)
        let results = requestCandidates(composing, reading: hiragana, context: nil, prediction: true)
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
