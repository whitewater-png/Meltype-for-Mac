// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import AppKit
import MeltypeDictionaryKit

/// メインのウインドウ: 「ユーザー辞書」「専門用語集」「設定」のタブ。メニューの独自の項目 (登録・取り込み・書き出し・検索・タブの切り替え) を受ける
/// (ウインドウの delegate はアクションの responder chain に入る)。取り消す (⌘Z) はウインドウの NSUndoManager を両方のタブで使う。
final class MainWindowController: NSWindowController, NSWindowDelegate, NSMenuItemValidation {
    private let dictionary: NativeDictionary
    private let tabs = NSTabViewController()
    private let userController: UserDictionaryViewController
    private let termController: TermDictionaryViewController
    private let settingsController: SettingsViewController

    init(dictionary: NativeDictionary) {
        self.dictionary = dictionary
        userController = UserDictionaryViewController(dictionary: dictionary)
        termController = TermDictionaryViewController(dictionary: dictionary)
        settingsController = SettingsViewController(dictionary: dictionary)
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
        let settingsTab = NSTabViewItem(viewController: settingsController)
        settingsTab.label = "設定"
        tabs.addTabViewItem(settingsTab)
        window.contentViewController = tabs
        window.setContentSize(NSSize(width: 900, height: 620))
        window.delegate = self
        // 隠れているタブの view は window が nil になるので、シート・取り消しのためにウインドウを渡しておく
        userController.hostWindow = window
        termController.hostWindow = window
        settingsController.hostWindow = window
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
        settingsController.pollChanges()
    }

    // ---- メニュー ----

    /// 設定のタブには検索が無い。
    func validateMenuItem(_ menuItem: NSMenuItem) -> Bool {
        switch menuItem.action {
        case #selector(focusSearch(_:)): return tabs.selectedTabViewItemIndex != 2
        case #selector(moveToTermDomain(_:)): return tabs.selectedTabViewItemIndex == 0 && userController.hasSelection
        case #selector(exportTermDomain(_:)): return tabs.selectedTabViewItemIndex == 1 && termController.hasUserDomainSelected
        default: return true
        }
    }

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
        switch tabs.selectedTabViewItemIndex {
        case 0: userController.focusSearch()
        case 1: termController.focusSearch()
        default: break   // 設定のタブに検索は無い
        }
    }

    /// ユーザー辞書で選んだ語を、自作の専門用語集へ移す。
    @objc func moveToTermDomain(_ sender: Any?) {
        tabs.selectedTabViewItemIndex = 0
        userController.moveToTermDomain(sender)
    }

    @objc func newTermDomain(_ sender: Any?) {
        tabs.selectedTabViewItemIndex = 1
        termController.newDomain(sender)
    }

    @objc func importTermDomain(_ sender: Any?) {
        tabs.selectedTabViewItemIndex = 1
        termController.importDomain(sender)
    }

    @objc func exportTermDomain(_ sender: Any?) {
        tabs.selectedTabViewItemIndex = 1
        termController.exportDomain(sender)
    }

    @objc func showUserDictionary(_ sender: Any?) { tabs.selectedTabViewItemIndex = 0 }

    @objc func showTermDictionary(_ sender: Any?) { tabs.selectedTabViewItemIndex = 1 }

    @objc func showSettings(_ sender: Any?) { tabs.selectedTabViewItemIndex = 2 }

    @objc func openDataFolder(_ sender: Any?) {
        guard let directory = dictionary.dataDirectory else { return }
        NSWorkspace.shared.open(URL(fileURLWithPath: directory, isDirectory: true))
    }
}
