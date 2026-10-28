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

/// 設定タブの項目 1 つ (名前・説明・グループは Windows の設定画面と同じ文言。本体の MacSettingsCatalog が決める)。
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
        let words = SearchText.normalize(query).components(separatedBy: .whitespacesAndNewlines).filter { !$0.isEmpty }
        guard !words.isEmpty else { return true }
        let text = SearchText.normalize([item.label, item.summary ?? "", item.example ?? "", item.description].joined(separator: "\u{1}"))
        return words.allSatisfy { text.contains($0) }
    }
}
