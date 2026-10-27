// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Config;

namespace Meltype.Tests;

/// <summary>
/// <see cref="Settings.Load"/> が手で編集された config.json をどう読むか (Q20)。
/// 1 項目の型違いでは、その項目だけ既定に戻して他は保つ。JSON 全体が読めないときだけ .broken に退避して既定値。
/// </summary>
internal static class SettingsLoadTests
{
    /// <summary>一時フォルダーの config.json のパス。終わったらフォルダーごと消す。</summary>
    private static string TempConfig(out Action cleanup)
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        cleanup = () => { try { Directory.Delete(directory, recursive: true); } catch { } };
        return Path.Combine(directory, "config.json");
    }

    [Test]
    public static void BrokenSyntax_DefaultsAndBackup()
    {
        var path = TempConfig(out var cleanup);
        try
        {
            File.WriteAllText(path, "{ \"SettingsVersion\": 5, \"LiveConversion\": false, ");
            var loaded = Settings.Load(path);
            Assert.True(loaded.LiveConversion, "構文が壊れた JSON は既定値");
            Assert.True(File.Exists(path + ".broken"), "元のファイルは .broken に退避する");
            Assert.True(File.Exists(path), "元のファイルは消さない");
        }
        finally { cleanup(); }
    }

    [Test]
    public static void WrongTypeItem_OnlyThatItemDefaults()
    {
        var path = TempConfig(out var cleanup);
        try
        {
            // 文字列の整数 ("2") と、無い列挙名 ("Z") の 2 項目だけ型が違う
            File.WriteAllText(path, "{ \"SettingsVersion\": 5, \"LiveConversion\": false, \"PredictionMinLength\": \"2\", \"Punctuation\": \"Z\", \"JapaneseThreshold\": 7 }");
            var loaded = Settings.Load(path);
            Assert.True(!loaded.LiveConversion, "型の合う項目 (LiveConversion: false) は保たれる");
            Assert.Equal(7, loaded.JapaneseThreshold, "型の合う項目 (JapaneseThreshold) は保たれる");
            Assert.Equal(new Settings().PredictionMinLength, loaded.PredictionMinLength, "型の違う項目 (PredictionMinLength) だけ既定");
            Assert.Equal(new Settings().Punctuation, loaded.Punctuation, "無い列挙名 (Punctuation) だけ既定");
            Assert.True(!File.Exists(path + ".broken"), "1 項目の型違いでは .broken を作らない");
            Assert.True(File.ReadAllText(path).Contains("\"LiveConversion\": false"), "元のファイルは書き換えない");
        }
        finally { cleanup(); }
    }

    [Test]
    public static void WrongTypeItem_LoadForUpdateStillReads()
    {
        // Q20 (査読 F4-1): 書き換えのための読み込み (辞書アプリの設定タブ・ConfigFlag など) も、1 項目の型違いでは null にしない
        var path = TempConfig(out var cleanup);
        try
        {
            File.WriteAllText(path, "{ \"SettingsVersion\": 5, \"LiveConversion\": false, \"PredictionMinLength\": \"2\" }");
            var loaded = Settings.LoadForUpdate(path);
            Assert.True(loaded is not null, "型違い 1 項目では書き換え用の読み込みも通る");
            Assert.True(!loaded!.LiveConversion, "型の合う項目は保たれる");
            Assert.Equal(new Settings().PredictionMinLength, loaded.PredictionMinLength, "型の違う項目だけ既定");
            File.WriteAllText(path, "{ not json");
            Assert.True(Settings.LoadForUpdate(path) is null, "JSON 全体が読めないときは今までどおり null");
        }
        finally { cleanup(); }
    }

    [Test]
    public static void UnknownKey_IgnoredOthersKept()
    {
        var path = TempConfig(out var cleanup);
        try
        {
            File.WriteAllText(path, "{ \"SettingsVersion\": 5, \"LiveConversion\": false, \"NoSuchSetting\": \"x\", \"AnotherUnknown\": { \"a\": 1 } }");
            var loaded = Settings.Load(path);
            Assert.True(!loaded.LiveConversion, "未知のキーがあっても他の項目は保たれる");
            Assert.True(!File.Exists(path + ".broken"), "未知のキーでは .broken を作らない");
        }
        finally { cleanup(); }
    }

    [Test]
    public static void MissingVersion_Migrates()
    {
        var path = TempConfig(out var cleanup);
        try
        {
            // SettingsVersion が無い = v1。v1 の既定 (IdleFlushMs 400) は新しい既定 (700) に移り、maya.exe (v5) が足される
            File.WriteAllText(path, "{ \"LiveConversion\": false, \"IdleFlushMs\": 400, \"AppRules\": [] }");
            var loaded = Settings.Load(path);
            Assert.Equal(Settings.CurrentVersion, loaded.SettingsVersion, "移行して最新の版になる");
            Assert.Equal(700, loaded.IdleFlushMs, "v1 の既定値は新しい既定値に移る");
            Assert.True(loaded.HasAppRule("maya.exe"), "v5 の既定のコードのアプリが足される");
            Assert.True(!loaded.LiveConversion, "ユーザーの値は保たれる");
            Assert.True(File.ReadAllText(path).Contains("\"SettingsVersion\": " + Settings.CurrentVersion), "移行後の形で保存し直す");
            Assert.True(!File.Exists(path + ".broken"), ".broken は作らない");
        }
        finally { cleanup(); }
    }

    [Test]
    public static void EmptyFile_DefaultsAndBackup()
    {
        var path = TempConfig(out var cleanup);
        try
        {
            File.WriteAllText(path, "");
            var loaded = Settings.Load(path);
            Assert.Equal(new Settings().ToJson(), loaded.ToJson(), "0 バイトは既定値");
            Assert.True(File.Exists(path + ".broken"), "0 バイトも JSON として読めないので .broken に退避する (今の挙動)");
        }
        finally { cleanup(); }
    }
}
