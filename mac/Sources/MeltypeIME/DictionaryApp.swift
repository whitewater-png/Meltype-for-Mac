// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import AppKit

/// 辞書の管理画面「Meltype 辞書」(Meltype.app/Contents/Helpers/MeltypeDictionary.app) を開く。
/// IME は背面専用のアプリ (LSBackgroundOnly) で、自分のウインドウはキーボード入力を受けられないので、画面は別のふつうのアプリにしてある。
/// すでに開いていれば、新しく起動せずにそのウインドウを前に出す (LaunchServices が既存のものを使い、画面の側も多重起動を止める)。
enum DictionaryApp {
    static let bundleIdentifier = "io.github.yksr-melt.Meltype.Dictionary"

    /// この IME の中の画面のアプリ。
    static var url: URL {
        Bundle.main.bundleURL.appendingPathComponent("Contents/Helpers/MeltypeDictionary.app", isDirectory: true)
    }

    /// 開く。開けなければ理由を completion に渡す (メインスレッド)。
    static func open(completion: @escaping (String?) -> Void) {
        let url = self.url
        guard FileManager.default.fileExists(atPath: url.appendingPathComponent("Contents/Info.plist").path) else {
            completion("辞書の管理画面 (MeltypeDictionary.app) が見つかりません。Meltype を入れ直してください。")
            return
        }
        let configuration = NSWorkspace.OpenConfiguration()
        configuration.activates = true
        configuration.addsToRecentItems = false
        configuration.createsNewApplicationInstance = false
        // 動作確認で IME を別の保存場所 (MELTYPE_DATA_DIR) で動かしているときは、画面も同じ場所を使う。
        if let directory = ProcessInfo.processInfo.environment["MELTYPE_DATA_DIR"], !directory.isEmpty {
            configuration.environment = ["MELTYPE_DATA_DIR": directory]
        }
        NSWorkspace.shared.openApplication(at: url, configuration: configuration) { application, error in
            DispatchQueue.main.async {
                if let error {
                    NSLog("Meltype: 辞書の管理画面を開けませんでした: %@", String(describing: error))
                    completion("辞書の管理画面を開けませんでした: \(error.localizedDescription)")
                    return
                }
                // 前に出す (すでに開いていて、ほかのウインドウの後ろにあるときも)
                application?.activate(options: [.activateAllWindows])
                completion(nil)
            }
        }
    }
}
