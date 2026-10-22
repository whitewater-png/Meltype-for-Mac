// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text.Json;
using Meltype.Config;

namespace Meltype.Tests;

/// <summary>
/// config.json の JSON Schema (config.schema.json) の書き出し (本家 #1073)。
/// 起動時に config.json の隣へ 1 回だけ書き、書き間違いに気づけるようにする。
/// config.json 本体には触らない (IME と「設定」タブが別のプロセスで書き合うため)。
/// </summary>
internal static class ConfigSchemaTests
{
    /// <summary>一時フォルダーに config.json を置き、そのパスを返す。</summary>
    private static string UseTempConfig(out Action restore)
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-schema-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "config.json");
        restore = () =>
        {
            ConfigSchema.ResetForTests();
            try { Directory.Delete(directory, recursive: true); } catch { }
        };
        return path;
    }

    private static JsonElement Root(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    [Test]
    public static void Schema_IsObjectWithProperties()
    {
        var root = Root(ConfigSchema.Generate());
        Assert.Equal("object", root.GetProperty("type").GetString(), "スキーマの type は object");
        Assert.True(root.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object, "properties を持つ");
        Assert.Equal(ConfigSchema.Dialect, root.GetProperty("$schema").GetString(), "$schema は 2020-12");
        Assert.True(root.TryGetProperty("title", out _), "title を持つ");
    }

    [Test]
    public static void Schema_ContainsMainSettings()
    {
        var properties = Root(ConfigSchema.Generate()).GetProperty("properties");
        // Mac の「設定」タブから変える主要な設定が、config.json の項目名のまま載っている
        foreach (var key in new[]
                 {
                     nameof(Settings.Punctuation), nameof(Settings.ZSymbolConversion), nameof(Settings.LiveConversion),
                     nameof(Settings.DetectionLevel), nameof(Settings.InputStyle), nameof(Settings.FileLog),
                 })
            Assert.True(properties.TryGetProperty(key, out _), $"{key} が properties に無い");

        // 選択肢は config.json に書く名前 (列挙型の名前) の文字列
        var punctuation = properties.GetProperty(nameof(Settings.Punctuation)).GetProperty("enum");
        Assert.True(punctuation.EnumerateArray().Any(v => v.GetString() == nameof(PunctuationStyle.Japanese)), "Punctuation に Japanese が無い");
        Assert.Equal("boolean", properties.GetProperty(nameof(Settings.ZSymbolConversion)).GetProperty("type").GetString(), "ZSymbolConversion は boolean");

        // [JsonIgnore] の計算プロパティは載せない
        Assert.True(!properties.TryGetProperty(nameof(Settings.EffectiveJapaneseThreshold), out _), "JsonIgnore の項目は載せない");
    }

    [Test]
    public static void Schema_HasTitlesFromAttributes()
    {
        var properties = Root(ConfigSchema.Generate()).GetProperty("properties");
        var live = properties.GetProperty(nameof(Settings.LiveConversion));
        Assert.Equal("ライブ変換", live.GetProperty("title").GetString(), "DisplayName が title に入る");
        Assert.True(live.GetProperty("description").GetString()!.Contains("Space"), "Description が description に入る");
    }

    [Test]
    public static void WriteNextTo_WritesBesideConfigJson()
    {
        var path = UseTempConfig(out var restore);
        try
        {
            ConfigSchema.ResetForTests();
            ConfigSchema.WriteNextTo(path);
            var schemaPath = Path.Combine(Path.GetDirectoryName(path)!, ConfigSchema.FileName);
            Assert.True(File.Exists(schemaPath), "config.json と同じフォルダーに config.schema.json ができる");
            Assert.Equal(Path.GetDirectoryName(Path.GetFullPath(path)), Path.GetDirectoryName(Path.GetFullPath(schemaPath)), "同じフォルダー");
            Assert.True(!File.Exists(ConfigSchema.PathFor(Path.Combine(Path.GetTempPath(), "meltype-schema-elsewhere", "config.json"))), "別の場所には書かない");
            var root = Root(File.ReadAllText(schemaPath));
            Assert.Equal("object", root.GetProperty("type").GetString(), "書き出したスキーマも object");
        }
        finally { restore(); }
    }

    [Test]
    public static void WriteNextTo_BrokenConfigJson_StillWritesSchema()
    {
        var path = UseTempConfig(out var restore);
        try
        {
            ConfigSchema.ResetForTests();
            // config.json が壊れていても、スキーマは config.json を読まずに書ける
            File.WriteAllText(path, "{ これは壊れた設定");
            ConfigSchema.WriteNextTo(path);
            Assert.True(File.Exists(ConfigSchema.PathFor(path)), "壊れた config.json でもスキーマは書ける");
        }
        finally { restore(); }
    }

    [Test]
    public static void WriteNextTo_DoesNotTouchConfigJson()
    {
        var path = UseTempConfig(out var restore);
        try
        {
            ConfigSchema.ResetForTests();
            const string original = "{ \"SettingsVersion\": 5, \"Punctuation\": \"Comma\" }\n";
            File.WriteAllText(path, original);
            ConfigSchema.WriteNextTo(path);
            Assert.Equal(original, File.ReadAllText(path), "config.json の中身は変わらない");
            Assert.True(!File.Exists(path + ".schema.json"), "config.json の名前は変えない");
            // 一時ファイルを残さない (書いてから置き換える)
            Assert.Equal(0, Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp").Length, "一時ファイルが残っていない");
        }
        finally { restore(); }
    }

    [Test]
    public static void WriteNextTo_WritesOnlyOncePerProcess()
    {
        var path = UseTempConfig(out var restore);
        try
        {
            ConfigSchema.ResetForTests();
            ConfigSchema.WriteNextTo(path);
            var schemaPath = ConfigSchema.PathFor(path);
            File.WriteAllText(schemaPath, "書き換えた");
            ConfigSchema.WriteNextTo(path);
            Assert.Equal("書き換えた", File.ReadAllText(schemaPath), "起動時に 1 回だけ (2 回目は書かない)");
        }
        finally { restore(); }
    }

    [Test]
    public static void WriteNextTo_DoesNotThrowWhenItCannotWrite()
    {
        var path = UseTempConfig(out var restore);
        try
        {
            ConfigSchema.ResetForTests();
            // config.schema.json と同じ名前のフォルダーを作って、置き換えを失敗させる
            Directory.CreateDirectory(ConfigSchema.PathFor(path));
            File.WriteAllText(path, "{ \"SettingsVersion\": 5 }");
            ConfigSchema.WriteNextTo(path);
            Assert.Equal("{ \"SettingsVersion\": 5 }", File.ReadAllText(path), "書けなくても config.json はそのまま");
        }
        finally { restore(); }
    }

    [Test]
    public static void CatalogKeys_AllExistInSchema()
    {
        // Mac の「設定」タブが見せる項目 (MacSettingsCatalog) は、すべて config.json の項目名としてスキーマにある
        var properties = Root(ConfigSchema.Generate()).GetProperty("properties");
        foreach (var item in MacSettingsCatalog.Items)
            Assert.True(properties.TryGetProperty(item.Key, out _), $"設定タブの「{item.Key}」がスキーマの properties に無い");
    }
}
