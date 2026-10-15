import AppKit
import Carbon

/// 候補の意味を、候補ウィンドウ (IMKUIPanel) の横に出す自前の小さなパネル。
/// IMKCandidates.showAnnotation は macOS 26 で画面に何も出さない (注釈の窓が作られない。実機のトレースで確認) ので、自前で出す。
/// 候補ウィンドウの位置は IMK の非公開のクラス名 IMKUIPanel で探す。見つからなければ出さない (安全側)。メインスレッドだけで触る。
final class AnnotationPanel {
    static let shared = AnnotationPanel()

    private static let maxWidth: CGFloat = 300
    private static let padding: CGFloat = 8
    private static let gap: CGFloat = 4

    private let panel: NSPanel
    private let label: NSTextField

    private init() {
        label = NSTextField(wrappingLabelWithString: "")
        label.font = NSFont.systemFont(ofSize: 12)
        label.textColor = NSColor.labelColor
        label.isSelectable = false
        label.maximumNumberOfLines = 3
        label.lineBreakMode = .byTruncatingTail
        label.preferredMaxLayoutWidth = Self.maxWidth - Self.padding * 2

        let effect = NSVisualEffectView()
        effect.material = .popover
        effect.blendingMode = .behindWindow
        effect.state = .active
        effect.wantsLayer = true
        effect.layer?.cornerRadius = 6
        effect.layer?.masksToBounds = true
        effect.addSubview(label)

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
    }

    /// 表示中の候補ウィンドウ (IMKUIPanel)。見つからなければ nil。
    private static func candidateWindow() -> NSWindow? {
        NSApp.windows.first { $0.isVisible && String(describing: type(of: $0)).contains("IMKUIPanel") }
    }

    func show(_ text: String) {
        guard !text.isEmpty, !IsSecureEventInputEnabled(), let host = Self.candidateWindow() else { hide(); return }
        label.stringValue = text
        let textSize = label.sizeThatFits(NSSize(width: Self.maxWidth - Self.padding * 2, height: .greatestFiniteMagnitude))
        let size = NSSize(width: ceil(textSize.width) + Self.padding * 2, height: ceil(textSize.height) + Self.padding)
        label.frame = NSRect(x: Self.padding, y: Self.padding / 2, width: ceil(textSize.width), height: ceil(textSize.height))

        let hostFrame = host.frame
        let center = NSPoint(x: hostFrame.midX, y: hostFrame.midY)
        let screen = NSScreen.screens.first { $0.frame.contains(center) } ?? NSScreen.main
        let area = screen?.visibleFrame ?? hostFrame

        var x = hostFrame.maxX + Self.gap
        if x + size.width > area.maxX { x = hostFrame.minX - size.width - Self.gap }
        x = max(x, area.minX)
        var y = hostFrame.maxY - size.height   // 上端をそろえる
        y = min(max(y, area.minY), area.maxY - size.height)

        panel.level = NSWindow.Level(rawValue: host.level.rawValue + 1)
        panel.setFrame(NSRect(origin: NSPoint(x: x, y: y), size: size), display: true)
        panel.orderFrontRegardless()
    }

    func hide() {
        if panel.isVisible { panel.orderOut(nil) }
    }
}

private final class AnnotationNSPanel: NSPanel {
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}
