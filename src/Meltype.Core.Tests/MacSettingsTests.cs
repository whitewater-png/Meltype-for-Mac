// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using Meltype.Config;

namespace Meltype.Tests;

/// <summary>
/// Mac の「Meltype 辞書」の「設定」タブ (MacSettingsCatalog = FFI の meltype_settings_*) と、
/// 設定が変わったら開いたままの入力欄にも反映する仕組み (SharedSettings / ConfigFlag)。
/// </summary>
internal static class MacSettingsTests
{
    /// <summary>一時フォルダーの config.json を、設定タブ・共有の設定・2 つの入力メニューの設定の保存場所にする。ファイルを調べる間隔は 0 にする。</summary>
    private static string UseTempConfig(out Action restore)
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "config.json");
        var previousCatalog = MacSettingsCatalog.ConfigPath;
        var previousContinue = ContinueAfterConversionSetting.ConfigPath;
        var previousShiftEnter = ShiftEnterNewlineSetting.ConfigPath;
        var previousInterval = ConfigFileWatch.IntervalMs;
        MacSettingsCatalog.ConfigPath = () => path;
        ContinueAfterConversionSetting.ConfigPath = () => path;
        ShiftEnterNewlineSetting.ConfigPath = () => path;
        ConfigFileWatch.IntervalMs = 0;
        ContinueAfterConversionSetting.Reset();
        ShiftEnterNewlineSetting.Reset();
        restore = () =>
        {
            MacSettingsCatalog.ConfigPath = previousCatalog;
            ContinueAfterConversionSetting.ConfigPath = previousContinue;
            ShiftEnterNewlineSetting.ConfigPath = previousShiftEnter;
            ConfigFileWatch.IntervalMs = previousInterval;
            ContinueAfterConversionSetting.Reset();
            ShiftEnterNewlineSetting.Reset();
            try { Directory.Delete(directory, recursive: true); } catch { }
        };
        return path;
    }

    private static JsonElement Item(string json, string key)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("items").EnumerateArray().First(i => i.GetProperty("key").GetString() == key).Clone();
    }

    [Test]
    public static void Catalog_MatchesSettingsAttributes()
    {
        var keys = new HashSet<string>();
        foreach (var item in MacSettingsCatalog.Items)
        {
            Assert.True(keys.Add(item.Key), $"{item.Key} が重複している");
            var property = typeof(Settings).GetProperty(item.Key) ?? throw new AssertionException($"{item.Key} は Settings のプロパティではない");
            var name = property.GetCustomAttribute<DisplayNameAttribute>();
            Assert.True(name is not null, $"{item.Key} に DisplayName が無い");
            Assert.Equal(name!.DisplayName, item.Label, $"{item.Key} の名前");
            var description = property.GetCustomAttribute<DescriptionAttribute>();
            Assert.True(description is not null, $"{item.Key} に Description が無い");
            Assert.Equal(description!.Description, item.Description, $"{item.Key} の説明 (Windows と共通の文)");
            // Mac 向けに直した説明は、Windows の言い回しを含まない
            if (item.MacDescription is { } mac)
                foreach (var windowsOnly in new[] { "トレイ", "LOCALAPPDATA", "Keyboard モード", "Windows の", "IME 自動切替", "Microsoft IME" })
                    Assert.True(!mac.Contains(windowsOnly), $"{item.Key} の Mac 向けの説明に「{windowsOnly}」がある");
            else
                foreach (var windowsOnly in new[] { "トレイ", "LOCALAPPDATA", "Keyboard モード", "Windows の", "IME 自動切替", "Microsoft IME" })
                    Assert.True(!item.Description.Contains(windowsOnly), $"{item.Key} の説明に Windows 向けの言い回し「{windowsOnly}」が残っている (Mac 向けの説明を足す)");
            Assert.True(item.Group.Length > 0, $"{item.Key} のグループが空");
            // 種類が型と合っている。選択肢は列挙型のすべての値で、名前と画面の名前 (Description) も合っている。
            if (item.Kind == MacSettingKind.Bool) Assert.True(property.PropertyType == typeof(bool), $"{item.Key} は bool");
            if (item.Kind == MacSettingKind.Int) Assert.True(property.PropertyType == typeof(int) && item.Min < item.Max, $"{item.Key} は int で範囲がある");
            if (item.Kind == MacSettingKind.Choice)
            {
                Assert.True(property.PropertyType.IsEnum, $"{item.Key} は列挙型");
                var fields = property.PropertyType.GetFields(BindingFlags.Public | BindingFlags.Static);
                Assert.Equal(fields.Length, item.Options.Count, $"{item.Key} の選択肢の数");
                foreach (var field in fields)
                {
                    var option = item.Options.FirstOrDefault(o => o.Value == field.Name);
                    Assert.True(option is not null, $"{item.Key} に {field.Name} の選択肢が無い");
                    Assert.Equal(field.GetCustomAttribute<DescriptionAttribute>()!.Description, option!.Label, $"{item.Key}.{field.Name} の画面の名前");
                }
            }
        }
        // Mac で効かない設定・プロファイルや更新の項目は入れない
        foreach (var excluded in new[] { nameof(Settings.Mode), nameof(Settings.InputStyle), nameof(Settings.AutoUpdate), nameof(Settings.AppRules), nameof(Settings.Profiles), nameof(Settings.LearningEnabled) })
            Assert.True(!keys.Contains(excluded), $"{excluded} は入れない");
    }

    [Test]
    public static void Catalog_DefaultsAndLimits_MatchSettings()
    {
        // 既定値が空の config.json から読めること、範囲が Normalize の範囲と同じこと
        var json = MacSettingsCatalog.ToJson(new Settings());
        Assert.Equal(true, Item(json, nameof(Settings.LiveConversion)).GetProperty("value").GetBoolean());
        Assert.Equal("Balanced", Item(json, nameof(Settings.DetectionLevel)).GetProperty("value").GetString());
        Assert.Equal("Japanese", Item(json, nameof(Settings.Punctuation)).GetProperty("value").GetString());
        var min = Item(json, nameof(Settings.PredictionMinLength));
        Assert.Equal(2, min.GetProperty("value").GetInt32());
        Assert.True(min.GetProperty("min").GetInt32() == 1 && min.GetProperty("max").GetInt32() == 5, "予測の最小の文字数は 1〜5");
        var threshold = Item(json, nameof(Settings.DictionarySuggestThreshold));
        Assert.True(threshold.GetProperty("min").GetInt32() == 2 && threshold.GetProperty("max").GetInt32() == 10, "登録を提案する回数は 2〜10");
        // 範囲の端が、そのまま保たれる (Normalize で削られない)
        var path = UseTempConfig(out var restore);
        try
        {
            foreach (var (key, low, high) in new[] { (nameof(Settings.PredictionMinLength), 1, 5), (nameof(Settings.DictionarySuggestThreshold), 2, 10) })
            {
                Assert.True(MacSettingsCatalog.Apply(key, low.ToString()) is null && MacSettingsCatalog.Apply(key, high.ToString()) is null, key + " の両端は通る");
                Assert.Equal(high, Item(MacSettingsCatalog.Read()!, key).GetProperty("value").GetInt32(), key);
            }
        }
        finally { restore(); }
    }

    [Test]
    public static void Apply_RoundTrip_BoolEnumInt_KeepsOtherSettings()
    {
        var path = UseTempConfig(out var restore);
        try
        {
            File.WriteAllText(path, "{ \"SettingsVersion\": 5, \"Enabled\": false, \"PasteApps\": \"Foo.exe\", \"EnabledTermDomains\": [\"ai\"] }");
            Assert.Equal<string?>(null, MacSettingsCatalog.Apply(nameof(Settings.LiveConversion), "false"), "bool");
            Assert.Equal<string?>(null, MacSettingsCatalog.Apply(nameof(Settings.DetectionLevel), "\"Conservative\""), "enum");
            Assert.Equal<string?>(null, MacSettingsCatalog.Apply(nameof(Settings.Punctuation), "\"CommaJapanese\""), "enum 2");
            Assert.Equal<string?>(null, MacSettingsCatalog.Apply(nameof(Settings.PredictionMinLength), "4"), "int");
            Assert.Equal<string?>(null, MacSettingsCatalog.Apply(nameof(Settings.FileLog), "true"), "ログ");
            var json = MacSettingsCatalog.Read()!;
            Assert.True(!Item(json, nameof(Settings.LiveConversion)).GetProperty("value").GetBoolean(), "ライブ変換 OFF");
            Assert.Equal("Conservative", Item(json, nameof(Settings.DetectionLevel)).GetProperty("value").GetString());
            Assert.Equal("CommaJapanese", Item(json, nameof(Settings.Punctuation)).GetProperty("value").GetString());
            Assert.Equal(4, Item(json, nameof(Settings.PredictionMinLength)).GetProperty("value").GetInt32());
            var saved = Settings.Load(path);
            Assert.True(saved.DetectionLevel == DetectionLevel.Conservative && saved.Punctuation == PunctuationStyle.CommaJapanese && saved.FileLog && !saved.LiveConversion, "config.json に保存される");
            Assert.True(!saved.Enabled && saved.PasteApps == "Foo.exe" && saved.EnabledTermDomains.SequenceEqual(["ai"]), "ほかの項目は消えない");
            Assert.True(File.ReadAllText(path).Contains("\"DetectionLevel\": \"Conservative\""), "列挙型は名前で保存される");
        }
        finally { restore(); }
    }

    [Test]
    public static void Read_UndefinedEnumValue_FallsBackToDefaultOption()
    {
        var path = UseTempConfig(out var restore);
        try
        {
            File.WriteAllText(path, "{ \"SettingsVersion\": 5, \"Punctuation\": 7, \"DetectionLevel\": 99, \"LiveConversion\": false }");
            var json = MacSettingsCatalog.Read();
            Assert.True(json is not null, "定義のない列挙値があっても一覧は返る");
            Assert.Equal("Japanese", Item(json!, nameof(Settings.Punctuation)).GetProperty("value").GetString(), "既定の選択肢として見せる");
            Assert.Equal("Balanced", Item(json!, nameof(Settings.DetectionLevel)).GetProperty("value").GetString(), "既定の選択肢として見せる (2)");
            Assert.True(!Item(json!, nameof(Settings.LiveConversion)).GetProperty("value").GetBoolean(), "ほかの項目は読める");
            // 直すと保存できる (Windows の読み込みの挙動は変えない)
            Assert.Equal<string?>(null, MacSettingsCatalog.Apply(nameof(Settings.Punctuation), "\"Comma\""));
            Assert.Equal<string?>(null, MacSettingsCatalog.ResetToDefaults());
            Assert.True(Settings.Load(path).Punctuation == PunctuationStyle.Japanese, "既定値に戻せる");
        }
        finally { restore(); }
    }

    [Test]
    public static void Apply_Invalid_ReturnsReason_AndWritesNothing()
    {
        var path = UseTempConfig(out var restore);
        try
        {
            File.WriteAllText(path, "{ \"SettingsVersion\": 5 }");
            var before = File.ReadAllText(path);
            foreach (var (key, value) in new[]
            {
                ("NoSuchSetting", "true"),
                (nameof(Settings.LiveConversion), "\"yes\""),
                (nameof(Settings.LiveConversion), "1"),
                (nameof(Settings.LiveConversion), "{ 壊れた"),
                (nameof(Settings.PredictionMinLength), "0"),
                (nameof(Settings.PredictionMinLength), "6"),
                (nameof(Settings.PredictionMinLength), "2.5"),
                (nameof(Settings.PredictionMinLength), "\"3\""),
                (nameof(Settings.DictionarySuggestThreshold), "11"),
                (nameof(Settings.DetectionLevel), "\"Nope\""),
                (nameof(Settings.DetectionLevel), "2"),
                (nameof(Settings.Mode), "\"AutoSwitch\""),
            })
            {
                var reason = MacSettingsCatalog.Apply(key, value);
                Assert.True(!string.IsNullOrEmpty(reason), $"{key}={value} は断る");
            }
            Assert.Equal(before, File.ReadAllText(path), "断ったときは何も書かない");
            Assert.True((MacSettingsCatalog.Apply(nameof(Settings.PredictionMinLength), "9") ?? "").Contains("1〜5"), "範囲を理由に書く");
        }
        finally { restore(); }
    }

    [Test]
    public static void Apply_DoesNotOverwriteUnreadableConfig()
    {
        var path = UseTempConfig(out var restore);
        try
        {
            File.WriteAllText(path, "{ これは壊れた設定");
            Assert.True(MacSettingsCatalog.Apply(nameof(Settings.LiveConversion), "false") is { Length: > 0 }, "読めない設定には保存せず理由を返す");
            Assert.True(MacSettingsCatalog.ResetToDefaults() is { Length: > 0 }, "既定値に戻すのも同じ");
            Assert.Equal<string?>(null, MacSettingsCatalog.Read(), "読めなければ null");
            Assert.Equal("{ これは壊れた設定", File.ReadAllText(path), "元のファイルはそのまま");
        }
        finally { restore(); }
    }

    [Test]
    public static void Apply_MissingConfig_CreatesIt()
    {
        var path = UseTempConfig(out var restore);
        try
        {
            Assert.True(!File.Exists(path), "最初は無い");
            Assert.True(MacSettingsCatalog.Read() is not null, "無くても既定値で出る");
            Assert.Equal<string?>(null, MacSettingsCatalog.Apply(nameof(Settings.SpaceAroundEnglish), "true"));
            Assert.True(Settings.Load(path).SpaceAroundEnglish, "作って保存する");
        }
        finally { restore(); }
    }

    [Test]
    public static void Reset_OnlyTouchesCatalogKeys()
    {
        var path = UseTempConfig(out var restore);
        try
        {
            File.WriteAllText(path, "{ \"SettingsVersion\": 5, \"LiveConversion\": false, \"PredictionMinLength\": 4, \"DetectionLevel\": \"Manual\", \"ContinueAfterConversion\": true, " +
                "\"ShiftEnterNewline\": false, \"FileLog\": true, \"Enabled\": false, \"Mode\": \"AutoSwitch\", \"LearningEnabled\": false, \"PasteApps\": \"Foo.exe\", \"AutoUpdate\": false, " +
                "\"EnabledTermDomains\": [\"ai\"], \"AppRules\": [ { \"Process\": \"com.example.app\", \"Enabled\": false } ], \"ActiveProfile\": \"仕事\", \"Profiles\": [ { \"Name\": \"仕事\" } ] }");
            Assert.Equal<string?>(null, MacSettingsCatalog.ResetToDefaults());
            var saved = Settings.Load(path);
            var defaults = new Settings();
            foreach (var item in MacSettingsCatalog.Items)
                Assert.Equal(Item(MacSettingsCatalog.ToJson(defaults), item.Key).GetProperty("value").ToString(), Item(MacSettingsCatalog.ToJson(saved), item.Key).GetProperty("value").ToString(), item.Key + " は既定値");
            Assert.True(!saved.Enabled && saved.Mode == InputMode.AutoSwitch && !saved.LearningEnabled && saved.PasteApps == "Foo.exe" && !saved.AutoUpdate, "Mac の一覧にない設定はそのまま");
            Assert.True(saved.EnabledTermDomains.SequenceEqual(["ai"]) && saved.AppRules.Count == 1 && saved.AppRules[0].Process == "com.example.app", "専門用語集・アプリ別設定はそのまま");
            Assert.True(saved.ActiveProfile == "仕事" && saved.Profiles.Any(p => p.Name == "仕事"), "プロファイルはそのまま");
            // 入力メニューの 2 つの設定も、既定値 (OFF / ON) に戻って見える
            Assert.True(!ContinueAfterConversionSetting.IsOn && ShiftEnterNewlineSetting.IsOn, "入力メニューの設定も戻る");
        }
        finally { restore(); }
    }

    [Test]
    public static void ProfileValues_AreNotAppliedOnLoad_TopLevelWins()
    {
        // 使っているプロファイルの保存値 (Values) が古くても、読み込みで上書きされない。画面が書く先は最上位の値。
        var path = UseTempConfig(out var restore);
        try
        {
            File.WriteAllText(path, "{ \"SettingsVersion\": 5, \"ActiveProfile\": \"標準\", \"LiveConversion\": true, \"Profiles\": [ { \"Name\": \"標準\", \"Values\": { \"LiveConversion\": true } } ] }");
            Assert.Equal<string?>(null, MacSettingsCatalog.Apply(nameof(Settings.LiveConversion), "false"));
            var saved = Settings.Load(path);
            Assert.True(!saved.LiveConversion, "最上位の値が効く");
            Assert.True(!(Settings.Load(path).Clone().Normalize().LiveConversion), "Normalize しても戻らない");
            // プロファイルを切り替えるときは、今の値が今のプロファイルに保存される (画面で変えた値が失われない)
            var withOther = saved.AddProfile("別")!;
            Assert.True(!withOther.SwitchProfile("標準").LiveConversion, "切り替えて戻っても OFF のまま");
        }
        finally { restore(); }
    }

    [Test]
    public static void SharedSettings_PicksUpExternalChange_AndKeepsLastGoodWhenBroken()
    {
        var path = UseTempConfig(out var restore);
        try
        {
            File.WriteAllText(path, "{ \"SettingsVersion\": 5, \"LiveConversion\": true }");
            var shared = new SharedSettings(() => path);
            Assert.True(shared.Current.LiveConversion, "最初は ON");
            Assert.True(ReferenceEquals(shared.Current, shared.Current), "変わらなければ同じ値");
            // 別のプロセスが書き換えたつもり
            Assert.Equal<string?>(null, MacSettingsCatalog.Apply(nameof(Settings.LiveConversion), "false"));
            Assert.True(!shared.Current.LiveConversion, "書き換えを拾う");
            // 壊れた設定は読み直せないので、最後に読めた値のまま
            File.WriteAllText(path, "{ これは壊れた設定 ");
            Assert.True(!shared.Current.LiveConversion, "壊れたら最後に読めた値");
            File.WriteAllText(path, "{ \"SettingsVersion\": 5, \"LiveConversion\": true, \"Prediction\": false }");
            Assert.True(shared.Current.LiveConversion && !shared.Current.Prediction, "直ったらまた拾う");
            // ファイルが消えたら既定値
            File.Delete(path);
            Assert.True(shared.Current.Prediction, "消えたら既定値");
        }
        finally { restore(); }
    }

    [Test]
    public static void SharedSettings_ChecksFileOnlyOncePerInterval()
    {
        var path = UseTempConfig(out var restore);
        try
        {
            File.WriteAllText(path, "{ \"SettingsVersion\": 5, \"LiveConversion\": true }");
            ConfigFileWatch.IntervalMs = 60_000;
            var shared = new SharedSettings(() => path);
            Assert.True(shared.Current.LiveConversion, "最初は ON");
            File.WriteAllText(path, "{ \"SettingsVersion\": 5, \"LiveConversion\": false }");
            Assert.True(shared.Current.LiveConversion, "間隔の内側ではファイルを調べない");
            ConfigFileWatch.IntervalMs = 0;
            Assert.True(!shared.Current.LiveConversion, "間隔が過ぎたら拾う");
        }
        finally { restore(); }
    }

    [Test]
    public static void ConfigFlag_PicksUpExternalChange()
    {
        var path = UseTempConfig(out var restore);
        try
        {
            File.WriteAllText(path, "{ \"SettingsVersion\": 5 }");
            Assert.True(!ContinueAfterConversionSetting.IsOn && ShiftEnterNewlineSetting.IsOn, "既定値");
            // 設定タブ (別のプロセス) が書いた
            Assert.Equal<string?>(null, MacSettingsCatalog.Apply(nameof(Settings.ContinueAfterConversion), "true"));
            Assert.Equal<string?>(null, MacSettingsCatalog.Apply(nameof(Settings.ShiftEnterNewline), "false"));
            Assert.True(ContinueAfterConversionSetting.IsOn && !ShiftEnterNewlineSetting.IsOn, "外から書かれた値を拾う");
            // 入力メニューで切り替えた値は、設定タブにも見える
            Assert.True(ContinueAfterConversionSetting.Set(false), "入力メニューで切り替え");
            Assert.True(!Item(MacSettingsCatalog.Read()!, nameof(Settings.ContinueAfterConversion)).GetProperty("value").GetBoolean(), "設定タブにも見える");
            // 壊れたら前の値のまま
            File.WriteAllText(path, "{ これは壊れた設定");
            Assert.True(!ContinueAfterConversionSetting.IsOn && !ShiftEnterNewlineSetting.IsOn, "壊れたら前の値のまま");
        }
        finally { restore(); }
    }

    [Test]
    public static void ChangeReachesRunningSession()
    {
        var path = UseTempConfig(out var restore);
        try
        {
            File.WriteAllText(path, "{ \"SettingsVersion\": 5 }");
            var shared = new SharedSettings(() => path);
            var session = new Meltype.Composition.MeltypeSession(CompositionTests.Detector, new CompositionTests.FakeConverter(),
                new Meltype.Composition.CompositionOptions { Punctuation = () => shared.Current.Punctuation, ShiftEnterNewline = () => ShiftEnterNewlineSetting.IsOn, History = new Meltype.Composition.ConversionHistory(null) },
                () => shared.Current);
            string? TypeAComma()
            {
                session.HandleKey('A', 'a', false, false, false, false);
                var view = session.HandleKey(0xBC, ',', false, false, false, false).View;
                session.CommitPending();
                return view?.Text;
            }
            Assert.Equal("あ、", TypeAComma(), "最初は「、」");
            // 設定タブ (別のプロセス) が句読点を変えた。セッションは作り直さない。
            Assert.Equal<string?>(null, MacSettingsCatalog.Apply(nameof(Settings.Punctuation), "\"Comma\""));
            Assert.Equal("あ，", TypeAComma(), "作り直さなくても「，」になる");
            Assert.Equal<string?>(null, MacSettingsCatalog.Apply(nameof(Settings.ShiftEnterNewline), "false"));
            Assert.True(!ShiftEnterNewlineSetting.IsOn, "入力メニューと同じ設定も変わる");
        }
        finally { restore(); }
    }
}
