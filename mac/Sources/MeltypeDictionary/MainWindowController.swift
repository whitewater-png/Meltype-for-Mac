// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import AppKit
import MeltypeDictionaryKit

/// メインのウインドウ: 「ユーザー辞書」「専門用語集」「学習した語」「設定」「判定を調べる」のタブ。メニューの独自の項目 (登録・取り込み・書き出し・検索・タブの切り替え) を受ける
/// (ウインドウの delegate はアクションの responder chain に入る)。取り消す (⌘Z) は、ユーザー辞書・専門用語集・設定のタブで 1 つの NSUndoManager を共有する。
/// 「学習した語」のタブには取り消しが無いので、そのタブを選んでいる間は別の空の NSUndoManager を返す (⌘Z でほかのタブの変更を取り消さないように)。
final class MainWindowController: NSWindowController, NSWindowDelegate, NSMenuItemValidation {
    private let dictionary: NativeDictionary
    private let tabs = NSTabViewController()
    private let userController: UserDictionaryViewController
    private let termController: TermDictionaryViewController
    private let learnedController: LearnedWordsViewController
    private let settingsController: SettingsViewController
    private let judgeController: JudgeViewController
    /// ウインドウの取り消し (windowWillReturnUndoManager で返す)。学習した語のタブは learnedUndoManager。
    private let sharedUndoManager = UndoManager()
    private let learnedUndoManager = UndoManager()

    init(dictionary: NativeDictionary) {
        self.dictionary = dictionary
        userController = UserDictionaryViewController(dictionary: dictionary)
        termController = TermDictionaryViewController(dictionary: dictionary)
        learnedController = LearnedWordsViewController(operations: dictionary)
        settingsController = SettingsViewController(dictionary: dictionary)
        judgeController = JudgeViewController(dictionary: dictionary)
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
        let learnedTab = NSTabViewItem(viewController: learnedController)
        learnedTab.label = "学習した語"
        tabs.addTabViewItem(learnedTab)
        let settingsTab = NSTabViewItem(viewController: settingsController)
        settingsTab.label = "設定"
        tabs.addTabViewItem(settingsTab)
        let judgeTab = NSTabViewItem(viewController: judgeController)
        judgeTab.label = "判定を調べる"
        tabs.addTabViewItem(judgeTab)
        window.contentViewController = tabs
        window.setContentSize(NSSize(width: 900, height: 620))
        window.delegate = self
        // 隠れているタブの view は window が nil になるので、シート・取り消しのためにウインドウを渡しておく
        userController.hostWindow = window
        termController.hostWindow = window
        learnedController.hostWindow = window
        settingsController.hostWindow = window
        judgeController.hostWindow = window
        // 片方のタブの変更 (専門用語の「直す」でユーザー辞書が変わるなど) を、もう片方にもすぐ反映する
        userController.didChange = { [weak self] in self?.termController.pollChanges() }
        termController.didChange = { [weak self] in self?.userController.pollChanges() }
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) is not supported") }

    /// NSWindow の undo:/redo: と、ほかのタブの registerUndo (window.undoManager) はここで決まる。
    /// 取り消しを積む操作はどれも、そのタブを選んでいるときのユーザーの操作なので、学習した語のタブを選んでいる間に共有の方へ積まれることはない
    /// (取り消しの中で積むやり直しは、引数で渡した同じ undoManager に積む)。
    func windowWillReturnUndoManager(_ window: NSWindow) -> UndoManager? {
        tabs.selectedTabViewItemIndex == Tab.learned.rawValue ? learnedUndoManager : sharedUndoManager
    }

    /// ほかのプロセス (IME) の変更を拾う。
    func pollChanges() {
        guard window?.isVisible == true else { return }
        userController.pollChanges()
        termController.pollChanges()
        learnedController.pollChanges()
        settingsController.pollChanges()
        judgeController.pollChanges()
    }

    // ---- メニュー ----

    /// タブの位置 (表示メニューの ⌘1〜⌘5 と同じ順)。
    private enum Tab: Int { case user = 0, term, learned, settings, judge }

    /// 設定・判定を調べるのタブには検索が無い。
    func validateMenuItem(_ menuItem: NSMenuItem) -> Bool {
        switch menuItem.action {
        case #selector(focusSearch(_:)): return ![Tab.settings.rawValue, Tab.judge.rawValue].contains(tabs.selectedTabViewItemIndex)
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
        case Tab.learned.rawValue: learnedController.focusSearch()
        default: break   // 設定・判定を調べるのタブに検索は無い
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

    @objc func showLearnedWords(_ sender: Any?) { tabs.selectedTabViewItemIndex = Tab.learned.rawValue }

    @objc func showSettings(_ sender: Any?) { tabs.selectedTabViewItemIndex = Tab.settings.rawValue }

    @objc func showJudge(_ sender: Any?) { tabs.selectedTabViewItemIndex = Tab.judge.rawValue }

    @objc func openDataFolder(_ sender: Any?) {
        guard let directory = dictionary.dataDirectory else { return }
        NSWorkspace.shared.open(URL(fileURLWithPath: directory, isDirectory: true))
    }
}
