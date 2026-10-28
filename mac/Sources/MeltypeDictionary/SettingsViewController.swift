// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import AppKit
import MeltypeDictionaryKit

/// 「設定」のタブ: Mac で効く Meltype の設定 (ライブ変換・自動判定の強さ・句読点・予測変換など) を、グループごとのカードで編集する。
/// 項目・名前・要約・説明・範囲は本体 (libMeltypeNative の MacSettingsCatalog) が決める。変更はすぐ config.json に保存され (OK ボタンは無い)、
/// 動いている IME の入力欄にも少し待つと反映される。入力メニューの切り替え (別のプロセス) は、定期的に読み直して画面に反映する。
/// 見た目は Mac の「システム設定」に寄せる: 左に検索とグループの一覧、右にグループごとの見出しと角の丸いカード。
/// 各項目は名前・1 行の要約・例を常に出し、全文の説明は「詳しく」で開く (説明が長い項目が多く、全部を出すと一覧が長くなりすぎるため)。
final class SettingsViewController: NSViewController, NSTableViewDataSource, NSTableViewDelegate {
    /// 項目 1 つ分の画面の部品。
    private final class Row {
        let item: SettingItem
        /// 画面を本体の値に合わせる。
        var refresh: (SettingValue) -> Void = { _ in }
        /// 利用者が今そこを操作している (数の入力中など) か。操作中の部品は読み直しで書き換えない。
        var isBusy: () -> Bool = { false }
        var controls: [NSControl] = []
        /// 行の全体 (検索で隠す)。
        var view = NSView()
        /// 行の上の区切り線 (カードの中で、見えている最初の行では隠す)。
        var separator = NSView()
        /// 親が OFF のときに薄くする文字と、その文字の普段の色。
        var texts: [(label: NSTextField, color: NSColor)] = []
        var description: NSTextField?
        var detailsButton: NSButton?
        /// 部品の読み上げの補足 (要約 + 例 + 全文の説明)。親が OFF のあいだは、先頭に理由を足して部品に入れる。
        var help = ""
        /// 親の名前 (子の項目だけ)。操作できない理由の文に使う。
        var parentLabel: String?
        /// 親が OFF のあいだだけ要約の下に出す案内 (「「予測変換」を ON にすると使えます」)。
        var unavailableNote: NSTextField?
        /// 利用者が「詳しく」で開いた / 閉じた状態 (検索していないとき)。検索を消すとこの状態に戻す。
        var userOpen = false
        /// 検索中に利用者が「詳しく」を押した状態 (nil = 検索の結果に任せる)。検索を消すと忘れる。
        var searchOpen: Bool?

        init(item: SettingItem) { self.item = item }

        /// 親が OFF のあいだ、文字を薄くする (操作できないことを見た目でも示す)。
        func setDimmed(_ dimmed: Bool) {
            for (label, color) in texts { label.textColor = dimmed ? .tertiaryLabelColor : color }
        }
    }

    /// グループ 1 つ分 (見出し + カード)。
    private final class Section {
        let name: String
        let view: NSStackView
        var rows: [Row] = []
        /// 検索で一致する行があるか。
        var hasMatch = true

        init(name: String, view: NSStackView) {
            self.name = name
            self.view = view
        }
    }

    private let dictionary: NativeDictionary
    private var rows: [String: Row] = [:]
    private var sections: [Section] = []
    /// 画面に反映した本体の値。
    private var values: [String: SettingValue] = [:]
    /// config.json を読めているか (読めないあいだは全部を操作できなくする。読めても、親が OFF の子は操作できないまま)。
    private var controlsEnabled = true
    private let content = NSStackView()
    private let document = FlippedView()
    private let scrollView = NSScrollView()
    private let problemLabel = UI.wrapping("", secondary: false)
    private let noMatchLabel = UI.label("一致する設定はありません", secondary: true)
    private let searchField = NSSearchField()
    private let groupTable = NSTableView()
    /// グループの一覧の選択を、画面のスクロールに合わせて変えている最中か (そのときは選択でスクロールし直さない)。
    private var syncingSelection = false
    private var resetButton: NSButton!
    /// このタブを入れているウインドウ (タブを切り替えると view.window が nil になるので、シートはこちらを使う)。
    weak var hostWindow: NSWindow?
    private var window: NSWindow? { view.window ?? hostWindow }

    private static let zenzaiKey = "Zenzai"
    /// Zenzai のモデルの状態 (「● ダウンロード中 (xx%)」「● 失敗: …」「● 準備できました」)。状態が無いときは隠す。長い文は折り返す。
    private let zenzaiStatusLabel = UI.wrapping("")

    /// IME が書く zenzai.json を読んで、状態の文を色の点つきで出す。壊れていれば (OFF 扱いで) 何も出さない。
    private func updateZenzaiStatus() {
        let now = Date()
        let status = dictionary.dataDirectory.flatMap { ZenzaiStatus.read(dataDirectory: $0) }
        guard let status, let text = status.displayText(now: now) else {
            zenzaiStatusLabel.stringValue = ""
            zenzaiStatusLabel.isHidden = true
            return
        }
        let color: NSColor
        switch status.state {
        case .ready: color = .systemGreen
        // 更新が止まったダウンロードは、進んでいないことが分かるよう注意の色にする
        case .downloading: color = now.timeIntervalSince(status.updatedAt) > ZenzaiStatus.staleAfter ? .systemOrange : .systemBlue
        case .checking: color = .systemBlue
        case .slow: color = .systemOrange
        case .failed, .loadFailed: color = .systemRed
        case .off: color = .systemGray
        }
        let font = NSFont.systemFont(ofSize: NSFont.smallSystemFontSize)
        let attributed = NSMutableAttributedString(string: "● ", attributes: [.foregroundColor: color, .font: font])
        attributed.append(NSAttributedString(string: text, attributes: [.foregroundColor: NSColor.secondaryLabelColor, .font: font]))
        // 同じ内容なら書き換えない (1.5 秒ごとの読み直しで、読み上げ中の要素を作り直さないように)
        if zenzaiStatusLabel.attributedStringValue != attributed { zenzaiStatusLabel.attributedStringValue = attributed }
        // 文字の欄の読み上げはセルが受け持つので、セルにも名前を付ける (欄だけだと読まれないことがある)
        zenzaiStatusLabel.setAccessibilityLabel("Zenzai の状態: \(text)")
        zenzaiStatusLabel.cell?.setAccessibilityLabel("Zenzai の状態: \(text)")
        zenzaiStatusLabel.isHidden = false
    }

    /// 右のカードの最大の幅 (広い窓で行が横に間延びしないように)。
    private static let maxContentWidth: CGFloat = 680
    private static let sidebarWidth: CGFloat = 180
    /// カードの中の余白と、子の項目 (親が OFF なら効かない項目) の字下げ。
    private static let rowInset: CGFloat = 12
    private static let childIndent: CGFloat = 20
    /// カードの地の色。ライトは白 (窓の灰色の上に浮かせる)、ダークは窓より少し明るい色 (controlBackgroundColor はダークだと窓より暗く、沈んで見える)。
    /// どちらも描くときに外観から決まる色なので、外観を切り替えても塗り直しは要らない (layer に CGColor を入れると切り替えに追従しない)。
    private static let cardFill = NSColor(name: nil) { appearance in
        appearance.bestMatch(from: [.aqua, .darkAqua]) == .darkAqua ? NSColor.white.withAlphaComponent(0.06) : .controlBackgroundColor
    }

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
        problemLabel.translatesAutoresizingMaskIntoConstraints = false

        let sidebar = makeSidebar()
        let divider = NSBox()
        divider.boxType = .separator
        divider.translatesAutoresizingMaskIntoConstraints = false

        // 右: グループごとの見出しとカード (スクロールする。窓が低いときも最後まで見られる)
        content.orientation = .vertical
        content.alignment = .leading
        content.spacing = 22
        content.translatesAutoresizingMaskIntoConstraints = false
        noMatchLabel.isHidden = true
        content.addArrangedSubview(noMatchLabel)
        document.translatesAutoresizingMaskIntoConstraints = false
        document.addSubview(content)
        // 右端まで広げたいが、窓の大きさは動かさない (UI.fillWidth の説明。強さを高くすると、タブを選んだだけで窓が中身の幅まで縮む)
        NSLayoutConstraint.activate([
            content.topAnchor.constraint(equalTo: document.topAnchor, constant: 14),
            content.leadingAnchor.constraint(equalTo: document.leadingAnchor, constant: 20),
        ] + UI.fillWidth(content, in: document, trailing: 20) + [
            content.widthAnchor.constraint(lessThanOrEqualToConstant: Self.maxContentWidth),
            content.bottomAnchor.constraint(equalTo: document.bottomAnchor, constant: -20),
        ])
        scrollView.documentView = document
        scrollView.hasVerticalScroller = true
        scrollView.drawsBackground = false
        scrollView.borderType = .noBorder
        scrollView.translatesAutoresizingMaskIntoConstraints = false
        document.widthAnchor.constraint(equalTo: scrollView.contentView.widthAnchor).isActive = true
        // スクロールに合わせて、左の一覧で今見ているグループを選ぶ
        scrollView.contentView.postsBoundsChangedNotifications = true
        NotificationCenter.default.addObserver(self, selector: #selector(contentScrolled(_:)), name: NSView.boundsDidChangeNotification, object: scrollView.contentView)

        let root = NSView()
        for part in [header, problemLabel, sidebar, divider, scrollView] { root.addSubview(part) }
        // 理由の文 (赤) は出ているときだけ場所を取る
        let belowHeader = scrollView.topAnchor.constraint(equalTo: header.bottomAnchor, constant: 8)
        belowHeader.priority = .defaultLow
        NSLayoutConstraint.activate([
            header.topAnchor.constraint(equalTo: root.topAnchor, constant: 12),
            header.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 24),
            header.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -24),
            problemLabel.topAnchor.constraint(equalTo: header.bottomAnchor, constant: 8),
            problemLabel.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 24),
            problemLabel.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -24),
            belowHeader,
            sidebar.topAnchor.constraint(equalTo: scrollView.topAnchor),
            sidebar.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 12),
            sidebar.widthAnchor.constraint(equalToConstant: Self.sidebarWidth),
            sidebar.bottomAnchor.constraint(equalTo: root.bottomAnchor, constant: -12),
            divider.topAnchor.constraint(equalTo: scrollView.topAnchor),
            divider.bottomAnchor.constraint(equalTo: root.bottomAnchor),
            divider.leadingAnchor.constraint(equalTo: sidebar.trailingAnchor, constant: 8),
            divider.widthAnchor.constraint(equalToConstant: 1),
            scrollView.leadingAnchor.constraint(equalTo: divider.trailingAnchor),
            scrollView.trailingAnchor.constraint(equalTo: root.trailingAnchor),
            scrollView.bottomAnchor.constraint(equalTo: root.bottomAnchor),
        ])
        problemTop = scrollView.topAnchor.constraint(equalTo: problemLabel.bottomAnchor, constant: 8)
        view = root
        reload()
    }

    /// 理由の文を出しているあいだ、一覧をその下に下げる。
    private var problemTop: NSLayoutConstraint?

    private func showProblem(_ text: String?) {
        problemLabel.stringValue = text ?? ""
        problemLabel.isHidden = text == nil
        problemTop?.isActive = text != nil
    }

    /// 左の列: 検索欄と、グループの一覧 (選ぶと右をそのグループまでスクロールする)。
    private func makeSidebar() -> NSView {
        searchField.placeholderString = "設定を検索"
        searchField.setAccessibilityLabel("設定を検索")
        UI.configure(search: searchField)
        searchField.target = self
        searchField.action = #selector(searchChanged(_:))
        searchField.translatesAutoresizingMaskIntoConstraints = false

        let column = NSTableColumn(identifier: NSUserInterfaceItemIdentifier("group"))
        column.resizingMask = .autoresizingMask
        groupTable.addTableColumn(column)
        groupTable.headerView = nil
        groupTable.style = .sourceList
        groupTable.backgroundColor = .clear
        groupTable.rowSizeStyle = .medium
        groupTable.allowsEmptySelection = true
        groupTable.dataSource = self
        groupTable.delegate = self
        // 選んであるグループをもう一度押しても、そのグループの先頭へ戻す (選択が変わらないと tableViewSelectionDidChange は来ない)
        groupTable.target = self
        groupTable.action = #selector(groupClicked(_:))
        groupTable.setAccessibilityLabel("設定のグループ")
        let tableScroll = NSScrollView()
        tableScroll.documentView = groupTable
        tableScroll.drawsBackground = false
        tableScroll.borderType = .noBorder
        tableScroll.hasVerticalScroller = true
        tableScroll.autohidesScrollers = true
        tableScroll.translatesAutoresizingMaskIntoConstraints = false

        let sidebar = NSView()
        sidebar.translatesAutoresizingMaskIntoConstraints = false
        sidebar.addSubview(searchField)
        sidebar.addSubview(tableScroll)
        NSLayoutConstraint.activate([
            searchField.topAnchor.constraint(equalTo: sidebar.topAnchor, constant: 10),
            searchField.leadingAnchor.constraint(equalTo: sidebar.leadingAnchor),
            searchField.trailingAnchor.constraint(equalTo: sidebar.trailingAnchor),
            tableScroll.topAnchor.constraint(equalTo: searchField.bottomAnchor, constant: 8),
            tableScroll.leadingAnchor.constraint(equalTo: sidebar.leadingAnchor),
            tableScroll.trailingAnchor.constraint(equalTo: sidebar.trailingAnchor),
            tableScroll.bottomAnchor.constraint(equalTo: sidebar.bottomAnchor),
        ])
        return sidebar
    }

    private final class FlippedView: NSView {
        override var isFlipped: Bool { true }
    }

    override func viewDidAppear() {
        super.viewDidAppear()
        // 窓に入って位置が決まってから、左の一覧の選択を今見えているグループに合わせる
        syncSelectionToScroll()
    }

    /// 検索欄に移る (編集メニューの「検索」⌘F)。
    func focusSearch() {
        view.window?.makeFirstResponder(searchField)
    }

    // ---- 項目の組み立て ----

    private func build(_ catalog: SettingsCatalog) {
        let labels = Dictionary(catalog.items.map { ($0.key, $0.label) }, uniquingKeysWith: { first, _ in first })
        for (group, items) in catalog.groups {
            let heading = NSTextField(labelWithString: group)
            heading.font = .boldSystemFont(ofSize: 13)
            heading.setAccessibilityRole(.staticText)

            // 角の丸いカード。色は外観で変わる色 (NSColor) をそのまま渡し、ライト・ダークの切り替えに追従させる
            let card = NSBox()
            card.boxType = .custom
            card.titlePosition = .noTitle
            card.cornerRadius = 10
            card.borderWidth = 1
            card.borderColor = .separatorColor
            card.fillColor = Self.cardFill
            card.contentViewMargins = .zero
            card.translatesAutoresizingMaskIntoConstraints = false
            let cardStack = NSStackView()
            cardStack.orientation = .vertical
            cardStack.alignment = .leading
            cardStack.spacing = 0
            cardStack.translatesAutoresizingMaskIntoConstraints = false
            card.contentView?.addSubview(cardStack)
            if let inside = card.contentView {
                NSLayoutConstraint.activate([
                    cardStack.topAnchor.constraint(equalTo: inside.topAnchor, constant: 2),
                    cardStack.bottomAnchor.constraint(equalTo: inside.bottomAnchor, constant: -2),
                    cardStack.leadingAnchor.constraint(equalTo: inside.leadingAnchor),
                    cardStack.trailingAnchor.constraint(equalTo: inside.trailingAnchor),
                ])
            }

            let sectionView = NSStackView(views: [heading, card])
            sectionView.orientation = .vertical
            sectionView.alignment = .leading
            sectionView.spacing = 6
            sectionView.translatesAutoresizingMaskIntoConstraints = false
            content.insertArrangedSubview(sectionView, at: content.arrangedSubviews.count - 1)
            sectionView.widthAnchor.constraint(equalTo: content.widthAnchor).isActive = true
            card.widthAnchor.constraint(equalTo: sectionView.widthAnchor).isActive = true

            let section = Section(name: group, view: sectionView)
            for item in items {
                let row = makeRow(item, parentLabel: SettingsLayout.parents[item.key].flatMap { labels[$0] })
                cardStack.addArrangedSubview(row.separator)
                cardStack.addArrangedSubview(row.view)
                row.separator.widthAnchor.constraint(equalTo: cardStack.widthAnchor).isActive = true
                row.view.widthAnchor.constraint(equalTo: cardStack.widthAnchor).isActive = true
                rows[item.key] = row
                section.rows.append(row)
                values[item.key] = item.value
                row.refresh(item.value)
            }
            sections.append(section)
        }
        groupTable.reloadData()
        applySearch()
    }

    private func makeRow(_ item: SettingItem, parentLabel: String?) -> Row {
        let row = Row(item: item)
        row.parentLabel = parentLabel
        row.help = Self.accessibilityHelp(for: item)
        let id = NSUserInterfaceItemIdentifier(item.key)
        let isChild = SettingsLayout.parents[item.key] != nil

        // 左の列: 名前・要約・例・(詳しく で開く) 説明。名前や要約が長くても折り返す (最小幅の 720pt でも切れないように)
        let name = NSTextField(wrappingLabelWithString: item.label)
        name.font = .systemFont(ofSize: 13)
        name.textColor = .labelColor
        Self.hideFromAccessibility(name)   // 部品の名前として読ませる (同じ名前を 2 回読ませない)
        let summary = UI.wrapping(item.displaySummary)
        Self.hideFromAccessibility(summary)   // 部品の補足 (ヘルプ = 要約 + 例 + 全文の説明) として読ませる
        var lines: [NSView] = [name, summary]
        row.texts = [(name, .labelColor), (summary, .secondaryLabelColor)]
        // 子は、親が OFF のあいだだけ、要約の下に使えるようにする方法を出す (薄くした文字より読める濃さ。読み上げは部品のヘルプの先頭で伝える)
        if let parentLabel {
            let note = UI.wrapping("「\(parentLabel)」を ON にすると使えます")
            note.isHidden = true
            Self.hideFromAccessibility(note)
            lines.append(note)
            row.unavailableNote = note
        }
        if let example = item.example, !example.isEmpty {
            // 例はダークでも読める濃さにする (tertiary だと暗い背景で読みにくい)。要約とは先頭の「例:」で見分ける
            let exampleLabel = UI.wrapping("例: \(example)")
            Self.hideFromAccessibility(exampleLabel)   // 部品のヘルプに「例: …」として入れてある
            lines.append(exampleLabel)
            row.texts.append((exampleLabel, .secondaryLabelColor))
        }
        // Zenzai は、モデルの取得 (IME が行う) の状態を、要約の下に出す (zenzai.json。ダウンロード中・失敗・準備できました)
        if item.key == Self.zenzaiKey {
            lines.append(zenzaiStatusLabel)
            updateZenzaiStatus()
        }
        // 全文の説明は「詳しく」で開く (開いたときは読み上げでも読める。閉じているあいだは隠れているので読まれない)
        if item.description != item.displaySummary {
            let description = UI.wrapping(item.description)
            description.isHidden = true
            description.setAccessibilityLabel("「\(item.label)」の説明")
            description.cell?.setAccessibilityLabel("「\(item.label)」の説明")
            let details = NSButton(title: "詳しく", target: self, action: #selector(toggleDetails(_:)))
            details.identifier = NSUserInterfaceItemIdentifier(Self.detailsPrefix + item.key)
            details.isBordered = false
            details.controlSize = .small
            details.font = .systemFont(ofSize: NSFont.smallSystemFontSize)
            details.contentTintColor = .controlAccentColor
            details.setAccessibilityLabel(Self.detailsAccessibilityLabel(item.label, open: false))
            details.setAccessibilityExpanded(false)
            lines.append(details)
            lines.append(description)
            row.texts.append((description, .secondaryLabelColor))
            row.description = description
            row.detailsButton = details
        }
        let left = NSStackView(views: lines)
        left.orientation = .vertical
        left.alignment = .leading
        left.spacing = 2
        left.translatesAutoresizingMaskIntoConstraints = false
        for line in lines where line is NSTextField {
            line.widthAnchor.constraint(equalTo: left.widthAnchor).isActive = true
            line.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        }
        // 「詳しく」の上は少し空ける。直前の行が隠れることがある (子の案内・Zenzai の状態) ときも同じ余白になるよう、
        // 常に見えている行まで遡って全部に付ける (NSStackView は隠れた行の後ろの余白を使わない)
        if let details = row.detailsButton, let index = lines.firstIndex(of: details) {
            for line in lines[..<index].reversed() {
                left.setCustomSpacing(4, after: line)
                if line !== row.unavailableNote && line !== zenzaiStatusLabel { break }
            }
        }

        // 右の列: 部品 (スイッチ・選択・数)。中身の幅だけ取り、残りを左の列に渡す
        let control: NSView
        switch item.kind {
        case .bool:
            let toggle = NSSwitch()
            toggle.identifier = id
            toggle.controlSize = .small
            toggle.target = self
            toggle.action = #selector(boolChanged(_:))
            toggle.setAccessibilityLabel(item.label)
            row.controls = [toggle]
            row.refresh = { value in
                if case let .bool(on) = value { toggle.state = on ? .on : .off }
            }
            control = toggle
        case .choice:
            let popup = NSPopUpButton(frame: .zero, pullsDown: false)
            popup.identifier = id
            popup.target = self
            popup.action = #selector(choiceChanged(_:))
            popup.controlSize = .small
            popup.font = .systemFont(ofSize: NSFont.smallSystemFontSize)
            for option in item.options ?? [] {
                popup.addItem(withTitle: option.label)
                popup.lastItem?.representedObject = option.value
            }
            popup.setAccessibilityLabel(item.label)
            row.controls = [popup]
            row.refresh = { value in
                guard case let .string(name) = value else { return }
                let index = popup.indexOfItem(withRepresentedObject: name)
                if index >= 0 { popup.selectItem(at: index) }
            }
            control = popup
        case .int:
            let field = NSTextField(string: "")
            field.identifier = id
            field.alignment = .right
            field.target = self
            field.action = #selector(intFieldChanged(_:))
            field.translatesAutoresizingMaskIntoConstraints = false
            field.widthAnchor.constraint(equalToConstant: 44).isActive = true
            field.setAccessibilityLabel(item.label)
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
            row.texts.append((range, .secondaryLabelColor))
            row.controls = [field, stepper]
            row.isBusy = { field.currentEditor() != nil }
            row.refresh = { value in
                guard case let .int(number) = value else { return }
                field.integerValue = number
                stepper.integerValue = number
            }
            let stack = NSStackView(views: [field, stepper, range])
            stack.orientation = .horizontal
            stack.spacing = 4
            stack.setCustomSpacing(6, after: stepper)
            // 並びの幅は中身に合わせる (NSStackView の既定は伸びるので、余りを左の列と取り合って範囲の文字が右端から離れる)
            stack.setHuggingPriority(.required, for: .horizontal)
            control = stack
        }
        // 読み上げの補足は、どの種類の部品 (スイッチ・選択・数の欄・ステッパー) も同じ文 (要約 + 例 + 全文の説明)
        for part in row.controls { part.setAccessibilityHelp(row.help) }
        control.translatesAutoresizingMaskIntoConstraints = false
        control.setContentHuggingPriority(.required, for: .horizontal)
        control.setContentCompressionResistancePriority(.required, for: .horizontal)

        let rowView = NSView()
        rowView.translatesAutoresizingMaskIntoConstraints = false
        rowView.addSubview(left)
        rowView.addSubview(control)
        let leading = Self.rowInset + (isChild ? Self.childIndent : 0)
        NSLayoutConstraint.activate([
            left.topAnchor.constraint(equalTo: rowView.topAnchor, constant: 9),
            left.leadingAnchor.constraint(equalTo: rowView.leadingAnchor, constant: leading),
            left.bottomAnchor.constraint(equalTo: rowView.bottomAnchor, constant: -9),
            left.trailingAnchor.constraint(equalTo: control.leadingAnchor, constant: -16),
            control.trailingAnchor.constraint(equalTo: rowView.trailingAnchor, constant: -Self.rowInset),
            // 部品は名前の 1 行目の高さにそろえる (説明を開いても、部品は名前の横に残る)
            control.centerYAnchor.constraint(equalTo: name.topAnchor, constant: 8),
            control.topAnchor.constraint(greaterThanOrEqualTo: rowView.topAnchor, constant: 5),
            control.bottomAnchor.constraint(lessThanOrEqualTo: rowView.bottomAnchor, constant: -5),
        ])
        row.view = rowView

        // 行の上の区切り線 (左右を 12pt あける)
        let separatorBox = NSBox()
        separatorBox.boxType = .separator
        separatorBox.translatesAutoresizingMaskIntoConstraints = false
        let separator = NSView()
        separator.translatesAutoresizingMaskIntoConstraints = false
        separator.addSubview(separatorBox)
        NSLayoutConstraint.activate([
            separatorBox.leadingAnchor.constraint(equalTo: separator.leadingAnchor, constant: Self.rowInset),
            separatorBox.trailingAnchor.constraint(equalTo: separator.trailingAnchor, constant: -Self.rowInset),
            separatorBox.centerYAnchor.constraint(equalTo: separator.centerYAnchor),
            separator.heightAnchor.constraint(equalToConstant: 1),
        ])
        row.separator = separator
        return row
    }

    private static let detailsPrefix = "details:"

    /// 部品の読み上げの補足: 要約 + 例 (画面の「例: …」と同じ) + 全文の説明 (要約と同じなら省く)。
    /// 画面の要約と例は読み上げの要素にしていないので、ここで読ませる (要約・例を別の要素にすると、部品と離れて同じ内容を 2 回読む)。
    private static func accessibilityHelp(for item: SettingItem) -> String {
        var parts = [item.displaySummary]
        if let example = item.example, !example.isEmpty { parts.append("例: \(example)") }
        if item.description != item.displaySummary { parts.append(item.description) }
        return parts.joined(separator: "\n")
    }

    private static func detailsAccessibilityLabel(_ label: String, open: Bool) -> String {
        open ? "「\(label)」の詳しい説明を閉じる" : "「\(label)」の詳しい説明を開く"
    }

    /// 読み上げの要素にしない (部品の名前・ヘルプと同じ内容を、別の文字として何度も読ませない)。
    /// 文字の欄の読み上げはセルが受け持つので、欄とセルの両方に指定する (欄だけでは AX の一覧に残った)。
    private static func hideFromAccessibility(_ label: NSTextField) {
        label.setAccessibilityElement(false)
        label.cell?.setAccessibilityElement(false)
    }

    @objc private func toggleDetails(_ sender: NSButton) {
        guard let key = sender.identifier?.rawValue, key.hasPrefix(Self.detailsPrefix),
              let row = rows[String(key.dropFirst(Self.detailsPrefix.count))], let description = row.description else { return }
        let open = description.isHidden
        // 検索していないときは利用者の状態として覚える。検索中に押したものは、検索を消すまでのあいだだけ
        if SettingsLayout.isEmptyQuery(searchField.stringValue) { row.userOpen = open } else { row.searchOpen = open }
        setDescription(row, open: open)
        // 開いたら、読み上げにも説明が出たことを知らせる
        if open { UI.announce(description.stringValue, in: window) }
    }

    /// 全文の説明を開く / 閉じる。ボタンの文字・読み上げの名前・開閉の状態 (AXExpanded) も合わせる。
    private func setDescription(_ row: Row, open: Bool) {
        guard let description = row.description, let button = row.detailsButton, description.isHidden == open else { return }
        description.isHidden = !open
        button.title = open ? "閉じる" : "詳しく"
        button.setAccessibilityLabel(Self.detailsAccessibilityLabel(row.item.label, open: open))
        button.setAccessibilityExpanded(open)
    }

    // ---- 検索 ----

    @objc private func searchChanged(_ sender: NSSearchField) {
        applySearch()
        // 絞り込んだら先頭から見せる
        scrollView.contentView.scroll(to: .zero)
        scrollView.reflectScrolledClipView(scrollView.contentView)
    }

    /// 検索の文字で行を絞る。一致しない行・その区切り線・一致の無いグループ (見出しとカード) を隠し、左の一覧では薄くする。
    /// 子が一致したら親も出す (薄くした子だけが、何の子か分からないまま出ないように)。
    /// 隠れている全文の説明でだけ一致した行は、説明を開いて一致した理由を見せる (検索を消すと、利用者が開いていた / 閉じていた状態に戻す)。
    private func applySearch() {
        let query = searchField.stringValue
        let searching = !SettingsLayout.isEmptyQuery(query)
        let shown = SettingsLayout.shownKeys(sections.flatMap { $0.rows.map(\.item) }, query: query)
        let wasNoMatch = !noMatchLabel.isHidden
        var anyMatch = false
        for section in sections {
            var first = true
            for row in section.rows {
                let match = shown.contains(row.item.key)
                row.view.isHidden = !match
                if searching {
                    let auto = match && SettingsLayout.matchesOnlyInDescription(row.item, query: query)
                    setDescription(row, open: row.searchOpen ?? (row.userOpen || auto))
                } else {
                    row.searchOpen = nil
                    setDescription(row, open: row.userOpen)
                }
                // カードの中で見えている最初の行には、上の区切り線を引かない
                row.separator.isHidden = !match || first
                if match { first = false }
            }
            section.hasMatch = !first
            section.view.isHidden = first
            anyMatch = anyMatch || !first
        }
        noMatchLabel.isHidden = anyMatch || sections.isEmpty
        // 読み上げは、一致があった状態から無くなったときだけ (一致の無いまま打ち続けても、1 文字ごとに読ませない)
        if !noMatchLabel.isHidden && !wasNoMatch { UI.announce(noMatchLabel.stringValue, in: window) }
        groupTable.reloadData()
        syncSelectionToScroll()
    }

    // ---- 左のグループの一覧 ----

    func numberOfRows(in tableView: NSTableView) -> Int { sections.count }

    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        let cell = UI.cell(in: tableView, identifier: NSUserInterfaceItemIdentifier("groupCell"))
        let section = sections[row]
        cell.textField?.stringValue = section.name
        // 検索で一致が無いグループは薄くする (選べない)
        cell.textField?.textColor = section.hasMatch ? .labelColor : .tertiaryLabelColor
        // 名前は、行の要素 (AXCell = NSTableCellView) と、その中の文字 (AXStaticText = 文字の欄のセル) の両方に付ける
        // (どちらを読むかは読み上げの辿り方で変わる。AXCell の名前を外すと、行の名前が空になった)
        let accessibilityName = section.hasMatch ? section.name : "\(section.name) (一致なし)"
        cell.setAccessibilityLabel(accessibilityName)
        cell.textField?.cell?.setAccessibilityLabel(accessibilityName)
        return cell
    }

    func tableView(_ tableView: NSTableView, shouldSelectRow row: Int) -> Bool {
        sections.indices.contains(row) && sections[row].hasMatch
    }

    func tableViewSelectionDidChange(_ notification: Notification) {
        guard !syncingSelection, sections.indices.contains(groupTable.selectedRow) else { return }
        scroll(to: sections[groupTable.selectedRow])
    }

    /// 一覧を押すたび (選んであるグループをもう一度押したときも) そのグループの先頭へスクロールする。
    @objc private func groupClicked(_ sender: NSTableView) {
        let index = sender.clickedRow
        guard !syncingSelection, sections.indices.contains(index), sections[index].hasMatch else { return }
        scroll(to: sections[index])
    }

    /// そのグループの見出しが右の一番上に来るまでスクロールする (最後のほうのグループは、下端までで止まる)。
    private func scroll(to section: Section) {
        document.layoutSubtreeIfNeeded()
        let clip = scrollView.contentView
        let frame = section.view.convert(section.view.bounds, to: document)
        let maxY = max(0, document.bounds.height - clip.bounds.height)
        let y = min(max(0, frame.minY - 10), maxY)
        syncingSelection = true
        clip.scroll(to: NSPoint(x: 0, y: y))
        scrollView.reflectScrolledClipView(clip)
        syncingSelection = false
    }

    @objc private func contentScrolled(_ notification: Notification) {
        guard !syncingSelection else { return }
        syncSelectionToScroll()
    }

    /// 右で今一番上に見えているグループを、左の一覧で選ぶ。
    private func syncSelectionToScroll() {
        let visible = sections.filter(\.hasMatch)
        guard !visible.isEmpty else {
            syncingSelection = true
            groupTable.deselectAll(nil)
            syncingSelection = false
            return
        }
        // まだ窓に入っていない (タブを開く前) と位置が決まっていないので、先頭のグループを選んでおく
        guard view.window != nil else {
            select(sections.firstIndex { $0 === visible[0] })
            return
        }
        document.layoutSubtreeIfNeeded()
        let clip = scrollView.contentView.bounds
        let frames = visible.map { $0.view.convert($0.view.bounds, to: document) }
        var current = visible[0]
        for (section, frame) in zip(visible, frames) where frame.minY <= clip.minY + 24 { current = section }
        // 下端まで来たら (最後のほうのグループを選んでスクロールした直後など)、選んでいるグループが見えているなら選び直さない
        if clip.maxY >= document.bounds.height - 2, sections.indices.contains(groupTable.selectedRow) {
            let selected = sections[groupTable.selectedRow]
            if let index = visible.firstIndex(where: { $0 === selected }), frames[index].intersects(clip) { current = selected }
        }
        select(sections.firstIndex { $0 === current })
    }

    /// 左の一覧の選択を変える (スクロールはし直さない)。
    private func select(_ index: Int?) {
        guard let index, groupTable.selectedRow != index else { return }
        syncingSelection = true
        groupTable.selectRowIndexes(IndexSet(integer: index), byExtendingSelection: false)
        syncingSelection = false
    }

    // ---- 読み込み ----

    /// 本体から読み直して画面に反映する。config.json が読めなければ、理由を出して操作できなくする。
    private func reload() {
        guard let catalog = dictionary.settings() else {
            showProblem("設定ファイル (config.json) を読めません。ファイルが壊れていないか確認してください (「ファイル」メニューの「データフォルダを開く」)。壊れたままの設定は書き換えません。")
            setEnabled(false)
            return
        }
        showProblem(nil)
        if rows.isEmpty {
            build(catalog)
            setEnabled(true, force: true)
            return
        }
        var changed = false
        for item in catalog.items {
            guard let row = rows[item.key], values[item.key] != item.value, !row.isBusy() else { continue }
            row.refresh(item.value)
            values[item.key] = item.value
            changed = true
        }
        // 値が変わったとき (親の値が変わったかもしれない) と、読めない状態から戻ったときだけ、子の操作可否を合わせ直す
        // (1.5 秒ごとの読み直しのたびに全部の部品を塗り直さない)
        setEnabled(true, force: changed)
    }

    /// config.json を読めるかどうかで、全体を操作できる / できないにする。読めるときも、親が OFF の子は操作できないまま。
    /// 操作できる / できないが変わったとき (または force) だけ、部品を合わせ直す。
    private func setEnabled(_ enabled: Bool, force: Bool = false) {
        let wasEnabled = controlsEnabled
        controlsEnabled = enabled
        resetButton?.isEnabled = enabled
        if force || wasEnabled != enabled { updateAvailability() }
    }

    /// 親が OFF の子を操作できなくして薄くする。値が変わるたび (自分の変更・戻し・既定値・読み直しで値が変わったとき) に呼ぶ。
    /// 操作できない子は、読み上げのヘルプの先頭に理由を足し、要約の下に使えるようにする方法を出し、「詳しく」も薄い色にする (押せるまま)。
    private func updateAvailability() {
        for row in rows.values {
            let available = SettingsLayout.isAvailable(row.item.key, values: values)
            for control in row.controls { control.isEnabled = controlsEnabled && available }
            row.setDimmed(!available)
            // 理由は 1 つだけ伝える: 設定ファイルを読めないあいだはそちら (親のスイッチも押せないので「ON にすると使えます」は出さない)
            var help = row.help
            if !controlsEnabled { help = "設定ファイルを読めないため変更できません。\n" + help }
            else if !available, let parentLabel = row.parentLabel { help = "「\(parentLabel)」が OFF のため変更できません。\n" + help }
            for control in row.controls where control.accessibilityHelp() != help { control.setAccessibilityHelp(help) }
            row.unavailableNote?.isHidden = !controlsEnabled || available
            row.detailsButton?.contentTintColor = available ? .controlAccentColor : .tertiaryLabelColor
        }
    }

    /// ほかのプロセス (入力メニュー) の変更を拾う。
    func pollChanges() {
        // タブの画面は、初めて開くまで作られない (作る前に組み立てると、loadView で順番が崩れる)
        guard isViewLoaded else { return }
        reload()
        updateZenzaiStatus()
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
        // 親を切り替えたら、子の操作可否も変わる
        updateAvailability()
    }

    private func revert(_ row: Row) {
        if let value = values[row.item.key] { row.refresh(value) }
        reload()
    }

    @objc private func boolChanged(_ sender: NSSwitch) {
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
        alert.informativeText = "このタブにある設定をすべて、初めの状態に戻します。ユーザー辞書・専門用語集・ほかの設定は変わりません。"
        // 取り消せない操作なので、Return で押される先頭のボタンはキャンセルにする (入力メニューの「学習データをすべて消去」と同じ方針)。
        alert.addButton(withTitle: "キャンセル")
        let reset = alert.addButton(withTitle: "既定値に戻す")
        reset.hasDestructiveAction = true
        reset.keyEquivalent = ""
        alert.beginSheetModal(for: window) { [weak self] response in
            guard response == .alertSecondButtonReturn, let self else { return }
            // 数を入力中なら、保存せずに閉じる (戻したあとに、入力中の値が書き戻されないように)
            self.abortEditing()
            if let reason = self.dictionary.resetSettings() {
                UI.showError(reason, title: "既定値に戻せませんでした", in: self.window)
                return
            }
            // 画面を本体の値に合わせる (子の操作可否も reload の中で合わせ直す)
            self.values = [:]
            self.reload()
        }
    }
}
