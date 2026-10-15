// 英字の候補の意味を macOS の辞書 (Dictionary Services) から引く。Core に意味が無いときの補い。
// 辞書は端末内にあり、通信はしない。語も定義も、ログやトレースには書かない。
import CoreServices
import Foundation

enum CandidateDictionary {
    /// 引いてよい語か: ASCII の英字で始まり、英字・`-`・`'` だけの 2 文字以上。
    static func isLookupWord(_ word: String) -> Bool {
        guard word.count >= 2, let first = word.unicodeScalars.first, isAsciiLetter(first) else { return false }
        return word.unicodeScalars.allSatisfy { isAsciiLetter($0) || $0 == "-" || $0 == "'" }
    }

    private static func isAsciiLetter(_ scalar: Unicode.Scalar) -> Bool {
        (scalar.value >= 0x41 && scalar.value <= 0x5A) || (scalar.value >= 0x61 && scalar.value <= 0x7A)
    }

    /// 辞書の定義の先頭の 1 文 (最初の改行・「。」・". " まで) を、空白をつめて maxLength 文字ほどで切る。空なら nil。
    static func summary(of definition: String, maxLength: Int = 80) -> String? {
        var head = Substring(definition)
        if let end = head.firstIndex(where: { $0 == "\n" || $0 == "\r" || $0 == "。" }) { head = head[..<end] }
        if let end = head.range(of: ". ")?.lowerBound { head = head[..<end] }
        let collapsed = head.split(whereSeparator: { $0.isWhitespace }).joined(separator: " ")
        guard !collapsed.isEmpty else { return nil }
        return collapsed.count > maxLength ? String(collapsed.prefix(maxLength)) + "…" : collapsed
    }

    /// 辞書を引く専用の直列キュー (DCS は遅いことがあるので、メインスレッドでは呼ばない)。cache もこのキューの中でだけ触る。
    private static let queue = DispatchQueue(label: "app.meltype.candidate-dictionary", qos: .utility)
    private static let cacheLimit = 200
    private static var cache: [String: String?] = [:]
    private static var cacheOrder: [String] = []

    /// 語の意味を引き、メインスレッドで completion を呼ぶ。ShowCandidateMeanings が OFF・語が対象外・辞書に無いときは nil。
    static func lookup(_ word: String, completion: @escaping (String?) -> Void) {
        guard isLookupWord(word) else { return completion(nil) }
        queue.async {
            let result: String? = NativeCore.shared.showCandidateMeanings ? cachedSummary(word) : nil
            DispatchQueue.main.async { completion(result) }
        }
    }

    private static func cachedSummary(_ word: String) -> String? {
        if let hit = cache[word] { return hit }
        let summary = define(word).flatMap { summary(of: $0) }
        cache[word] = .some(summary)
        cacheOrder.append(word)
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
