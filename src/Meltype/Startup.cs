// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Microsoft.Win32;

namespace Meltype;

/// <summary>
/// Windows の起動 (サインイン) 時に Meltype を起動するか (トレイの「Windows の起動時に起動」)。
/// Install.cmd はスタートアップのフォルダーにショートカットを置く。winget・Scoop で入れたときは無いので、
/// ON にしたときは HKCU の Run に登録する。OFF にしたときはどちらも消す。
/// </summary>
internal static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Meltype";

    private static string Shortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Meltype.lnk");

    public static bool IsEnabled
    {
        get
        {
            if (File.Exists(Shortcut)) return true;
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            if (!File.Exists(Shortcut)) key.SetValue(ValueName, $"\"{Application.ExecutablePath}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            if (File.Exists(Shortcut)) File.Delete(Shortcut);
        }
        Diagnostics.Log.Info(enabled ? "Windows の起動時に Meltype を起動します。" : "Windows の起動時に Meltype を起動しません。");
    }
}
