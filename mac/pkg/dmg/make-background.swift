// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// dmg の Finder ウィンドウの背景 (開き方の図解) を描く。make-dmg.sh から呼ばれる。
//   swift make-background.swift <pkg のファイル名> <出力 1x.png> <出力 2x.png>
// 左に pkg のアイコン (Finder が描く。位置は settings.py の icon_locations) が来る前提で、右側に手順を書く。文字は make-dmg.sh の「開けないときは.txt」と同じ内容。
import AppKit

let args = CommandLine.arguments
guard args.count == 4 else {
    FileHandle.standardError.write("usage: make-background.swift <pkg名> <1x.png> <2x.png>\n".data(using: .utf8)!)
    exit(2)
}
let pkgName = args[1]
let W: CGFloat = 720, H: CGFloat = 540  // 幅は pkg/dmg/settings.py の window_rect とそろえ、窓の高さは H より大きくする (settings.py のコメント)

func draw() {
    // 背景 (ダークモードでも Finder のラベルが読めるよう、明るい単色。窓の余りの部分 (白) と継ぎ目が出ないよう白)
    NSColor.white.setFill()
    NSRect(x: 0, y: 0, width: W, height: H).fill()
    // 左のアイコン置き場
    NSColor(calibratedRed: 0.95, green: 0.96, blue: 0.98, alpha: 1).setFill()
    let card = NSBezierPath(roundedRect: NSRect(x: 30, y: 150, width: 210, height: 280), xRadius: 14, yRadius: 14)
    card.fill()
    NSColor(calibratedWhite: 0.85, alpha: 1).setStroke()
    card.lineWidth = 1
    card.stroke()

    let dark = NSColor(calibratedWhite: 0.12, alpha: 1)
    let gray = NSColor(calibratedWhite: 0.35, alpha: 1)
    let accent = NSColor(calibratedRed: 0.0, green: 0.42, blue: 0.85, alpha: 1)
    let warn = NSColor(calibratedRed: 0.72, green: 0.40, blue: 0.0, alpha: 1)

    func text(_ s: String, _ x: CGFloat, _ yTop: CGFloat, _ w: CGFloat, size: CGFloat,
              bold: Bool = false, color: NSColor = dark) -> CGFloat {
        let para = NSMutableParagraphStyle()
        para.lineSpacing = 3
        para.lineBreakMode = .byWordWrapping
        let attrs: [NSAttributedString.Key: Any] = [
            .font: bold ? NSFont.boldSystemFont(ofSize: size) : NSFont.systemFont(ofSize: size),
            .foregroundColor: color, .paragraphStyle: para]
        let a = NSAttributedString(string: s, attributes: attrs)
        let r = a.boundingRect(with: NSSize(width: w, height: 1000), options: [.usesLineFragmentOrigin])
        let h = ceil(r.height)
        a.draw(with: NSRect(x: x, y: H - yTop - h, width: w, height: h), options: [.usesLineFragmentOrigin])
        return yTop + h
    }

    // 左: アイコンの上下の案内
    _ = text("① これをダブルクリック", 44, 178, 190, size: 14, bold: true, color: accent)
    _ = text("インストーラーが開いたら、\n案内に従って進めます", 44, 352, 190, size: 11, color: gray)

    // 右: 開けないとき
    let x: CGFloat = 270, w: CGFloat = 420
    var y: CGFloat = 34
    y = text("Meltype のインストール", x, y, w, size: 20, bold: true) + 8
    y = text("「開発元が未確認のため開けません」「開いていません」などと出て開けないときは、macOS のバージョンに合わせて次のどちらかで開きます。", x, y, w, size: 12, color: gray) + 14

    y = text("macOS 15 以降", x, y, w, size: 14, bold: true, color: warn) + 4
    y = text("1. 一度ダブルクリックして開こうとし、出た表示を閉じる\n    (「ゴミ箱に入れる」は押さない)\n2. システム設定 → プライバシーとセキュリティ\n3. 下のほうの「このまま開く」を押す\n    (ログインパスワードを聞かれることがあります。\n    開こうとしてから約 1 時間だけ押せます)\n4. 確認の表示が出たら、開くほうを選ぶ", x + 8, y, w - 8, size: 12) + 14

    y = text("macOS 14 以前", x, y, w, size: 14, bold: true, color: warn) + 4
    y = text("pkg を右クリック (control + クリック) →「開く」→「開く」", x + 8, y, w - 8, size: 12) + 16

    _ = text("この pkg は Apple の署名・公証を受けていません。心配なときは、リリースページの SHA-256 と比べてから開いてください (確かめ方は「開けないときは.txt」に書いています)。", x, y, w, size: 10.5, color: gray)
}

func render(scale: CGFloat, to path: String) {
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: Int(W * scale), pixelsHigh: Int(H * scale),
                               bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
                               colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
    rep.size = NSSize(width: W, height: H)  // 72dpi 換算の大きさ。2x は 144dpi になる
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    draw()
    NSGraphicsContext.restoreGraphicsState()
    try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: path))
}

render(scale: 1, to: args[2])
render(scale: 2, to: args[3])
