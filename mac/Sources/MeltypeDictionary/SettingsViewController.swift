// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import AppKit
import MeltypeDictionaryKit

/// 「設定」のタブ: Mac で効く Meltype の設定 (ライブ変換・自動判定の強さ・句読点・予測変換など) を、グループごとの一覧で編集する。
/// 項目・名前・説明・範囲は本体 (libMeltypeNative の MacSettingsCatalog) が決める。変更はすぐ config.json に保存され (OK ボタンは無い)、
/// 動いている IME の入力欄にも少し待つと反映される。入力メニューの切り替え (別のプロセス) は、定期的に読み直して画面に反映する。
final class SettingsViewController: NSViewController, NSTextFieldDelegate {
    /// 項目 1 つ分の画面の部品。
    private final class Row {
        let item: SettingItem
        /// 画面を本体の値に合わせる。
        var refresh: (SettingValue) -> Void = { _ in }
        /// 利用者が今そこを操作している (数の入力中など) か。操作中の部品は読み直しで書き換えない。
        var isBusy: () -> Bool = { false }
        var controls: [NSControl] = []

        init(item: SettingItem) { self.item = item }
    }

    private let dictionary: NativeDictionary
    private var rows: [String: Row] = [:]
    /// 画面に反映した本体の値。
    private var values: [String: SettingValue] = [:]
    private let content = NSStackView()
    private let problemLabel = UI.wrapping("", secondary: false)
    private var resetButton: NSButton!
    /// このタブを入れているウインドウ (タブを切り替えると view.window が nil になるので、シートはこちらを使う)。
    weak var hostWindow: NSWindow?
    private var window: NSWindow? { view.window ?? hostWindow }

    /// 説明の文字を、チェックボックスの文字にそろえるための字下げ。
    private static let indent: CGFloat = 20
    private static let maxContentWidth: CGFloat = 640

    init(dictionary: NativeDictionary) {
        self.dictionary = dictionary
        super.init(nibName: nil, bundle: nil)
        title = "設定"
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) is not supported") }

    // ---- 画面 ----

    override func loadView() {
        let note = UI.label("変更はすぐに反映されます", secondary: true)
        note.setAccessibilityLabel("変更はすぐに反映されます")
        resetButton = UI.button("既定値に戻す…", target: self, action: #selector(confirmReset(_:)))
        resetButton.setAccessibilityHelp("このタブの設定をすべて既定値に戻します")
        let header = NSStackView(views: [note, UI.spacer(), resetButton])
        header.orientation = .horizontal
        header.translatesAutoresizingMaskIntoConstraints = false

        problemLabel.textColor = .systemRed
        problemLabel.isHidden = true

        content.orientation = .vertical
        content.alignment = .leading
        content.spacing = 10
        content.translatesAutoresizingMaskIntoConstraints = false
        content.addArrangedSubview(problemLabel)
        problemLabel.widthAnchor.constraint(equalTo: content.widthAnchor).isActive = true

        // スクロールする中身 (窓が低いときも最後まで見られる)
        let document = FlippedView()
        document.translatesAutoresizingMaskIntoConstraints = false
        document.addSubview(content)
        let trailing = content.trailingAnchor.constraint(equalTo: document.trailingAnchor, constant: -24)
        trailing.priority = .defaultHigh
        NSLayoutConstraint.activate([
            content.topAnchor.constraint(equalTo: document.topAnchor, constant: 16),
            content.leadingAnchor.constraint(equalTo: document.leadingAnchor, constant: 24),
            trailing,
            content.widthAnchor.constraint(lessThanOrEqualToConstant: Self.maxContentWidth),
            content.bottomAnchor.constraint(equalTo: document.bottomAnchor, constant: -20),
        ])
        let scroll = NSScrollView()
        scroll.documentView = document
        scroll.hasVerticalScroller = true
        scroll.drawsBackground = false
        scroll.borderType = .noBorder
        scroll.translatesAutoresizingMaskIntoConstraints = false
        document.widthAnchor.constraint(equalTo: scroll.contentView.widthAnchor).isActive = true

        let root = NSView()
        root.addSubview(header)
        root.addSubview(scroll)
        NSLayoutConstraint.activate([
            header.topAnchor.constraint(equalTo: root.topAnchor, constant: 12),
            header.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 24),
            header.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -24),
            scroll.topAnchor.constraint(equalTo: header.bottomAnchor, constant: 8),
            scroll.leadingAnchor.constraint(equalTo: root.leadingAnchor),
            scroll.trailingAnchor.constraint(equalTo: root.trailingAnchor),
            scroll.bottomAnchor.constraint(equalTo: root.bottomAnchor),
        ])
        view = root
        reload()
    }

    private final class FlippedView: NSView {
        override var isFlipped: Bool { true }
    }

    // ---- 項目の組み立て ----

    private func build(_ catalog: SettingsCatalog) {
        for (group, items) in catalog.groups {
            let heading = NSTextField(labelWithString: group)
            heading.font = .boldSystemFont(ofSize: 13)
            heading.setAccessibilityRole(.staticText)
            content.addArrangedSubview(heading)
            content.setCustomSpacing(4, after: heading)
            let separator = NSBox()
            separator.boxType = .separator
            separator.translatesAutoresizingMaskIntoConstraints = false
            content.addArrangedSubview(separator)
            separator.widthAnchor.constraint(equalTo: content.widthAnchor).isActive = true
            content.setCustomSpacing(10, after: separator)
            for item in items {
                let row = makeRow(item)
                rows[item.key] = row
                values[item.key] = item.value
                row.refresh(item.value)
            }
            if let last = content.arrangedSubviews.last { content.setCustomSpacing(22, after: last) }
        }
    }

    private func makeRow(_ item: SettingItem) -> Row {
        let row = Row(item: item)
        let id = NSUserInterfaceItemIdentifier(item.key)
        let description = UI.wrapping(item.description)
        description.setAccessibilityElement(false)   // 部品の補足 (ヘルプ) として読ませる
        let line: NSView
        var descriptionIndent = Self.indent

        switch item.kind {
        case .bool:
            let checkbox = NSButton(checkboxWithTitle: item.label, target: self, action: #selector(boolChanged(_:)))
            checkbox.identifier = id
            checkbox.setAccessibilityHelp(item.description)
            row.controls = [checkbox]
            row.refresh = { value in
                if case let .bool(on) = value { checkbox.state = on ? .on : .off }
            }
            line = checkbox
            descriptionIndent = Self.indent
        case .choice:
            let title = NSTextField(labelWithString: item.label)
            let popup = NSPopUpButton(frame: .zero, pullsDown: false)
            popup.identifier = id
            popup.target = self
            popup.action = #selector(choiceChanged(_:))
            for option in item.options ?? [] {
                popup.addItem(withTitle: option.label)
                popup.lastItem?.representedObject = option.value
            }
            popup.setAccessibilityLabel(item.label)
            popup.setAccessibilityHelp(item.description)
            row.controls = [popup]
            row.refresh = { value in
                guard case let .string(name) = value else { return }
                let index = popup.indexOfItem(withRepresentedObject: name)
                if index >= 0 { popup.selectItem(at: index) }
            }
            let stack = NSStackView(views: [title, popup])
            stack.orientation = .horizontal
            stack.spacing = 8
            line = indented(stack, by: Self.indent)
        case .int:
            let title = NSTextField(labelWithString: item.label)
            let field = NSTextField(string: "")
            field.identifier = id
            field.alignment = .right
            field.target = self
            field.action = #selector(intFieldChanged(_:))
            field.delegate = self
            field.translatesAutoresizingMaskIntoConstraints = false
            field.widthAnchor.constraint(equalToConstant: 44).isActive = true
            field.setAccessibilityLabel(item.label)
            field.setAccessibilityHelp(item.description)
            let stepper = NSStepper()
            stepper.identifier = id
            stepper.minValue = Double(item.min ?? 0)
            stepper.maxValue = Double(item.max ?? 100)
            stepper.increment = 1
            stepper.valueWraps = false
            stepper.target = self
            stepper.action = #selector(intStepperChanged(_:))
            stepper.setAccessibilityLabel("\(item.label) (増減)")
            let range = UI.label(item.min.flatMap { low in item.max.map { "(\(low)〜\($0))" } } ?? "", secondary: true)
            row.controls = [field, stepper]
            row.isBusy = { field.currentEditor() != nil }
            row.refresh = { value in
                guard case let .int(number) = value else { return }
                field.integerValue = number
                stepper.integerValue = number
            }
            let stack = NSStackView(views: [title, field, stepper, range])
            stack.orientation = .horizontal
            stack.spacing = 6
            stack.setCustomSpacing(8, after: title)
            line = indented(stack, by: Self.indent)
        }

        let block = NSStackView(views: [line, indented(description, by: descriptionIndent)])
        block.orientation = .vertical
        block.alignment = .leading
        block.spacing = 2
        content.addArrangedSubview(block)
        block.widthAnchor.constraint(equalTo: content.widthAnchor).isActive = true
        return row
    }

    /// 左に字下げした入れ物 (説明の文章は幅いっぱいで折り返す)。
    private func indented(_ view: NSView, by indent: CGFloat) -> NSView {
        let container = NSView()
        view.translatesAutoresizingMaskIntoConstraints = false
        container.addSubview(view)
        NSLayoutConstraint.activate([
            view.topAnchor.constraint(equalTo: container.topAnchor),
            view.bottomAnchor.constraint(equalTo: container.bottomAnchor),
            view.leadingAnchor.constraint(equalTo: container.leadingAnchor, constant: indent),
            view.trailingAnchor.constraint(lessThanOrEqualTo: container.trailingAnchor),
        ])
        if view is NSTextField { view.trailingAnchor.constraint(equalTo: container.trailingAnchor).isActive = true }
        return container
    }

    // ---- 読み込み ----

    /// 本体から読み直して画面に反映する。config.json が読めなければ、理由を出して操作できなくする。
    private func reload() {
        guard let catalog = dictionary.settings() else {
            problemLabel.stringValue = "設定ファイル (config.json) を読めません。ファイルが壊れていないか確認してください (「ファイル」メニューの「データフォルダを開く」)。壊れたままの設定は書き換えません。"
            problemLabel.isHidden = false
            setEnabled(false)
            return
        }
        problemLabel.isHidden = true
        setEnabled(true)
        if rows.isEmpty {
            build(catalog)
            return
        }
        for item in catalog.items {
            guard let row = rows[item.key], values[item.key] != item.value, !row.isBusy() else { continue }
            row.refresh(item.value)
            values[item.key] = item.value
        }
    }

    private func setEnabled(_ enabled: Bool) {
        resetButton?.isEnabled = enabled
        for row in rows.values { for control in row.controls { control.isEnabled = enabled } }
    }

    /// ほかのプロセス (入力メニュー) の変更を拾う。
    func pollChanges() {
        // タブの画面は、初めて開くまで作られない (作る前に組み立てると、loadView で順番が崩れる)
        guard isViewLoaded else { return }
        reload()
    }

    /// 入力中の数の欄を、内容を保存せずに閉じる (閉じるときに action が送られて、古い値を書き戻さないように)。
    private func abortEditing() {
        guard let editor = window?.firstResponder as? NSText, let field = editor.delegate as? NSTextField else { return }
        field.abortEditing()
    }

    // ---- 編集 ----

    private func row(for sender: NSControl) -> Row? { sender.identifier.flatMap { rows[$0.rawValue] } }

    /// 保存する。だめなら理由を出して、画面を本体の値に戻す。
    private func commit(_ row: Row, _ value: SettingValue) {
        let key = row.item.key
        if values[key] == value { return }
        if let reason = dictionary.setSetting(key: key, value: value) {
            UI.showError(reason, title: "「\(row.item.label)」を変更できませんでした", in: window)
            UI.announce(reason, in: window)
            revert(row)
            return
        }
        values[key] = value
    }

    private func revert(_ row: Row) {
        if let value = values[row.item.key] { row.refresh(value) }
        reload()
    }

    @objc private func boolChanged(_ sender: NSButton) {
        guard let row = row(for: sender) else { return }
        commit(row, .bool(sender.state == .on))
    }

    @objc private func choiceChanged(_ sender: NSPopUpButton) {
        guard let row = row(for: sender), let name = sender.selectedItem?.representedObject as? String else { return }
        commit(row, .string(name))
    }

    @objc private func intFieldChanged(_ sender: NSTextField) {
        guard let row = row(for: sender) else { return }
        // 全角の数字 (日本語入力のまま打った「４」) も受け付ける
        let typed = (sender.stringValue.applyingTransform(.fullwidthToHalfwidth, reverse: false) ?? sender.stringValue).trimmingCharacters(in: .whitespaces)
        guard let number = Int(typed) else {
            UI.showError("整数を入力してください。", title: "「\(row.item.label)」を変更できませんでした", in: window)
            revert(row)
            return
        }
        commit(row, .int(number))
        // 保存できたらステッパーも合わせる (範囲外で断られたときは revert 済み)
        if values[row.item.key] == .int(number), let stepper = row.controls.compactMap({ $0 as? NSStepper }).first { stepper.integerValue = number }
    }

    @objc private func intStepperChanged(_ sender: NSStepper) {
        guard let row = row(for: sender) else { return }
        if let field = row.controls.compactMap({ $0 as? NSTextField }).first { field.integerValue = sender.integerValue }
        commit(row, .int(sender.integerValue))
    }

    // ---- 既定値に戻す ----

    @objc private func confirmReset(_ sender: Any?) {
        guard let window else { return }
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = "設定を既定値に戻しますか?"
        alert.informativeText = "このタブにある設定をすべて、買ったときの状態に戻します。ユーザー辞書・専門用語集・ほかの設定は変わりません。"
        alert.addButton(withTitle: "既定値に戻す").hasDestructiveAction = true
        alert.addButton(withTitle: "キャンセル")
        alert.beginSheetModal(for: window) { [weak self] response in
            guard response == .alertFirstButtonReturn, let self else { return }
            // 数を入力中なら、保存せずに閉じる (戻したあとに、入力中の値が書き戻されないように)
            self.abortEditing()
            if let reason = self.dictionary.resetSettings() {
                UI.showError(reason, title: "既定値に戻せませんでした", in: self.window)
                return
            }
            // 画面を本体の値に合わせる
            self.values = [:]
            self.reload()
        }
    }
}
