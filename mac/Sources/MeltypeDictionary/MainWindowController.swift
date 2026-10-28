// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import AppKit
import MeltypeDictionaryKit

/// メインのウインドウ: 「ユーザー辞書」「専門用語集」「学習した語」「設定」「判定を調べる」のタブ。メニューの独自の項目 (登録・取り込み・書き出し・検索・タブの切り替え) を受ける
/// (ウインドウの delegate はアクションの responder chain に入る)。取り消す (⌘Z) は、ユーザー辞書・専門用語集のタブで 1 つの NSUndoManager を共有する
/// (専門用語集の「直す」はユーザー辞書も変えるので、2 つのタブの変更は 1 本の履歴で戻す)。
/// 「学習した語」「設定」「判定を調べる」のタブは辞書の取り消しを積まないので、そのタブを選んでいる間はタブごとに別の空の NSUndoManager を返す
/// (⌘Z で隠れたタブの変更を取り消さないように)。
final class MainWindowController: NSWindowController, NSWindowDelegate, NSMenuItemValidation {
    private let dictionary: NativeDictionary
    private let tabs = NSTabViewController()
    private let userController: UserDictionaryViewController
    private let termController: TermDictionaryViewController
    private let learnedController: LearnedWordsViewController
    private let settingsController: SettingsViewController
    private let judgeController: JudgeViewController
    /// ユーザー辞書・専門用語集の取り消し (windowWillReturnUndoManager で返すほか、2 つのタブの registerUndo はウインドウを経由せず直接これに積む)。
    private let sharedUndoManager = UndoManager()
    /// 学習した語・設定・判定を調べるのタブを選んでいる間に返す (辞書の操作は積まれない)。
    private let learnedUndoManager = UndoManager()
    private let settingsUndoManager = UndoManager()
    private let judgeUndoManager = UndoManager()

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
        // 「設定」は右端 (ほかのアプリの設定と同じく、いちばん端に置く)
        let judgeTab = NSTabViewItem(viewController: judgeController)
        judgeTab.label = "判定を調べる"
        tabs.addTabViewItem(judgeTab)
        let settingsTab = NSTabViewItem(viewController: settingsController)
        settingsTab.label = "設定"
        tabs.addTabViewItem(settingsTab)
        window.contentViewController = tabs
        window.setContentSize(NSSize(width: 900, height: 620))
        window.delegate = self
        // 隠れているタブの view は window が nil になるので、シートのためにウインドウを渡しておく
        userController.hostWindow = window
        termController.hostWindow = window
        learnedController.hostWindow = window
        settingsController.hostWindow = window
        judgeController.hostWindow = window
        // 辞書の取り消しは共有の undoManager に直接積む (window.undoManager は選んでいるタブで変わるので経由しない)
        userController.dictionaryUndoManager = sharedUndoManager
        termController.dictionaryUndoManager = sharedUndoManager
        // 片方のタブの変更 (専門用語の「直す」でユーザー辞書が変わるなど) を、もう片方にもすぐ反映する
        userController.didChange = { [weak self] in self?.termController.pollChanges() }
        termController.didChange = { [weak self] in self?.userController.pollChanges() }
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) is not supported") }

    /// NSWindow の undo:/redo: (⌘Z / ⇧⌘Z) と、テキスト欄の文字の取り消しが使う undoManager はここで決まる。
    /// ユーザー辞書・専門用語集のタブを選んでいる間だけ共有の undoManager を返す。ほかのタブでは、そのタブ専用の空の undoManager を返すので、
    /// ⌘Z で隠れたタブの辞書の変更が取り消されることはない (辞書の変更を積む側は window.undoManager を経由せず sharedUndoManager に直接積む)。
    func windowWillReturnUndoManager(_ window: NSWindow) -> UndoManager? {
        switch Tab(rawValue: tabs.selectedTabViewItemIndex) {
        case .learned: return learnedUndoManager
        case .settings: return settingsUndoManager
        case .judge: return judgeUndoManager
        case .user, .term, nil: return sharedUndoManager
        }
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
    private enum Tab: Int { case user = 0, term, learned, judge, settings }

    /// 判定を調べるのタブには検索が無い。
    func validateMenuItem(_ menuItem: NSMenuItem) -> Bool {
        switch menuItem.action {
        case #selector(focusSearch(_:)): return tabs.selectedTabViewItemIndex != Tab.judge.rawValue
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
        case Tab.settings.rawValue: settingsController.focusSearch()
        default: break   // 判定を調べるのタブに検索は無い
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
