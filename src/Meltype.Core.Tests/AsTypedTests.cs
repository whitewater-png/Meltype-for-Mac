// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text.Json;
using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>
/// 本家 #515: 設定 AlphanumericKeyStartsAsTyped「F9 / F10 の 1 回目は打ったまま」。
/// 入力中の F9 / F10 を続けて押すと、打ったまま → すべて大文字 → 先頭だけ大文字 と切り替える (ON のときだけ)。
/// OFF のときは今までどおり: F10 は何回押しても打ったままの英字 (大文字・小文字は切り替えない)。
/// </summary>
internal static class AsTypedTests
{
    private static JsonElement Item(string json, string key)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("items").EnumerateArray().First(i => i.GetProperty("key").GetString() == key).Clone();
    }

    [Test]
    public static void Setting_DefaultsToOff_AndIsInCatalog()
    {
        Assert.Equal(false, new Settings().AlphanumericKeyStartsAsTyped);
        Assert.Equal(false, new CompositionOptions().AlphanumericKeyStartsAsTyped());
        var item = Item(MacSettingsCatalog.ToJson(new Settings()), nameof(Settings.AlphanumericKeyStartsAsTyped));
        Assert.Equal("bool", item.GetProperty("kind").GetString());
        Assert.Equal(false, item.GetProperty("value").GetBoolean());
        Assert.Equal("入力", item.GetProperty("group").GetString());
        var attribute = typeof(Settings).GetProperty(nameof(Settings.AlphanumericKeyStartsAsTyped))!
            .GetCustomAttributes(typeof(System.ComponentModel.DisplayNameAttribute), false)
            .Cast<System.ComponentModel.DisplayNameAttribute>().Single();
        Assert.Equal(attribute.DisplayName, item.GetProperty("label").GetString());
    }

    [Test]
    public static void Off_F10StaysAsTyped_EveryPress()
    {
        var k = new CompositionTests.Keyboard();
        k.Type("hello");
        k.Press(VirtualKeys.F10);
        Assert.Equal("hello", k.Showing);
        k.Press(VirtualKeys.F10);
        Assert.Equal("hello", k.Showing, "OFF: 2 回目も打ったまま (今までどおり)");
        k.Press(VirtualKeys.F10);
        Assert.Equal("hello", k.Showing);
        var capital = new CompositionTests.Keyboard();
        capital.Type("Hello");
        capital.Press(VirtualKeys.F10);
        capital.Press(VirtualKeys.F10);
        Assert.Equal("Hello", capital.Showing);
        var full = new CompositionTests.Keyboard();
        full.Type("hello");
        full.Press(VirtualKeys.F9);
        full.Press(VirtualKeys.F9);
        Assert.Equal("ｈｅｌｌｏ", full.Showing, "OFF: F9 も切り替えない");
    }

    [Test]
    public static void On_F10FirstPressIsAsTyped_ThenUpper_ThenCapitalized_ThenAsTyped()
    {
        var k = new CompositionTests.Keyboard { AlphanumericKeyStartsAsTyped = true };
        k.Type("hello");
        k.Press(VirtualKeys.F10);
        Assert.Equal("hello", k.Showing, "1 回目は打ったまま");
        k.Press(VirtualKeys.F10);
        Assert.Equal("HELLO", k.Showing);
        k.Press(VirtualKeys.F10);
        Assert.Equal("Hello", k.Showing);
        k.Press(VirtualKeys.F10);
        Assert.Equal("hello", k.Showing, "一巡して打ったままに戻る");
    }

    [Test]
    public static void On_KanaShowing_FirstF10IsAsTyped()
    {
        var k = new CompositionTests.Keyboard { AlphanumericKeyStartsAsTyped = true };
        k.Type("hoge");
        k.Press(VirtualKeys.F6);
        k.Press(VirtualKeys.F10);
        Assert.Equal("hoge", k.Showing, "かなに見えているときも 1 回目は打ったまま");
    }

    [Test]
    public static void On_F10AfterOtherMode_StartsAgainFromAsTyped()
    {
        var k = new CompositionTests.Keyboard { AlphanumericKeyStartsAsTyped = true };
        k.Type("hello");
        k.Press(VirtualKeys.F10);
        k.Press(VirtualKeys.F10);
        Assert.Equal("HELLO", k.Showing);
        k.Press(VirtualKeys.F6);
        k.Press(VirtualKeys.F10);
        Assert.Equal("hello", k.Showing, "F6 を挟んだら 1 回目に戻る");
    }

    [Test]
    public static void On_F9_CyclesFullWidth_AndSkipsInvisibleStage()
    {
        var k = new CompositionTests.Keyboard { AlphanumericKeyStartsAsTyped = true };
        k.Type("hello");
        k.Press(VirtualKeys.F9);
        Assert.Equal("ｈｅｌｌｏ", k.Showing);
        k.Press(VirtualKeys.F9);
        Assert.Equal("ＨＥＬＬＯ", k.Showing);
        k.Press(VirtualKeys.F9);
        Assert.Equal("Ｈｅｌｌｏ", k.Showing);
        // 打ったままがすでにすべて大文字なら、見た目の変わらない段は飛ばす (AI → Ai)
        var ai = new CompositionTests.Keyboard { AlphanumericKeyStartsAsTyped = true };
        ai.Type("AI");
        ai.Press(VirtualKeys.F10);
        Assert.Equal("AI", ai.Showing);
        ai.Press(VirtualKeys.F10);
        Assert.Equal("Ai", ai.Showing, "AI → すべて大文字は同じ見た目なので飛ばして Ai");
    }

    [Test]
    public static void On_CommitUsesShownCase()
    {
        var k = new CompositionTests.Keyboard { AlphanumericKeyStartsAsTyped = true };
        k.Type("hello");
        k.Press(VirtualKeys.F10);
        k.Press(VirtualKeys.F10);
        k.Press(VirtualKeys.Return);
        Assert.Equal("HELLO", k.Host.Document);
    }
}
