// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>
/// Shift+Enter で確定して改行 (Settings.ShiftEnterNewline)。既定 ON は CompositionTests の既存テストが見る。
/// ここは OFF (Enter と同じく確定のみ) と、設定の読み書き。
/// </summary>
internal static class ShiftEnterNewlineTests
{
    private static void ShiftEnter(CompositionTests.Keyboard k)
    {
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Return);
        k.Key(VirtualKeys.LShift, up: true);
    }

    [Test]
    public static void Off_CommitsOnly_NoEnterReplay()
    {
        // 未変換
        var k = new CompositionTests.Keyboard { ShiftEnterNewline = false };
        k.Type("aiueo");
        ShiftEnter(k);
        Assert.Equal("あいうえお", k.Host.Document, "確定する");
        Assert.True(!k.Host.Events.Contains("down:0D"), "OFF は Enter を送り直さない: " + string.Join("|", k.Host.Events));

        // 変換中
        var converting = new CompositionTests.Keyboard { ShiftEnterNewline = false };
        converting.Type("aiueo ");
        Assert.True(converting.Host.View is { Converting: true }, "Space で変換中");
        ShiftEnter(converting);
        Assert.True(converting.Host.Document.Length > 0 && converting.Host.View is null, "確定する");
        Assert.True(!converting.Host.Events.Contains("down:0D"), "変換中も送り直さない: " + string.Join("|", converting.Host.Events));

        // 予測中
        var predicting = new CompositionTests.Keyboard(predictions: CompositionTests.OsewaPredictions) { ShiftEnterNewline = false };
        predicting.Type("osewa");
        predicting.Press(VirtualKeys.Tab);
        ShiftEnter(predicting);
        Assert.Equal("お世話になります", predicting.Host.Document, "予測が確定する");
        Assert.True(!predicting.Host.Events.Contains("down:0D"), "予測中も送り直さない: " + string.Join("|", predicting.Host.Events));
    }

    [Test]
    public static void Off_EmptyBox_StillPassesThrough()
    {
        var k = new CompositionTests.Keyboard { ShiftEnterNewline = false };
        ShiftEnter(k);
        Assert.True(k.Host.Events.Contains("passed:0D"), "変換ボックスが空なら OFF でも素通し: " + string.Join("|", k.Host.Events));
    }

    // ---- 設定の読み書き (Mac の入力メニュー → Core → FFI の meltype_*_shift_enter_newline の中身) ----

    private static string UseTempConfig(out Func<string> restore)
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "config.json");
        var previous = ShiftEnterNewlineSetting.ConfigPath;
        ShiftEnterNewlineSetting.ConfigPath = () => path;
        ShiftEnterNewlineSetting.Reset();
        restore = () =>
        {
            ShiftEnterNewlineSetting.ConfigPath = previous;
            ShiftEnterNewlineSetting.Reset();
            try { Directory.Delete(directory, recursive: true); } catch { }
            return "";
        };
        return path;
    }

    [Test]
    public static void Setting_DefaultOn_AndHasAttributes()
    {
        Assert.True(new Settings().ShiftEnterNewline, "既定は ON");
        var property = typeof(Settings).GetProperty(nameof(Settings.ShiftEnterNewline))!;
        var category = (System.ComponentModel.CategoryAttribute)Attribute.GetCustomAttribute(property, typeof(System.ComponentModel.CategoryAttribute))!;
        var name = (System.ComponentModel.DisplayNameAttribute)Attribute.GetCustomAttribute(property, typeof(System.ComponentModel.DisplayNameAttribute))!;
        Assert.Equal("1. 全般", category.Category);
        Assert.Equal("Shift+Enter で確定して改行", name.DisplayName);
        // 古い config.json (この項目が無い) を読むと ON のまま
        var path = UseTempConfig(out var restore);
        try
        {
            File.WriteAllText(path, "{ \"SettingsVersion\": 5, \"LiveConversion\": false }");
            Assert.True(Settings.Load(path).ShiftEnterNewline, "項目が無ければ ON");
            Assert.True(ShiftEnterNewlineSetting.IsOn, "共有の値も ON");
        }
        finally { restore(); }
    }

    [Test]
    public static void Setting_Toggle_SavesAndReloads()
    {
        var path = UseTempConfig(out var restore);
        try
        {
            File.WriteAllText(path, "{ \"SettingsVersion\": 5, \"LiveConversion\": false }");
            Assert.True(ShiftEnterNewlineSetting.IsOn, "最初は ON");
            Assert.True(ShiftEnterNewlineSetting.Set(false), "保存できる");
            Assert.True(!ShiftEnterNewlineSetting.IsOn, "OFF になる");
            var saved = Settings.Load(path);
            Assert.True(!saved.ShiftEnterNewline && !saved.LiveConversion, "config.json に保存され、ほかの項目は消えない");
            ShiftEnterNewlineSetting.Reset();
            Assert.True(!ShiftEnterNewlineSetting.IsOn, "再起動後も OFF");
            Assert.True(ShiftEnterNewlineSetting.Set(true), "確認");
            Assert.True(Settings.Load(path).ShiftEnterNewline && ShiftEnterNewlineSetting.IsOn, "ON に戻せる");
        }
        finally { restore(); }
    }

    [Test]
    public static void Setting_Toggle_DoesNotOverwriteUnreadableConfig()
    {
        var path = UseTempConfig(out var restore);
        try
        {
            File.WriteAllText(path, "{ これは壊れた設定");
            Assert.True(!ShiftEnterNewlineSetting.Set(false), "読めない設定には保存しない");
            Assert.Equal("{ これは壊れた設定", File.ReadAllText(path), "元のファイルはそのまま");
            Assert.True(ShiftEnterNewlineSetting.IsOn, "値も変えない (既定の ON)");
        }
        finally { restore(); }
    }

    [Test]
    public static void Setting_ReachesRunningSession()
    {
        var path = UseTempConfig(out var restore);
        try
        {
            var session = new MeltypeSession(CompositionTests.Detector, new CompositionTests.FakeConverter(),
                new CompositionOptions { ShiftEnterNewline = () => ShiftEnterNewlineSetting.IsOn, History = new ConversionHistory(null) }, () => new Settings());
            foreach (var c in "aiueo") session.HandleKey(char.ToUpperInvariant(c), c, false, false, false, false);
            Assert.True(ShiftEnterNewlineSetting.Set(false), "確認");
            Assert.True(session.HandleKey(VirtualKeys.Return, '\r', true, false, false, false).Consumed, "作り直さなくても OFF が効く");
        }
        finally { restore(); }
    }
}
