// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>P52 (本家 #785): じこう → 今月・前月・翌月の時候の挨拶。</summary>
internal static class SeasonalGreetingsTests
{
    [Test]
    public static void OctoberOffersAutumnGreetings()
    {
        var greetings = CompositionController.SeasonalGreetings(10).ToList();
        Assert.True(greetings.Contains("秋冷の候"), "今月: " + string.Join("|", greetings));
        Assert.True(greetings.Contains("紅葉の候"), "今月: " + string.Join("|", greetings));
        Assert.True(greetings.Contains("初秋の候"), "前月: " + string.Join("|", greetings));
        Assert.True(greetings.Contains("晩秋の候"), "翌月: " + string.Join("|", greetings));
        Assert.Equal("拝啓　秋冷の候、", greetings[^1], "書き出し: " + string.Join("|", greetings));
    }

    [Test]
    public static void AprilOffersSpringGreetings()
    {
        var greetings = CompositionController.SeasonalGreetings(4).ToList();
        Assert.True(greetings.Contains("陽春の候"), "4 月: " + string.Join("|", greetings));
        Assert.Equal("拝啓　桜花の候、", greetings[^1], "4 月の書き出し: " + string.Join("|", greetings));
    }

    [Test]
    public static void JanuaryStartsAnew()
    {
        var greetings = CompositionController.SeasonalGreetings(1).ToList();
        Assert.Equal("新春の候", greetings[0], "1 月の先頭");
        Assert.Equal("拝啓　新春の候、", greetings[^1], "1 月の書き出し");
    }
}
