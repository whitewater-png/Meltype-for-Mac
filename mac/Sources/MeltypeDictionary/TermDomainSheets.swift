// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import AppKit
import MeltypeDictionaryKit

/// 入力欄の中身に応じて、シートの確定ボタンを押せるかを決める (名前が空なら押せない)。
private final class NameWatcher: NSObject, NSTextFieldDelegate {
    var update: () -> Void = {}
    func controlTextDidChange(_ notification: Notification) { update() }
    @objc func popupChanged(_ sender: Any?) { update() }
}

/// 自作の専門用語集の名前・移し先を入れるシート。
enum TermSheets {
    /// 移し先の選び方: すでにある自作の専門用語集か、新しく作る名前。
    enum MoveChoice {
        case existing(TermDomainInfo)
        case new(name: String)
    }

    /// 名前を入れるシート。submit が理由を返したら、その理由を出して、入れ直せるようにもう一度出す。
    static func promptName(title: String, message: String, initial: String = "", confirmTitle: String, problem: String? = nil,
                           in window: NSWindow, submit: @escaping (String) -> String?) {
        let alert = NSAlert()
        alert.alertStyle = problem == nil ? .informational : .warning
        alert.messageText = title
        alert.informativeText = problem ?? message
        let field = NSTextField(string: initial)
        field.placeholderString = "専門用語集の名前 (50 文字まで)"
        field.setAccessibilityLabel("専門用語集の名前")
        field.frame = NSRect(x: 0, y: 0, width: 320, height: 24)
        alert.accessoryView = field
        let confirm = alert.addButton(withTitle: confirmTitle)
        alert.addButton(withTitle: "キャンセル")
        confirm.isEnabled = !initial.trimmingCharacters(in: .whitespaces).isEmpty
        let watcher = NameWatcher()
        watcher.update = { confirm.isEnabled = !field.stringValue.trimmingCharacters(in: .whitespaces).isEmpty }
        field.delegate = watcher
        alert.window.initialFirstResponder = field
        alert.beginSheetModal(for: window) { response in
            _ = watcher
            guard response == .alertFirstButtonReturn else { return }
            let name = field.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
            if let reason = submit(name) {
                DispatchQueue.main.async {
                    promptName(title: title, message: message, initial: name, confirmTitle: confirmTitle, problem: reason, in: window, submit: submit)
                }
            }
        }
    }

    /// ユーザー辞書の語を移す先を選ぶシート (自作の専門用語集の一覧 + 「新しい専門用語集…」)。submit が理由を返したら、出し直す。
    static func chooseMoveTarget(count: Int, domains: [TermDomainInfo], selectedId: String? = nil, problem: String? = nil, newName: String = "",
                                 in window: NSWindow, submit: @escaping (MoveChoice) -> String?) {
        let alert = NSAlert()
        alert.alertStyle = problem == nil ? .informational : .warning
        alert.messageText = "選んだ \(count) 語を専門用語集へ移します"
        alert.informativeText = problem ?? "移した語は、ユーザー辞書から専門用語集に入ります (変換には、ユーザー辞書の語と同じように使われます。予測変換の並びは少し変わることがあります)。移し先が使わない設定なら、使う設定にします。「編集」→「取り消す」(⌘Z) で元に戻せます。"
        let popup = NSPopUpButton(frame: .zero, pullsDown: false)
        for domain in domains { popup.addItem(withTitle: "\(domain.name) (\(domain.count) 語)") }
        if !domains.isEmpty { popup.menu?.addItem(.separator()) }
        popup.addItem(withTitle: "新しい専門用語集…")
        let newIndex = popup.numberOfItems - 1
        if let selectedId, let index = domains.firstIndex(where: { $0.id == selectedId }) { popup.selectItem(at: index) } else if problem != nil || domains.isEmpty { popup.selectItem(at: newIndex) }
        popup.setAccessibilityLabel("移し先の専門用語集")
        let field = NSTextField(string: newName)
        field.placeholderString = "新しい専門用語集の名前 (50 文字まで)"
        field.setAccessibilityLabel("新しい専門用語集の名前")
        let stack = NSStackView(views: [popup, field])
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 8
        stack.frame = NSRect(x: 0, y: 0, width: 320, height: 58)
        popup.widthAnchor.constraint(equalToConstant: 320).isActive = true
        field.widthAnchor.constraint(equalToConstant: 320).isActive = true
        alert.accessoryView = stack
        let confirm = alert.addButton(withTitle: "移す")
        alert.addButton(withTitle: "キャンセル")
        let watcher = NameWatcher()
        watcher.update = {
            let isNew = popup.indexOfSelectedItem == newIndex
            field.isHidden = !isNew
            confirm.isEnabled = !isNew || !field.stringValue.trimmingCharacters(in: .whitespaces).isEmpty
            if isNew { alert.window.makeFirstResponder(field) }
        }
        field.delegate = watcher
        popup.target = watcher
        popup.action = #selector(NameWatcher.popupChanged(_:))
        watcher.update()
        alert.beginSheetModal(for: window) { response in
            _ = watcher
            guard response == .alertFirstButtonReturn else { return }
            let isNew = popup.indexOfSelectedItem == newIndex
            let name = field.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
            let choice: MoveChoice = isNew ? .new(name: name) : .existing(domains[popup.indexOfSelectedItem])
            if let reason = submit(choice) {
                DispatchQueue.main.async {
                    chooseMoveTarget(count: count, domains: domains, selectedId: isNew ? nil : domains[popup.indexOfSelectedItem].id, problem: reason, newName: name, in: window, submit: submit)
                }
            }
        }
    }

    /// 移せなかった語の一覧 (理由つき。長いときは先頭だけ)。
    static func skippedSummary(_ skipped: [SkippedWord]) -> String {
        let shown = skipped.prefix(8).map { "・\($0.key.reading) → \($0.key.word) (\($0.reason))" }
        let rest = skipped.count > shown.count ? ["…ほか \(skipped.count - shown.count) 語"] : []
        return (shown + rest).joined(separator: "\n")
    }
}
