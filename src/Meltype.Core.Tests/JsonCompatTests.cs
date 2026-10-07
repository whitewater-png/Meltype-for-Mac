// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text.Json;
using System.Text.Json.Serialization;
using Meltype.Composition;
using Meltype.Config;
using Meltype.Learning;

namespace Meltype.Tests;

/// <summary>
/// JSON の読み書きはソース生成 (SettingsJsonContext・LearningJsonContext)。NativeAOT では reflection が使えず、
/// 列挙型のプロパティを持つ Settings の保存が例外になった。ここでは、書き出し形式・読み込み結果が以前の reflection 版と同じことを確かめる
/// (AOT で動くことそのものは、mac/README.md の「AOT 版での設定保存の確認」= tools/check-mac-aot-settings.sh で確かめる)。
/// </summary>
internal static class JsonCompatTests
{
    // 以前の Settings の reflection 版のオプション
    private static readonly JsonSerializerOptions Legacy = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static Settings Customized()
    {
        var settings = new Settings { Mode = InputMode.AutoSwitch, ConversionEngine = ConversionEngine.Mozc, ContinueAfterConversion = true, EnabledTermDomains = ["civil", "it"] };
        settings.AppKinds.Add(new AppKind { Name = "メモ", Base = AppProfile.Code, DetectionLevel = DetectionLevel.Aggressive, LiveConversion = true });
        return settings.Normalize();
    }

    [Test]
    public static void SettingsJson_SameAsReflectionVersion()
    {
        foreach (var settings in new[] { new Settings().Normalize(), Customized() })
        {
            Assert.Equal(JsonSerializer.Serialize(settings, Legacy), settings.ToJson(), "書き出しの形式が以前と同じ (インデント・列挙型は名前)");
        }
        Assert.True(Customized().ToJson().Contains("\"Mode\": \"AutoSwitch\""), "列挙型は名前の文字列");
    }

    [Test]
    public static void SettingsJson_ReadsCommentsTrailingCommasAndNumbers()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "config.json");
        try
        {
            File.WriteAllText(path, "{ // コメント\n \"SettingsVersion\": 5, \"Mode\": \"AutoSwitch\", /* c */ \"ConversionEngine\": 1, \"EnabledTermDomains\": [\"ai\",], }");
            var loaded = Settings.Load(path);
            Assert.True(loaded.Mode == InputMode.AutoSwitch, "名前の列挙型を読める");
            Assert.True(loaded.ConversionEngine == (ConversionEngine)1, "数値の列挙型も読める (以前と同じ)");
            Assert.Equal("ai", string.Join(",", loaded.EnabledTermDomains), "末尾のカンマ・コメントを許す");
            // 書いて読み直しても同じ
            var again = Customized();
            again.Save(path);
            Assert.Equal(again.ToJson(), Settings.Load(path).ToJson(), "保存して読み直した結果が同じ");
        }
        finally { try { Directory.Delete(directory, recursive: true); } catch { } }
    }

    [Test]
    public static void SettingsJson_ProfilesStillWork()
    {
        var settings = Customized();
        var added = settings.AddProfile("仕事用")!;
        var switched = added.SwitchProfile("標準");
        Assert.Equal("標準", switched.ActiveProfile, "プロファイルを切り替えられる (JsonNode 経由の読み書き)");
        Assert.True(switched.Mode == InputMode.AutoSwitch, "列挙型の値が保たれる");
    }

    [Test]
    public static void LearningJson_SameAsReflectionVersion()
    {
        var counts = new Dictionary<string, Dictionary<string, int>> { ["かんたん"] = new() { ["simple"] = 2, ["easy"] = 1 } };
        Assert.Equal(JsonSerializer.Serialize(counts), JsonSerializer.Serialize(counts, LearningJsonContext.Default.TranslationCounts), "英訳の履歴");
        var memory = new Dictionary<string, LanguageMemory.Entry> { ["taro"] = new() { English = true, Used = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), Count = 2, Explicit = true } };
        Assert.Equal(JsonSerializer.Serialize(memory), JsonSerializer.Serialize(memory, LearningJsonContext.Default.LanguageEntries), "英語/日本語の記憶");
        var back = JsonSerializer.Deserialize(JsonSerializer.Serialize(memory), LearningJsonContext.Default.LanguageEntries)!;
        Assert.True(back["taro"].English && back["taro"].Count == 2 && back["taro"].Explicit && back["taro"].Used.Year == 2026, "読み込み");
        var model = new UserModel.ModelFile { Version = 1, Prefixes = new() { ["abc"] = new PrefixStats { Japanese = 1, English = 2, LastUsed = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc) } } };
        Assert.Equal(JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true }), JsonSerializer.Serialize(model, UserModelJsonContext.Default.ModelFile), "ユーザーモデル (キー名・インデント)");
    }
}
