// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import Cocoa
import InputMethodKit

/// ユーザー辞書への登録提案ができたことを知らせる 1 行のヒント。
/// 候補ウィンドウの注釈 (意味・モード表示) と取り合わないよう、別の小さなパネルを入力位置の下に数秒だけ出す。
/// フォーカスを奪わず (nonactivating)、クリックも受けない。1 日 1 回までの制限は本体 (Core) が持つ。
final class SuggestionHint {
    static let shared = SuggestionHint()

    private var panel: NSPanel?
    private var generation = 0

    func show(_ text: String, near client: IMKTextInput) {
        // 入力位置 (キャレット) の行の矩形。取れなければマウスの位置の近くに出す。
        var rect = NSRect.zero
        let location = client.selectedRange().location
        _ = client.attributes(forCharacterIndex: location == NSNotFound ? 0 : location, lineHeightRectangle: &rect)
        let origin = rect == .zero
            ? NSPoint(x: NSEvent.mouseLocation.x, y: NSEvent.mouseLocation.y - 24)
            : NSPoint(x: rect.minX, y: rect.minY - 28)

        let label = NSTextField(labelWithString: text)
        label.font = .systemFont(ofSize: 12)
        label.textColor = .labelColor
        label.sizeToFit()
        let size = NSSize(width: label.frame.width + 20, height: label.frame.height + 10)
        label.frame.origin = NSPoint(x: 10, y: 5)

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

        // 次のヒントが出たときに古い消去予約が効かないよう、世代で見分ける。
        generation += 1
        let current = generation
        DispatchQueue.main.asyncAfter(deadline: .now() + 4) { [weak self] in
            guard let self, self.generation == current else { return }
            self.panel?.orderOut(nil)
        }
    }
}
