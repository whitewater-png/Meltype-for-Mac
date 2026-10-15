// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import AppKit
import Carbon

/// 候補の意味を、候補ウィンドウ (IMKUIPanel) の横に出す自前の小さなパネル。
/// IMKCandidates.showAnnotation は macOS 26 で画面に何も出さない (注釈の窓が作られない。実機のトレースで確認) ので、自前で出す。
/// 候補ウィンドウの位置は IMK の非公開のクラス名 IMKUIPanel で探す。見つからなければ出さない (安全側)。メインスレッドだけで触る。
/// 出している間は 0.3 秒ごとに候補ウィンドウを見張り、消えた・動いた・大きさが変わったら隠す (IMK から閉じた通知が来ないことがあるため)。
final class AnnotationPanel {
    static let shared = AnnotationPanel()

    private static let maxWidth: CGFloat = 300
    private static let padding: CGFloat = 8
    private static let gap: CGFloat = 4
    private static let cornerRadius: CGFloat = 6

    private let panel: NSPanel
    private let label: NSTextField
    private let effect: NSVisualEffectView
    /// 出したときの候補ウィンドウの frame と、見張りのタイマー。
    private var hostFrame: NSRect?
    private var watchTimer: Timer?
    /// 3 行ぶんの高さ (同じラベル・同じ幅で "あ\nあ\nあ" を 1 回だけ測る)。これを超える分は切る。
    private let maxTextHeight: CGFloat
    private let maskImage = AnnotationPanel.roundedMask()

    /// show が出せなかった理由 (トレース用。語・意味は含めない)。
    enum Result { case shown, empty, noCandidateWindow, noRoom }

    private init() {
        label = NSTextField(wrappingLabelWithString: "")
        label.font = NSFont.systemFont(ofSize: 12)
        label.textColor = NSColor.labelColor
        label.isSelectable = false
        label.maximumNumberOfLines = 3
        label.lineBreakMode = .byTruncatingTail
        label.preferredMaxLayoutWidth = Self.maxWidth - Self.padding * 2

        effect = NSVisualEffectView()
        effect.material = .popover
        effect.blendingMode = .behindWindow
        effect.state = .active
        effect.addSubview(label)

        label.stringValue = "あ\nあ\nあ"
        maxTextHeight = ceil(label.sizeThatFits(NSSize(width: Self.maxWidth - Self.padding * 2, height: .greatestFiniteMagnitude)).height)
        label.stringValue = ""

        panel = AnnotationNSPanel(contentRect: NSRect(x: 0, y: 0, width: 100, height: 30),
                                  styleMask: [.borderless, .nonactivatingPanel],
                                  backing: .buffered, defer: true)
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = true
        panel.ignoresMouseEvents = true
        panel.hidesOnDeactivate = false
        panel.isReleasedWhenClosed = false
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .transient]
        panel.contentView = effect
        effect.maskImage = maskImage
    }

    /// 表示中の候補ウィンドウ (IMKUIPanel)。見つからなければ nil。
    private static func candidateWindow() -> NSWindow? {
        NSApp.windows.first { $0.isVisible && String(describing: type(of: $0)).contains("IMKUIPanel") }
    }

    /// 出せなかったときは隠して、理由を返す。
    @discardableResult
    func show(_ text: String) -> Result {
        guard !text.isEmpty else { hide(); return .empty }
        guard let host = Self.candidateWindow() else { hide(); return .noCandidateWindow }
        label.stringValue = text
        let textSize = label.sizeThatFits(NSSize(width: Self.maxWidth - Self.padding * 2, height: .greatestFiniteMagnitude))
        let textHeight = min(ceil(textSize.height), maxTextHeight)
        let size = NSSize(width: ceil(textSize.width) + Self.padding * 2, height: textHeight + Self.padding)
        label.frame = NSRect(x: Self.padding, y: Self.padding / 2, width: ceil(textSize.width), height: textHeight)

        let frame = host.frame
        let center = NSPoint(x: frame.midX, y: frame.midY)
        let screen = NSScreen.screens.first { $0.frame.contains(center) } ?? NSScreen.main
        let area = screen?.visibleFrame ?? frame

        // 右隣に入ればそこ、入らなければ左隣。どちらにも入らない狭い画面では出さない (候補ウィンドウに重ねない)。
        var x = frame.maxX + Self.gap
        if x + size.width > area.maxX {
            x = frame.minX - size.width - Self.gap
            if x < area.minX { hide(); return .noRoom }
        }
        var y = frame.maxY - size.height   // 上端をそろえる
        y = min(max(y, area.minY), area.maxY - size.height)

        panel.level = NSWindow.Level(rawValue: host.level.rawValue + 1)
        panel.setFrame(NSRect(origin: NSPoint(x: x, y: y), size: size), display: true)
        panel.invalidateShadow()
        panel.orderFrontRegardless()
        hostFrame = frame
        startWatching()
        return .shown
    }

    func hide() {
        watchTimer?.invalidate()
        watchTimer = nil
        hostFrame = nil
        if panel.isVisible { panel.orderOut(nil) }
    }

    /// 出している間、候補ウィンドウが消えた・動いた・大きさが変わったら隠す。出し直すたびに古いタイマーを止めてから作る。
    private func startWatching() {
        watchTimer?.invalidate()
        let timer = Timer(timeInterval: 0.3, repeats: true) { [weak self] _ in
            guard let self else { return }
            guard let host = Self.candidateWindow(), host.frame == self.hostFrame else { self.hide(); return }
        }
        timer.tolerance = 0.1
        RunLoop.main.add(timer, forMode: .common)
        watchTimer = timer
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

private final class AnnotationNSPanel: NSPanel {
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}
