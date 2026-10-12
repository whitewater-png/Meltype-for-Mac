// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import Foundation

/// 学習した語 (languages.json の 1 語)。打った英字を、英字のまま (english) / かなにする と覚えた語。
/// active は今効いているか (よく使う日本語に見える語は、2 回目を待ってから効く)。
public struct LearnedWord: Hashable {
    public let word: String
    public let english: Bool
    public let count: Int
    /// 最後に使った時刻。読めなかったとき・既定値 (0001 年) のときは distantPast (画面では「不明」)。
    public let used: Date
    public let active: Bool

    public init(word: String, english: Bool, count: Int, used: Date, active: Bool) {
        self.word = word
        self.english = english
        self.count = count
        self.used = used
        self.active = active
    }
}

/// 学習した語の操作 (本物は NativeDictionary。--self-test は偽物でも確かめる)。理由の文字列は、だめなときだけ (画面にそのまま出す)。
public protocol LearnedWordOperations: AnyObject {
    /// 学習した語 (新しく使った順)。取れなければ nil。
    func learnedWords() -> [LearnedWord]?
    /// 学習した語の版 (IME が覚えた・消したときも変わる)。取れなければ -1。
    func learnedVersion() -> Int32
    /// 足してよいか・直してよいか (保存はしない)。except は編集中の元の語。
    func checkLearned(_ word: String, except: String?) -> String?
    /// 足す (oldWord が nil) か直す。手で足した語・直した語は、2 回目を待たずにすぐ効く。
    func setLearned(_ word: String, english: Bool, oldWord: String?) -> String?
    func removeLearned(_ words: [String]) -> String?
}

extension FfiFormat {
    /// 「語\tE|J\t回数\t最後に使った時刻 (ISO 8601)\t有効なら 1」の行 → 学習した語。欄が足りない・扱いが E/J でない・回数が数でない行は飛ばす。
    /// 時刻だけ読めない行は残す (その語を消せるように)。
    public static func parseLearnedWords(_ text: String) -> [LearnedWord] {
        text.split(separator: "\n", omittingEmptySubsequences: true).compactMap { line in
            let parts = line.split(separator: "\t", omittingEmptySubsequences: false)
            guard parts.count >= 5, !parts[0].isEmpty, parts[1] == "E" || parts[1] == "J", let count = Int(parts[2]) else { return nil }
            return LearnedWord(word: String(parts[0]), english: parts[1] == "E", count: count,
                               used: parseIsoDate(String(parts[3])) ?? .distantPast, active: parts[4] == "1")
        }
    }

    /// 消す語を 1 行 1 語にする。
    public static func formatWords(_ words: [String]) -> String {
        words.joined(separator: "\n")
    }

    /// C# の DateTime.ToString("o") (例 2026-10-12T03:04:05.1234567Z・2026-10-12T12:04:05.1234567+09:00) を読む。
    /// 一覧の行ごとに呼ぶ (多いと数千行) ので、Formatter は作らず手で分ける。小数の秒は何桁でもよい。時差が無ければ UTC とみなす。
    /// 年が 1 以下 (C# の既定値 0001-01-01) は「使った日が無い」として nil (画面では「不明」)。形が違えば nil。
    public static func parseIsoDate(_ text: String) -> Date? {
        let s = Array(text.trimmingCharacters(in: .whitespaces).utf8)
        guard s.count >= 19, s[4] == UInt8(ascii: "-"), s[7] == UInt8(ascii: "-"), s[10] == UInt8(ascii: "T"),
              s[13] == UInt8(ascii: ":"), s[16] == UInt8(ascii: ":") else { return nil }
        func number(_ start: Int, _ length: Int) -> Int? {
            var value = 0
            for index in start..<(start + length) {
                let byte = s[index]
                guard byte >= UInt8(ascii: "0"), byte <= UInt8(ascii: "9") else { return nil }
                value = value * 10 + Int(byte - UInt8(ascii: "0"))
            }
            return value
        }
        guard let year = number(0, 4), let month = number(5, 2), let day = number(8, 2),
              let hour = number(11, 2), let minute = number(14, 2), let second = number(17, 2),
              year > 1, (1...12).contains(month), day >= 1, day <= daysInMonth(year, month),
              hour < 24, minute < 60, second < 60 else { return nil }
        var index = 19
        var fraction = 0.0
        if index < s.count, s[index] == UInt8(ascii: ".") {
            index += 1
            var scale = 0.1
            let start = index
            while index < s.count, s[index] >= UInt8(ascii: "0"), s[index] <= UInt8(ascii: "9") {
                fraction += Double(s[index] - UInt8(ascii: "0")) * scale
                scale /= 10
                index += 1
            }
            guard index > start else { return nil }
        }
        var offset = 0
        if index < s.count {
            let sign = s[index]
            if sign == UInt8(ascii: "Z"), index + 1 == s.count {
                offset = 0
            } else if sign == UInt8(ascii: "+") || sign == UInt8(ascii: "-"), s.count == index + 6, s[index + 3] == UInt8(ascii: ":"),
                      let hours = number(index + 1, 2), let minutes = number(index + 4, 2), hours < 24, minutes < 60 {
                offset = (hours * 3600 + minutes * 60) * (sign == UInt8(ascii: "-") ? -1 : 1)
            } else {
                return nil
            }
        }
        let seconds = daysFromCivil(year, month, day) * 86_400 + hour * 3600 + minute * 60 + second - offset
        return Date(timeIntervalSince1970: Double(seconds) + fraction)
    }

    private static func daysInMonth(_ year: Int, _ month: Int) -> Int {
        switch month {
        case 2: return (year % 4 == 0 && year % 100 != 0) || year % 400 == 0 ? 29 : 28
        case 4, 6, 9, 11: return 30
        default: return 31
        }
    }

    /// 1970-01-01 からの日数 (グレゴリオ暦。H. Hinnant の days_from_civil)。
    private static func daysFromCivil(_ year: Int, _ month: Int, _ day: Int) -> Int {
        let y = month <= 2 ? year - 1 : year
        let era = (y >= 0 ? y : y - 399) / 400
        let yearOfEra = y - era * 400
        let dayOfYear = (153 * ((month + 9) % 12) + 2) / 5 + day - 1
        let dayOfEra = yearOfEra * 365 + yearOfEra / 4 - yearOfEra / 100 + dayOfYear
        return era * 146_097 + dayOfEra - 719_468
    }
}

/// 学習した語の一覧の並べ替えの列。
public enum LearnedSortColumn: String, CaseIterable {
    case word, english, count, used, state
}

/// 学習した語の一覧: 全部の行と、検索・並べ替えをした見える行 (語は数百〜数千なので、毎回並べ直してよい)。
public final class LearnedWordList {
    public private(set) var all: [LearnedWord] = []
    public private(set) var visible: [LearnedWord] = []
    /// 検索の文字 (語に含まれる行だけを見せる。大文字・全角は SearchText でそろえる)。
    public var query = ""
    public var column: LearnedSortColumn
    public var ascending: Bool

    public init(column: LearnedSortColumn = .used, ascending: Bool = false) {
        self.column = column
        self.ascending = ascending
    }

    public func replace(_ rows: [LearnedWord]) {
        all = rows
        apply()
    }

    public func apply() {
        let needle = SearchText.normalize(query).trimmingCharacters(in: .whitespacesAndNewlines)
        let rows = needle.isEmpty ? all : all.filter { SearchText.normalize($0.word).contains(needle) }
        let column = column, ascending = ascending
        visible = rows.sorted { a, b in
            let order: Bool?
            switch column {
            case .word: order = a.word == b.word ? nil : a.word < b.word
            case .english: order = a.english == b.english ? nil : a.english && !b.english   // 英字が先
            case .count: order = a.count == b.count ? nil : a.count < b.count
            case .used: order = a.used == b.used ? nil : a.used < b.used
            case .state: order = a.active == b.active ? nil : a.active && !b.active        // 有効が先
            }
            // 同じなら新しく使った順、それも同じなら語の順 (並べ替えても順が揺れないように)
            guard let order else { return a.used != b.used ? a.used > b.used : a.word < b.word }
            return ascending ? order : !order
        }
    }
}
