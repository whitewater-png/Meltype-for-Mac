// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text.Json;
using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>
/// 本家 #676: 入力中の Esc を、打った内容を全部消すのではなく一段ずつ戻す設定 (EscStepsBack)。
/// 漢字 → ひらがな → 打ったローマ字 → 消す。見た目が変わらない段は飛ばす。戻した語は日本語 / 英語の判定として覚えない。既定は OFF (今までは全部消す)。
/// </summary>
internal static class EscStepsBackTests
{
    private static bool Empty(CompositionTests.Keyboard k) => string.IsNullOrEmpty(k.Showing);

    private static JsonElement Item(string json, string key)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("items").EnumerateArray().First(i => i.GetProperty("key").GetString() == key).Clone();
    }

    [Test]
    public static void Setting_DefaultsToOff_AndIsInCatalog()
    {
        Assert.Equal(false, new Settings().EscStepsBack);
        Assert.Equal(false, new CompositionOptions().EscStepsBack());
        var item = Item(MacSettingsCatalog.ToJson(new Settings()), nameof(Settings.EscStepsBack));
        Assert.Equal("bool", item.GetProperty("kind").GetString());
        Assert.Equal(false, item.GetProperty("value").GetBoolean());
        Assert.Equal("キー操作", item.GetProperty("group").GetString());
        var attribute = typeof(Settings).GetProperty(nameof(Settings.EscStepsBack))!
            .GetCustomAttributes(typeof(System.ComponentModel.DisplayNameAttribute), false)
            .Cast<System.ComponentModel.DisplayNameAttribute>().Single();
        Assert.Equal(attribute.DisplayName, item.GetProperty("label").GetString());
    }

    [Test]
    public static void Off_EscClearsEverythingAtOnce()
    {
        var k = new CompositionTests.Keyboard();
        k.Type("kyou");
        k.Press(VirtualKeys.F7);
        k.Press(VirtualKeys.Escape);
        Assert.True(Empty(k), "OFF は今までどおり 1 回で全部消す: " + k.Showing);
        Assert.Equal("", k.Host.Document);
    }

    [Test]
    public static void On_KatakanaStepsBackToHiraganaThenRomajiThenClears()
    {
        var k = new CompositionTests.Keyboard { EscStepsBack = true };
        k.Type("kyou");
        k.Press(VirtualKeys.F7);
        Assert.Equal("キョウ", k.Showing);
        k.Press(VirtualKeys.Escape);
        Assert.Equal("きょう", k.Showing, "1 回目: ひらがな");
        k.Press(VirtualKeys.Escape);
        Assert.Equal("kyou", k.Showing, "2 回目: 打ったローマ字");
        k.Press(VirtualKeys.Escape);
        Assert.True(Empty(k), "3 回目: 消える: " + k.Showing);
        Assert.Equal("", k.Host.Document);
    }

    [Test]
    public static void On_ConversionStepsBackToKanaThenRomaji()
    {
        var k = new CompositionTests.Keyboard { EscStepsBack = true };
        k.Type("kyou");
        k.Press(VirtualKeys.Space);
        k.Press(VirtualKeys.Escape);
        Assert.Equal("きょう", k.Showing, "変換中の Esc: 読みに戻る");
        k.Press(VirtualKeys.Escape);
        Assert.Equal("kyou", k.Showing, "次の Esc: 打ったローマ字");
        k.Press(VirtualKeys.Escape);
        Assert.True(Empty(k), "その次で消える: " + k.Showing);
    }

    [Test]
    public static void On_PlainKanaSkipsTheUnchangedStep()
    {
        // 打ったまま (Auto) の kyou は、ひらがな表示。ひらがなへの段は見た目が変わらないので飛ばして、ローマ字へ
        var k = new CompositionTests.Keyboard { EscStepsBack = true };
        k.Type("kyou");
        Assert.Equal("きょう", k.Showing);
        k.Press(VirtualKeys.Escape);
        Assert.Equal("kyou", k.Showing);
        k.Press(VirtualKeys.Escape);
        Assert.True(Empty(k), k.Showing);
    }

    [Test]
    public static void On_EnglishAsTypedJustClears()
    {
        var k = new CompositionTests.Keyboard { EscStepsBack = true };
        k.Type("hello");
        Assert.Equal("hello", k.Showing);
        k.Press(VirtualKeys.Escape);
        Assert.True(Empty(k), "英字は戻す段が無く消える: " + k.Showing);
    }

    [Test]
    public static void On_ReconvertEscStillRestoresOriginal()
    {
        // 再変換 (選択した文字を変換) の Esc は、今までどおり元の文字を書き戻す
        foreach (var on in new[] { false, true })
        {
            var k = new CompositionTests.Keyboard { EscStepsBack = on };
            Assert.True(k.Controller.ReconvertKana("きょう", "今日"), "前提: 再変換が始まる");
            k.Press(VirtualKeys.Escape);
            Assert.Equal("きょう", k.Host.View?.Text, $"on={on}: 1 回目で読みに戻る");
            k.Press(VirtualKeys.Escape);
            Assert.Equal("今日", k.Host.Document, $"on={on}: 次の Esc で元の文字を書き戻す");
            Assert.True(Empty(k), $"on={on}: " + k.Showing);
        }
    }

    // ---- 学習を汚さない ----

    private static string Learn(bool escFirst, LanguageMemory memory)
    {
        var k = new CompositionTests.Keyboard(languages: memory) { EscStepsBack = true };
        k.Type("kyouhamergedesu");
        Assert.Equal("きょうはmergeです", k.Showing, "前提: 自動で merge が英語の区間");
        k.Press(VirtualKeys.F7);
        if (escFirst)
        {
            k.Press(VirtualKeys.Escape);
            Assert.True(k.Showing!.StartsWith("きょうは", StringComparison.Ordinal) && !k.Showing.Contains("キ") && !k.Showing.Contains("メ"), "ひらがなに戻る: " + k.Showing);
        }
        k.Press(VirtualKeys.Return);
        return k.Host.Document;
    }

    [Test]
    public static void On_StepBackDoesNotTeachLanguage()
    {
        var memory = new LanguageMemory(null);
        Detector().Memory = memory;
        try
        {
            // 対照: Esc で戻さず F7 (カタカナ) のまま確定すると、merge を日本語として覚える (学習は生きている)
            Learn(escFirst: false, memory);
            Assert.True(memory.Get("merge") == false, "対照: 覚える");

            var fresh = new LanguageMemory(null);
            Detector().Memory = fresh;
            Learn(escFirst: true, fresh);
            Assert.True(fresh.Get("merge") is null, "Esc で戻した語は判定として覚えない: " + fresh.Get("merge"));

            // 戻した入力を確定したあと、次の入力は今までどおり学習する
            var k = new CompositionTests.Keyboard(languages: fresh) { EscStepsBack = true };
            k.Type("kyouhamergedesu");
            k.Press(VirtualKeys.F7);
            k.Press(VirtualKeys.Return);
            Assert.True(fresh.Get("merge") == false, "次の入力は覚える (戻した印を引きずらない)");
        }
        finally
        {
            Detector().Memory = null;
        }
    }

    [Test]
    public static void On_EscCancelledInputDoesNotLeaveFlagBehind()
    {
        // 戻したあと全部消した次の入力でも、学習は今までどおり
        var memory = new LanguageMemory(null);
        Detector().Memory = memory;
        try
        {
            var k = new CompositionTests.Keyboard(languages: memory) { EscStepsBack = true };
            k.Type("kyouhamergedesu");
            k.Press(VirtualKeys.F7);
            k.Press(VirtualKeys.Escape);
            k.Press(VirtualKeys.Escape);
            k.Press(VirtualKeys.Escape);
            Assert.True(Empty(k), k.Showing);
            k.Type("kyouhamergedesu");
            k.Press(VirtualKeys.F7);
            k.Press(VirtualKeys.Return);
            Assert.True(memory.Get("merge") == false, "消した後の次の入力は覚える");
        }
        finally
        {
            Detector().Memory = null;
        }
    }

    private static CompositionDetector Detector() => CompositionTests.Detector;
}
