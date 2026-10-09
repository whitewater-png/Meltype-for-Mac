// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import Foundation

/// 読みと語の組 (ユーザー辞書・専門用語集の 1 語を見分ける鍵。同じ組は 1 つしか登録できない)。
public struct WordKey: Hashable, CustomStringConvertible {
    public let reading: String
    public let word: String

    public init(reading: String, word: String) {
        self.reading = reading
        self.word = word
    }

    public var description: String { "\(reading)\t\(word)" }
}

/// ユーザー辞書の 1 語。order はファイルの中の位置 (0 が最も古い。大きいほど新しく登録した)。
public struct UserEntry: Hashable {
    public let key: WordKey
    public let order: Int

    public init(key: WordKey, order: Int) {
        self.key = key
        self.order = order
    }

    public var reading: String { key.reading }
    public var word: String { key.word }
}

/// 消した語と、消す前の位置 (元に戻すときに、本体へそのまま返す)。
public struct RemovedEntry: Hashable {
    public let index: Int
    public let key: WordKey

    public init(index: Int, key: WordKey) {
        self.index = index
        self.key = key
    }
}

/// 専門用語集の分野。
public struct TermDomainInfo: Hashable {
    public let id: String
    public let name: String
    public let count: Int
    public let enabled: Bool
    /// 利用者が自分で作った分野 (語を足し・直し・消せる。同梱の分野は読み取り専用)。
    public let isUser: Bool

    public init(id: String, name: String, count: Int, enabled: Bool, isUser: Bool = false) {
        self.id = id
        self.name = name
        self.count = count
        self.enabled = enabled
        self.isUser = isUser
    }
}

/// 自作の専門用語集から消した語と、消す前の位置 (元に戻すときに、本体へそのまま返す)。
public struct RemovedTerm: Hashable {
    public let index: Int
    public let key: WordKey
    public let note: String

    public init(index: Int, key: WordKey, note: String = "") {
        self.index = index
        self.key = key
        self.note = note
    }
}

/// 専門用語集に入れられなかった語と、その理由。
public struct SkippedWord: Hashable {
    public let key: WordKey
    public let reason: String

    public init(key: WordKey, reason: String) {
        self.key = key
        self.reason = reason
    }
}

/// ユーザー辞書から自作の専門用語集へ移した結果。removed はユーザー辞書から消した語 (元の位置つき)、added は専門用語集に新しく足した語。
public struct MoveOutcome: Hashable {
    public var removed: [RemovedEntry]
    public var added: [WordKey]
    public var skipped: [SkippedWord]

    public init(removed: [RemovedEntry] = [], added: [WordKey] = [], skipped: [SkippedWord] = []) {
        self.removed = removed
        self.added = added
        self.skipped = skipped
    }
}

/// 消した自作の専門用語集 (元に戻すために、中身と、有効だったかを持つ)。
public struct DeletedDomain: Hashable {
    public let id: String
    public let name: String
    public let content: String
    public let wasEnabled: Bool

    public init(id: String, name: String, content: String, wasEnabled: Bool) {
        self.id = id
        self.name = name
        self.content = content
        self.wasEnabled = wasEnabled
    }
}

/// 自作の専門用語集の取り込みの結果。
public struct DomainImportSummary: Hashable {
    public let id: String
    public let name: String
    public let added: Int
    public let skipped: Int
    public let duplicates: Int

    public init(id: String, name: String, added: Int, skipped: Int, duplicates: Int) {
        self.id = id
        self.name = name
        self.added = added
        self.skipped = skipped
        self.duplicates = duplicates
    }

    public var message: String {
        var lines = ["専門用語集「\(name)」を作り、\(added) 語を取り込みました (すぐ有効です)。"]
        if skipped > 0 { lines.append("読みがかなでない・短すぎるなどで飛ばした行: \(skipped)") }
        if duplicates > 0 { lines.append("重複して省いた語: \(duplicates)") }
        return lines.joined(separator: "\n")
    }
}

/// 専門用語集の 1 語。order はファイルの中の順、excluded は利用者が除外した (変換に使わない) 語。
public struct TermWord: Hashable {
    public let key: WordKey
    public let note: String
    public let excluded: Bool
    public let order: Int

    public init(key: WordKey, note: String, excluded: Bool, order: Int) {
        self.key = key
        self.note = note
        self.excluded = excluded
        self.order = order
    }

    public var reading: String { key.reading }
    public var word: String { key.word }
}

/// 取り込みの結果。
public struct ImportSummary: Hashable {
    public let added: Int
    public let duplicates: Int
    public let skipped: Int
    public let encoding: String

    public init(added: Int, duplicates: Int, skipped: Int, encoding: String) {
        self.added = added
        self.duplicates = duplicates
        self.skipped = skipped
        self.encoding = encoding
    }

    /// 画面に出す文。
    public var message: String {
        var lines = ["\(added) 語を登録しました。"]
        if duplicates > 0 { lines.append("登録済み・登録できない語: \(duplicates)") }
        if skipped > 0 { lines.append("読みがかなでない・短すぎるなどで飛ばした行: \(skipped)") }
        lines.append("文字コード: \(encoding)")
        return lines.joined(separator: "\n")
    }
}

/// 本体 (Exports.cs) とやり取りする文字列の形式。1 行 1 件、欄は Tab 区切り (読み・語には Tab・改行が入らない)。
public enum FfiFormat {
    private static func lines(_ text: String) -> [Substring] {
        text.split(separator: "\n", omittingEmptySubsequences: true)
    }

    private static func fields(_ line: Substring) -> [Substring] {
        line.split(separator: "\t", omittingEmptySubsequences: false)
    }

    /// 「読み\t単語」の行 → ユーザー辞書の語 (順番つき)。
    public static func parseUserEntries(_ text: String) -> [UserEntry] {
        parseKeys(text).enumerated().map { UserEntry(key: $0.element, order: $0.offset) }
    }

    /// 「読み\t語」の行 → 鍵 (欄が足りない行・読みが空の行は飛ばす)。
    public static func parseKeys(_ text: String) -> [WordKey] {
        lines(text).compactMap { line in
            let parts = fields(line)
            guard parts.count >= 2, !parts[0].isEmpty else { return nil }
            return WordKey(reading: String(parts[0]), word: String(parts[1]))
        }
    }

    public static func formatKeys(_ keys: [WordKey]) -> String {
        keys.map { "\($0.reading)\t\($0.word)" }.joined(separator: "\n")
    }

    /// 「位置\t読み\t単語」の行 → 消した語。
    public static func parseRemoved(_ text: String) -> [RemovedEntry] {
        lines(text).compactMap { line in
            let parts = fields(line)
            guard parts.count >= 3, let index = Int(parts[0]), index >= 0 else { return nil }
            return RemovedEntry(index: index, key: WordKey(reading: String(parts[1]), word: String(parts[2])))
        }
    }

    public static func formatRemoved(_ entries: [RemovedEntry]) -> String {
        entries.map { "\($0.index)\t\($0.key.reading)\t\($0.key.word)" }.joined(separator: "\n")
    }

    /// 「ID\t名称\t語数\t有効なら 1\t自作なら 1」の行 → 分野。5 つ目の欄は無くてもよい (古い本体の形)。欄が増えても読める。
    public static func parseDomains(_ text: String) -> [TermDomainInfo] {
        lines(text).compactMap { line in
            let parts = fields(line)
            guard parts.count >= 4, let count = Int(parts[2]) else { return nil }
            return TermDomainInfo(id: String(parts[0]), name: String(parts[1]), count: count, enabled: parts[3] == "1", isUser: parts.count >= 5 && parts[4] == "1")
        }
    }

    /// 「読み\t語\t理由」の行 → 入れられなかった語。
    public static func parseSkipped(_ text: String) -> [SkippedWord] {
        lines(text).compactMap { line in
            let parts = fields(line)
            guard parts.count >= 3, !parts[0].isEmpty else { return nil }
            return SkippedWord(key: WordKey(reading: String(parts[0]), word: String(parts[1])), reason: String(parts[2]))
        }
    }

    /// 「位置\t読み\t語\t注記」の行 → 自作の専門用語集から消した語。
    public static func parseRemovedTerms(_ text: String) -> [RemovedTerm] {
        lines(text).compactMap { line in
            let parts = fields(line)
            guard parts.count >= 3, let index = Int(parts[0]), index >= 0 else { return nil }
            return RemovedTerm(index: index, key: WordKey(reading: String(parts[1]), word: String(parts[2])), note: parts.count >= 4 ? String(parts[3]) : "")
        }
    }

    public static func formatRemovedTerms(_ entries: [RemovedTerm]) -> String {
        entries.map { "\($0.index)\t\($0.key.reading)\t\($0.key.word)\t\($0.note)" }.joined(separator: "\n")
    }

    /// 「取り込んだ語数\t飛ばした行数\t重複して省いた数\t名前」(+ ID) → 自作の専門用語集の取り込みの結果。
    public static func parseDomainImportSummary(_ text: String, id: String) -> DomainImportSummary? {
        let parts = text.split(separator: "\t", omittingEmptySubsequences: false)
        guard parts.count >= 4, let added = Int(parts[0]), let skipped = Int(parts[1]), let duplicates = Int(parts[2]) else { return nil }
        return DomainImportSummary(id: id, name: String(parts[3]), added: added, skipped: skipped, duplicates: duplicates)
    }

    /// 「読み\t語\t注記\t除外なら 1」の行 → 専門用語。
    public static func parseTermWords(_ text: String) -> [TermWord] {
        var words: [TermWord] = []
        words.reserveCapacity(text.utf8.count / 24)
        for line in lines(text) {
            let parts = fields(line)
            guard parts.count >= 4, !parts[0].isEmpty else { continue }
            words.append(TermWord(key: WordKey(reading: String(parts[0]), word: String(parts[1])), note: String(parts[2]), excluded: parts[3] == "1", order: words.count))
        }
        return words
    }

    /// 「登録した数\t登録済み\t飛ばした行\t文字コード」→ 取り込みの結果。
    public static func parseImportSummary(_ text: String) -> ImportSummary? {
        let parts = text.split(separator: "\t", omittingEmptySubsequences: false)
        guard parts.count == 4, let added = Int(parts[0]), let duplicates = Int(parts[1]), let skipped = Int(parts[2]) else { return nil }
        return ImportSummary(added: added, duplicates: duplicates, skipped: skipped, encoding: String(parts[3]))
    }
}
