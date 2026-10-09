// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Config;

/// <summary>
/// 「Shift+Enter で確定して改行」(Settings.ShiftEnterNewline) の、Mac の入力メニューから切り替える口。
/// 各セッションは CompositionOptions.ShiftEnterNewline の Func で毎回ここを見る。保存の作法は <see cref="ConfigFlag"/>。既定は ON。
/// </summary>
internal static class ShiftEnterNewlineSetting
{
    private static readonly ConfigFlag Flag = new("Shift+Enter で確定して改行", true, s => s.ShiftEnterNewline, (s, on) => s.ShiftEnterNewline = on);

    /// <summary>設定ファイルの場所。テストが差し替える。</summary>
    internal static Func<string> ConfigPath { get => Flag.ConfigPath; set => Flag.ConfigPath = value; }

    /// <summary>今の値。最初に使うときに config.json から読む (項目が無い・読めなければ ON)。</summary>
    public static bool IsOn => Flag.IsOn;

    /// <summary>切り替えて config.json に保存する。保存できなければ false (値は変えない)。</summary>
    public static bool Set(bool on) => Flag.Set(on);

    /// <summary>覚えた値を捨てて、次に使うときに読み直す (テスト用)。</summary>
    internal static void Reset() => Flag.Reset();
}
