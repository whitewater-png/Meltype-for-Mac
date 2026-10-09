// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import AppKit
import MeltypeDictionaryKit
import UniformTypeIdentifiers

/// 「ユーザー辞書」のタブ: 一覧 (読み・単語・登録順。検索・並べ替え・複数選択)、登録・編集・削除 (確認あり・⌘Z で元に戻す)、取り込み・書き出し。
/// 変更はすぐ userdict.txt に保存され (本体がロックの中で読み直してから書く)、動いている IME にもすぐ反映される。
final class UserDictionaryViewController: NSViewController, NSTableViewDataSource, NSTableViewDelegate, NSSearchFieldDelegate, NSMenuItemValidation {
    private let dictionary: NativeDictionary
    private let list = RowList<UserEntry>(sort: SortSpec(column: .order, ascending: false))
    private let searchField = NSSearchField()
    private let searchDebouncer = SearchDebouncer()
    private let tableView = KeyTableView()
    private let countLabel = UI.label(secondary: true)
    private let statusLabel = UI.label(secondary: true)
    private let emptyLabel = UI.label(secondary: true, size: 13)
    private var editButton: NSButton!
    private var deleteButton: NSButton!
    private var moveButton: NSButton!
    private var lastVersion: Int32 = .min
    private let problemLabel = UI.wrapping("", secondary: false)
    /// ほかのタブにも変更を知らせる (専門用語集の「直す」でユーザー辞書が変わるなど)。
    var didChange: (() -> Void)?
    /// このタブを入れているウインドウ (タブを切り替えると view.window が nil になるので、シート・取り消しはこちらを使う)。
    weak var hostWindow: NSWindow?
    private var window: NSWindow? { view.window ?? hostWindow }

    init(dictionary: NativeDictionary) {
        self.dictionary = dictionary
        super.init(nibName: nil, bundle: nil)
        title = "ユーザー辞書"
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) is not supported") }

    // ---- 画面 ----

    override func loadView() {
        searchField.placeholderString = "読み・単語で検索"
        searchField.setAccessibilityLabel("ユーザー辞書を検索 (読み・単語)")
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
        tableView.addTableColumn(UI.column("reading", title: "読み", width: 220, sortKey: "reading"))
        tableView.addTableColumn(UI.column("word", title: "単語", width: 320, sortKey: "word"))
        tableView.addTableColumn(UI.column("order", title: "登録順", width: 70, sortKey: "order", ascending: false))
        tableView.dataSource = self
        tableView.delegate = self
        tableView.target = self
        tableView.doubleAction = #selector(editSelected(_:))
        tableView.setAccessibilityLabel("ユーザー辞書の単語の一覧")
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

        let add = UI.button("登録…", target: self, action: #selector(addWord(_:)))
        add.setAccessibilityHelp("新しい単語をユーザー辞書に登録します (⌘N)")
        editButton = UI.button("編集…", target: self, action: #selector(editSelected(_:)))
        editButton.setAccessibilityHelp("選んだ単語を直します (Return)")
        deleteButton = UI.button("削除…", target: self, action: #selector(deleteSelected(_:)))
        deleteButton.setAccessibilityHelp("選んだ単語を削除します (Delete。⌘Z で元に戻せます)")
        moveButton = UI.button("専門用語集へ移す…", target: self, action: #selector(moveToTermDomain(_:)))
        moveButton.setAccessibilityHelp("選んだ語を、自作の専門用語集へ移します (ユーザー辞書からは消えます。⌘Z で元に戻せます)")
        let importButton = UI.button("取り込む…", target: self, action: #selector(importDictionary(_:)))
        importButton.setAccessibilityHelp("Microsoft IME・Google 日本語入力の書き出したファイルや、Meltype の userdict.txt を取り込みます")
        let exportButton = UI.button("書き出す…", target: self, action: #selector(exportDictionary(_:)))
        exportButton.setAccessibilityHelp("Microsoft IME の形式で書き出します (Google 日本語入力・ATOK でも取り込めます)")

        let top = NSStackView(views: [searchField, UI.spacer(), countLabel])
        let bottom = NSStackView(views: [add, editButton, deleteButton, moveButton, UI.spacer(), importButton, exportButton])
        statusLabel.setAccessibilityLabel("状態")
        // userdict.txt を読めない (文字コード・権限)・大きすぎて読み込んでいないときの警告 (ふだんは隠す)
        problemLabel.textColor = .systemOrange
        problemLabel.font = .systemFont(ofSize: NSFont.smallSystemFontSize)
        problemLabel.setAccessibilityLabel("ユーザー辞書の警告")
        problemLabel.isHidden = true
        let stack = NSStackView(views: [problemLabel, top, scroll, bottom, statusLabel])
        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 8
        stack.edgeInsets = NSEdgeInsets(top: 12, left: 16, bottom: 12, right: 16)
        stack.translatesAutoresizingMaskIntoConstraints = false
        let root = NSView(frame: NSRect(x: 0, y: 0, width: 820, height: 540))
        root.addSubview(stack)
        for view in [problemLabel, top, scroll, bottom, statusLabel] {
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
        tableView.sortDescriptors = [NSSortDescriptor(key: "order", ascending: false)]
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

    /// IME などほかのプロセスが変えていたら読み直す (1.5 秒ごと・アプリが前に出たとき)。
    func pollChanges() {
        if dictionary.userVersion() != lastVersion { reload() }
    }

    /// 一覧を読み直す。選んでいた語は選んだまま。
    func reload() {
        let version = dictionary.userVersion()
        guard let entries = dictionary.userEntries() else {
            showStatus("ユーザー辞書を読めませんでした。データフォルダの userdict.txt を確認してください。")
            return
        }
        lastVersion = version
        updateProblem()
        let selected = selectedKeys()
        list.replace(entries)
        tableView.reloadData()
        UI.select(Set(selected), in: tableView, rows: list.visible.map(\.key))
        updateState()
    }

    /// 読めない・大きすぎるファイルなら警告を出す (一覧が 0 語に見えても、ファイルの中身が消えたわけではないことを伝える)。
    private func updateProblem() {
        let problem = dictionary.userProblem()
        let message = problem.map { "注意: \($0)" } ?? ""
        if message != problemLabel.stringValue, !message.isEmpty { UI.announce(problem ?? "", in: window) }
        problemLabel.stringValue = message
        problemLabel.isHidden = problem == nil
    }

    private func applyList(keepSelection: Bool = true) {
        let selected = keepSelection ? selectedKeys() : []
        list.apply()
        tableView.reloadData()
        UI.select(Set(selected), in: tableView, rows: list.visible.map(\.key))
        updateState()
    }

    private func updateState() {
        let total = list.all.count
        let shown = list.visible.count
        countLabel.stringValue = shown == total ? "全 \(total) 語" : "\(shown) / 全 \(total) 語"
        if total == 0 {
            emptyLabel.stringValue = "まだ単語がありません。「登録…」(⌘N) で登録できます。"
        } else if shown == 0 {
            emptyLabel.stringValue = "見つかりません"
        }
        emptyLabel.isHidden = shown > 0
        let count = tableView.selectedRowIndexes.count
        editButton.isEnabled = count == 1
        deleteButton.isEnabled = count > 0
        moveButton.isEnabled = count > 0
    }

    /// 語を選んでいるか (メニューの「専門用語集へ移す…」を使えるか)。
    var hasSelection: Bool { !tableView.selectedRowIndexes.isEmpty }

    private func selectedKeys() -> [WordKey] {
        tableView.selectedRowIndexes.compactMap { $0 < list.visible.count ? list.visible[$0].key : nil }
    }

    private func showStatus(_ message: String) {
        statusLabel.stringValue = message
        UI.announce(message, in: window)
    }

    // ---- 一覧 (NSTableView) ----

    func numberOfRows(in tableView: NSTableView) -> Int { list.visible.count }

    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        guard let column = tableColumn, row < list.visible.count else { return nil }
        let entry = list.visible[row]
        let cell = UI.cell(in: tableView, identifier: column.identifier)
        guard let text = cell.textField else { return cell }
        switch column.identifier.rawValue {
        case "reading":
            text.stringValue = entry.reading
            text.alignment = .left
            text.textColor = .labelColor
        case "word":
            text.stringValue = entry.word
            text.alignment = .left
            text.textColor = .labelColor
        default:
            text.stringValue = "\(entry.order + 1)"
            text.alignment = .right
            text.textColor = .secondaryLabelColor
            text.setAccessibilityLabel("登録順 \(entry.order + 1)")
        }
        return cell
    }

    func tableView(_ tableView: NSTableView, sortDescriptorsDidChange oldDescriptors: [NSSortDescriptor]) {
        guard let descriptor = tableView.sortDescriptors.first, let key = descriptor.key, let column = SortColumn(rawValue: key) else { return }
        list.sort = SortSpec(column: column, ascending: descriptor.ascending)
        applyList()
    }

    func tableViewSelectionDidChange(_ notification: Notification) { updateState() }

    /// 検索欄の✕ボタン・Return。入力途中の絞り込みは controlTextDidChange で済んでいるので、ここは確定として即反映する。
    @objc private func searchChanged(_ sender: Any?) {
        searchDebouncer.cancel()
        list.query = searchField.stringValue
        applyList()
        tableView.scrollRowToVisible(0)
    }

    /// 1 文字打つたびに絞り込む (Enter を待たない)。
    func controlTextDidChange(_ notification: Notification) {
        guard (notification.object as? NSSearchField) === searchField else { return }
        searchDebouncer.schedule(rowCount: list.all.count) { [weak self] in
            guard let self else { return }
            self.list.query = self.searchField.stringValue
            self.applyList()
            self.tableView.scrollRowToVisible(0)
        }
    }

    /// 検索の欄で ↓ を押したら一覧へ。
    func control(_ control: NSControl, textView: NSTextView, doCommandBy commandSelector: Selector) -> Bool {
        // Esc: 検索の文字を消して全部の語に戻す (空のときは何もせず、ほかの処理に任せる)
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
        menu.addItem(withTitle: "専門用語集へ移す…", action: #selector(moveToTermDomain(_:)), keyEquivalent: "").target = self
        menu.addItem(withTitle: "コピー", action: #selector(copy(_:)), keyEquivalent: "").target = self
        return menu
    }

    func validateMenuItem(_ menuItem: NSMenuItem) -> Bool {
        switch menuItem.action {
        case #selector(editSelected(_:)): return tableView.selectedRowIndexes.count == 1
        case #selector(deleteSelected(_:)), #selector(copy(_:)), #selector(delete(_:)), #selector(moveToTermDomain(_:)): return !tableView.selectedRowIndexes.isEmpty
        default: return true
        }
    }

    // ---- 変更 (元に戻せる) ----

    /// 変更を行い、元に戻す操作を積む。だめなら理由を返す (呼び出し側が出す)。
    @discardableResult
    private func perform(_ change: DictionaryChange, message: String, hiddenMessage: String? = nil, select: [WordKey] = []) -> String? {
        let result = change.perform(on: dictionary)
        if let error = result.error {
            reload()
            return error
        }
        if let undo = result.undo { registerUndo(undo, name: change.actionName) }
        reload()
        // 登録・編集した語を選んで、見える位置までスクロールする。検索で絞り込み中で表示対象外なら、絞り込みは解除せず、その旨を知らせる
        var shownMessage = message
        if !select.isEmpty {
            let visibleKeys = list.visible.map(\.key)
            if select.contains(where: visibleKeys.contains) {
                // 前の選択を残さず、登録した語だけを選ぶ
                tableView.deselectAll(nil)
                UI.select(Set(select), in: tableView, rows: visibleKeys, scroll: true)
            } else if let hiddenMessage {
                shownMessage = hiddenMessage
            }
        }
        showStatus(shownMessage)
        didChange?()
        return nil
    }

    /// 元に戻す操作を積む。undoManager はウインドウのもの (タブを切り替えて、このタブの view がウインドウから外れていても同じものを使う。
    /// 取り消しの中でやり直しを積むときも、引数で渡された同じ undoManager に積む)。
    private func registerUndo(_ change: DictionaryChange, name: String, on manager: UndoManager? = nil) {
        guard let undoManager = manager ?? window?.undoManager else { return }
        // undoManager は弱く持つ (積んだ操作が undoManager を持ち、undoManager が操作を持つ循環を作らない)
        undoManager.registerUndo(withTarget: self) { [weak undoManager] target in
            let undoing = undoManager?.isUndoing ?? true
            let result = change.perform(on: target.dictionary)
            if let redo = result.undo, let undoManager { target.registerUndo(redo, name: name, on: undoManager) }
            target.reload()
            target.didChange?()
            if let error = result.error {
                UI.showError(error, title: undoing ? "元に戻せませんでした" : "やり直せませんでした", in: target.window)
                return
            }
            target.showStatus(undoing ? "\(name)を取り消しました" : "\(name)をやり直しました")
        }
        undoManager.setActionName(name)
    }

    @objc func addWord(_ sender: Any?) {
        guard let window else { return }
        let editor = WordEditor(.init(title: "単語を登録", explanation: "登録した語は、変換で最優先に使われます (専門用語集・変換エンジンより先)。", confirmTitle: "登録", key: WordKey(reading: "", word: "")),
                                validate: { [dictionary] in dictionary.check($0, except: nil) },
                                submit: { [weak self] key in self?.perform(.add(key), message: "「\(key.word)」(\(key.reading)) を登録しました", hiddenMessage: "登録しました (検索条件に合わないため表示されていません)", select: [key]) },
                                toReading: { [dictionary] in dictionary.toReading($0) })
        editor.begin(on: window)
    }

    @objc func editSelected(_ sender: Any?) {
        let keys = selectedKeys()
        guard keys.count == 1, let original = keys.first, let window else { return }
        let editor = WordEditor(.init(title: "単語を編集", explanation: nil, confirmTitle: "保存", key: original),
                                validate: { [dictionary] in dictionary.check($0, except: original) },
                                submit: { [weak self] key in self?.perform(.update(from: original, to: key), message: "「\(key.word)」(\(key.reading)) に直しました", hiddenMessage: "直しました (検索条件に合わないため表示されていません)", select: [key]) },
                                toReading: { [dictionary] in dictionary.toReading($0) })
        editor.begin(on: window)
    }

    /// 「編集」メニューの「削除」(一覧を選んでいるとき)。
    @objc func delete(_ sender: Any?) { deleteSelected(sender) }

    @objc func deleteSelected(_ sender: Any?) {
        let keys = selectedKeys()
        guard !keys.isEmpty, let window else { return }
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = keys.count == 1 ? "「\(keys[0].word)」(\(keys[0].reading)) を削除しますか？" : "選んだ \(keys.count) 語を削除しますか？"
        alert.informativeText = "「編集」→「取り消す」(⌘Z) で元に戻せます。削除の直前の内容は、データフォルダの userdict.txt.bak にも残ります。"
        // 取り消せなくなりうる操作なので、Return で押される先頭のボタンはキャンセルにする (入力メニューの「学習データをすべて消去」と同じ方針)。
        alert.addButton(withTitle: "キャンセル")
        let delete = alert.addButton(withTitle: "削除")
        delete.hasDestructiveAction = true
        delete.keyEquivalent = ""
        alert.beginSheetModal(for: window) { [weak self] response in
            guard let self, response == .alertSecondButtonReturn else { return }
            if let error = self.perform(.remove(keys), message: keys.count == 1 ? "「\(keys[0].word)」を削除しました (⌘Z で元に戻せます)" : "\(keys.count) 語を削除しました (⌘Z で元に戻せます)") {
                UI.showError(error, title: "削除できませんでした", in: window)
            }
        }
    }

    // ---- 専門用語集へ移す ----

    /// 選んだ語を、自作の専門用語集へ移す (先を選ぶシート → 本体が検めて移す。専門用語集に入れられない語はユーザー辞書に残して、あとで知らせる)。
    @objc func moveToTermDomain(_ sender: Any?) {
        let keys = selectedKeys()
        guard !keys.isEmpty, let window else { return }
        let domains = dictionary.termDomains().filter(\.isUser)
        TermSheets.chooseMoveTarget(count: keys.count, domains: domains, in: window) { [weak self] choice in
            guard let self else { return nil }
            let id: String
            let name: String
            var wasDisabled = false
            var newDomainId: String?
            switch choice {
            case let .existing(domain):
                id = domain.id
                name = domain.name
                wasDisabled = !domain.enabled
            case let .new(newName):
                let created = self.dictionary.createDomain(name: newName)
                if let error = created.error { return error }
                guard let createdId = created.id else { return "専門用語集を作れませんでした。" }
                id = createdId
                name = newName
                newDomainId = id
            }
            self.performMove(id: id, name: name, keys: keys, wasDisabled: wasDisabled, createdId: newDomainId)
            return nil
        }
    }

    /// createdId: 移すために新しく作った分野 (既存の分野へ移すときは nil)。1 語も入らなければ消し、入ったなら ⌘Z 1 回で語と一緒に消せるようにする。
    private func performMove(id: String, name: String, keys: [WordKey], wasDisabled: Bool, createdId: String? = nil) {
        let result = dictionary.moveToDomain(id: id, keys: keys)
        let outcome = result.outcome
        let actionName = DictionaryChange.moveToDomain(id: id, keys: keys).actionName
        // 専門用語集に書けたのにユーザー辞書から消せなかったとき (result.error) も、足した語を取り消せるようにしておく
        let undoable = !outcome.added.isEmpty || !outcome.removed.isEmpty
        if undoable {
            if let createdId, let undoManager = window?.undoManager {
                // 取り消しは後に積んだものから戻る: 語を戻してから、作った分野を消す (1 回の ⌘Z にまとめる)
                undoManager.beginUndoGrouping()
                registerUndo(.deleteDomain(id: createdId), name: actionName, on: undoManager)
                registerUndo(.undoMove(id: id, added: outcome.added, removed: outcome.removed), name: actionName, on: undoManager)
                undoManager.endUndoGrouping()
            } else {
                registerUndo(.undoMove(id: id, added: outcome.added, removed: outcome.removed), name: actionName)
            }
        } else if let createdId {
            // 1 語も入らなかった (移せる語が無い・書けなかった) ので、移すために作った空の分野は残さない
            _ = dictionary.deleteDomain(id: createdId)
        }
        reload()
        didChange?()
        if let error = result.error {
            UI.showError(error, title: "移し終えられませんでした", in: window)
            return
        }
        // ⌘Z の案内は、取り消しを積んだときだけ出す
        var message = outcome.removed.isEmpty
            ? "専門用語集「\(name)」へ移せる語はありませんでした"
            : "\(outcome.removed.count) 語を専門用語集「\(name)」へ移しました\(undoable ? " (⌘Z で元に戻せます)" : "")"
        if wasDisabled && !outcome.removed.isEmpty { message += "。使わない設定だったので、使う設定にしました" }
        if !outcome.skipped.isEmpty { message += "。\(outcome.skipped.count) 語は移せずユーザー辞書に残しました" }
        showStatus(message)
        if !outcome.skipped.isEmpty, let window {
            let alert = NSAlert()
            alert.alertStyle = .warning
            alert.messageText = "\(outcome.skipped.count) 語は専門用語集に入れられないため、ユーザー辞書に残しました"
            alert.informativeText = TermSheets.skippedSummary(outcome.skipped)
            alert.beginSheetModal(for: window)
        }
    }

    /// 選んだ語を「読み<Tab>単語」の行でコピーする。
    @objc func copy(_ sender: Any?) {
        let keys = selectedKeys()
        guard !keys.isEmpty else { return }
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(FfiFormat.formatKeys(keys), forType: .string)
        showStatus("\(keys.count) 語をコピーしました")
    }

    // ---- 取り込み・書き出し ----

    @objc func importDictionary(_ sender: Any?) {
        guard let window else { return }
        let panel = NSOpenPanel()
        panel.title = "ほかの辞書を取り込む"
        panel.message = "Microsoft IME・Google 日本語入力で書き出した辞書のファイル、または Meltype の userdict.txt を選んでください (UTF-8 か UTF-16)。"
        panel.prompt = "取り込む"
        panel.canChooseFiles = true
        panel.canChooseDirectories = false
        panel.allowsMultipleSelection = false
        panel.beginSheetModal(for: window) { [weak self] response in
            guard let self, response == .OK, let url = panel.url else { return }
            if let problem = UI.regularFileProblem(url) {
                UI.showError(problem, title: "取り込めませんでした", in: window)
                return
            }
            let result = self.dictionary.importFile(path: url.path)
            if let error = result.error {
                UI.showError(error, title: "取り込めませんでした", in: window)
                return
            }
            self.reload()
            // 取り込みで新しく登録した語 (本体が返したもの) を、まとめて取り消せるようにする
            let added = result.added
            if !added.isEmpty { self.registerUndo(.remove(added), name: "取り込み", on: window.undoManager) }
            self.didChange?()
            let message = result.summary?.message ?? "取り込みました。"
            self.showStatus(message.replacingOccurrences(of: "\n", with: " / "))
            let alert = NSAlert()
            alert.messageText = "辞書を取り込みました"
            alert.informativeText = message + (added.isEmpty ? "" : "\n\n「編集」→「取り消す」(⌘Z) で、取り込んだ語をまとめて消せます。")
            alert.beginSheetModal(for: window)
        }
    }

    @objc func exportDictionary(_ sender: Any?) {
        guard let window else { return }
        let panel = NSSavePanel()
        panel.title = "ユーザー辞書を書き出す"
        panel.message = "Microsoft IME の形式 (UTF-16) で書き出します。Microsoft IME・Google 日本語入力・ATOK の辞書ツールで取り込めます。"
        panel.prompt = "書き出す"
        let formatter = DateFormatter()
        formatter.dateFormat = "yyyyMMdd"
        panel.nameFieldStringValue = "Meltype-ユーザー辞書-\(formatter.string(from: Date())).txt"
        panel.allowedContentTypes = [.plainText]
        panel.beginSheetModal(for: window) { [weak self] response in
            guard let self, response == .OK, let url = panel.url else { return }
            let result = self.dictionary.exportFile(path: url.path)
            if let error = result.error {
                UI.showError(error, title: "書き出せませんでした", in: window)
                return
            }
            self.showStatus("\(result.count) 語を書き出しました: \(url.lastPathComponent)")
        }
    }
}
