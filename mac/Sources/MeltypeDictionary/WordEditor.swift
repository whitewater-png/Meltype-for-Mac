// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import AppKit
import MeltypeDictionaryKit

/// 読みと単語を入れるシート (ユーザー辞書の登録・編集、専門用語の「直す」)。
/// 入力のたびに本体で確かめ (UserDictionary.Validate と同じ規則 + 重複)、理由を赤字で出して、だめなら確定のボタンを押せなくする。
/// 確定 (Return) で submit を呼び、理由が返ればシートを閉じずに出す (保存できなかった・ほかで変わっていたなど)。Esc でキャンセル。
final class WordEditor: NSObject, NSTextFieldDelegate {
    struct Configuration {
        var title: String
        var explanation: String?
        var confirmTitle: String
        var key: WordKey
    }

    private let configuration: Configuration
    private let validate: (WordKey) -> String?
    private let submit: (WordKey) -> String?
    private let toReading: (String) -> String
    private let sheet: NSWindow
    private let readingField = NSTextField()
    private let wordField = NSTextField()
    private let messageLabel = NSTextField(wrappingLabelWithString: "")
    private var hiraganaButton: NSButton!
    private var confirmButton: NSButton!
    private weak var parent: NSWindow?
    /// シートを出している間、自分を持っておく (呼び出し側は持たなくてよい)。
    private var keepAlive: WordEditor?
    private var lastMessage = ""

    init(_ configuration: Configuration, validate: @escaping (WordKey) -> String?, submit: @escaping (WordKey) -> String?, toReading: @escaping (String) -> String) {
        self.configuration = configuration
        self.validate = validate
        self.submit = submit
        self.toReading = toReading
        sheet = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 460, height: 220), styleMask: [.titled], backing: .buffered, defer: true)
        super.init()
        sheet.title = configuration.title
        sheet.isRestorable = false
        build()
    }

    private func build() {
        let title = NSTextField(labelWithString: configuration.title)
        title.font = .boldSystemFont(ofSize: NSFont.systemFontSize + 1)

        readingField.stringValue = configuration.key.reading
        readingField.placeholderString = "ひらがな 2 文字以上"
        readingField.setAccessibilityLabel("読み (ひらがな 2 文字以上)")
        readingField.delegate = self
        wordField.stringValue = configuration.key.word
        wordField.placeholderString = "変換したときに出す語"
        wordField.setAccessibilityLabel("単語")
        wordField.delegate = self
        for field in [readingField, wordField] {
            field.translatesAutoresizingMaskIntoConstraints = false
            field.widthAnchor.constraint(greaterThanOrEqualToConstant: 260).isActive = true
            field.lineBreakMode = .byTruncatingTail
            field.usesSingleLineMode = true
        }
        hiraganaButton = UI.button("ひらがなにする", target: self, action: #selector(convertReading(_:)))
        hiraganaButton.controlSize = .small
        hiraganaButton.font = .systemFont(ofSize: NSFont.smallSystemFontSize)
        hiraganaButton.toolTip = "ローマ字・カタカナで打った読みを、ひらがなにします"

        let grid = NSGridView(views: [
            [UI.label("読み"), readingField, hiraganaButton],
            [UI.label("単語"), wordField, NSGridCell.emptyContentView],
        ])
        grid.rowSpacing = 10
        grid.columnSpacing = 8
        grid.column(at: 0).xPlacement = .trailing
        grid.rowAlignment = .firstBaseline

        messageLabel.textColor = .systemRed
        messageLabel.font = .systemFont(ofSize: NSFont.smallSystemFontSize)
        messageLabel.setAccessibilityLabel("入力の確認")
        messageLabel.preferredMaxLayoutWidth = 420

        let cancel = UI.button("キャンセル", target: self, action: #selector(cancel(_:)))
        cancel.keyEquivalent = "\u{1b}"
        confirmButton = UI.button(configuration.confirmTitle, target: self, action: #selector(confirm(_:)))
        confirmButton.keyEquivalent = "\r"
        let buttons = NSStackView(views: [UI.spacer(), cancel, confirmButton])
        buttons.orientation = .horizontal

        var rows: [NSView] = [title]
        if let explanation = configuration.explanation {
            let label = UI.wrapping(explanation)
            label.preferredMaxLayoutWidth = 420
            rows.append(label)
        }
        rows.append(contentsOf: [grid, messageLabel, buttons])
        let stack = NSStackView(views: rows)
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 12
        stack.edgeInsets = NSEdgeInsets(top: 20, left: 20, bottom: 20, right: 20)
        stack.translatesAutoresizingMaskIntoConstraints = false
        buttons.translatesAutoresizingMaskIntoConstraints = false
        let content = NSView()
        content.addSubview(stack)
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: content.leadingAnchor),
            stack.trailingAnchor.constraint(equalTo: content.trailingAnchor),
            stack.topAnchor.constraint(equalTo: content.topAnchor),
            stack.bottomAnchor.constraint(equalTo: content.bottomAnchor),
            buttons.widthAnchor.constraint(equalTo: stack.widthAnchor, constant: -40),
            content.widthAnchor.constraint(greaterThanOrEqualToConstant: 460),
        ])
        sheet.contentView = content
        sheet.initialFirstResponder = configuration.key.reading.isEmpty || !configuration.key.word.isEmpty ? readingField : wordField
        refresh()
    }

    /// シートを出す。
    func begin(on window: NSWindow) {
        parent = window
        keepAlive = self
        window.beginSheet(sheet) { [weak self] _ in self?.keepAlive = nil }
        sheet.makeFirstResponder(sheet.initialFirstResponder)
    }

    private var currentKey: WordKey {
        WordKey(reading: Self.normalizedReading(readingField.stringValue),
                word: wordField.stringValue.trimmingCharacters(in: .whitespacesAndNewlines))
    }

    /// 読みを、本体が保存する形にそろえる (UserDictionary.NormalizeReading と同じ: 前後の空白を取り、カタカナ ァ〜ヶ をひらがなにする)。
    /// 登録したあとの選択と ⌘Z の取り消しを、保存された語と同じキーで行うため。
    static func normalizedReading(_ text: String) -> String {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        var scalars = String.UnicodeScalarView()
        for scalar in trimmed.unicodeScalars {
            if (0x30A1...0x30F6).contains(scalar.value), let hiragana = Unicode.Scalar(scalar.value - 0x60) {
                scalars.append(hiragana)
            } else {
                scalars.append(scalar)
            }
        }
        return String(scalars)
    }

    func controlTextDidChange(_ notification: Notification) { refresh() }

    /// 入力を確かめて、理由・ボタンの状態を更新する。
    private func refresh() {
        let key = currentKey
        // 「ひらがなにする」は、欄に入っている文字そのもの (カタカナのままでも) を見て出す
        let typed = readingField.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        let converted = typed.isEmpty ? typed : toReading(typed)
        hiraganaButton.isEnabled = !typed.isEmpty && converted != typed
        hiraganaButton.setAccessibilityHelp(hiraganaButton.isEnabled ? "読みを「\(converted)」にします" : nil)
        let empty = key.reading.isEmpty && key.word.isEmpty
        let error = empty ? nil : validate(key)
        confirmButton.isEnabled = !empty && error == nil
        show(error ?? "")
    }

    private func show(_ message: String) {
        messageLabel.stringValue = message
        messageLabel.isHidden = message.isEmpty
        if message != lastMessage { UI.announce(message, in: sheet) }
        lastMessage = message
    }

    @objc private func convertReading(_ sender: Any?) {
        readingField.stringValue = toReading(readingField.stringValue)
        refresh()
    }

    @objc private func confirm(_ sender: Any?) {
        let key = currentKey
        if let error = validate(key) {
            show(error)
            return
        }
        if let error = submit(key) {
            show(error)
            return
        }
        close()
    }

    @objc private func cancel(_ sender: Any?) { close() }

    private func close() {
        guard let parent else { return }
        parent.endSheet(sheet)
    }
}
