// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Composition;

/// <summary>
/// 入力モード (あ / A) をカーソルの近くに一瞬だけ出す小さなウィンドウ。フォーカスを奪わない。
/// Meltype キーボードの使用中は Windows の IME を OFF にしているので、タスクバーの IME の表示は常に「A」になる。
/// 今どちらで入力されるかは、入力欄に入ったときと 半角/全角 を押したときにこれで知らせる。
/// </summary>
internal sealed class ModeIndicatorWindow : Form
{
    private const int WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080, WS_EX_TOPMOST = 0x00000008, WS_EX_TRANSPARENT = 0x00000020;
    private static readonly Color JapaneseColor = Color.FromArgb(76, 160, 255);
    private static readonly Color DirectColor = Color.FromArgb(110, 110, 120);
    private readonly Font _font = new("Yu Gothic UI", 14F, FontStyle.Bold);
    private readonly System.Windows.Forms.Timer _hideTimer = new() { Interval = 1200 };
    private string _text = "あ";

    public ModeIndicatorWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        Size = new Size(34, 34);
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            Hide();
        };
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // クリックは下のウィンドウへ通す。
            cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_TRANSPARENT;
            return cp;
        }
    }

    /// <summary>japanese なら「あ」、そうでなければ「A」を location に出し、しばらくしたら消す。</summary>
    public void Flash(bool japanese, Point location)
    {
        _text = japanese ? "あ" : "A";
        BackColor = japanese ? JapaneseColor : DirectColor;
        var screen = Screen.FromPoint(location).WorkingArea;
        location.X = Math.Clamp(location.X, screen.Left, screen.Right - Width);
        location.Y = Math.Clamp(location.Y, screen.Top, screen.Bottom - Height);
        Location = location;
        if (!Visible) Show();
        Invalidate();
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        TextRenderer.DrawText(e.Graphics, _text, _font, ClientRectangle, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hideTimer.Dispose();
            _font.Dispose();
        }
        base.Dispose(disposing);
    }
}
