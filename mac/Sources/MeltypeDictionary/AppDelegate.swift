// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import AppKit
import MeltypeDictionaryKit

/// アプリ全体: 多重起動の防止、本体 (libMeltypeNative.dylib) の読み込み、メインのウインドウ、ほかのプロセス (IME) の変更の確認。
final class AppDelegate: NSObject, NSApplicationDelegate {
    private var windowController: MainWindowController?
    private var pollTimer: Timer?

    func applicationWillFinishLaunching(_ notification: Notification) {
        // 多重起動しない: すでに動いていれば、そちらを前に出して終わる
        // (入力メニューから開くときは LaunchServices が既存のものを前に出すので、ここに来るのは実行ファイルを直接起動したときなど)。
        if let identifier = Bundle.main.bundleIdentifier {
            let me = ProcessInfo.processInfo.processIdentifier
            if let other = NSRunningApplication.runningApplications(withBundleIdentifier: identifier).first(where: { $0.processIdentifier != me }) {
                other.activate(options: [.activateAllWindows])
                exit(0)
            }
        }
        NSApp.mainMenu = MainMenu.build()
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        let dictionary: NativeDictionary
        do {
            dictionary = try NativeDictionary(libraryPath: NativeDictionary.defaultLibraryPath())
        } catch {
            NSApp.activate(ignoringOtherApps: true)
            let alert = NSAlert()
            alert.alertStyle = .critical
            alert.messageText = "辞書を開けませんでした"
            alert.informativeText = "\(error)"
            alert.runModal()
            NSApp.terminate(nil)
            return
        }
        let controller = MainWindowController(dictionary: dictionary)
        windowController = controller
        showMainWindow()
        // IME (別のプロセス) が登録・分野の切り替えをしたら、一覧に反映する (本体は版を比べるだけなので軽い)。
        let timer = Timer(timeInterval: 1.5, repeats: true) { [weak self] _ in self?.windowController?.pollChanges() }
        RunLoop.main.add(timer, forMode: .common)
        pollTimer = timer
    }

    /// ウインドウを前に出す (入力メニューから開いたとき・Dock から開き直したとき)。
    private func showMainWindow() {
        guard let window = windowController?.window else { return }
        if !window.isVisible { window.center() }
        window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
    }

    func applicationDidBecomeActive(_ notification: Notification) {
        windowController?.pollChanges()
        if let window = windowController?.window, !window.isVisible || window.isMiniaturized {
            window.deminiaturize(nil)
            window.makeKeyAndOrderFront(nil)
        }
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        showMainWindow()
        return true
    }

    /// ウインドウを閉じたら終わる (設定アプリと同じ。次に開くときは入力メニューから)。
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }

    func applicationWillTerminate(_ notification: Notification) {
        pollTimer?.invalidate()
    }
}
