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

    public init(id: String, name: String, count: Int, enabled: Bool) {
        self.id = id
        self.name = name
        self.count = count
        self.enabled = enabled
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

    /// 「ID\t名称\t語数\t有効なら 1」の行 → 分野。
    public static func parseDomains(_ text: String) -> [TermDomainInfo] {
        lines(text).compactMap { line in
            let parts = fields(line)
            guard parts.count == 4, let count = Int(parts[2]) else { return nil }
            return TermDomainInfo(id: String(parts[0]), name: String(parts[1]), count: count, enabled: parts[3] == "1")
        }
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
