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
    public let group: String
    public let kind: SettingKind
    public let value: SettingValue
    /// kind == .choice のときの選択肢。
    public let options: [SettingOption]?
    /// kind == .int のときの範囲。
    public let min: Int?
    public let max: Int?

    public init(key: String, label: String, description: String, group: String, kind: SettingKind, value: SettingValue,
                options: [SettingOption]? = nil, min: Int? = nil, max: Int? = nil) {
        self.key = key
        self.label = label
        self.description = description
        self.group = group
        self.kind = kind
        self.value = value
        self.options = options
        self.min = min
        self.max = max
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
