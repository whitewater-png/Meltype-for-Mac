// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import AppKit
import MeltypeDictionaryKit

// 「Meltype 辞書」: ユーザー辞書と専門用語集を、見て・足して・直して・消すための画面。
// IME (Meltype.app) は背面専用のアプリ (LSBackgroundOnly) で、自分のウインドウがキーボード入力を受けられないので、
// 画面は別のふつうのアプリにして Meltype.app/Contents/Helpers/MeltypeDictionary.app に入れ、入力メニューの「設定・辞書…」から開く。
// 辞書の読み書きは、IME と同じ libMeltypeNative.dylib (外側の Meltype.app/Contents/Frameworks) を通す (ファイルの形式・ロック・入力チェックを 1 か所にするため)。
//
// `MeltypeDictionary --self-test`: 画面を出さずにロジックを確かめる (環境変数 MELTYPE_DATA_DIR が一時フォルダーなら、本体を通した操作も)。
if CommandLine.arguments.dropFirst().first == "--self-test" {
    let passed = SelfTest.run(libraryPath: NativeDictionary.defaultLibraryPath()) { print($0) }
    exit(passed ? 0 : 1)
}

let application = NSApplication.shared
// NSApplication の delegate は弱い参照なので、ここで持っておく (トップレベルの定数はプロセスが終わるまで残る)。
let appDelegate = AppDelegate()
application.delegate = appDelegate
application.setActivationPolicy(.regular)
application.run()
