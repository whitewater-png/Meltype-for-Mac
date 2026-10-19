// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>P51 (本家 #787): 尺貫法の候補は既存の後ろに出し、学習しない。</summary>
internal static class TraditionalUnitIntegrationTests
{
    [Test]
    public static void Input_OffersUnitAndMetric()
    {
        var k = new CompositionTests.Keyboard();
        k.Type("3syaku ");
        var candidates = k.Host.View?.Candidates.ToList() ?? [];
        Assert.True(candidates.Contains("3尺"), "3尺: " + string.Join("|", candidates));
        Assert.True(candidates.Contains("約90.91cm"), "約90.91cm: " + string.Join("|", candidates));
    }

    [Test]
    public static void ThreeTo_OffersToAndMetric_WithoutMovingTheFirst()
    {
        var k = new CompositionTests.Keyboard();
        k.Type("3to ");
        var candidates = k.Host.View?.Candidates.ToList() ?? [];
        Assert.True(candidates.Contains("3斗"), "3斗: " + string.Join("|", candidates));
        Assert.True(candidates.Contains("約54.12L"), "約54.12L: " + string.Join("|", candidates));
    }

    [Test]
    public static void Choice_IsNotLearned()
    {
        var history = new ConversionHistory(null);
        var k = new CompositionTests.Keyboard(history: history);
        k.Type("3syaku ");
        for (var i = 0; i < 20 && k.Showing != "3尺"; i++) k.Type(" ");
        Assert.Equal("3尺", k.Showing, "3尺 まで進む: " + string.Join("|", k.Host.View?.Candidates ?? []));
        k.Type("\n");
        Assert.True(history.Get("3しゃく") is null, "尺貫法の候補は学習しない: " + history.Get("3しゃく"));
    }
}
