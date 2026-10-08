// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import Foundation

/// 検索用の文字のそろえ方: カタカナ → ひらがな、全角英数 → 半角、英字は小文字 (読みはひらがななので、カタカナで探しても見つかるように)。
public enum SearchText {
    public static func normalize(_ text: String) -> String {
        var scalars = String.UnicodeScalarView()
        for scalar in text.unicodeScalars {
            var value = scalar.value
            if (0x30A1...0x30F6).contains(value) { value -= 0x60 }            // ァ〜ヶ → ぁ〜ゖ
            else if (0xFF01...0xFF5E).contains(value) { value -= 0xFEE0 }     // ！〜～ → !〜~
            if (0x41...0x5A).contains(value) { value += 0x20 }                // A〜Z → a〜z
            scalars.append(Unicode.Scalar(value) ?? scalar)
        }
        return String(scalars)
    }
}

/// 一覧の 1 行 (ユーザー辞書の語・専門用語)。
public protocol ListRow {
    var reading: String { get }
    var word: String { get }
    var order: Int { get }
    /// 読み・語のほかに検索の対象にする文字 (専門用語の注記)。
    var extraSearchText: String { get }
    /// 状態の列で並べるときの順 (0 = ふつう、1 = 除外中)。
    var stateRank: Int { get }
}

extension UserEntry: ListRow {
    public var extraSearchText: String { "" }
    public var stateRank: Int { 0 }
}

extension TermWord: ListRow {
    public var extraSearchText: String { note }
    public var stateRank: Int { excluded ? 1 : 0 }
}

/// 並べ替えの列。
public enum SortColumn: String, CaseIterable {
    case order, reading, word, note, state
}

public struct SortSpec: Equatable {
    public var column: SortColumn
    public var ascending: Bool

    public init(column: SortColumn, ascending: Bool) {
        self.column = column
        self.ascending = ascending
    }
}

/// 一覧の中身: 全部の行と、検索・絞り込み・並べ替えをした見える行。数万行でも 1 回の検索・並べ替えが軽いよう、
/// 検索・並べ替えの鍵を読み込んだときに 1 回だけ作る (比べるのは UTF-8 のバイト列 = 文字コードの順。ひらがなの読みは五十音順になる)。
public final class RowList<Row: ListRow> {
    public private(set) var all: [Row] = []
    public private(set) var visible: [Row] = []

    /// 検索の文字 (読み・語・注記のどこかに含まれる行だけを見せる。カタカナ・全角・大文字は SearchText でそろえる)。
    public var query = ""
    public var sort: SortSpec
    /// 絞り込み (専門用語の「除外した語だけ」など)。nil なら全部。
    public var include: ((Row) -> Bool)?

    private var searchKeys: [String] = []
    private var readingNorm: [String] = []
    private var wordNorm: [String] = []
    private var readingKeys: [[UInt8]] = []
    private var wordKeys: [[UInt8]] = []
    private var noteKeys: [[UInt8]] = []

    public init(sort: SortSpec) {
        self.sort = sort
    }

    /// 全部の行を入れ替えて、検索・並べ替えをし直す。
    public func replace(_ rows: [Row]) {
        all = rows
        searchKeys = rows.map { SearchText.normalize("\($0.reading)\u{1}\($0.word)\u{1}\($0.extraSearchText)") }
        readingNorm = rows.map { SearchText.normalize($0.reading) }
        wordNorm = rows.map { SearchText.normalize($0.word) }
        readingKeys = readingNorm.map { Array($0.utf8) }
        wordKeys = wordNorm.map { Array($0.utf8) }
        noteKeys = rows.map { Array($0.extraSearchText.utf8) }
        apply()
    }

    /// 検索の文字を空白 (半角・全角) で区切ったキーワード。すべて含む行だけが残る (AND)。
    public static func keywords(_ query: String) -> [String] {
        SearchText.normalize(query).components(separatedBy: .whitespacesAndNewlines).filter { !$0.isEmpty }
    }

    /// 関連度 (小さいほど上): 0 = 読みか語が完全一致、1 = 読みの前方一致、2 = 語の前方一致、3 = 読み・語の途中に含む、4 = 注記だけに含む。
    /// キーワードが複数なら、それぞれの値の合計。
    private func relevance(_ index: Int, _ words: [String]) -> Int {
        var total = 0
        for word in words {
            let reading = readingNorm[index], text = wordNorm[index]
            if reading == word || text == word { total += 0 }
            else if reading.hasPrefix(word) { total += 1 }
            else if text.hasPrefix(word) { total += 2 }
            else if reading.contains(word) || text.contains(word) { total += 3 }
            else { total += 4 }
        }
        return total
    }

    /// 検索・絞り込み・並べ替えをし直す (query・sort・include を変えたあとに呼ぶ)。
    public func apply() {
        let words = Self.keywords(query)
        var indices = all.indices.filter { index in
            guard include?(all[index]) ?? true else { return false }
            return words.allSatisfy { searchKeys[index].contains($0) }
        }
        let ascending = sort.ascending
        let column = sort.column
        // 検索中で、並べ替えの列を選んでいない (既定の「登録順」のまま) ときだけ、関連度の順を先にする。
        // 列を選んでいる間は、その指定を優先する (利用者が自分で選んだ並びを検索で勝手に崩さない)。
        // 関連度が同じ行は、従来どおり登録順。
        let scores: [Int]? = (!words.isEmpty && column == .order) ? all.indices.map { relevance($0, words) } : nil
        indices.sort { a, b in
            if let scores, scores[a] != scores[b] { return scores[a] < scores[b] }
            let order: Bool?
            switch column {
            case .order: order = nil
            case .reading: order = Self.compare(readingKeys[a], readingKeys[b])
            case .word: order = Self.compare(wordKeys[a], wordKeys[b])
            case .note: order = Self.compare(noteKeys[a], noteKeys[b])
            case .state: order = all[a].stateRank == all[b].stateRank ? nil : all[a].stateRank < all[b].stateRank
            }
            // 同じなら登録順 (ファイルの順) で、並べ替えても順が揺れないようにする
            guard let order else { return ascending ? all[a].order < all[b].order : all[a].order > all[b].order }
            return ascending ? order : !order
        }
        visible = indices.map { all[$0] }
    }

    /// a が b より前なら true、後なら false、同じなら nil。
    private static func compare(_ a: [UInt8], _ b: [UInt8]) -> Bool? {
        if a == b { return nil }
        return a.lexicographicallyPrecedes(b)
    }
}
