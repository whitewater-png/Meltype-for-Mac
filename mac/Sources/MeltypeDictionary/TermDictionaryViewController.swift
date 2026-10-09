// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import AppKit
import MeltypeDictionaryKit

/// 「専門用語集」のタブ: 左に分野 (チェックで ON/OFF)、右にその分野の語 (約 2 万語でも軽い一覧。検索・並べ替え・絞り込み)。
/// 同梱の分野の下に、自分で作った分野 (「自作」の印) が並ぶ。自作の分野は、新規作成・名前の変更・削除 (⌘Z で戻せる)・書き出し・取り込みができ、
/// 語の追加・編集・削除もできる (ユーザー辞書の語は、ユーザー辞書のタブの「専門用語集へ移す…」で入れる)。
/// 同梱の語は書き換えられないので、
///   - 「除外」(Delete) = その語を変換に使わない (terms-excluded.txt に保存。「除外をやめる」・⌘Z で元に戻せる。除外中の語も一覧に出る)
///   - 「直す」(Return) = 元の語を除外して、直した語をユーザー辞書に登録する (ユーザー辞書が専門用語集より優先)
///   - 「複製」 = そのままユーザー辞書にコピーする
final class TermDictionaryViewController: NSViewController, NSTableViewDataSource, NSTableViewDelegate, NSSearchFieldDelegate, NSMenuItemValidation {
    /// 左の一覧の 1 行: 分野か、すべての分野の除外した語。
    private enum Source: Equatable {
        case domain(TermDomainInfo)
        case excluded
    }

    /// 右の一覧の絞り込み。
    private enum Filter: Int {
        case all = 0, used, excluded
    }

    private let dictionary: NativeDictionary
    private var sources: [Source] = []
    private let list = RowList<TermWord>(sort: SortSpec(column: .order, ascending: true))
    private let sourceTable = KeyTableView()
    private let tableView = KeyTableView()
    private let searchField = NSSearchField()
    private let searchDebouncer = SearchDebouncer()
    private let filterPopup = NSPopUpButton()
    private let countLabel = UI.label(secondary: true)
    private let statusLabel = UI.label(secondary: true)
    private let infoLabel = UI.wrapping("")
    private let offLabel = UI.wrapping("", secondary: false)
    private let emptyLabel = UI.label(secondary: true, size: 13)
    private let spinner = NSProgressIndicator()
    private var excludeButton: NSButton!
    private var includeButton: NSButton!
    private var editButton: NSButton!
    private var copyButton: NSButton!
    private var addTermButton: NSButton!
    private var editTermButton: NSButton!
    private var deleteTermButton: NSButton!
    private var bundledButtons: [NSView] = []
    private var userButtons: [NSView] = []
    private var domainActions: NSPopUpButton!
    private var lastRevision: Int32 = .min
    private var loadToken = 0
    private var loadedSource: Source?
    private var loading = false
    var didChange: (() -> Void)?
    /// このタブを入れているウインドウ (タブを切り替えると view.window が nil になるので、シート・取り消しはこちらを使う)。
    weak var hostWindow: NSWindow?
    private var window: NSWindow? { view.window ?? hostWindow }

    init(dictionary: NativeDictionary) {
        self.dictionary = dictionary
        super.init(nibName: nil, bundle: nil)
        title = "専門用語集(サンプル)"
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) is not supported") }

    // ---- 画面 ----

    override func loadView() {
        // 左: 分野
        let sourceColumn = UI.column("source", title: "分野", width: 250, sortKey: nil)
        sourceColumn.resizingMask = .autoresizingMask
        sourceTable.addTableColumn(sourceColumn)
        sourceTable.columnAutoresizingStyle = .uniformColumnAutoresizingStyle
        sourceTable.headerView = nil
        sourceTable.style = .sourceList
        sourceTable.rowHeight = 26
        sourceTable.dataSource = self
        sourceTable.delegate = self
        sourceTable.setAccessibilityLabel("専門用語集の分野 (Space で ON/OFF)")
        sourceTable.onSpace = { [weak self] in self?.toggleSelectedSource() }
        let sourceScroll = NSScrollView()
        sourceScroll.documentView = sourceTable
        sourceScroll.hasVerticalScroller = true
        sourceScroll.translatesAutoresizingMaskIntoConstraints = false
        sourceScroll.widthAnchor.constraint(equalToConstant: 260).isActive = true
        sourceTable.contextMenu = { [weak self] in self?.sourceMenu() }

        // 左の下: 自作の専門用語集の操作 (新規・取り込み・名前の変更・書き出し・削除)
        domainActions = NSPopUpButton(frame: .zero, pullsDown: true)
        domainActions.addItem(withTitle: "自作の専門用語集")
        for (title, action) in [("新しい専門用語集…", #selector(newDomain(_:))), ("ファイルから取り込む…", #selector(importDomain(_:))), (nil, nil),
                                ("名前を変更…", #selector(renameDomain(_:))), ("書き出す…", #selector(exportDomain(_:))), ("削除…", #selector(deleteDomain(_:)))] as [(String?, Selector?)] {
            if let title, let action {
                domainActions.menu?.addItem(withTitle: title, action: action, keyEquivalent: "").target = self
            } else {
                domainActions.menu?.addItem(.separator())
            }
        }
        domainActions.setAccessibilityLabel("自作の専門用語集の操作")
        domainActions.setAccessibilityHelp("専門用語集を新しく作る・ファイルから取り込む・名前を変える・書き出す・削除する")
        domainActions.translatesAutoresizingMaskIntoConstraints = false
        let left = NSStackView(views: [sourceScroll, domainActions])
        left.orientation = .vertical
        left.alignment = .leading
        left.spacing = 8
        left.translatesAutoresizingMaskIntoConstraints = false

        // 右: 語
        let info = infoLabel
        info.stringValue = Self.bundledInfo
        offLabel.textColor = .systemOrange
        offLabel.font = .systemFont(ofSize: NSFont.smallSystemFontSize)
        offLabel.isHidden = true

        searchField.placeholderString = "読み・語・注記で検索"
        searchField.setAccessibilityLabel("専門用語を検索 (読み・語・注記)")
        searchField.target = self
        searchField.action = #selector(searchChanged(_:))
        searchField.delegate = self
        UI.configure(search: searchField)
        searchField.translatesAutoresizingMaskIntoConstraints = false
        searchField.widthAnchor.constraint(greaterThanOrEqualToConstant: 220).isActive = true
        filterPopup.addItems(withTitles: ["すべての語", "使う語だけ", "除外した語だけ"])
        filterPopup.target = self
        filterPopup.action = #selector(filterChanged(_:))
        filterPopup.setAccessibilityLabel("表示する語の絞り込み")
        spinner.style = .spinning
        spinner.controlSize = .small
        spinner.isDisplayedWhenStopped = false
        countLabel.alignment = .right

        tableView.allowsMultipleSelection = true
        tableView.usesAlternatingRowBackgroundColors = true
        tableView.style = .fullWidth
        tableView.addTableColumn(UI.column("reading", title: "読み", width: 200, sortKey: "reading"))
        tableView.addTableColumn(UI.column("word", title: "語", width: 220, sortKey: "word"))
        tableView.addTableColumn(UI.column("note", title: "注記", width: 120, sortKey: "note"))
        tableView.addTableColumn(UI.column("state", title: "状態", width: 70, sortKey: "state"))
        tableView.dataSource = self
        tableView.delegate = self
        tableView.target = self
        tableView.doubleAction = #selector(editSelected(_:))
        tableView.setAccessibilityLabel("専門用語の一覧")
        tableView.onDelete = { [weak self] in self?.deleteKey() }
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

        excludeButton = UI.button("除外する", target: self, action: #selector(excludeSelected(_:)))
        excludeButton.setAccessibilityHelp("選んだ語を変換に使わないようにします (Delete。元に戻せます)")
        includeButton = UI.button("除外をやめる", target: self, action: #selector(includeSelected(_:)))
        includeButton.setAccessibilityHelp("除外した語を、また変換に使うようにします")
        editButton = UI.button("直してユーザー辞書へ…", target: self, action: #selector(editSelected(_:)))
        editButton.setAccessibilityHelp("元の語を除外して、直した語をユーザー辞書に登録します (Return)")
        copyButton = UI.button("ユーザー辞書へ複製", target: self, action: #selector(copyToUserDictionary(_:)))
        copyButton.setAccessibilityHelp("選んだ語を、そのままユーザー辞書に登録します")

        addTermButton = UI.button("語を追加…", target: self, action: #selector(addTerm(_:)))
        addTermButton.setAccessibilityHelp("この専門用語集に語を足します")
        editTermButton = UI.button("編集…", target: self, action: #selector(editSelected(_:)))
        editTermButton.setAccessibilityHelp("選んだ語を直します (Return)")
        deleteTermButton = UI.button("削除…", target: self, action: #selector(deleteTerms(_:)))
        deleteTermButton.setAccessibilityHelp("選んだ語をこの専門用語集から消します (Delete。⌘Z で元に戻せます)")
        bundledButtons = [excludeButton, includeButton, editButton]
        userButtons = [addTermButton, editTermButton, deleteTermButton]
        userButtons.forEach { $0.isHidden = true }
        let top = NSStackView(views: [searchField, filterPopup, spinner, UI.spacer(), countLabel])
        let bottom = NSStackView(views: [excludeButton, includeButton, addTermButton, editTermButton, deleteTermButton, UI.spacer(), editButton, copyButton])
        statusLabel.setAccessibilityLabel("状態")
        let right = NSStackView(views: [info, offLabel, top, scroll, bottom, statusLabel])
        right.orientation = .vertical
        right.alignment = .leading
        right.spacing = 8
        right.translatesAutoresizingMaskIntoConstraints = false
        for view in [info, offLabel, top, scroll, bottom, statusLabel] {
            view.translatesAutoresizingMaskIntoConstraints = false
            view.widthAnchor.constraint(equalTo: right.widthAnchor).isActive = true
        }
        scroll.setContentHuggingPriority(.init(1), for: .vertical)

        let root = NSView(frame: NSRect(x: 0, y: 0, width: 880, height: 560))
        root.addSubview(left)
        root.addSubview(right)
        NSLayoutConstraint.activate([
            left.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 16),
            left.topAnchor.constraint(equalTo: root.topAnchor, constant: 12),
            left.bottomAnchor.constraint(equalTo: root.bottomAnchor, constant: -12),
            sourceScroll.widthAnchor.constraint(equalTo: left.widthAnchor),
            domainActions.widthAnchor.constraint(equalTo: left.widthAnchor),
            right.leadingAnchor.constraint(equalTo: left.trailingAnchor, constant: 12),
            right.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -16),
            right.topAnchor.constraint(equalTo: root.topAnchor, constant: 12),
            right.bottomAnchor.constraint(equalTo: root.bottomAnchor, constant: -12),
            scroll.heightAnchor.constraint(greaterThanOrEqualToConstant: 200),
            emptyLabel.centerXAnchor.constraint(equalTo: scroll.centerXAnchor),
            emptyLabel.centerYAnchor.constraint(equalTo: scroll.centerYAnchor),
            emptyLabel.widthAnchor.constraint(lessThanOrEqualTo: scroll.widthAnchor, constant: -40),
        ])
        view = root
    }

    override func viewDidLoad() {
        super.viewDidLoad()
        reloadSources()
        // 選ぶと tableViewSelectionDidChange が語を読み込む
        if !sources.isEmpty { sourceTable.selectRowIndexes(IndexSet(integer: 0), byExtendingSelection: false) }
    }

    func focusSearch() {
        window?.makeFirstResponder(searchField)
    }

    // ---- 読み込み ----

    /// IME などほかのプロセスが分野・除外を変えていたら読み直す。
    func pollChanges() {
        let revision = dictionary.termRevision()
        guard revision != lastRevision else { return }
        reloadSources()
        loadWords(keepPosition: true)
    }

    private func reloadSources() {
        lastRevision = dictionary.termRevision()
        let selected = selectedSource
        // 同梱の分野の下に、自作の分野を並べる
        let domains = dictionary.termDomains()
        sources = (domains.filter { !$0.isUser } + domains.filter(\.isUser)).map { .domain($0) } + [.excluded]
        sourceTable.reloadData()
        if let selected, let index = sources.firstIndex(where: { Self.sameSource($0, selected) }) {
            sourceTable.selectRowIndexes(IndexSet(integer: index), byExtendingSelection: false)
        } else if selected != nil, !sources.isEmpty {
            // 選んでいた分野が無くなった (削除した・ほかの画面で消えた): 先頭の分野に移る
            sourceTable.selectRowIndexes(IndexSet(integer: 0), byExtendingSelection: false)
        }
        updateOffLabel()
    }

    private static func sameSource(_ a: Source, _ b: Source) -> Bool {
        switch (a, b) {
        case let (.domain(x), .domain(y)): return x.id == y.id
        case (.excluded, .excluded): return true
        default: return false
        }
    }

    private var selectedSource: Source? {
        let row = sourceTable.selectedRow
        return row >= 0 && row < sources.count ? sources[row] : nil
    }

    /// 選んでいる分野の語を、裏のスレッドで読み込む (IT は約 1.5 万語)。終わったら一覧に出す。
    private func loadWords(keepPosition: Bool = false) {
        guard let source = selectedSource else { return }
        loadToken += 1
        let token = loadToken
        let selected = Set(selectedKeys())
        // 読み直しても、見ていた位置 (一番上の行) と選択を保つ
        let topRow = tableView.rows(in: tableView.visibleRect).location
        spinner.startAnimation(nil)
        loading = true
        let dictionary = self.dictionary
        DispatchQueue.global(qos: .userInitiated).async { [weak self] in
            let words: [TermWord]?
            switch source {
            case let .domain(domain):
                words = dictionary.termWords(domain: domain.id)
            case .excluded:
                words = dictionary.excludedTerms().enumerated().map { TermWord(key: $0.element, note: "", excluded: true, order: $0.offset) }
            }
            DispatchQueue.main.async {
                guard let self, token == self.loadToken else { return }
                self.spinner.stopAnimation(nil)
                self.loading = false
                let sameSource = self.loadedSource.map { Self.sameSource($0, source) } ?? false
                self.loadedSource = source
                self.list.replace(words ?? [])
                self.tableView.reloadData()
                if keepPosition && sameSource {
                    UI.select(selected, in: self.tableView, rows: self.list.visible.map(\.key))
                    if topRow > 0, topRow < self.list.visible.count { self.tableView.scroll(self.tableView.rect(ofRow: topRow).origin) }
                } else {
                    // 別の分野に替えたときは、前の分野の選択 (行の番号) を残さない (Delete で別の語を除外しないように)
                    self.tableView.deselectAll(nil)
                    self.tableView.scrollRowToVisible(0)
                }
                if words == nil { self.showStatus("この分野の語を読めませんでした。") }
                self.updateState()
            }
        }
    }

    private func applyList() {
        let selected = Set(selectedKeys())
        list.apply()
        tableView.reloadData()
        UI.select(selected, in: tableView, rows: list.visible.map(\.key))
        updateState()
    }

    /// 選んでいる分野が自作の分野なら、その情報。
    private var selectedUserDomain: TermDomainInfo? {
        if case let .domain(domain)? = selectedSource, domain.isUser { return domain }
        return nil
    }

    private static let bundledInfo = "ここにある専門用語集はサンプルです。ご自身の用途に合わせて育てていくための出発点として使ってください (不要な語は「除外」、直したい語は「直す」、使いたい語は「複製」、足したい語はユーザー辞書への登録で、自分の辞書にできます)。同梱の専門用語は書き換えられません。「除外」はその語を変換に使わないこと (元に戻せます)、「直す」は元の語を除外して直した語をユーザー辞書に登録すること、「複製」はそのままユーザー辞書にコピーすることです。ユーザー辞書の語は専門用語集より優先されます。"
    private static let userInfo = "自分で作った専門用語集です。語は、ユーザー辞書の語と同じように変換で使われます (読みが短くても、日常語と同じ読みでも、英単語でも)。語はここで追加・編集・削除できます。ユーザー辞書のタブで語を選び、「専門用語集へ移す…」で入れることもできます。左のチェックで、専門用語集ごとに使う・使わないを切り替えられます。"

    private func updateOffLabel() {
        infoLabel.stringValue = selectedUserDomain == nil ? Self.bundledInfo : Self.userInfo
        if case let .domain(domain)? = selectedSource, !domain.enabled {
            offLabel.stringValue = "「\(domain.name)」は OFF です (変換に使われません)。左のチェックを入れると、すべての入力欄ですぐ使われます。"
            offLabel.isHidden = false
        } else {
            offLabel.isHidden = true
        }
    }

    private func updateState() {
        let total = list.all.count
        let shown = list.visible.count
        let excluded = list.all.lazy.filter(\.excluded).count
        var text = shown == total ? "全 \(total) 語" : "\(shown) / 全 \(total) 語"
        if excluded > 0, case .domain? = selectedSource { text += " (除外 \(excluded))" }
        countLabel.stringValue = text
        if total == 0 {
            emptyLabel.stringValue = selectedSource == .excluded ? "除外した語はありません。" : (loading ? "読み込み中…" : "語がありません。")
        } else if shown == 0 {
            emptyLabel.stringValue = list.query.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? "条件に合う語はありません。" : "見つかりません"
        }
        emptyLabel.isHidden = shown > 0
        let rows = selectedRows()
        let isUser = selectedUserDomain != nil
        bundledButtons.forEach { $0.isHidden = isUser }
        userButtons.forEach { $0.isHidden = !isUser }
        if isUser, total == 0, !loading { emptyLabel.stringValue = "まだ語がありません。「語を追加…」か、ユーザー辞書のタブの「専門用語集へ移す…」で入れられます。" }
        excludeButton.isEnabled = rows.contains { !$0.excluded }
        includeButton.isEnabled = rows.contains { $0.excluded }
        editButton.isEnabled = rows.count == 1
        copyButton.isEnabled = !rows.isEmpty
        addTermButton.isEnabled = isUser
        editTermButton.isEnabled = rows.count == 1
        deleteTermButton.isEnabled = !rows.isEmpty
    }

    /// 自作の専門用語集を選んでいるか (メニューの書き出しなどを使えるか)。
    var hasUserDomainSelected: Bool { selectedUserDomain != nil }

    private func selectedRows() -> [TermWord] {
        tableView.selectedRowIndexes.compactMap { $0 < list.visible.count ? list.visible[$0] : nil }
    }

    private func selectedKeys() -> [WordKey] { selectedRows().map(\.key) }

    private func showStatus(_ message: String) {
        statusLabel.stringValue = message
        UI.announce(message, in: window)
    }

    // ---- 一覧 (左右とも NSTableView) ----

    func numberOfRows(in tableView: NSTableView) -> Int {
        tableView === sourceTable ? sources.count : list.visible.count
    }

    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        if tableView === sourceTable { return sourceCell(row: row) }
        guard let column = tableColumn, row < list.visible.count else { return nil }
        let word = list.visible[row]
        let cell = UI.cell(in: tableView, identifier: column.identifier)
        guard let text = cell.textField else { return cell }
        text.textColor = word.excluded ? .secondaryLabelColor : .labelColor
        text.attributedStringValue = NSAttributedString(string: "")
        switch column.identifier.rawValue {
        case "reading":
            text.stringValue = word.reading
        case "word":
            // 除外した語は取り消し線 (色だけに頼らない)
            text.attributedStringValue = NSAttributedString(string: word.word, attributes: word.excluded ? [
                .strikethroughStyle: NSUnderlineStyle.single.rawValue,
                .foregroundColor: NSColor.secondaryLabelColor,
            ] : [.foregroundColor: NSColor.labelColor])
        case "note":
            text.stringValue = word.note
            text.textColor = .secondaryLabelColor
        default:
            text.stringValue = word.excluded ? "除外中" : ""
            text.textColor = .systemOrange
        }
        return cell
    }

    /// 左の一覧の 1 行: 分野はチェック (ON/OFF) と名称・語数。最後の行は「除外した語 (すべて)」。
    private func sourceCell(row: Int) -> NSView? {
        guard row < sources.count else { return nil }
        let identifier = NSUserInterfaceItemIdentifier("sourceCell")
        let cell = sourceTable.makeView(withIdentifier: identifier, owner: nil) as? SourceCellView ?? SourceCellView(identifier: identifier, target: self, action: #selector(toggleDomain(_:)))
        switch sources[row] {
        case let .domain(domain):
            cell.configure(name: domain.name, detail: domain.isUser ? "自作 \(domain.count) 語" : "\(domain.count) 語", checkbox: domain.enabled, tag: row, isUser: domain.isUser)
        case .excluded:
            cell.configure(name: "除外した語 (すべての分野)", detail: nil, checkbox: nil, tag: row)
        }
        return cell
    }

    func tableView(_ tableView: NSTableView, sortDescriptorsDidChange oldDescriptors: [NSSortDescriptor]) {
        guard tableView === self.tableView, let descriptor = tableView.sortDescriptors.first, let key = descriptor.key, let column = SortColumn(rawValue: key) else { return }
        list.sort = SortSpec(column: column, ascending: descriptor.ascending)
        applyList()
    }

    func tableViewSelectionDidChange(_ notification: Notification) {
        if (notification.object as? NSTableView) === sourceTable {
            updateOffLabel()
            updateState()
            loadWords()
        } else {
            updateState()
        }
    }

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

    @objc private func filterChanged(_ sender: Any?) {
        switch Filter(rawValue: filterPopup.indexOfSelectedItem) ?? .all {
        case .all: list.include = nil
        case .used: list.include = { !$0.excluded }
        case .excluded: list.include = { $0.excluded }
        }
        applyList()
    }

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

    /// Delete キー: 自作の専門用語集なら語を消し、同梱ならその語を除外する。
    private func deleteKey() {
        if selectedUserDomain != nil { deleteTerms(nil) } else { excludeSelected(nil) }
    }

    private func sourceMenu() -> NSMenu? {
        guard selectedUserDomain != nil else { return nil }
        let menu = NSMenu()
        menu.addItem(withTitle: "名前を変更…", action: #selector(renameDomain(_:)), keyEquivalent: "").target = self
        menu.addItem(withTitle: "書き出す…", action: #selector(exportDomain(_:)), keyEquivalent: "").target = self
        menu.addItem(withTitle: "削除…", action: #selector(deleteDomain(_:)), keyEquivalent: "").target = self
        return menu
    }

    private func rowMenu() -> NSMenu? {
        if selectedUserDomain != nil {
            let menu = NSMenu()
            menu.addItem(withTitle: "編集…", action: #selector(editSelected(_:)), keyEquivalent: "").target = self
            menu.addItem(withTitle: "削除…", action: #selector(deleteTerms(_:)), keyEquivalent: "").target = self
            menu.addItem(.separator())
            menu.addItem(withTitle: "ユーザー辞書へ複製", action: #selector(copyToUserDictionary(_:)), keyEquivalent: "").target = self
            menu.addItem(withTitle: "コピー", action: #selector(copy(_:)), keyEquivalent: "").target = self
            return menu
        }
        let menu = NSMenu()
        menu.addItem(withTitle: "除外する", action: #selector(excludeSelected(_:)), keyEquivalent: "").target = self
        menu.addItem(withTitle: "除外をやめる", action: #selector(includeSelected(_:)), keyEquivalent: "").target = self
        menu.addItem(.separator())
        menu.addItem(withTitle: "直してユーザー辞書へ…", action: #selector(editSelected(_:)), keyEquivalent: "").target = self
        menu.addItem(withTitle: "ユーザー辞書へ複製", action: #selector(copyToUserDictionary(_:)), keyEquivalent: "").target = self
        menu.addItem(withTitle: "コピー", action: #selector(copy(_:)), keyEquivalent: "").target = self
        return menu
    }

    func validateMenuItem(_ menuItem: NSMenuItem) -> Bool {
        let rows = selectedRows()
        switch menuItem.action {
        case #selector(renameDomain(_:)), #selector(exportDomain(_:)), #selector(deleteDomain(_:)): return selectedUserDomain != nil
        case #selector(deleteTerms(_:)): return selectedUserDomain != nil && !rows.isEmpty
        case #selector(addTerm(_:)): return selectedUserDomain != nil
        case #selector(delete(_:)): return selectedUserDomain != nil ? !rows.isEmpty : rows.contains { !$0.excluded }
        case #selector(excludeSelected(_:)): return selectedUserDomain == nil && rows.contains { !$0.excluded }
        case #selector(includeSelected(_:)): return rows.contains { $0.excluded }
        case #selector(editSelected(_:)): return rows.count == 1
        case #selector(copyToUserDictionary(_:)), #selector(copy(_:)): return !rows.isEmpty
        default: return true
        }
    }

    // ---- 分野の ON/OFF ----

    @objc private func toggleDomain(_ sender: NSButton) {
        guard sender.tag < sources.count, case let .domain(domain) = sources[sender.tag] else { return }
        setDomain(domain, enabled: sender.state == .on)
    }

    private func toggleSelectedSource() {
        guard case let .domain(domain)? = selectedSource else { return }
        setDomain(domain, enabled: !domain.enabled)
    }

    private func setDomain(_ domain: TermDomainInfo, enabled: Bool) {
        if dictionary.setTermDomain(id: domain.id, enabled: enabled) {
            showStatus(enabled ? "「\(domain.name)」を使います (すべての入力欄にすぐ反映)" : "「\(domain.name)」を使わないようにしました")
        } else {
            UI.showError("Meltype のデータフォルダの config.json を確認してください (読めない・書けないときは、設定を変えません)。", title: "設定を保存できませんでした", in: window)
        }
        reloadSources()
    }

    // ---- 変更 (元に戻せる) ----

    @discardableResult
    private func perform(_ change: DictionaryChange, message: String) -> String? {
        let result = change.perform(on: dictionary)
        if let error = result.error {
            // 途中までの変更が残ったとき (本体が戻せなかったとき) は、それを取り消せるようにしておく
            if let undo = result.undo { registerUndo(undo, name: change.actionName) }
            pollChanges()
            didChange?()
            return error
        }
        if let undo = result.undo { registerUndo(undo, name: change.actionName) }
        reloadSources()
        loadWords(keepPosition: true)
        showStatus(message)
        didChange?()
        return nil
    }

    /// 元に戻す操作を積む (UserDictionaryViewController.registerUndo と同じ: タブが隠れていても同じ undoManager に積む)。
    private func registerUndo(_ change: DictionaryChange, name: String, on manager: UndoManager? = nil) {
        guard let undoManager = manager ?? window?.undoManager else { return }
        // undoManager は弱く持つ (積んだ操作が undoManager を持ち、undoManager が操作を持つ循環を作らない)
        undoManager.registerUndo(withTarget: self) { [weak undoManager] target in
            let undoing = undoManager?.isUndoing ?? true
            let result = change.perform(on: target.dictionary)
            if let redo = result.undo, let undoManager {
                if result.error != nil && undoing {
                    // 取り消しに失敗したとき (消した専門用語集を戻せないなど) の「もう一度」は、取り消しが終わってから積む
                    // (取り消しの最中に積むと「やり直し」の側に入ってしまい、⌘Z でもう一度戻せない)。
                    DispatchQueue.main.async { target.registerUndo(redo, name: name, on: undoManager) }
                } else {
                    target.registerUndo(redo, name: name, on: undoManager)
                }
            }
            target.reloadSources()
            target.loadWords(keepPosition: true)
            target.didChange?()
            if let error = result.error {
                UI.showError(error, title: undoing ? "元に戻せませんでした" : "やり直せませんでした", in: target.window)
                return
            }
            target.showStatus(undoing ? "\(name)を取り消しました" : "\(name)をやり直しました")
        }
        undoManager.setActionName(name)
    }

    /// Delete: 選んだ語 (まだ除外していないもの) を除外する。
    @objc func excludeSelected(_ sender: Any?) {
        let keys = selectedRows().filter { !$0.excluded }.map(\.key)
        guard !keys.isEmpty else { return }
        if let error = perform(.exclude(keys), message: keys.count == 1 ? "「\(keys[0].word)」を除外しました (変換に使いません。⌘Z で元に戻せます)" : "\(keys.count) 語を除外しました (変換に使いません。⌘Z で元に戻せます)") {
            UI.showError(error, title: "除外できませんでした", in: window)
        }
    }

    @objc func delete(_ sender: Any?) { deleteKey() }

    @objc func includeSelected(_ sender: Any?) {
        let keys = selectedRows().filter(\.excluded).map(\.key)
        guard !keys.isEmpty else { return }
        if let error = perform(.include(keys), message: keys.count == 1 ? "「\(keys[0].word)」の除外をやめました" : "\(keys.count) 語の除外をやめました") {
            UI.showError(error, title: "元に戻せませんでした", in: window)
        }
    }

    /// Return: 選んだ語を直して、ユーザー辞書に登録する (元の語は除外)。
    @objc func editSelected(_ sender: Any?) {
        let rows = selectedRows()
        guard rows.count == 1, let original = rows.first, let window else { return }
        if let domain = selectedUserDomain {
            let editor = WordEditor(.init(title: "専門用語を編集", explanation: "「\(domain.name)」の語を直します (⌘Z で元に戻せます)。", confirmTitle: "保存", key: original.key),
                                    validate: { [dictionary] key in dictionary.checkTerm(id: domain.id, key, except: original.key) },
                                    submit: { [weak self] key in
                                        self?.perform(.updateTerm(id: domain.id, from: original.key, to: key), message: "「\(key.word)」(\(key.reading)) に直しました")
                                    },
                                    toReading: { [dictionary] in dictionary.toReading($0) })
            editor.begin(on: window)
            return
        }
        let explanation = "同梱の「\(original.word)」(\(original.reading)) は書き換えられないので、直した語をユーザー辞書に登録し、元の語は除外します (⌘Z で元に戻せます)。ユーザー辞書の語は専門用語集より優先されます。"
        let editor = WordEditor(.init(title: "専門用語を直す", explanation: explanation, confirmTitle: "ユーザー辞書に登録", key: original.key),
                                // 直した語がもうユーザー辞書にあっても、登録はせず除外だけ行うので、重複は理由にしない (入力の規則だけ確かめる)
                                validate: { [dictionary] key in dictionary.check(key, except: key) },
                                submit: { [weak self] key in
                                    self?.perform(.editTerm(original: original.key, to: key, originalExcluded: original.excluded),
                                                  message: "「\(key.word)」(\(key.reading)) をユーザー辞書に登録し、元の「\(original.word)」を除外しました")
                                },
                                toReading: { [dictionary] in dictionary.toReading($0) })
        editor.begin(on: window)
    }

    // ---- 自作の専門用語集 ----

    /// 自作の専門用語集に語を足す。
    @objc func addTerm(_ sender: Any?) {
        guard let domain = selectedUserDomain, let window else { return }
        let editor = WordEditor(.init(title: "専門用語を追加", explanation: "「\(domain.name)」に語を足します。ユーザー辞書の語と同じように変換で使われます。", confirmTitle: "追加", key: WordKey(reading: "", word: "")),
                                validate: { [dictionary] key in dictionary.checkTerm(id: domain.id, key, except: nil) },
                                submit: { [weak self] key in
                                    self?.perform(.addTerms(id: domain.id, keys: [key]), message: "「\(key.word)」(\(key.reading)) を追加しました")
                                },
                                toReading: { [dictionary] in dictionary.toReading($0) })
        editor.begin(on: window)
    }

    /// 自作の専門用語集から、選んだ語を消す (確認あり・⌘Z で戻せる)。
    @objc func deleteTerms(_ sender: Any?) {
        guard let domain = selectedUserDomain, let window else { return }
        let keys = selectedKeys()
        guard !keys.isEmpty else { return }
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = keys.count == 1 ? "「\(keys[0].word)」(\(keys[0].reading)) を削除しますか？" : "選んだ \(keys.count) 語を削除しますか？"
        alert.informativeText = "「\(domain.name)」から消します。「編集」→「取り消す」(⌘Z) で元に戻せます。"
        alert.addButton(withTitle: "削除").hasDestructiveAction = true
        alert.addButton(withTitle: "キャンセル").keyEquivalent = "\u{1b}"
        alert.beginSheetModal(for: window) { [weak self] response in
            guard let self, response == .alertFirstButtonReturn else { return }
            if let error = self.perform(.removeTerms(id: domain.id, keys: keys), message: keys.count == 1 ? "「\(keys[0].word)」を削除しました (⌘Z で元に戻せます)" : "\(keys.count) 語を削除しました (⌘Z で元に戻せます)") {
                UI.showError(error, title: "削除できませんでした", in: window)
            }
        }
    }

    /// 自作の専門用語集を新しく作る (すぐ有効)。
    @objc func newDomain(_ sender: Any?) {
        guard let window else { return }
        TermSheets.promptName(title: "新しい専門用語集", message: "名前を付けてください。作るとすぐ有効になります (左のチェックで切り替えられます)。", confirmTitle: "作成", in: window) { [weak self] name in
            guard let self else { return nil }
            let result = self.dictionary.createDomain(name: name)
            if let error = result.error { return error }
            self.reloadSources()
            if let id = result.id { self.selectDomain(id: id) }
            self.showStatus("専門用語集「\(name)」を作りました")
            self.didChange?()
            return nil
        }
    }

    private func selectDomain(id: String) {
        if let index = sources.firstIndex(where: { if case let .domain(domain) = $0 { return domain.id == id } else { return false } }) {
            sourceTable.selectRowIndexes(IndexSet(integer: index), byExtendingSelection: false)
            sourceTable.scrollRowToVisible(index)
        }
    }

    @objc func renameDomain(_ sender: Any?) {
        guard let domain = selectedUserDomain, let window else { return }
        TermSheets.promptName(title: "名前を変更", message: "「\(domain.name)」の新しい名前を入れてください。", initial: domain.name, confirmTitle: "変更", in: window) { [weak self] name in
            guard let self else { return nil }
            if name == domain.name { return nil }
            return self.perform(.renameDomain(id: domain.id, to: name, from: domain.name), message: "名前を「\(name)」に変えました")
        }
    }

    @objc func deleteDomain(_ sender: Any?) {
        guard let domain = selectedUserDomain, let window else { return }
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = "専門用語集「\(domain.name)」(\(domain.count) 語) を削除しますか？"
        alert.informativeText = "この専門用語集の語は、変換に使われなくなります。「編集」→「取り消す」(⌘Z) で、語ごと元に戻せます。"
        alert.addButton(withTitle: "削除").hasDestructiveAction = true
        alert.addButton(withTitle: "キャンセル").keyEquivalent = "\u{1b}"
        alert.beginSheetModal(for: window) { [weak self] response in
            guard let self, response == .alertFirstButtonReturn else { return }
            if let error = self.perform(.deleteDomain(id: domain.id), message: "専門用語集「\(domain.name)」を削除しました (⌘Z で元に戻せます)") {
                UI.showError(error, title: "削除できませんでした", in: window)
            }
        }
    }

    @objc func exportDomain(_ sender: Any?) {
        guard let domain = selectedUserDomain, let window else { return }
        let panel = NSSavePanel()
        panel.title = "専門用語集を書き出す"
        panel.message = "「\(domain.name)」を、Meltype の専門用語集のファイル (UTF-8 のテキスト) に書き出します。「ファイルから取り込む…」で、ほかの Mac でも使えます。"
        panel.prompt = "書き出す"
        panel.nameFieldStringValue = "Meltype-専門用語集-\(domain.name).txt"
        panel.allowedContentTypes = [.plainText]
        panel.beginSheetModal(for: window) { [weak self] response in
            guard let self, response == .OK, let url = panel.url else { return }
            let result = self.dictionary.exportDomain(id: domain.id, path: url.path)
            if let error = result.error {
                UI.showError(error, title: "書き出せませんでした", in: window)
                return
            }
            self.showStatus("\(result.count) 語を書き出しました: \(url.lastPathComponent)")
        }
    }

    @objc func importDomain(_ sender: Any?) {
        guard let window else { return }
        let panel = NSOpenPanel()
        panel.title = "専門用語集を取り込む"
        panel.message = "Meltype の専門用語集のファイル (「読み」と「語」を Tab で区切った UTF-8 のテキスト。先頭の「# 名称:」が名前になります) を選んでください。新しい専門用語集として追加します。"
        panel.prompt = "取り込む"
        panel.canChooseFiles = true
        panel.canChooseDirectories = false
        panel.allowsMultipleSelection = false
        panel.beginSheetModal(for: window) { [weak self] response in
            guard let self, response == .OK, let url = panel.url else { return }
            let result = self.dictionary.importDomain(path: url.path)
            if let error = result.error {
                UI.showError(error, title: "取り込めませんでした", in: window)
                return
            }
            self.reloadSources()
            if let summary = result.summary {
                self.selectDomain(id: summary.id)
                self.showStatus(summary.message.replacingOccurrences(of: "\n", with: " / "))
                let alert = NSAlert()
                alert.messageText = "専門用語集を取り込みました"
                alert.informativeText = summary.message
                alert.beginSheetModal(for: window)
            }
            self.didChange?()
        }
    }

    /// 選んだ語を、そのままユーザー辞書にコピーする (すでにあるものは飛ばす)。本体の 1 回の読み直し・保存で済ませ (数千語でも待たせない)、
    /// 1 回の ⌘Z で、新しく登録した語だけを消せる。
    @objc func copyToUserDictionary(_ sender: Any?) {
        let keys = selectedKeys()
        guard !keys.isEmpty else { return }
        let result = DictionaryChange.addMany(keys).perform(on: dictionary)
        if let error = result.error {
            UI.showError(error, title: "複製できませんでした", in: window)
            return
        }
        let added: Int
        if case let .remove(addedKeys)? = result.undo {
            added = addedKeys.count
            registerUndo(result.undo!, name: DictionaryChange.addMany(keys).actionName)
        } else {
            added = 0
        }
        didChange?()
        var message = "\(added) 語をユーザー辞書に複製しました"
        if added < keys.count { message += " (\(keys.count - added) 語は登録済み)" }
        showStatus(message)
    }

    /// 選んだ語を「読み<Tab>語」の行でコピーする。
    @objc func copy(_ sender: Any?) {
        let keys = selectedKeys()
        guard !keys.isEmpty else { return }
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(FfiFormat.formatKeys(keys), forType: .string)
        showStatus("\(keys.count) 語をコピーしました")
    }
}

/// 左の一覧の行: チェック (分野の ON/OFF) と名称・語数。
private final class SourceCellView: NSTableCellView {
    private let checkbox = NSButton(checkboxWithTitle: "", target: nil, action: nil)
    private let name = NSTextField(labelWithString: "")
    private let detail = NSTextField(labelWithString: "")

    init(identifier: NSUserInterfaceItemIdentifier, target: AnyObject, action: Selector) {
        super.init(frame: .zero)
        self.identifier = identifier
        checkbox.target = target
        checkbox.action = action
        name.lineBreakMode = .byTruncatingTail
        detail.textColor = .secondaryLabelColor
        detail.font = .systemFont(ofSize: NSFont.smallSystemFontSize)
        // 語数 (「1111 語」) は欠けさせない。足りないときは分野の名称のほうを「…」で縮める
        detail.setContentCompressionResistancePriority(.required, for: .horizontal)
        detail.setContentHuggingPriority(.required, for: .horizontal)
        name.setContentCompressionResistancePriority(.init(250), for: .horizontal)
        let stack = NSStackView(views: [checkbox, name, UI.spacer(), detail])
        stack.spacing = 4
        stack.translatesAutoresizingMaskIntoConstraints = false
        addSubview(stack)
        textField = name
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 2),
            stack.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -4),
            stack.centerYAnchor.constraint(equalTo: centerYAnchor),
        ])
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) is not supported") }

    /// checkbox が nil なら、チェックを出さない (除外した語の一覧)。
    func configure(name text: String, detail detailText: String?, checkbox state: Bool?, tag: Int, isUser: Bool = false) {
        detail.textColor = isUser ? .controlAccentColor : .secondaryLabelColor
        name.stringValue = text
        detail.stringValue = detailText ?? ""
        detail.isHidden = detailText == nil
        checkbox.isHidden = state == nil
        checkbox.state = state == true ? .on : .off
        checkbox.tag = tag
        checkbox.setAccessibilityLabel("\(text) を変換に使う")
        setAccessibilityLabel(detailText.map { "\(text)、\($0)、\(state == true ? "ON" : "OFF")" } ?? text)
    }
}
