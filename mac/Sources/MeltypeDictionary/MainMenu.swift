// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import AppKit

/// メニューバー。「編集」の取り消す (⌘Z)・やり直す (⇧⌘Z)・コピー・削除などは標準の動作を使う
/// (入力欄では文字の取り消し、一覧では辞書の変更の取り消しになる)。アプリ独自の項目は、ウインドウ (MainWindowController) に届く。
enum MainMenu {
    static let appName = "Meltype 辞書"

    static func build() -> NSMenu {
        let main = NSMenu()

        let app = submenu(appName, in: main)
        app.addItem(withTitle: "\(appName)について", action: #selector(NSApplication.orderFrontStandardAboutPanel(_:)), keyEquivalent: "")
        app.addItem(.separator())
        app.addItem(withTitle: "\(appName)を隠す", action: #selector(NSApplication.hide(_:)), keyEquivalent: "h")
        let hideOthers = app.addItem(withTitle: "ほかを隠す", action: #selector(NSApplication.hideOtherApplications(_:)), keyEquivalent: "h")
        hideOthers.keyEquivalentModifierMask = [.command, .option]
        app.addItem(withTitle: "すべてを表示", action: #selector(NSApplication.unhideAllApplications(_:)), keyEquivalent: "")
        app.addItem(.separator())
        app.addItem(withTitle: "\(appName)を終了", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")

        let file = submenu("ファイル", in: main)
        file.addItem(withTitle: "単語を登録…", action: #selector(MainWindowController.addWord(_:)), keyEquivalent: "n")
        file.addItem(.separator())
        let importItem = file.addItem(withTitle: "ほかの辞書を取り込む…", action: #selector(MainWindowController.importDictionary(_:)), keyEquivalent: "i")
        importItem.keyEquivalentModifierMask = [.command, .shift]
        let exportItem = file.addItem(withTitle: "ユーザー辞書を書き出す…", action: #selector(MainWindowController.exportDictionary(_:)), keyEquivalent: "e")
        exportItem.keyEquivalentModifierMask = [.command, .shift]
        file.addItem(.separator())
        file.addItem(withTitle: "データフォルダを開く", action: #selector(MainWindowController.openDataFolder(_:)), keyEquivalent: "")
        file.addItem(.separator())
        file.addItem(withTitle: "閉じる", action: #selector(NSWindow.performClose(_:)), keyEquivalent: "w")

        let edit = submenu("編集", in: main)
        edit.addItem(withTitle: "取り消す", action: Selector(("undo:")), keyEquivalent: "z")
        let redo = edit.addItem(withTitle: "やり直す", action: Selector(("redo:")), keyEquivalent: "z")
        redo.keyEquivalentModifierMask = [.command, .shift]
        edit.addItem(.separator())
        edit.addItem(withTitle: "カット", action: #selector(NSText.cut(_:)), keyEquivalent: "x")
        edit.addItem(withTitle: "コピー", action: #selector(NSText.copy(_:)), keyEquivalent: "c")
        edit.addItem(withTitle: "ペースト", action: #selector(NSText.paste(_:)), keyEquivalent: "v")
        edit.addItem(withTitle: "削除", action: #selector(NSText.delete(_:)), keyEquivalent: "")
        edit.addItem(withTitle: "すべてを選択", action: #selector(NSText.selectAll(_:)), keyEquivalent: "a")
        edit.addItem(.separator())
        edit.addItem(withTitle: "検索", action: #selector(MainWindowController.focusSearch(_:)), keyEquivalent: "f")

        let view = submenu("表示", in: main)
        view.addItem(withTitle: "ユーザー辞書", action: #selector(MainWindowController.showUserDictionary(_:)), keyEquivalent: "1")
        view.addItem(withTitle: "専門用語集(サンプル)", action: #selector(MainWindowController.showTermDictionary(_:)), keyEquivalent: "2")

        let window = submenu("ウインドウ", in: main)
        window.addItem(withTitle: "しまう", action: #selector(NSWindow.performMiniaturize(_:)), keyEquivalent: "m")
        window.addItem(withTitle: "拡大/縮小", action: #selector(NSWindow.performZoom(_:)), keyEquivalent: "")
        NSApp.windowsMenu = window
        return main
    }

    private static func submenu(_ title: String, in main: NSMenu) -> NSMenu {
        let item = NSMenuItem(title: title, action: nil, keyEquivalent: "")
        let menu = NSMenu(title: title)
        item.submenu = menu
        main.addItem(item)
        return menu
    }
}
