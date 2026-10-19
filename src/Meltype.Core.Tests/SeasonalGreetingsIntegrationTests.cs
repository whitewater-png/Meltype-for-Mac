// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>P52 (本家 #785): じこう / じこうのあいさつ に今月の挨拶を出し、学習しない。</summary>
internal static class SeasonalGreetingsIntegrationTests
{
    private static readonly DateTime October = new(2026, 10, 9);

    [Test]
    public static void Jikou_OffersGreetingsForTheCurrentMonth()
    {
        var k = new CompositionTests.Keyboard(now: () => October);
        k.Type("jikou ");
        var candidates = k.Host.View?.Candidates.ToList() ?? [];
        Assert.True(candidates.Contains("秋冷の候"), "秋冷の候: " + string.Join("|", candidates));
        Assert.True(candidates.Contains("拝啓　秋冷の候、"), "書き出し: " + string.Join("|", candidates));
    }

    [Test]
    public static void JikouNoAisatsu_AlsoOffers()
    {
        var k = new CompositionTests.Keyboard(now: () => October);
        k.Type("jikounoaisatsu ");
        var candidates = k.Host.View?.Candidates.ToList() ?? [];
        Assert.True(candidates.Contains("秋冷の候"), "秋冷の候: " + string.Join("|", candidates));
    }

    [Test]
    public static void OtherReadings_GiveNothing()
    {
        var k = new CompositionTests.Keyboard(now: () => October);
        k.Type("jikouha ");
        var candidates = k.Host.View?.Candidates.ToList() ?? [];
        Assert.True(!candidates.Contains("秋冷の候"), "じこうは には出さない: " + string.Join("|", candidates));
    }

    [Test]
    public static void Choice_IsNotLearned()
    {
        var history = new ConversionHistory(null);
        var k = new CompositionTests.Keyboard(history: history, now: () => October);
        k.Type("jikou ");
        for (var i = 0; i < 20 && k.Showing != "秋冷の候"; i++) k.Type(" ");
        Assert.Equal("秋冷の候", k.Showing, "秋冷の候 まで進む: " + string.Join("|", k.Host.View?.Candidates ?? []));
        k.Type("\n");
        Assert.True(history.Get("じこう") is null, "時候の挨拶は学習しない: " + history.Get("じこう"));
    }
}
