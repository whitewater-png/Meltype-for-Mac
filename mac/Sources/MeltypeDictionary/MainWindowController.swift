// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import AppKit
import MeltypeDictionaryKit

/// メインのウインドウ: 「ユーザー辞書」と「専門用語集」のタブ。メニューの独自の項目 (登録・取り込み・書き出し・検索・タブの切り替え) を受ける
/// (ウインドウの delegate はアクションの responder chain に入る)。取り消す (⌘Z) はウインドウの NSUndoManager を両方のタブで使う。
final class MainWindowController: NSWindowController, NSWindowDelegate {
    private let dictionary: NativeDictionary
    private let tabs = NSTabViewController()
    private let userController: UserDictionaryViewController
    private let termController: TermDictionaryViewController

    init(dictionary: NativeDictionary) {
        self.dictionary = dictionary
        userController = UserDictionaryViewController(dictionary: dictionary)
        termController = TermDictionaryViewController(dictionary: dictionary)
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 900, height: 620),
                              styleMask: [.titled, .closable, .miniaturizable, .resizable], backing: .buffered, defer: false)
        window.title = MainMenu.appName
        // 保存場所を小さく出す (MELTYPE_DATA_DIR で別の場所を使っているときに分かるように)
        if let directory = dictionary.dataDirectory {
            window.subtitle = (directory as NSString).abbreviatingWithTildeInPath
        }
        window.minSize = NSSize(width: 720, height: 460)
        // ウインドウの状態を ~/Library/Saved Application State に残さない
        window.isRestorable = false
        super.init(window: window)

        tabs.tabStyle = .segmentedControlOnTop
        let userTab = NSTabViewItem(viewController: userController)
        userTab.label = "ユーザー辞書"
        let termTab = NSTabViewItem(viewController: termController)
        termTab.label = "専門用語集(サンプル)"
        tabs.addTabViewItem(userTab)
        tabs.addTabViewItem(termTab)
        window.contentViewController = tabs
        window.setContentSize(NSSize(width: 900, height: 620))
        window.delegate = self
        // 隠れているタブの view は window が nil になるので、シート・取り消しのためにウインドウを渡しておく
        userController.hostWindow = window
        termController.hostWindow = window
        // 片方のタブの変更 (専門用語の「直す」でユーザー辞書が変わるなど) を、もう片方にもすぐ反映する
        userController.didChange = { [weak self] in self?.termController.pollChanges() }
        termController.didChange = { [weak self] in self?.userController.pollChanges() }
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) is not supported") }

    /// ほかのプロセス (IME) の変更を拾う。
    func pollChanges() {
        guard window?.isVisible == true else { return }
        userController.pollChanges()
        termController.pollChanges()
    }

    private var showingUserTab: Bool { tabs.selectedTabViewItemIndex == 0 }

    // ---- メニュー ----

    @objc func addWord(_ sender: Any?) {
        tabs.selectedTabViewItemIndex = 0
        userController.addWord(sender)
    }

    @objc func importDictionary(_ sender: Any?) {
        tabs.selectedTabViewItemIndex = 0
        userController.importDictionary(sender)
    }

    @objc func exportDictionary(_ sender: Any?) {
        tabs.selectedTabViewItemIndex = 0
        userController.exportDictionary(sender)
    }

    @objc func focusSearch(_ sender: Any?) {
        if showingUserTab { userController.focusSearch() } else { termController.focusSearch() }
    }

    @objc func showUserDictionary(_ sender: Any?) { tabs.selectedTabViewItemIndex = 0 }

    @objc func showTermDictionary(_ sender: Any?) { tabs.selectedTabViewItemIndex = 1 }

    @objc func openDataFolder(_ sender: Any?) {
        guard let directory = dictionary.dataDirectory else { return }
        NSWorkspace.shared.open(URL(fileURLWithPath: directory, isDirectory: true))
    }
}
