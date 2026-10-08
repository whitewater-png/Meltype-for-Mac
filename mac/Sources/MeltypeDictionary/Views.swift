// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import AppKit

/// キーボードで操作できる一覧: Delete (⌫ / ⌦、⌘ 付きも) で削除、Return / Enter で編集、Space で切り替え (専門用語集の分野)。
final class KeyTableView: NSTableView {
    var onDelete: (() -> Void)?
    var onActivate: (() -> Void)?
    var onSpace: (() -> Void)?
    /// 右クリックした行に合わせて出すメニュー (クリックした行が選ばれていなければ、その行だけを選び直す)。
    var contextMenu: (() -> NSMenu?)?

    override func keyDown(with event: NSEvent) {
        let modifiers = event.modifierFlags.intersection([.command, .option, .control, .shift])
        let key = Int(event.keyCode)
        // Delete (⌫)・Forward Delete (⌦)。⌘ 付きも (Finder と同じ)
        if [51, 117].contains(key), modifiers.isEmpty || modifiers == .command, let onDelete {
            onDelete()
            return
        }
        // Return・テンキーの Enter
        if [36, 76].contains(key), modifiers.isEmpty, let onActivate {
            onActivate()
            return
        }
        // Space
        if key == 49, modifiers.isEmpty, let onSpace {
            onSpace()
            return
        }
        super.keyDown(with: event)
    }

    override func menu(for event: NSEvent) -> NSMenu? {
        let row = self.row(at: convert(event.locationInWindow, from: nil))
        if row >= 0, !selectedRowIndexes.contains(row) {
            selectRowIndexes(IndexSet(integer: row), byExtendingSelection: false)
        }
        return contextMenu?() ?? super.menu(for: event)
    }
}

/// 検索欄の設定と、入力のたびの絞り込み (Enter を待たない)。語数が多い (5000 語超) ときだけ 0.12 秒のデバウンスで、打鍵ごとの再計算を間引く。
final class SearchDebouncer {
    private var pending: DispatchWorkItem?
    func schedule(rowCount: Int, _ action: @escaping () -> Void) {
        pending?.cancel()
        guard rowCount > 5000 else { action(); return }
        let item = DispatchWorkItem(block: action)
        pending = item
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.12, execute: item)
    }
    func cancel() { pending?.cancel() }
}

enum UI {
    static func configure(search field: NSSearchField) {
        field.sendsSearchStringImmediately = true
        field.sendsWholeSearchString = false
    }

    /// 一覧のセル (文字 1 つ)。作り直さずに使い回す。
    static func cell(in tableView: NSTableView, identifier: NSUserInterfaceItemIdentifier) -> NSTableCellView {
        if let reused = tableView.makeView(withIdentifier: identifier, owner: nil) as? NSTableCellView { return reused }
        let cell = NSTableCellView()
        cell.identifier = identifier
        let text = NSTextField(labelWithString: "")
        text.lineBreakMode = .byTruncatingTail
        text.translatesAutoresizingMaskIntoConstraints = false
        text.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        cell.addSubview(text)
        cell.textField = text
        NSLayoutConstraint.activate([
            text.leadingAnchor.constraint(equalTo: cell.leadingAnchor, constant: 2),
            text.trailingAnchor.constraint(equalTo: cell.trailingAnchor, constant: -2),
            text.centerYAnchor.constraint(equalTo: cell.centerYAnchor),
        ])
        return cell
    }

    static func column(_ id: String, title: String, width: CGFloat, sortKey: String?, ascending: Bool = true) -> NSTableColumn {
        let column = NSTableColumn(identifier: NSUserInterfaceItemIdentifier(id))
        column.title = title
        column.width = width
        column.minWidth = 50
        if let sortKey { column.sortDescriptorPrototype = NSSortDescriptor(key: sortKey, ascending: ascending) }
        return column
    }

    static func button(_ title: String, target: AnyObject, action: Selector) -> NSButton {
        let button = NSButton(title: title, target: target, action: action)
        button.bezelStyle = .rounded
        return button
    }

    static func label(_ text: String = "", secondary: Bool = false, size: CGFloat? = nil) -> NSTextField {
        let label = NSTextField(labelWithString: text)
        if secondary { label.textColor = .secondaryLabelColor }
        if let size { label.font = .systemFont(ofSize: size) }
        label.lineBreakMode = .byTruncatingTail
        return label
    }

    static func wrapping(_ text: String, secondary: Bool = true) -> NSTextField {
        let label = NSTextField(wrappingLabelWithString: text)
        if secondary { label.textColor = .secondaryLabelColor }
        label.font = .systemFont(ofSize: NSFont.smallSystemFontSize)
        return label
    }

    static func spacer() -> NSView {
        let view = NSView()
        view.setContentHuggingPriority(.init(1), for: .horizontal)
        return view
    }

    /// VoiceOver に知らせる (状態の表示が変わったとき・エラー)。
    static func announce(_ message: String, in window: NSWindow?) {
        guard !message.isEmpty else { return }
        let element: Any = window ?? NSApp as Any
        NSAccessibility.post(element: element, notification: .announcementRequested, userInfo: [
            .announcement: message,
            .priority: NSAccessibilityPriorityLevel.high.rawValue,
        ])
    }

    /// 理由を出す (ウインドウがあればシート、無ければモーダル)。
    static func showError(_ message: String, title: String, in window: NSWindow?) {
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = title
        alert.informativeText = message
        if let window { alert.beginSheetModal(for: window) } else { alert.runModal() }
    }

    /// 一覧の選択を、読み・語の組で選び直す (読み直したあとも同じ語を選んだままにする)。
    static func select<Key: Hashable>(_ keys: Set<Key>, in tableView: NSTableView, rows: [Key], scroll: Bool = false) {
        guard !keys.isEmpty else { return }
        var indexes = IndexSet()
        for (index, key) in rows.enumerated() where keys.contains(key) { indexes.insert(index) }
        tableView.selectRowIndexes(indexes, byExtendingSelection: false)
        if scroll, let first = indexes.first { tableView.scrollRowToVisible(first) }
    }
}
