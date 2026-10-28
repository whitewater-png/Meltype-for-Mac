// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import Foundation

/// 設定タブの項目の種類。
public enum SettingKind: String, Decodable {
    case bool
    case choice
    case int
}

/// 設定の値 (真偽・数・選択肢の名前)。本体とは JSON の値 (true / 3 / "Balanced") でやり取りする。
public enum SettingValue: Hashable, Decodable {
    case bool(Bool)
    case int(Int)
    case string(String)

    public init(from decoder: Decoder) throws {
        let container = try decoder.singleValueContainer()
        if let value = try? container.decode(Bool.self) {
            self = .bool(value)
        } else if let value = try? container.decode(Int.self) {
            self = .int(value)
        } else {
            self = .string(try container.decode(String.self))
        }
    }

    /// 本体に渡す JSON の値。
    public var jsonLiteral: String {
        switch self {
        case let .bool(value): return value ? "true" : "false"
        case let .int(value): return String(value)
        case let .string(value):
            let data = (try? JSONEncoder().encode(value)) ?? Data("\"\"".utf8)
            return String(decoding: data, as: UTF8.self)
        }
    }
}

public struct SettingOption: Hashable, Decodable {
    public let value: String
    public let label: String
}

/// 設定タブの項目 1 つ (本体の MacSettingsCatalog が決める)。グループ・要約・例は Mac だけのもの (Windows の設定画面には無い)。
/// 説明は Windows と同じ文を基にしているが、Mac 向けに書き直したもの (MacDescriptions) もある。
public struct SettingItem: Hashable, Decodable {
    public let key: String
    public let label: String
    public let description: String
    /// 画面に常に出す 1 行の要約 (全文の説明は「詳しく」の中)。古い本体・テストの JSON には無いので省略できる (無ければ displaySummary が説明から作る)。
    public let summary: String?
    /// 要約の下に出す例 (打つもの → 結果。先頭の「例:」は画面が付ける)。無い項目もある。
    public let example: String?
    public let group: String
    public let kind: SettingKind
    public let value: SettingValue
    /// kind == .choice のときの選択肢。
    public let options: [SettingOption]?
    /// kind == .int のときの範囲。
    public let min: Int?
    public let max: Int?

    public init(key: String, label: String, description: String, group: String, kind: SettingKind, value: SettingValue,
                options: [SettingOption]? = nil, min: Int? = nil, max: Int? = nil, summary: String? = nil, example: String? = nil) {
        self.key = key
        self.label = label
        self.description = description
        self.summary = summary
        self.example = example
        self.group = group
        self.kind = kind
        self.value = value
        self.options = options
        self.min = min
        self.max = max
    }

    /// 画面に常に出す 1 行。要約が無ければ (古い本体)、説明の最初の文 (最初の「。」まで)、それも無ければ説明の全文。
    public var displaySummary: String {
        if let summary, !summary.isEmpty { return summary }
        if let end = description.firstIndex(of: "。") { return String(description[...end]) }
        return description
    }
}

/// 設定タブに出す項目と今の値 (meltype_settings_get の JSON)。並びは本体の決めた画面の並び。
public struct SettingsCatalog: Decodable {
    public let items: [SettingItem]

    public init(items: [SettingItem]) { self.items = items }

    public static func parse(_ json: String) -> SettingsCatalog? {
        try? JSONDecoder().decode(SettingsCatalog.self, from: Data(json.utf8))
    }

    /// グループごとの項目 (グループは最初に出てきた順)。
    public var groups: [(name: String, items: [SettingItem])] {
        var result: [(name: String, items: [SettingItem])] = []
        for item in items {
            if let index = result.firstIndex(where: { $0.name == item.group }) {
                result[index].items.append(item)
            } else {
                result.append((item.group, [item]))
            }
        }
        return result
    }
}

/// 設定タブの並べ方のうち、本体の一覧 (JSON) に無いもの: 親子の関係と検索。
/// 画面 (AppKit) から切り離して、--self-test で確かめられるようにしてある。
public enum SettingsLayout {
    /// 子の項目 → 親の項目 (ON / OFF)。親が OFF のあいだ、子は効かないので、画面では字下げして薄くし、操作できなくする。
    /// 鍵を Swift に直に書いているので、本体の一覧から消えたり名前が変わったりしたら --self-test が落ちる (親が真偽の項目であることも確かめる)。
    public static let parents: [String: String] = [
        "PredictionMinLength": "Prediction",
        "DownSelectsPrediction": "Prediction",
        "DictionarySuggestThreshold": "DictionarySuggest",
        "LogTypedText": "FileLog",
    ]

    /// その項目を今操作できるか (親が OFF なら false)。親の値が分からないとき (一覧に無い・読めていない) は操作できる側にする。
    public static func isAvailable(_ key: String, values: [String: SettingValue]) -> Bool {
        guard let parent = parents[key] else { return true }
        return values[parent] != .bool(false)
    }

    /// 検索の文字が、項目の名前・要約・例・説明のどこかに含まれるか。全角英数・大文字・カタカナは SearchText でそろえ、
    /// 空白 (半角・全角) で区切ったキーワードはすべて含むときだけ一致にする (ほかのタブの検索と同じ)。空なら全部が一致。
    public static func matches(_ item: SettingItem, query: String) -> Bool {
        let words = searchWords(query)
        guard !words.isEmpty else { return true }
        let text = SearchText.normalize([item.label, item.summary ?? "", item.example ?? "", item.description].joined(separator: "\u{1}"))
        return words.allSatisfy { text.contains($0) }
    }

    /// 検索の文字が空 (空白だけを含む) か。空なら全部を出し、説明を開く / 閉じるも利用者の状態に戻す。
    public static func isEmptyQuery(_ query: String) -> Bool { searchWords(query).isEmpty }

    /// 一致はするが、画面に常に出ている文字 (名前・要約・例) だけでは一致しない (どれかのキーワードが「詳しく」の中の説明にしか無い) か。
    /// そういう行は、画面が説明を開いて一致した理由を見せる。説明が要約と同じ (「詳しく」が無い) 項目は、説明が常に見えているので false。
    public static func matchesOnlyInDescription(_ item: SettingItem, query: String) -> Bool {
        let words = searchWords(query)
        guard !words.isEmpty, item.description != item.displaySummary, matches(item, query: query) else { return false }
        let visible = SearchText.normalize([item.label, item.displaySummary, item.example ?? ""].joined(separator: "\u{1}"))
        return !words.allSatisfy { visible.contains($0) }
    }

    /// 検索で出す項目の鍵: 一致した項目と、一致した子の親 (薄くした子だけが、何の子か分からないまま出ないように)。
    public static func shownKeys(_ items: [SettingItem], query: String) -> Set<String> {
        let present = Set(items.map(\.key))
        var keys = Set(items.filter { matches($0, query: query) }.map(\.key))
        for key in keys {
            if let parent = parents[key], present.contains(parent) { keys.insert(parent) }
        }
        return keys
    }

    private static func searchWords(_ query: String) -> [String] {
        SearchText.normalize(query).components(separatedBy: .whitespacesAndNewlines).filter { !$0.isEmpty }
    }
}
