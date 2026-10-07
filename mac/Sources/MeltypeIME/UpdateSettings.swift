// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import Foundation

/// update.json の中身。保存場所は Application Support/Meltype (uninstall.sh がデータごと消せる場所)。
/// Foundation だけに依存させ、読み方の規則を単体で確かめられるようにしてある。
struct UpdateSettings {
    /// 新しい版の確認をするか。既定 ON。OFF のときは手動の「今すぐ確認」以外で一切通信しない。
    var enabled = true
    var lastCheck: Date?
    /// 通知を出した版。同じ版の通知を 2 回出さない。
    var notifiedVersion: String?

    init() {}

    /// ファイルの内容から読む。ファイルが無いときだけ既定 (ON)。
    /// ファイルがあるのに読めない・JSON として壊れているときは、勝手に通信を始めないよう OFF にする (安全側)。
    /// 読めたファイルは項目ごとに見て、型が違う項目だけ既定値にする (1 項目の誤りで、ほかの項目を捨てない)。
    init(data: Data?, fileExists: Bool) {
        guard fileExists else { return }
        guard let data, let object = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] else {
            enabled = false
            return
        }
        if let value = object["enabled"] as? Bool { enabled = value }
        if let text = object["lastCheck"] as? String { lastCheck = Self.formatter.date(from: text) }
        if let text = object["notifiedVersion"] as? String { notifiedVersion = text }
    }

    func encoded() -> Data? {
        var object: [String: Any] = ["enabled": enabled]
        if let lastCheck { object["lastCheck"] = Self.formatter.string(from: lastCheck) }
        if let notifiedVersion { object["notifiedVersion"] = notifiedVersion }
        return try? JSONSerialization.data(withJSONObject: object, options: [.prettyPrinted, .sortedKeys])
    }

    private static var formatter: ISO8601DateFormatter { ISO8601DateFormatter() }
}
