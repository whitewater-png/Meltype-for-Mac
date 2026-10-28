// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import Cocoa
import InputMethodKit

/// ユーザー辞書への登録提案ができたことを知らせる 1 行のヒント。
/// 候補ウィンドウの横の意味のパネル (AnnotationPanel) と取り合わないよう、別の小さなパネルを入力位置の下に数秒だけ出す。
/// フォーカスを奪わず (nonactivating)、クリックも受けない。1 日 1 回までの制限は本体 (Core) が持つ。
/// 「もしかして」(書き間違いの指摘) の案内も同じ作りの別のパネル (misspelling) で出す。こちらは消えずに、
/// 書き間違いが無くなるか変換ボックスが閉じるまで、入力位置の行の上に出し続ける (行の下は予測・変換の候補ウィンドウが使う)。
/// 画面の下端で候補ウィンドウ・格子が行の上に回ったとき (画面の上端で案内が行の下に回ったときも) は、重ならないように
/// 候補ウィンドウの右 → 上 → 下の順に置き直す (obstacles。2026-10-28 の実機で、画面の下端の入力欄で案内が候補ウィンドウに隠れた)。
final class SuggestionHint {
    static let shared = SuggestionHint()
    /// 「もしかして: ブレスレット　<Tab>で修正」の案内 (本体の CompositionView.suggestion)。Mac 版はこれまで出していなかった (Tab の修正だけ効いていた)。
    static let misspelling = SuggestionHint(above: true)

    private let above: Bool
    private var panel: NSPanel?
    private var generation = 0
    private var shownText: String?
    /// 避ける窓 (候補ウィンドウ・格子・意味のパネル) の枠 (画面座標)。見えているものだけを返す。nil なら避けない (登録提案のヒント)。
    var obstacles: (() -> [NSRect])?
    /// 避ける前の位置 (行の上、または上端で回り込んだ行の下)。候補ウィンドウが後から動いたとき、ここから置き直す。
    private var baseFrame: NSRect?

    private init(above: Bool = false) {
        self.above = above
    }

    /// duration 秒後に消す。nil なら hide() まで出し続ける。
    /// characterIndex: 位置を測る文字の添字。nil ならキャレット (selectedRange) で測る (確定の直後の登録提案のヒント)。
    /// 変換中の文字があるときは 0 (変換中の文字の先頭) を渡す (格子の caretTopLeft と同じ測り方。打つたびに案内が横へ滑らない)。
    func show(_ text: String, near client: IMKTextInput, duration: TimeInterval? = 4, characterIndex: Int? = nil) {
        // 入力位置 (キャレット) の行の矩形。取れなければマウスの位置の近くに出す。
        var rect = NSRect.zero
        let location = characterIndex ?? client.selectedRange().location
        _ = client.attributes(forCharacterIndex: location == NSNotFound ? 0 : location, lineHeightRectangle: &rect)
        var origin = rect == .zero
            ? NSPoint(x: NSEvent.mouseLocation.x, y: NSEvent.mouseLocation.y - 24)
            : NSPoint(x: rect.minX, y: above ? rect.maxY + 4 : rect.minY - 28)

        // 同じ文を出し続けている (消えない案内で、打つたびに呼ばれる) ときは、作り直さず位置だけ合わせる (すりガラスの描き直しでちらつかないように)。
        if duration == nil, text == shownText, let panel, panel.isVisible {
            var frame = panel.frame
            frame.origin = origin
            if above, rect != .zero,
               let screen = NSScreen.screens.first(where: { $0.frame.contains(NSPoint(x: rect.midX, y: rect.midY)) }) ?? NSScreen.main,
               frame.maxY > screen.visibleFrame.maxY {
                frame.origin.y = rect.minY - frame.height - 4
            }
            baseFrame = frame
            panel.setFrameOrigin(avoided(frame).origin)
            return
        }

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
        let base = NSRect(origin: origin, size: size)
        baseFrame = base
        panel.setFrame(avoided(base), display: true)
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
        baseFrame = nil
    }

    /// 候補ウィンドウなどが出た・動いたあとに、避ける前の位置から置き直す (出していなければ何もしない)。
    func reposition() {
        guard let panel, panel.isVisible, let baseFrame else { return }
        let frame = avoided(baseFrame)
        if frame.origin != panel.frame.origin { panel.setFrameOrigin(frame.origin) }
    }

    /// frame が避ける窓と重なるなら、重ならない位置を返す。候補ウィンドウの右 (行の高さのまま。入力位置から離れにくい) →
    /// 避ける窓すべての上 → 下 の順に、画面 (visibleFrame) に収まり、どの窓とも重ならない最初の位置。どこも無理なら元のまま。
    private func avoided(_ frame: NSRect) -> NSRect {
        let blocks = (obstacles?() ?? []).filter { !$0.isEmpty }
        guard blocks.contains(where: { $0.intersects(frame) }) else { return frame }
        let all = blocks.dropFirst().reduce(blocks[0]) { $0.union($1) }
        let center = NSPoint(x: frame.midX, y: frame.midY)
        guard let screen = (NSScreen.screens.first { $0.frame.contains(center) } ?? NSScreen.main)?.visibleFrame else { return frame }
        let tries = [
            NSRect(x: all.maxX + 4, y: frame.minY, width: frame.width, height: frame.height),
            NSRect(x: frame.minX, y: all.maxY + 4, width: frame.width, height: frame.height),
            NSRect(x: frame.minX, y: all.minY - frame.height - 4, width: frame.width, height: frame.height),
        ]
        return tries.first { screen.contains($0) && !blocks.contains(where: $0.intersects) } ?? frame
    }
}
