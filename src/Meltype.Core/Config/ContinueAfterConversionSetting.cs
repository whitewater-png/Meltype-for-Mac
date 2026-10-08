// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Config;

/// <summary>
/// 「変換後も続けて入力できる」(Settings.ContinueAfterConversion) の、Mac の入力メニューから切り替える口。
/// 入力欄ごとに MeltypeSession が作られるが、切り替えがすべての入力欄にすぐ反映されるよう、値はここで 1 つだけ持つ
/// (各セッションは CompositionOptions.ContinueAfterConversion の Func で毎回ここを見る)。
/// 切り替えは config.json に保存する。読めない config.json は上書きしない。
/// </summary>
internal static class ContinueAfterConversionSetting
{
    private static readonly object Lock = new();
    private static bool? s_value;

    /// <summary>設定ファイルの場所。テストが差し替える。</summary>
    internal static Func<string> ConfigPath { get; set; } = () => AppPaths.ConfigFile;

    /// <summary>今の値。最初に使うときに config.json から読む (読めなければ OFF)。</summary>
    public static bool IsOn
    {
        get
        {
            lock (Lock)
            {
                if (s_value is { } value) return value;
                bool on;
                try
                {
                    on = Settings.LoadForUpdate(ConfigPath())?.ContinueAfterConversion ?? false;
                }
                catch (Exception ex)
                {
                    Diagnostics.Log.Warn($"変換後も続けて入力の設定を読めませんでした (OFF にします): {ex.Message}");
                    on = false;
                }
                s_value = on;
                return on;
            }
        }
    }

    /// <summary>切り替えて config.json に保存する。保存できなければ false (値は変えない)。</summary>
    public static bool Set(bool on)
    {
        lock (Lock)
        {
            try
            {
                var path = ConfigPath();
                Config.SafeFile.EnsureDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                // Mac の「Meltype 辞書」の画面 (別のプロセス) も config.json を書く (専門用語集の分野) ので、ロックの中で読み直して書く。
                using (FileLock.Acquire(path))
                {
                    // 読めない設定は、既定値で上書きして失わないよう、何もしない。
                    if (Settings.LoadForUpdate(path) is not { } settings) return false;
                    settings.ContinueAfterConversion = on;
                    settings.Save(path);
                }
                s_value = on;
                return true;
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Warn($"変換後も続けて入力の設定を保存できませんでした: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>覚えた値を捨てて、次に使うときに読み直す (テスト用)。</summary>
    internal static void Reset()
    {
        lock (Lock) s_value = null;
    }
}
