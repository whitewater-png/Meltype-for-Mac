// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import AppKit
import MeltypeDictionaryKit

/// 「判定を調べる」のタブ: ローマ字を打つと、今の設定・学習での英語 / 日本語の区間分け (確定する前) と、
/// 「条件を 1 つだけ変えたら結果が変わるか」(学習した語・前後の文字・判定の強さ) を出す。理由の文は作らず、判定し直して比べる (切り分け)。
/// 調べるだけで、学習 (languages.json) は変えない。学習した語が原因なら、その場で消せる (「学習した語」タブと同じ削除)。
/// 「設定」タブの作り (スクロールする縦並び) を土台にしている。
final class JudgeViewController: NSViewController, NSTextFieldDelegate {
    private let explainer: DetectionExplainOperations
    private let learned: LearnedWordOperations
    private let settings: NativeDictionary

    private let typedField = NSTextField(string: "")
    private let beforeField = NSTextField(string: "")
    private let afterField = NSTextField(string: "")
    private let levelPopup = NSPopUpButton(frame: .zero, pullsDown: false)
    private let kanaNotice = UI.wrapping(DetectionExplainInput.kanaNotice, secondary: true)
    private let resultStack = NSStackView()
    private let problemLabel = UI.wrapping("", secondary: false)

    private var pending: DispatchWorkItem?
    /// 最後に表示した結果を出したときの「学習した語」の版 (別の画面・IME で学習が変わったら、出し直す)。
    private var shownLearnedVersion: Int32 = -1
    private var hasResult = false
    /// 学習した語の行が 10 件を超えたとき、残りも出しているか (打つ文字を変えたら畳む)。
    private var showAllWords = false
    weak var hostWindow: NSWindow?
    private var window: NSWindow? { view.window ?? hostWindow }

    private static let maxContentWidth: CGFloat = 720
    private static let resultFont = NSFont.monospacedSystemFont(ofSize: 12, weight: .regular)

    init(dictionary: NativeDictionary) {
        explainer = dictionary
        learned = dictionary
        settings = dictionary
        super.init(nibName: nil, bundle: nil)
        title = "判定を調べる"
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) is not supported") }

    // ---- 画面 ----

    override func loadView() {
        let note = UI.label("打つと 0.3 秒後に調べます。調べるだけで、学習は変わりません", secondary: true)
        note.setAccessibilityLabel("打つと 0.3 秒後に調べます。調べるだけで、学習は変わりません")

        typedField.placeholderString = "打つローマ字 (例: kyouhasushidesu)"
        typedField.setAccessibilityLabel("打つローマ字")
        beforeField.placeholderString = "前の文字 (空でもよい。\(DetectionExplainInput.contextLength) 文字で切ります)"
        beforeField.setAccessibilityLabel("前の文字")
        afterField.placeholderString = "後ろの文字 (空でもよい。\(DetectionExplainInput.contextLength) 文字で切ります)"
        afterField.setAccessibilityLabel("後ろの文字")
        for field in [typedField, beforeField, afterField] {
            field.delegate = self
            field.translatesAutoresizingMaskIntoConstraints = false
        }

        for strength in DetectionStrength.allCases {
            levelPopup.addItem(withTitle: strength.label)
            levelPopup.lastItem?.representedObject = strength.rawValue
        }
        levelPopup.target = self
        levelPopup.action = #selector(conditionChanged(_:))
        levelPopup.setAccessibilityLabel("判定の強さ")
        kanaNotice.setAccessibilityLabel(DetectionExplainInput.kanaNotice)
        kanaNotice.isHidden = true
        syncWithSettings()

        let levelRow = NSStackView(views: [UI.label("判定の強さ"), levelPopup, UI.spacer()])
        levelRow.orientation = .horizontal
        levelRow.spacing = 8

        let form = NSStackView(views: [
            labeled("打つローマ字", typedField),
            labeled("前の文字", beforeField),
            labeled("後ろの文字", afterField),
            levelRow,
        ])
        form.orientation = .vertical
        form.alignment = .leading
        form.spacing = 8
        form.translatesAutoresizingMaskIntoConstraints = false

        problemLabel.textColor = .systemRed
        problemLabel.isHidden = true

        resultStack.orientation = .vertical
        resultStack.alignment = .leading
        resultStack.spacing = 4
        resultStack.translatesAutoresizingMaskIntoConstraints = false

        let guidance = UI.wrapping(DetectionExplainInput.guidance)
        guidance.setAccessibilityLabel("案内")

        let content = NSStackView(views: [form, problemLabel, kanaNotice, resultStack, guidance])
        content.orientation = .vertical
        content.alignment = .leading
        content.spacing = 14
        content.translatesAutoresizingMaskIntoConstraints = false
        for part in [form, problemLabel, kanaNotice, resultStack, guidance] { part.widthAnchor.constraint(equalTo: content.widthAnchor).isActive = true }

        let document = FlippedView()
        document.translatesAutoresizingMaskIntoConstraints = false
        document.addSubview(content)
        // 右端まで広げたいが、窓の大きさは動かさない (UI.fillWidth の説明。強さを高くすると、タブを選んだだけで窓が中身の幅まで縮む)
        NSLayoutConstraint.activate([
            content.topAnchor.constraint(equalTo: document.topAnchor, constant: 16),
            content.leadingAnchor.constraint(equalTo: document.leadingAnchor, constant: 24),
        ] + UI.fillWidth(content, in: document, trailing: 24) + [
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

        note.translatesAutoresizingMaskIntoConstraints = false
        let root = NSView()
        root.addSubview(note)
        root.addSubview(scroll)
        NSLayoutConstraint.activate([
            note.topAnchor.constraint(equalTo: root.topAnchor, constant: 12),
            note.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 24),
            note.trailingAnchor.constraint(lessThanOrEqualTo: root.trailingAnchor, constant: -24),
            scroll.topAnchor.constraint(equalTo: note.bottomAnchor, constant: 8),
            scroll.leadingAnchor.constraint(equalTo: root.leadingAnchor),
            scroll.trailingAnchor.constraint(equalTo: root.trailingAnchor),
            scroll.bottomAnchor.constraint(equalTo: root.bottomAnchor),
        ])
        view = root
    }

    private final class FlippedView: NSView {
        override var isFlipped: Bool { true }
    }

    /// 見出し + 入力欄の 1 行。
    private func labeled(_ title: String, _ field: NSTextField) -> NSView {
        let label = UI.label(title)
        label.translatesAutoresizingMaskIntoConstraints = false
        label.widthAnchor.constraint(equalToConstant: 90).isActive = true
        let row = NSStackView(views: [label, field])
        row.orientation = .horizontal
        row.spacing = 8
        return row
    }

    /// 判定の強さを今の設定に合わせ (取れなければ標準)、かな入力の設定が ON なら「調べられません」の 1 行を出す。
    /// 画面を表示するたびに取り直す (設定タブで変えたあとに開いても追う。ここで強さを選び直していても、設定に戻る)。
    private func syncWithSettings() {
        let items = settings.settings()?.items ?? []
        if case let .string(name)? = items.first(where: { $0.key == "DetectionLevel" })?.value, let strength = DetectionStrength(settingName: name) {
            levelPopup.selectItem(at: Int(strength.rawValue))
        } else {
            levelPopup.selectItem(at: Int(DetectionStrength.balanced.rawValue))
        }
        if case let .string(name)? = items.first(where: { $0.key == "InputStyle" })?.value {
            kanaNotice.isHidden = name != "Kana"
        } else {
            kanaNotice.isHidden = true
        }
    }

    // ---- 調べる ----

    private var strength: DetectionStrength {
        (levelPopup.selectedItem?.representedObject as? Int32).flatMap(DetectionStrength.init(rawValue:)) ?? .balanced
    }

    func controlTextDidChange(_ notification: Notification) {
        showAllWords = false
        schedule(after: 0.3)
    }

    @objc private func conditionChanged(_ sender: Any?) { schedule(after: 0) }
    // 注: 調べる処理 (refresh) はメインスレッドで呼ぶ (NSSpellChecker を使うため)。ここからは DispatchQueue.main だけで呼ぶ。

    private func schedule(after delay: TimeInterval) {
        pending?.cancel()
        let item = DispatchWorkItem { [weak self] in self?.refresh() }
        pending = item
        DispatchQueue.main.asyncAfter(deadline: .now() + delay, execute: item)
    }

    /// 今の入力で調べ直して、結果を出す。
    private func refresh() {
        pending?.cancel()
        let typed = typedField.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        clearResult()
        guard !typed.isEmpty else {
            problemLabel.isHidden = true
            hasResult = false
            return
        }
        if let problem = DetectionExplainInput.problem(typed: typed) {
            problemLabel.stringValue = problem
            problemLabel.isHidden = false
            hasResult = false
            return
        }
        shownLearnedVersion = learned.learnedVersion()
        guard let text = explainer.explainDetection(typed: typed,
                                                    before: DetectionExplainInput.clipBefore(beforeField.stringValue),
                                                    after: DetectionExplainInput.clipAfter(afterField.stringValue),
                                                    level: strength),
              let explanation = DetectionExplanation.parse(text) else {
            problemLabel.stringValue = "調べられませんでした。Meltype を入れ直してから、もう一度試してください。"
            problemLabel.isHidden = false
            hasResult = false
            return
        }
        problemLabel.isHidden = true
        hasResult = true
        let lines = explanation.lines
        let folded = lines.filter(\.foldable).count
        for line in lines where !line.foldable || showAllWords { resultStack.addArrangedSubview(row(for: line)) }
        if folded > 0 {
            let toggle = UI.button(showAllWords ? "たたむ" : "残り \(folded) 件の学習した語を表示", target: self, action: #selector(toggleWords(_:)))
            resultStack.addArrangedSubview(toggle)
        }
        UI.announce(explanation.lines.first?.text ?? "", in: window)
    }

    @objc private func toggleWords(_ sender: Any?) {
        showAllWords.toggle()
        refresh()
    }

    private func clearResult() {
        for view in resultStack.arrangedSubviews {
            resultStack.removeArrangedSubview(view)
            view.removeFromSuperview()
        }
    }

    private func row(for line: ExplainLine) -> NSView {
        let label = NSTextField(wrappingLabelWithString: line.text)
        label.font = Self.resultFont
        label.isSelectable = true
        label.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        guard let word = line.removableWord else { return label }
        let button = UI.button("この語を消す", target: self, action: #selector(removeWord(_:)))
        button.identifier = NSUserInterfaceItemIdentifier(word)
        button.setAccessibilityLabel("学習した語「\(word)」を消す")
        let stack = NSStackView(views: [label, button])
        stack.orientation = .horizontal
        stack.alignment = .firstBaseline
        stack.spacing = 8
        return stack
    }

    /// 学習した語を消す。「学習した語」タブの削除と同じく、取り消せないので確認を出す (Return で押される先頭はキャンセル)。
    @objc private func removeWord(_ sender: NSButton) {
        guard let word = sender.identifier?.rawValue, let window else { return }
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = "「\(word)」を学習した語から削除しますか？"
        alert.informativeText = "削除は元に戻せません。次にその語を打ったときは、学習する前と同じ判定に戻ります。"
        alert.addButton(withTitle: "キャンセル")
        let delete = alert.addButton(withTitle: "削除")
        delete.hasDestructiveAction = true
        delete.keyEquivalent = ""
        alert.beginSheetModal(for: window) { [weak self] response in
            guard let self, response == .alertSecondButtonReturn else { return }
            if let error = self.learned.removeLearned([word]) {
                UI.showError(error, title: "削除できませんでした", in: window)
            }
            self.refresh()
        }
    }

    // ---- タブの出入り ----

    override func viewWillAppear() {
        super.viewWillAppear()
        syncWithSettings()
    }

    override func viewDidAppear() {
        super.viewDidAppear()
        if !typedField.stringValue.isEmpty { refresh() }
    }

    /// 「学習した語」タブ・IME で学習が変わったら、出ている結果を出し直す (本体は版を比べるだけなので軽い)。
    func pollChanges() {
        guard isViewLoaded, hasResult, learned.learnedVersion() != shownLearnedVersion else { return }
        refresh()
    }
}
