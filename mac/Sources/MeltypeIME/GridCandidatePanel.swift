// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import AppKit

/// 格子状 (5 列) の候補を出す自前のパネル。設定「Tab で候補を格子状に広げる」が ON のとき、変換中に Tab を押すと出る。
/// macOS 26 の IMKit は IMKCandidatePanelType を無視し、どの種別でも 1 行の候補バー (IMKUICandidateBarListView) で描く。
/// 右端の下向き三角 (IMKUICandidateArrowButton) を利用者が押したときだけ格子に広げるが、そのボタンを
/// performClick でプログラムから押すと IME が SIGTRAP で落ちる (実機の診断レポート)。なので格子は自前で描く。
/// キーは取らない (canBecomeKey = false)。キーは今までどおり IMK → MeltypeInputController に届き、
/// 選択の移動・確定は Core が決める。このパネルは Core の表示 (candidates / selectedIndex) を写すだけ。
final class GridCandidatePanel: NSPanel {
    /// セルが押されたときの候補の位置。InputController が candidateSelected の経路で確定する。
    var onSelect: ((Int) -> Void)?

    static let columns = 5
    static let cellHeight: CGFloat = 22
    private static let minCellWidth: CGFloat = 64
    private static let maxCellWidth: CGFloat = 120
    private static let inset: CGFloat = 4
    private static let cornerRadius: CGFloat = 6

    private let effect = NSVisualEffectView()
    private let gridView = GridCandidateView()
    private let maskImage = GridCandidatePanel.roundedMask()
    /// 出したときの左上 (画面座標)。中身が変わったときの置き直しに使う。
    private var topLeft: NSPoint?
    private var contentSize = NSSize(width: 100, height: 30)

    init() {
        super.init(contentRect: NSRect(x: 0, y: 0, width: 100, height: 30),
                   styleMask: [.borderless, .nonactivatingPanel],
                   backing: .buffered, defer: true)
        isOpaque = false
        backgroundColor = .clear
        hasShadow = true
        // 候補をクリックで選ぶので、意味のパネル (AnnotationPanel) と違いマウスは受ける。キーは受けない。
        ignoresMouseEvents = false
        hidesOnDeactivate = false
        isReleasedWhenClosed = false
        // クリックしてもキーを奪わない (入力欄からフォーカスが離れない)。
        becomesKeyOnlyIfNeeded = true
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .transient]

        effect.material = .popover
        effect.blendingMode = .behindWindow
        effect.state = .active
        effect.maskImage = maskImage
        gridView.onSelect = { [weak self] index in self?.onSelect?(index) }
        effect.addSubview(gridView)
        contentView = effect
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError("init(coder:) は使わない") }

    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }

    /// 候補と選択を差し替えて、大きさを合わせる。出し直しでも左上の位置は変えない (place で決めた位置に置き直す)。
    func update(candidates: [String], selected: Int) {
        let font = GridCandidateView.font
        let widest = candidates.reduce(CGFloat(0)) { widest, text in
            max(widest, (text as NSString).size(withAttributes: [.font: font]).width)
        }
        let cellWidth = min(max(ceil(widest) + 16, Self.minCellWidth), Self.maxCellWidth)
        let rows = max(1, (candidates.count + Self.columns - 1) / Self.columns)
        let gridSize = NSSize(width: cellWidth * CGFloat(Self.columns), height: Self.cellHeight * CGFloat(rows))
        contentSize = NSSize(width: gridSize.width + Self.inset * 2, height: gridSize.height + Self.inset * 2)
        gridView.frame = NSRect(origin: NSPoint(x: Self.inset, y: Self.inset), size: gridSize)
        gridView.update(candidates: candidates, selected: selected,
                        cellWidth: cellWidth, cellHeight: Self.cellHeight, columns: Self.columns)
        if let topLeft { place(topLeft: topLeft) }
    }

    /// 左上を point (画面座標。候補バーの左上) に合わせて出す。下に伸ばし、画面からはみ出す分は上へ寄せる。
    func place(topLeft point: NSPoint) {
        topLeft = point
        let size = contentSize
        let center = NSPoint(x: point.x + size.width / 2, y: point.y - size.height / 2)
        let screen = NSScreen.screens.first { $0.frame.contains(center) } ?? NSScreen.main
        let area = screen?.visibleFrame ?? NSRect(origin: point, size: size)
        // 下に伸ばす。入らなければ候補バーの上端より上に伸ばし、それでも入らなければ画面の下端にそろえる。
        var y = point.y - size.height
        if y < area.minY { y = min(point.y, area.maxY - size.height) }
        if y < area.minY { y = area.minY }
        var x = point.x
        if size.width <= area.width {
            x = min(max(x, area.minX), area.maxX - size.width)
        }
        // Spotlight (macOS 27) は layer 23 にあり、候補ウィンドウの既定のレベル (3) より上に来る。
        // 意味のパネルと同じく .popUpMenu (101) を下限にして、格子が裏に隠れないようにする。
        level = NSWindow.Level(rawValue: max(level.rawValue, NSWindow.Level.popUpMenu.rawValue))
        setFrame(NSRect(origin: NSPoint(x: x, y: y), size: size), display: true)
        invalidateShadow()
        orderFrontRegardless()
    }

    func hide() {
        topLeft = nil
        if isVisible { orderOut(nil) }
    }

    /// 角丸の画像 (NSVisualEffectView.maskImage 用。角だけ固定して中は引き伸ばす)。
    private static func roundedMask() -> NSImage {
        let edge = cornerRadius * 2 + 1
        let image = NSImage(size: NSSize(width: edge, height: edge), flipped: false) { rect in
            NSColor.black.setFill()
            NSBezierPath(roundedRect: rect, xRadius: cornerRadius, yRadius: cornerRadius).fill()
            return true
        }
        image.capInsets = NSEdgeInsets(top: cornerRadius, left: cornerRadius, bottom: cornerRadius, right: cornerRadius)
        image.resizingMode = .stretch
        return image
    }
}

/// 格子の中身を描く。座標は左上が原点 (isFlipped) なので、行は上から数える。
/// 選択中のセルだけ青系の角丸 + 白文字。フォントは本文と同じ 13pt。
final class GridCandidateView: NSView {
    static let font = NSFont.systemFont(ofSize: 13)

    var onSelect: ((Int) -> Void)?

    private var candidates: [String] = []
    private var selected = 0
    private var cellWidth: CGFloat = 64
    private var cellHeight: CGFloat = 22
    private var columns = 5

    override var isFlipped: Bool { true }

    func update(candidates: [String], selected: Int, cellWidth: CGFloat, cellHeight: CGFloat, columns: Int) {
        self.candidates = candidates
        self.selected = selected
        self.cellWidth = cellWidth
        self.cellHeight = cellHeight
        self.columns = columns
        needsDisplay = true
    }

    /// 1 回目のクリックでも選べるようにする (パネルはキーを取らないので、窓を前に出すためのクリックで消費させない)。
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    override func mouseDown(with event: NSEvent) {
        let point = convert(event.locationInWindow, from: nil)
        guard point.x >= 0, point.y >= 0, cellWidth > 0, cellHeight > 0 else { return }
        let column = Int(point.x / cellWidth)
        let row = Int(point.y / cellHeight)
        guard column >= 0, column < columns else { return }
        let index = row * columns + column
        guard candidates.indices.contains(index) else { return }
        onSelect?(index)
    }

    override func draw(_ dirtyRect: NSRect) {
        super.draw(dirtyRect)
        guard !candidates.isEmpty, cellWidth > 0 else { return }
        let lineHeight = ceil(Self.font.ascender - Self.font.descender + Self.font.leading)
        let paragraph = NSMutableParagraphStyle()
        paragraph.alignment = .center
        paragraph.lineBreakMode = .byTruncatingTail
        for (index, text) in candidates.enumerated() {
            let column = index % columns
            let row = index / columns
            let cell = NSRect(x: CGFloat(column) * cellWidth, y: CGFloat(row) * cellHeight,
                              width: cellWidth, height: cellHeight)
            if index == selected {
                NSColor.controlAccentColor.setFill()
                NSBezierPath(roundedRect: cell.insetBy(dx: 1, dy: 1), xRadius: 4, yRadius: 4).fill()
            }
            let attributes: [NSAttributedString.Key: Any] = [
                .font: Self.font,
                .foregroundColor: index == selected ? NSColor.white : NSColor.labelColor,
                .paragraphStyle: paragraph,
            ]
            let textRect = NSRect(x: cell.minX + 4, y: cell.minY + (cellHeight - lineHeight) / 2,
                                  width: cell.width - 8, height: lineHeight)
            (text as NSString).draw(with: textRect, options: [.usesLineFragmentOrigin, .truncatesLastVisibleLine],
                                    attributes: attributes)
        }
    }
}
