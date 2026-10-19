// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>P51 (本家 #787): 尺貫法の単位 → 単位の字とメートル法の大きさ。</summary>
internal static class TraditionalUnitTests
{
    [Test]
    public static void Units_OfferUnitAndMetric()
    {
        Assert.Equal("3尺|約90.91cm", string.Join("|", CompositionController.TypedUnitCandidates("3しゃく")!));
        Assert.Equal("1坪|約3.31㎡", string.Join("|", CompositionController.TypedUnitCandidates("1つぼ")!));
        Assert.Equal("2貫|約7.5kg", string.Join("|", CompositionController.TypedUnitCandidates("2かん")!));
        Assert.Equal("1丈|約3.03m", string.Join("|", CompositionController.TypedUnitCandidates("1じょう")!));
        Assert.Equal("1合|約180mL", string.Join("|", CompositionController.TypedUnitCandidates("1ごう")!), "100 以上は整数に丸める");
        Assert.Equal("3斗|約54.12L", string.Join("|", CompositionController.TypedUnitCandidates("3と")!));
        Assert.Equal("3尺|約90.91cm", string.Join("|", CompositionController.TypedUnitCandidates("３しゃく")!), "全角の数字");
        Assert.Equal("1.5尺|約45.45cm", string.Join("|", CompositionController.TypedUnitCandidates("1.5しゃく")!), "小数");
    }

    [Test]
    public static void NotAUnit_GivesNothing()
    {
        foreach (var reading in new[] { "かん", "3", "3しゃくら", "abc", "3しゃく3" })
            Assert.True(CompositionController.TypedUnitCandidates(reading) is null, reading);
    }
}
