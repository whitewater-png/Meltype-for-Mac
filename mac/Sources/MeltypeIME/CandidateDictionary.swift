// 英字の候補の意味を macOS の辞書 (Dictionary Services) から引く。Core に意味が無いときの補い。
// 辞書は端末内にあり、通信はしない。語も定義も、ログやトレースには書かない。
import CoreServices
import Foundation

enum CandidateDictionary {
    /// 引く前の整え: 全角英字などを NFKC で半角に、`’` を `'` に。
    static func normalize(_ word: String) -> String {
        word.precomposedStringWithCompatibilityMapping.replacingOccurrences(of: "\u{2019}", with: "'")
    }

    /// 引いてよい語か: (整えたあと) ASCII の英字で始まり、英字・`-`・`'` だけの 2 文字以上。
    static func isLookupWord(_ raw: String) -> Bool {
        let word = normalize(raw)
        guard word.count >= 2, let first = word.unicodeScalars.first, isAsciiLetter(first) else { return false }
        return word.unicodeScalars.allSatisfy { isAsciiLetter($0) || $0 == "-" || $0 == "'" }
    }

    private static func isAsciiLetter(_ scalar: Unicode.Scalar) -> Bool {
        (scalar.value >= 0x41 && scalar.value <= 0x5A) || (scalar.value >= 0x61 && scalar.value <= 0x7A)
    }

    /// 辞書の定義 (DCSCopyTextDefinition の先頭) から、引いた語 word の意味を 1 文だけ取り出す。
    /// 次のどちらかの形のときだけ出す。どちらでもなければ nil (別の語・別の意味を出さない)。比べるときは大文字小文字を区別せず、
    /// 定義側の `･`・`·`・`•` (音節の区切り) は取り除く。
    ///  (A) 国語辞典の外来語: `見出し␣` + 数字 0 個以上 + word で始まる本文。表示は `見出し：本文の残り`。
    ///      例 (このMacでの実測): meeting「ミーティング 0meeting打ち合わせや連絡のための会合。」→「ミーティング：打ち合わせや連絡のための会合」
    ///      error「エラー 1error（名）スル ① やり損なうこと。…」→「エラー：（名）スル ① やり損なうこと」
    ///      apple「アップル 1appleりんご。…」→「アップル：りんご」/ deploy「デプロイ 3deploy →デプロイメント」→「デプロイ：→デプロイメント」
    ///  (B) 英和・英英: 定義の先頭が word で、その直後が ` |`。表示は 2 つ目の `|` の後ろ。
    ///      例: Tokyo「To･ky･o | tóʊkiòʊ | 名詞東京.」→「名詞東京.」
    ///  どちらにも当たらない例: river「R. ① 〖ratio〗数学で「比」…」/ api「API 〖American Petroleum Institute〗…」/
    ///  Einstein「アインシュタイン Albert Einstein［1879〜1955］…」(人名の項目)。
    /// 最初の改行・「。」・「②」の手前で切り、空白をつめ、maxLength 文字ほどで切って「…」。
    static func summary(of definition: String, word: String, maxLength: Int = 50) -> String? {
        let key = normalize(word).lowercased()
        guard !key.isEmpty else { return nil }
        let text = definition.filter { $0 != "\u{FF65}" && $0 != "\u{B7}" && $0 != "\u{2022}" }
        guard let body = formB(text, key) ?? formA(text, key) else { return nil }
        var head = Substring(body)
        if let end = head.firstIndex(where: { $0 == "\n" || $0 == "\r" || $0 == "。" || $0 == "②" }) { head = head[..<end] }
        let collapsed = head.split(whereSeparator: { $0.isWhitespace }).joined(separator: " ")
        guard !collapsed.isEmpty else { return nil }
        return collapsed.count > maxLength ? String(collapsed.prefix(maxLength)) + "…" : collapsed
    }

    /// `word` の直後が英字なら別の語 (running など) なので不一致。`-`・`'` の次が英字のときも別の語 (run-up・don't。R55 任意 1)。
    private static func startsWithWord(_ text: Substring, _ key: String) -> Substring? {
        guard text.lowercased().hasPrefix(key) else { return nil }
        let rest = text.dropFirst(key.count)
        let scalars = Array(rest.unicodeScalars.prefix(2))
        if let next = scalars.first {
            if isAsciiLetter(next) { return nil }
            if (next == "-" || next == "'"), scalars.count > 1, isAsciiLetter(scalars[1]) { return nil }
        }
        return rest
    }

    private static func formA(_ text: String, _ key: String) -> String? {
        guard let space = text.firstIndex(of: " ") else { return nil }
        let headword = text[..<space]
        guard !headword.isEmpty else { return nil }
        let afterDigits = text[text.index(after: space)...].drop(while: { $0.isASCII && $0.isNumber })
        guard let rest = startsWithWord(afterDigits, key) else { return nil }
        var body = rest.trimmingCharacters(in: .whitespacesAndNewlines)
        // 英英の地名など「Kyoto Kyo·to | kēˈ(y)ōdō | an industrial city…」は、発音 (| … |) の後ろだけを本文にする。
        if body.hasPrefix("|") {
            let parts = body.dropFirst().split(separator: "|", maxSplits: 1, omittingEmptySubsequences: false)
            guard parts.count == 2 else { return nil }
            body = parts[1].trimmingCharacters(in: .whitespacesAndNewlines)
        }
        return body.isEmpty ? nil : "\(headword)：\(body)"
    }

    private static func formB(_ text: String, _ key: String) -> String? {
        guard let rest = startsWithWord(Substring(text), key), rest.hasPrefix(" |") else { return nil }
        let parts = rest.dropFirst(2).split(separator: "|", maxSplits: 1, omittingEmptySubsequences: false)
        return parts.count == 2 ? String(parts[1]) : nil
    }

    /// 辞書を引く専用の直列キュー (DCS は遅いことがあるので、メインスレッドでは呼ばない)。cache もこのキューの中でだけ触る。
    private static let queue = DispatchQueue(label: "app.meltype.candidate-dictionary", qos: .utility)
    private static let cacheLimit = 200
    private static var cache: [String: String?] = [:]
    private static var cacheOrder: [String] = []

    /// 語の意味を引き、メインスレッドで completion を呼ぶ。ShowCandidateMeanings が OFF・語が対象外・辞書に無いときは nil。
    static func lookup(_ raw: String, completion: @escaping (String?) -> Void) {
        guard isLookupWord(raw) else { return completion(nil) }
        let word = normalize(raw)
        queue.async {
            let result: String? = meaningsEnabled() ? cachedSummary(word) : nil
            DispatchQueue.main.async { completion(result) }
        }
    }

    /// ShowCandidateMeanings (config.json を読む) を 2 秒ほどキャッシュする。キューの中でだけ呼ぶ。
    private static var enabledCache: (value: Bool, at: Date)?

    private static func meaningsEnabled() -> Bool {
        if let cached = enabledCache, Date().timeIntervalSince(cached.at) < 2 { return cached.value }
        let value = NativeCore.shared.showCandidateMeanings
        enabledCache = (value, Date())
        return value
    }

    /// キャッシュは整えた語そのものをキーにする。小文字でまとめると、`it` (イット：性的魅力) を先に引いたあとの `IT` に
    /// その意味が出てしまう (辞書は IT と it で別の項目を返す。このMacで実測。R55 推奨 2)。
    private static func cachedSummary(_ word: String) -> String? {
        let key = word
        if let hit = cache[key] { return hit }
        let summary = define(word).flatMap { summary(of: $0, word: word) }
        cache[key] = .some(summary)
        cacheOrder.append(key)
        if cacheOrder.count > cacheLimit { cache[cacheOrder.removeFirst()] = nil }
        return summary
    }

    private static func define(_ word: String) -> String? {
        let length = (word as NSString).length
        guard length > 0,
              let definition = DCSCopyTextDefinition(nil, word as CFString, CFRange(location: 0, length: length)) else { return nil }
        return definition.takeRetainedValue() as String
    }
}
