// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import AppKit
import MeltypeDictionaryKit

/// 「学習した語」のタブ: 打った英字を、英字のまま / かなにする と覚えた語 (languages.json) の一覧。検索・並べ替え・複数選択、追加・編集・削除。
/// 変更はすぐ languages.json に保存される。IME (別のプロセス) は、次に語を確かめるときにファイルを読み直す (本体の LanguageMemory.Refresh)。
/// 削除は元に戻せない (回数・最後に使った日まで戻す口が本体に無いため)。確認の文でそう伝える。
final class LearnedWordsViewController: NSViewController, NSTableViewDataSource, NSTableViewDelegate, NSSearchFieldDelegate, NSMenuItemValidation {
    private let operations: LearnedWordOperations
    private let list = LearnedWordList(column: .used, ascending: false)
    private let searchField = NSSearchField()
    private let tableView = KeyTableView()
    private let countLabel = UI.statusLabel()
    private let statusLabel = UI.statusLabel()
    private let emptyLabel = UI.label(secondary: true, size: 13)
    private var addButton: NSButton!
    private var editButton: NSButton!
    private var deleteButton: NSButton!
    private var lastVersion: Int32 = .min
    /// 一覧を読めなかった版 (同じ版で読み直しても同じなので、知らせは 1 回だけにする)。
    private var failedVersion: Int32?
    private let dateFormatter: DateFormatter = {
        let formatter = DateFormatter()
        formatter.dateStyle = .short
        formatter.timeStyle = .short
        return formatter
    }()
    /// このタブを入れているウインドウ (タブを切り替えると view.window が nil になるので、シートはこちらを使う)。
    weak var hostWindow: NSWindow?
    private var window: NSWindow? { view.window ?? hostWindow }

    init(operations: LearnedWordOperations) {
        self.operations = operations
        super.init(nibName: nil, bundle: nil)
        title = "学習した語"
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) is not supported") }

    // ---- 画面 ----

    override func loadView() {
        let explanation = UI.wrapping("打った英字を「英字のまま」にするか「かなにする」かを、Meltype が覚えた語です。F10 (英字にする)・F6 (ひらがなにする) などで直すと増えます。間違って覚えた語は、ここで直すか削除してください。")
        explanation.setAccessibilityLabel("この一覧の説明")

        searchField.placeholderString = "語で検索"
        searchField.setAccessibilityLabel("学習した語を検索")
        searchField.target = self
        searchField.action = #selector(searchChanged(_:))
        searchField.delegate = self
        UI.configure(search: searchField)
        searchField.translatesAutoresizingMaskIntoConstraints = false
        searchField.widthAnchor.constraint(greaterThanOrEqualToConstant: 240).isActive = true
        countLabel.alignment = .right

        tableView.allowsMultipleSelection = true
        tableView.usesAlternatingRowBackgroundColors = true
        tableView.style = .fullWidth
        tableView.addTableColumn(UI.column("word", title: "語 (英字)", width: 220, sortKey: "word"))
        tableView.addTableColumn(UI.column("english", title: "扱い", width: 90, sortKey: "english"))
        tableView.addTableColumn(UI.column("count", title: "回数", width: 60, sortKey: "count", ascending: false))
        tableView.addTableColumn(UI.column("used", title: "最後に使った日", width: 160, sortKey: "used", ascending: false))
        tableView.addTableColumn(UI.column("state", title: "状態", width: 170, sortKey: "state"))
        tableView.dataSource = self
        tableView.delegate = self
        tableView.target = self
        tableView.doubleAction = #selector(editSelected(_:))
        tableView.setAccessibilityLabel("学習した語の一覧")
        tableView.onDelete = { [weak self] in self?.deleteSelected(nil) }
        tableView.onActivate = { [weak self] in self?.editSelected(nil) }
        tableView.contextMenu = { [weak self] in self?.rowMenu() }
        let scroll = NSScrollView()
        scroll.documentView = tableView
        scroll.hasVerticalScroller = true
        scroll.borderType = .bezelBorder
        scroll.translatesAutoresizingMaskIntoConstraints = false

        emptyLabel.alignment = .center
        emptyLabel.translatesAutoresizingMaskIntoConstraints = false
        scroll.addSubview(emptyLabel)

        addButton = UI.button("追加…", target: self, action: #selector(addWord(_:)))
        addButton.setAccessibilityHelp("英字のまま / かなにする語を、自分で足します")
        editButton = UI.button("編集…", target: self, action: #selector(editSelected(_:)))
        editButton.setAccessibilityHelp("選んだ語のつづり・扱いを直します (Return)")
        deleteButton = UI.button("削除…", target: self, action: #selector(deleteSelected(_:)))
        deleteButton.setAccessibilityHelp("選んだ語を忘れさせます (Delete。元に戻せません)")

        let top = NSStackView(views: [searchField, UI.spacer(), countLabel])
        let bottom = NSStackView(views: [addButton, editButton, deleteButton, UI.spacer()])
        statusLabel.setAccessibilityLabel("状態")
        let stack = NSStackView(views: [explanation, top, scroll, bottom, statusLabel])
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 8
        stack.edgeInsets = NSEdgeInsets(top: 12, left: 16, bottom: 12, right: 16)
        stack.translatesAutoresizingMaskIntoConstraints = false
        let root = NSView(frame: NSRect(x: 0, y: 0, width: 820, height: 540))
        root.addSubview(stack)
        for view in [explanation, top, scroll, bottom, statusLabel] {
            view.translatesAutoresizingMaskIntoConstraints = false
            view.widthAnchor.constraint(equalTo: stack.widthAnchor, constant: -32).isActive = true
        }
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: root.leadingAnchor),
            stack.trailingAnchor.constraint(equalTo: root.trailingAnchor),
            stack.topAnchor.constraint(equalTo: root.topAnchor),
            stack.bottomAnchor.constraint(equalTo: root.bottomAnchor),
            scroll.heightAnchor.constraint(greaterThanOrEqualToConstant: 200),
            emptyLabel.centerXAnchor.constraint(equalTo: scroll.centerXAnchor),
            emptyLabel.centerYAnchor.constraint(equalTo: scroll.centerYAnchor),
            emptyLabel.widthAnchor.constraint(lessThanOrEqualTo: scroll.widthAnchor, constant: -40),
        ])
        scroll.setContentHuggingPriority(.init(1), for: .vertical)
        view = root
        tableView.sortDescriptors = [NSSortDescriptor(key: "used", ascending: false)]
    }

    override func viewDidLoad() {
        super.viewDidLoad()
        reload()
    }

    /// ⌘F: 検索の欄へ。
    func focusSearch() {
        window?.makeFirstResponder(searchField)
    }

    // ---- 読み込み ----

    /// IME (ほかのプロセス) が覚えた・消したら読み直す (1.5 秒ごと・アプリが前に出たとき)。
    func pollChanges() {
        guard isViewLoaded else { return }
        if operations.learnedVersion() != lastVersion { reload() }
    }

    /// 一覧を読み直す。選んでいた語・検索の文字はそのまま。
    func reload() {
        let version = operations.learnedVersion()
        // 読めなくても版は覚える (1.5 秒ごとのポーリングで、同じ版を読み直して同じ知らせを繰り返さないように)
        lastVersion = version
        guard let words = operations.learnedWords() else {
            if failedVersion != version {
                showStatus("学習した語を読めませんでした。データフォルダの languages.json を確認してください。")
            }
            failedVersion = version
            return
        }
        failedVersion = nil
        let selected = selectedWords()
        list.replace(words)
        tableView.reloadData()
        UI.select(Set(selected), in: tableView, rows: list.visible.map(\.word))
        updateState()
    }

    private func applyList() {
        let selected = selectedWords()
        list.apply()
        tableView.reloadData()
        UI.select(Set(selected), in: tableView, rows: list.visible.map(\.word))
        updateState()
    }

    private func updateState() {
        let total = list.all.count
        let shown = list.visible.count
        countLabel.stringValue = shown == total ? "全 \(total) 語" : "\(shown) / 全 \(total) 語"
        if total == 0 {
            emptyLabel.stringValue = "学習した語はまだありません。F10・F6 などで英字/かなを直すと、ここに増えます。"
        } else if shown == 0 {
            emptyLabel.stringValue = "見つかりません"
        }
        emptyLabel.isHidden = shown > 0
        let count = tableView.selectedRowIndexes.count
        editButton.isEnabled = count == 1
        deleteButton.isEnabled = count > 0
    }

    private func selectedWords() -> [String] {
        tableView.selectedRowIndexes.compactMap { $0 < list.visible.count ? list.visible[$0].word : nil }
    }

    private func showStatus(_ message: String) {
        statusLabel.stringValue = message
        UI.announce(message, in: window)
    }

    private static func kind(_ english: Bool) -> String { english ? "英字のまま" : "かなにする" }

    // ---- 一覧 (NSTableView) ----

    func numberOfRows(in tableView: NSTableView) -> Int { list.visible.count }

    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        guard let column = tableColumn, row < list.visible.count else { return nil }
        let entry = list.visible[row]
        let cell = UI.cell(in: tableView, identifier: column.identifier)
        guard let text = cell.textField else { return cell }
        text.alignment = .left
        text.textColor = .labelColor
        switch column.identifier.rawValue {
        case "word":
            text.stringValue = entry.word
        case "english":
            text.stringValue = entry.english ? "英字" : "かな"
            text.setAccessibilityLabel(Self.kind(entry.english))
        case "count":
            text.stringValue = "\(entry.count)"
            text.alignment = .right
            text.textColor = .secondaryLabelColor
            text.setAccessibilityLabel("\(entry.count) 回")
        case "used":
            text.stringValue = entry.used == .distantPast ? "不明" : dateFormatter.string(from: entry.used)
            text.textColor = .secondaryLabelColor
        default:
            text.stringValue = entry.active ? "有効" : "2 回目を待っています"
            text.textColor = entry.active ? .labelColor : .secondaryLabelColor
        }
        return cell
    }

    func tableView(_ tableView: NSTableView, sortDescriptorsDidChange oldDescriptors: [NSSortDescriptor]) {
        guard let descriptor = tableView.sortDescriptors.first, let key = descriptor.key, let column = LearnedSortColumn(rawValue: key) else { return }
        list.column = column
        list.ascending = descriptor.ascending
        applyList()
    }

    func tableViewSelectionDidChange(_ notification: Notification) { updateState() }

    /// 検索欄の✕ボタン・Return。
    @objc private func searchChanged(_ sender: Any?) {
        list.query = searchField.stringValue
        applyList()
        tableView.scrollRowToVisible(0)
    }

    /// 1 文字打つたびに絞り込む (語は多くても数千なので、間引かない)。
    func controlTextDidChange(_ notification: Notification) {
        guard (notification.object as? NSSearchField) === searchField else { return }
        searchChanged(nil)
    }

    /// 検索の欄で ↓ を押したら一覧へ。Esc で検索の文字を消す。
    func control(_ control: NSControl, textView: NSTextView, doCommandBy commandSelector: Selector) -> Bool {
        if control === searchField, commandSelector == #selector(NSResponder.cancelOperation(_:)), !searchField.stringValue.isEmpty {
            searchField.stringValue = ""
            searchChanged(nil)
            return true
        }
        guard control === searchField, commandSelector == #selector(NSResponder.moveDown(_:)), !list.visible.isEmpty else { return false }
        window?.makeFirstResponder(tableView)
        if tableView.selectedRowIndexes.isEmpty { tableView.selectRowIndexes(IndexSet(integer: 0), byExtendingSelection: false) }
        return true
    }

    private func rowMenu() -> NSMenu? {
        let menu = NSMenu()
        menu.addItem(withTitle: "編集…", action: #selector(editSelected(_:)), keyEquivalent: "").target = self
        menu.addItem(withTitle: "削除…", action: #selector(deleteSelected(_:)), keyEquivalent: "").target = self
        menu.addItem(withTitle: "コピー", action: #selector(copy(_:)), keyEquivalent: "").target = self
        return menu
    }

    func validateMenuItem(_ menuItem: NSMenuItem) -> Bool {
        switch menuItem.action {
        case #selector(editSelected(_:)): return tableView.selectedRowIndexes.count == 1
        case #selector(deleteSelected(_:)), #selector(copy(_:)), #selector(delete(_:)): return !tableView.selectedRowIndexes.isEmpty
        default: return true
        }
    }

    // ---- 変更 ----

    /// 足した・直した語を選んで見える位置へ。検索で隠れていれば、絞り込みは解かずにその旨を出す。
    private func didSave(_ word: String, message: String) {
        reload()
        if list.visible.contains(where: { $0.word == word }) {
            tableView.deselectAll(nil)
            UI.select([word], in: tableView, rows: list.visible.map(\.word), scroll: true)
            showStatus(message)
        } else {
            showStatus(message + " (検索条件に合わないため表示されていません)")
        }
    }

    @objc func addWord(_ sender: Any?) {
        guard let window else { return }
        // 検索欄に打った語があれば、それを初めから入れておく (探して無かった語をそのまま足せるように)。
        // 全角英字・大文字は一覧の検索と同じ SearchText でそろえる (全角のまま入れると「英字だけ」の理由が出るため)
        let typed = SearchText.normalize(searchField.stringValue).trimmingCharacters(in: .whitespacesAndNewlines)
        let editor = LearnedWordEditor(title: "学習した語を追加", confirmTitle: "追加", word: typed, english: true,
                                       validate: { [operations] in operations.checkLearned($0, except: nil) },
                                       submit: { [weak self] word, english in
                                           guard let self else { return nil }
                                           if let error = self.operations.setLearned(word, english: english, oldWord: nil) { return error }
                                           self.didSave(word, message: "「\(word)」を「\(Self.kind(english))」で追加しました")
                                           return nil
                                       })
        editor.begin(on: window)
    }

    @objc func editSelected(_ sender: Any?) {
        let words = selectedWords()
        guard words.count == 1, let original = list.visible.first(where: { $0.word == words[0] }), let window else { return }
        let editor = LearnedWordEditor(title: "学習した語を編集", confirmTitle: "保存", word: original.word, english: original.english,
                                       validate: { [operations] in operations.checkLearned($0, except: original.word) },
                                       submit: { [weak self] word, english in
                                           guard let self else { return nil }
                                           if word == original.word && english == original.english { return nil }   // 何も変えていない
                                           if let error = self.operations.setLearned(word, english: english, oldWord: original.word) { return error }
                                           self.didSave(word, message: "「\(word)」(\(Self.kind(english))) に直しました")
                                           return nil
                                       })
        editor.begin(on: window)
    }

    /// 「編集」メニューの「削除」(一覧を選んでいるとき)。
    @objc func delete(_ sender: Any?) { deleteSelected(sender) }

    @objc func deleteSelected(_ sender: Any?) {
        let words = selectedWords()
        guard !words.isEmpty, let window else { return }
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = words.count == 1 ? "「\(words[0])」を学習した語から削除しますか？" : "選んだ \(words.count) 語を学習した語から削除しますか？"
        alert.informativeText = "削除は元に戻せません。次にその語を打ったときは、学習する前と同じ判定に戻ります。"
        // 元に戻せない操作なので、Return で押される先頭のボタンはキャンセルにする (ユーザー辞書の削除と同じ方針)
        alert.addButton(withTitle: "キャンセル")
        let delete = alert.addButton(withTitle: "削除")
        delete.hasDestructiveAction = true
        delete.keyEquivalent = ""
        alert.beginSheetModal(for: window) { [weak self] response in
            guard let self, response == .alertSecondButtonReturn else { return }
            let error = self.operations.removeLearned(words)
            self.reload()
            if let error {
                UI.showError(error, title: "削除できませんでした", in: window)
                return
            }
            self.showStatus(words.count == 1 ? "「\(words[0])」を削除しました" : "\(words.count) 語を削除しました")
        }
    }

    /// 選んだ語を 1 行 1 語でコピーする。
    @objc func copy(_ sender: Any?) {
        let words = selectedWords()
        guard !words.isEmpty else { return }
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(words.joined(separator: "\n"), forType: .string)
        showStatus("\(words.count) 語をコピーしました")
    }
}

/// 学習した語の追加・編集のシート: 語 (英字) と、扱い (英字のまま / かなにする)。
/// 入力のたびに本体で確かめ (LanguageMemory.Check)、理由を赤字で出して、だめなら確定のボタンを押せなくする。
final class LearnedWordEditor: NSObject, NSTextFieldDelegate {
    private let validate: (String) -> String?
    private let submit: (String, Bool) -> String?
    private let sheet: NSWindow
    private let wordField = NSTextField()
    private let kindControl = NSSegmentedControl(labels: ["英字のまま", "かなにする"], trackingMode: .selectOne, target: nil, action: nil)
    private let messageLabel = NSTextField(wrappingLabelWithString: "")
    private var confirmButton: NSButton!
    private weak var parent: NSWindow?
    /// シートを出している間、自分を持っておく (呼び出し側は持たなくてよい)。
    private var keepAlive: LearnedWordEditor?
    private var lastMessage = ""

    init(title: String, confirmTitle: String, word: String, english: Bool, validate: @escaping (String) -> String?, submit: @escaping (String, Bool) -> String?) {
        self.validate = validate
        self.submit = submit
        sheet = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 460, height: 200), styleMask: [.titled], backing: .buffered, defer: true)
        super.init()
        sheet.title = title
        sheet.isRestorable = false
        build(title: title, confirmTitle: confirmTitle, word: word, english: english)
    }

    private func build(title: String, confirmTitle: String, word: String, english: Bool) {
        let heading = NSTextField(labelWithString: title)
        heading.font = .boldSystemFont(ofSize: NSFont.systemFontSize + 1)
        let explanation = UI.wrapping("手で足した語・直した語は、すぐ効きます (2 回目を待ちません)。")
        explanation.preferredMaxLayoutWidth = 420

        wordField.stringValue = word
        wordField.placeholderString = "英字 2 文字以上 (例 github)"
        wordField.setAccessibilityLabel("語 (英字 2 文字以上)")
        wordField.delegate = self
        wordField.translatesAutoresizingMaskIntoConstraints = false
        wordField.widthAnchor.constraint(greaterThanOrEqualToConstant: 260).isActive = true
        wordField.lineBreakMode = .byTruncatingTail
        wordField.usesSingleLineMode = true
        kindControl.selectedSegment = english ? 0 : 1
        kindControl.setAccessibilityLabel("扱い")
        kindControl.target = self
        kindControl.action = #selector(kindChanged(_:))

        let grid = NSGridView(views: [
            [UI.label("語"), wordField],
            [UI.label("扱い"), kindControl],
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
        confirmButton = UI.button(confirmTitle, target: self, action: #selector(confirm(_:)))
        confirmButton.keyEquivalent = "\r"
        let buttons = NSStackView(views: [UI.spacer(), cancel, confirmButton])
        buttons.orientation = .horizontal

        let stack = NSStackView(views: [heading, explanation, grid, messageLabel, buttons])
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
        sheet.initialFirstResponder = wordField
        refresh()
    }

    func begin(on window: NSWindow) {
        parent = window
        keepAlive = self
        window.beginSheet(sheet) { [weak self] _ in self?.keepAlive = nil }
        sheet.makeFirstResponder(sheet.initialFirstResponder)
    }

    /// 本体が保存する形 (前後の空白を取り、小文字) にそろえる。追加したあとに同じ語を選ぶため。
    private var currentWord: String { wordField.stringValue.trimmingCharacters(in: .whitespacesAndNewlines).lowercased() }
    private var english: Bool { kindControl.selectedSegment == 0 }

    func controlTextDidChange(_ notification: Notification) { refresh() }

    @objc private func kindChanged(_ sender: Any?) { refresh() }

    private func refresh() {
        let word = currentWord
        let error = word.isEmpty ? nil : validate(word)
        confirmButton.isEnabled = !word.isEmpty && error == nil
        show(error ?? "")
    }

    private func show(_ message: String) {
        messageLabel.stringValue = message
        messageLabel.isHidden = message.isEmpty
        if message != lastMessage { UI.announce(message, in: sheet) }
        lastMessage = message
    }

    @objc private func confirm(_ sender: Any?) {
        let word = currentWord
        guard !word.isEmpty else { return }
        if let error = validate(word) ?? submit(word, english) {
            show(error)
            return
        }
        close()
    }

    @objc private func cancel(_ sender: Any?) { close() }

    private func close() {
        parent?.endSheet(sheet)
    }
}
