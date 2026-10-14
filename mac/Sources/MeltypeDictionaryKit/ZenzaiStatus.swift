// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import Foundation

/// zenzai.json の中身: Zenzai (ニューラル変換) のモデルの取得・使用の状態。保存場所は Application Support/Meltype (update.json と同じ)。
/// 書くのは IME (モデルを取得するのは IME の側)、読むのは「Meltype 辞書」の設定タブ (状態の文を出すだけ)。別のプロセスなので、ファイルでやり取りする。
/// Foundation だけに依存させ、IME と辞書アプリの両方から使い、読み方の規則を単体で確かめられるようにしてある。
public struct ZenzaiStatus: Equatable {
    public enum State: String {
        /// 起動時に、既にあるモデルのファイルを検査している (通信はしない)。
        case checking
        /// モデルを取得している。progress に百分率。
        case downloading
        /// モデルがあり、検査 (サイズと SHA-256) も通った。
        case ready
        /// 取得できなかった・検査に通らなかった。error に理由。設定は OFF に戻してある。
        case failed
        /// 動いたが、1 キーの変換が遅いので、この IME のプロセスだけ OFF にしている (IME を起動し直すまで)。
        case slow
        /// 検査は通ったが、モデルを読み込めなかったので、この IME のプロセスだけ OFF にしている (IME を起動し直すまで)。
        case loadFailed
        /// 設定を OFF にした (取得は中止し、使わない)。
        case off
    }

    public var state: State
    /// downloading のときの百分率 (0〜100)。
    public var progress: Int?
    /// failed・slow・loadFailed のときの理由 (画面にそのまま出す。打った内容は入れない)。
    public var error: String?
    public var updatedAt: Date

    public init(state: State, progress: Int? = nil, error: String? = nil, updatedAt: Date = Date()) {
        self.state = state
        self.progress = progress
        self.error = error
        self.updatedAt = updatedAt
    }

    /// ファイルの内容から読む。JSON として壊れている・state が知らない値・型が違うときは nil (= 状態なし。OFF と同じ扱いで、何も出さない)。
    public init?(data: Data) {
        guard let object = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any],
              let name = object["state"] as? String, let state = State(rawValue: name) else { return nil }
        self.state = state
        if let value = object["progress"] as? Int { progress = min(max(value, 0), 100) }
        if let value = object["error"] as? String { error = String(value.prefix(200)) }
        if let text = object["updatedAt"] as? String, let date = Self.formatter.date(from: text) { updatedAt = date } else { updatedAt = Date(timeIntervalSince1970: 0) }
    }

    /// データフォルダーの zenzai.json から読む。無い・壊れているときは nil。
    public static func read(dataDirectory: String) -> ZenzaiStatus? {
        let url = URL(fileURLWithPath: dataDirectory, isDirectory: true).appendingPathComponent("zenzai.json")
        return (try? Data(contentsOf: url)).flatMap { ZenzaiStatus(data: $0) }
    }

    public func encoded() -> Data? {
        var object: [String: Any] = ["state": state.rawValue, "updatedAt": Self.formatter.string(from: updatedAt)]
        if let progress { object["progress"] = progress }
        if let error { object["error"] = error }
        return try? JSONSerialization.data(withJSONObject: object, options: [.prettyPrinted, .sortedKeys])
    }

    /// 「ダウンロード中」のまま、この時間 (秒) 更新が無ければ、IME が止まった (落ちた・終了した) とみなす。IME は進みが変わるたびに 1 秒に 1 回以内で書く。
    public static let staleAfter: TimeInterval = 120

    /// 設定タブに出す 1 行 (Zenzai の項目の下)。出すものが無ければ nil。
    public func displayText(now: Date = Date()) -> String? {
        switch state {
        case .checking:
            // 検査は数秒で終わる。長く更新が無ければ IME が止まったあとの古い記録なので、何も出さない
            return now.timeIntervalSince(updatedAt) > Self.staleAfter ? nil : "確認中"
        case .off:
            return "OFF"
        case .downloading:
            if now.timeIntervalSince(updatedAt) > Self.staleAfter { return "ダウンロードが止まっています (Meltype を使い始めると、設定が ON のあいだ再開します)" }
            return "ダウンロード中 (\(progress ?? 0)%)"
        case .ready:
            return "準備できました"
        case .failed:
            return "失敗: \(error ?? "理由は分かりません")"
        case .slow:
            return "変換が遅いため一時的に OFF (Meltype を起動し直すと戻ります)"
        case .loadFailed:
            return "モデルを読み込めなかったため一時的に OFF (Meltype を起動し直すと戻ります)"
        }
    }

    private static var formatter: ISO8601DateFormatter { ISO8601DateFormatter() }
}
