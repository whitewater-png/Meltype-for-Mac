// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Config;

/// <summary>保存場所はすべて %LOCALAPPDATA%\Meltype\ 配下 (設計書 §21)。ネットワークには何も送らない。</summary>
internal static class AppPaths
{
    /// <summary>
    /// 保存場所。環境変数 MELTYPE_DATA_DIR があれば、そこ (動作確認で、実際の設定・学習データに触らないため。tools/check-mac-aot-settings.py)。
    /// 無ければ %LOCALAPPDATA%\Meltype (Mac は ~/Library/Application Support/Meltype。.NET は HOME ではなくアカウント情報から決めるので、HOME の差し替えでは変わらない)。
    /// </summary>
    public static string DataDirectory { get; } =
        Environment.GetEnvironmentVariable("MELTYPE_DATA_DIR") is { Length: > 0 } custom && Path.IsPathRooted(custom)
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Meltype");

    /// <summary>旧名 (AutoIME) のときの保存場所。</summary>
    private static string OldDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoIME");

    /// <summary>
    /// 旧名 (AutoIME) の設定・学習データ・ユーザー辞書があれば、新しい保存場所に移す (新しい場所がまだ無いときだけ)。
    /// 起動時に 1 回呼ぶ。移せなくても旧データは消さず、既定値で動く。
    /// </summary>
    public static void MigrateFromOldName()
    {
        try
        {
            if (Directory.Exists(DataDirectory) || !Directory.Exists(OldDataDirectory)) return;
            Directory.Move(OldDataDirectory, DataDirectory);
            var oldLog = Path.Combine(DataDirectory, "autoime.log");
            if (File.Exists(oldLog)) File.Move(oldLog, LogFile, overwrite: true);
            Diagnostics.Log.Info($"旧名 (AutoIME) の設定と学習データを {DataDirectory} に移しました。");
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"旧名 (AutoIME) の設定と学習データを移せませんでした: {ex.Message}");
        }
    }

    public static string ConfigFile => Path.Combine(DataDirectory, "config.json");
    public static string ModelFile => Path.Combine(DataDirectory, "model.json");
    public static string ConversionHistoryFile => Path.Combine(DataDirectory, "conversions.json");
    public static string DictionarySuggestionFile => Path.Combine(DataDirectory, "suggest.json");
    public static string TranslationHistoryFile => Path.Combine(DataDirectory, "translations.json");
    public static string LanguageMemoryFile => Path.Combine(DataDirectory, "languages.json");
    public static string UserDictionaryFile => Path.Combine(DataDirectory, "userdict.txt");

    /// <summary>専門用語集のうち、利用者が使わないことにした (除外した) 語。1 行に「読み[Tab]語」。config.json とは別にする (数千行になりうるため)。</summary>
    public static string TermExclusionFile => Path.Combine(DataDirectory, "terms-excluded.txt");
    public static string LogFile => Path.Combine(DataDirectory, "meltype.log");

    /// <summary>落ちたときの例外 (ファイルへのログが OFF でも書く)。</summary>
    public static string CrashLogFile => Path.Combine(DataDirectory, "crash.log");

    /// <summary>ユーザー辞書 (japanese.txt / english.txt) を置くと組み込み辞書に追加される。</summary>
    public static string UserDictionaryDirectory => Path.Combine(DataDirectory, "dictionaries");
}
