// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import Cocoa
import InputMethodKit

/// ユーザー辞書への登録提案ができたことを知らせる 1 行のヒント。
/// 候補ウィンドウの横の意味のパネル (AnnotationPanel) と取り合わないよう、別の小さなパネルを入力位置の下に数秒だけ出す。
/// フォーカスを奪わず (nonactivating)、クリックも受けない。1 日 1 回までの制限は本体 (Core) が持つ。
/// 「もしかして」(書き間違いの指摘) の案内も同じ作りの別のパネル (misspelling) で出す。こちらは消えずに、
/// 書き間違いが無くなるか変換ボックスが閉じるまで、入力位置の行の上に出し続ける (行の下は予測・変換の候補ウィンドウが使う)。
final class SuggestionHint {
    static let shared = SuggestionHint()
    /// 「もしかして: ブレスレット　<Tab>で修正」の案内 (本体の CompositionView.suggestion)。Mac 版はこれまで出していなかった (Tab の修正だけ効いていた)。
    static let misspelling = SuggestionHint(above: true)

    private let above: Bool
    private var panel: NSPanel?
    private var generation = 0
    private var shownText: String?

    private init(above: Bool = false) {
        self.above = above
    }

    /// duration 秒後に消す。nil なら hide() まで出し続ける。
    func show(_ text: String, near client: IMKTextInput, duration: TimeInterval? = 4) {
        // 入力位置 (キャレット) の行の矩形。取れなければマウスの位置の近くに出す。
        var rect = NSRect.zero
        let location = client.selectedRange().location
        _ = client.attributes(forCharacterIndex: location == NSNotFound ? 0 : location, lineHeightRectangle: &rect)
        var origin = rect == .zero
            ? NSPoint(x: NSEvent.mouseLocation.x, y: NSEvent.mouseLocation.y - 24)
            : NSPoint(x: rect.minX, y: above ? rect.maxY + 4 : rect.minY - 28)

        let label = NSTextField(labelWithString: text)
        label.font = .systemFont(ofSize: 12)
        label.textColor = .labelColor
        label.sizeToFit()
        let size = NSSize(width: label.frame.width + 20, height: label.frame.height + 10)
        label.frame.origin = NSPoint(x: 10, y: 5)
        // 行の上に出すとき、画面の上端からはみ出すなら行の下に回す。
        if above, rect != .zero,
           let screen = NSScreen.screens.first(where: { $0.frame.contains(NSPoint(x: rect.midX, y: rect.midY)) }) ?? NSScreen.main,
           origin.y + size.height > screen.visibleFrame.maxY {
            origin.y = rect.minY - size.height - 4
        }

        let background = NSVisualEffectView(frame: NSRect(origin: .zero, size: size))
        background.material = .popover
        background.state = .active
        background.wantsLayer = true
        background.layer?.cornerRadius = 6
        background.addSubview(label)

        let panel = self.panel ?? NSPanel(contentRect: NSRect(origin: origin, size: size),
                                          styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = true
        panel.level = .popUpMenu
        panel.ignoresMouseEvents = true
        panel.collectionBehavior = [.canJoinAllSpaces, .transient, .ignoresCycle]
        panel.contentView = background
        panel.setFrame(NSRect(origin: origin, size: size), display: true)
        panel.orderFrontRegardless()
        self.panel = panel
        shownText = text

        // 次のヒントが出たときに古い消去予約が効かないよう、世代で見分ける。
        generation += 1
        guard let duration else { return }
        let current = generation
        DispatchQueue.main.asyncAfter(deadline: .now() + duration) { [weak self] in
            guard let self, self.generation == current else { return }
            self.panel?.orderOut(nil)
            self.shownText = nil
        }
    }

    /// 出しているヒントを消す (出していなければ何もしない)。消去予約も無効にする。
    func hide() {
        guard shownText != nil || panel?.isVisible == true else { return }
        generation += 1
        panel?.orderOut(nil)
        shownText = nil
    }
}
