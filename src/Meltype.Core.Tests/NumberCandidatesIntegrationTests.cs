// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>
/// P50 (本家 #769・#786): 数字の漢数字・元号を候補に出し、元号の年を西暦にする。
/// 2 文字以上の漢数字・元号は学習しない (次からその候補が先頭に出ない)。1 文字の数字は P55 で学習する。
/// </summary>
internal static class NumberCandidatesIntegrationTests
{
    private static List<string> CandidatesOf(string keys)
    {
        var k = new CompositionTests.Keyboard();
        k.Type(keys);
        return k.Host.View?.Candidates.ToList() ?? [];
    }

    [Test]
    public static void Digits_OfferKanjiAndEra_WithoutMovingTheFirst()
    {
        var candidates = CandidatesOf("2026 ");
        Assert.Equal("2026", candidates[0], "先頭は打った数字のまま: " + string.Join("|", candidates));
        Assert.True(candidates.Contains("二〇二六"), "二〇二六: " + string.Join("|", candidates));
        Assert.True(candidates.Contains("二千二十六"), "二千二十六: " + string.Join("|", candidates));
        Assert.True(candidates.Contains("令和8年"), "令和8年: " + string.Join("|", candidates));
    }

    [Test]
    public static void EraReading_OffersYear()
    {
        var candidates = CandidatesOf("reiwa8nen ");
        Assert.True(candidates.Contains("2026年"), "2026年: " + string.Join("|", candidates));
        Assert.True(candidates.Contains("2026"), "2026: " + string.Join("|", candidates));
    }

    [Test]
    public static void MultiDigitChoice_IsNotLearned()
    {
        var history = new ConversionHistory(null);
        var k = new CompositionTests.Keyboard(history: history);
        k.Type("2026 ");
        for (var i = 0; i < 20 && k.Showing != "令和8年"; i++) k.Type(" ");
        Assert.Equal("令和8年", k.Showing, "令和8年 まで進む: " + string.Join("|", k.Host.View?.Candidates ?? []));
        k.Type("\n");
        Assert.True(history.Get("2026") is null, "2 文字以上の元号は学習しない: " + history.Get("2026"));

        var next = new CompositionTests.Keyboard(history: history);
        next.Type("2026 ");
        Assert.Equal("2026", next.Host.View!.Candidates[0], "次の最初の候補は変わらない");
    }

    [Test]
    public static void OneDigitChoice_IsLearned()
    {
        // P55: 1 文字の数字は学習する (P50 の除外は 2 文字以上だけ)。
        var history = new ConversionHistory(null);
        var k = new CompositionTests.Keyboard(history: history);
        k.Type("1 ");
        for (var i = 0; i < 20 && k.Showing != "一"; i++) k.Type(" ");
        Assert.Equal("一", k.Showing, "一 まで進む: " + string.Join("|", k.Host.View?.Candidates ?? []));
        k.Type("\n");
        Assert.Equal("一", history.Get("1"), "1 文字の数字は学習する");
    }
}
