// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text.Json;
using Meltype.Composition;
using Meltype.Config;

namespace Meltype.Tests;

/// <summary>
/// 本家 #677 の設定 EnterCommitsAsShown「Enter は見えているとおりに確定」。
/// 既定は OFF。OFF のときは、Enter で確定するときに打ち間違いを直す (今までと同じ)。
/// ON なら Enter では直さず、変換ボックスに見えているとおりに確定する。Space で変換するときの直しは今までどおり。
/// </summary>
internal static class EnterAsShownTests
{
    private const string Typo = "onegaishimsu";

    private static JsonElement Item(string json, string key)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("items").EnumerateArray().First(i => i.GetProperty("key").GetString() == key).Clone();
    }

    [Test]
    public static void Setting_DefaultsToOff_AndIsInCatalog()
    {
        Assert.Equal(false, new Settings().EnterCommitsAsShown);
        Assert.Equal(false, new CompositionOptions().EnterCommitsAsShown());
        var item = Item(MacSettingsCatalog.ToJson(new Settings()), nameof(Settings.EnterCommitsAsShown));
        Assert.Equal("bool", item.GetProperty("kind").GetString());
        Assert.Equal(false, item.GetProperty("value").GetBoolean());
        Assert.Equal("入力", item.GetProperty("group").GetString());
        var attribute = typeof(Settings).GetProperty(nameof(Settings.EnterCommitsAsShown))!
            .GetCustomAttributes(typeof(System.ComponentModel.DisplayNameAttribute), false)
            .Cast<System.ComponentModel.DisplayNameAttribute>().Single();
        Assert.Equal(attribute.DisplayName, item.GetProperty("label").GetString());
    }

    [Test]
    public static void Off_EnterFixesTyposAsBefore()
    {
        var k = new CompositionTests.Keyboard();
        k.Type(Typo);
        var shown = k.Showing;
        k.Type("\n");
        Assert.Equal("おねがいします", k.Host.Document, "OFF: 確定するとき直す");
        Assert.True(k.Host.Document != shown, "前提: 表示と確定が食い違う: " + shown);
    }

    [Test]
    public static void On_EnterCommitsExactlyWhatIsShown()
    {
        var k = new CompositionTests.Keyboard { EnterCommitsAsShown = true };
        k.Type(Typo);
        var shown = k.Showing;
        k.Type("\n");
        Assert.Equal(shown, k.Host.Document, "ON: 見えているとおりに確定");
        Assert.Equal("おねがいしmす", k.Host.Document);
    }

    [Test]
    public static void On_SpaceStillFixesTypos()
    {
        var k = new CompositionTests.Keyboard { EnterCommitsAsShown = true };
        k.Type(Typo + " ");
        Assert.True(k.Showing is { } text && text.StartsWith("おねがいします"), "ON でも Space の変換では直す: " + k.Showing);
    }

    [Test]
    public static void On_CleanInputIsUnchanged()
    {
        var on = new CompositionTests.Keyboard { EnterCommitsAsShown = true };
        on.Type("onegaishimasu\n");
        var off = new CompositionTests.Keyboard();
        off.Type("onegaishimasu\n");
        Assert.Equal("おねがいします", on.Host.Document);
        Assert.Equal(off.Host.Document, on.Host.Document);
    }

    private sealed class KantanConverter : IKanjiConverter
    {
        public string? Convert(string hiragana) => hiragana == "かんたん" ? "簡単" : null;
        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) => null;
    }

    [Test]
    public static void LiveConversion_FinalNIsNotTurnedIntoKanji_WhenOn()
    {
        // かんたn と見えていた入力は、ON なら Enter で かんたん に確定する (語末の n が ん になったところで漢字にしない)
        var on = new CompositionTests.Keyboard(live: true, converter: new KantanConverter()) { EnterCommitsAsShown = true };
        on.Type("kantan\n");
        Assert.Equal("かんたん", on.Host.Document, "ON: 見えていたかなのまま");
        var off = new CompositionTests.Keyboard(live: true, converter: new KantanConverter());
        off.Type("kantan\n");
        Assert.Equal("簡単", off.Host.Document, "OFF: 今までどおり");
        var done = new CompositionTests.Keyboard(live: true, converter: new KantanConverter()) { EnterCommitsAsShown = true };
        done.Type("kantann\n");
        Assert.Equal("簡単", done.Host.Document, "nn まで打てば漢字");
    }
}
