// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Config;

/// <summary>
/// 「変換後も続けて入力できる」(Settings.ContinueAfterConversion) の、Mac の入力メニューから切り替える口。
/// 各セッションは CompositionOptions.ContinueAfterConversion の Func で毎回ここを見る。保存の作法は <see cref="ConfigFlag"/>。既定は OFF。
/// </summary>
internal static class ContinueAfterConversionSetting
{
    /// <summary>既定値 (OFF)。config.json に項目が無い・読めないときの値で、FFI の入口 (Exports.cs) が例外のときに返す値もこれ。
    /// 定数なので、読んでも型の初期化 (設定ファイルの場所の取得など) は起きない。</summary>
    internal const bool Default = false;

    private static readonly ConfigFlag Flag = new("変換後も続けて入力", Default, s => s.ContinueAfterConversion, (s, on) => s.ContinueAfterConversion = on);

    /// <summary>設定ファイルの場所。テストが差し替える。</summary>
    internal static Func<string> ConfigPath { get => Flag.ConfigPath; set => Flag.ConfigPath = value; }

    /// <summary>今の値。最初に使うときに config.json から読む (読めなければ OFF)。</summary>
    public static bool IsOn => Flag.IsOn;

    /// <summary>切り替えて config.json に保存する。保存できなければ false (値は変えない)。</summary>
    public static bool Set(bool on) => Flag.Set(on);

    /// <summary>覚えた値を捨てて、次に使うときに読み直す (テスト用)。</summary>
    internal static void Reset() => Flag.Reset();
}
