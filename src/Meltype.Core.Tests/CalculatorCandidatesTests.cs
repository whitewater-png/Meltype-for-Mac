// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>
/// P49 (本家 #796): 設定「計算式を変換すると結果を候補に出す」(既定 OFF)。
/// 式の候補は 1 文節 (英語扱い) なので、学習・登録の提案・変換エンジンへの学習には入らない。
/// </summary>
internal static class CalculatorCandidatesTests
{
    [Test]
    public static void SettingOn_OffersResultAndEquation()
    {
        var k = new CompositionTests.Keyboard { CalculatorCandidates = true };
        k.Type("1+2*3 ");
        var candidates = k.Host.View?.Candidates.ToList() ?? [];
        Assert.Equal("1+2*3", candidates[0], "先頭は打った式: " + string.Join("|", candidates));
        Assert.True(candidates.Contains("7"), "結果: " + string.Join("|", candidates));
        Assert.True(candidates.Contains("1+2*3=7"), "式=結果: " + string.Join("|", candidates));

        // / を含む式 (IsNumeric の「確定して空白」の経路に流れやすい)
        foreach (var (keys, expected) in new[] { ("10/4 ", "2.5"), ("1/3 ", "0.3333333333") })
        {
            var divided = new CompositionTests.Keyboard { CalculatorCandidates = true };
            divided.Type(keys);
            var list = divided.Host.View?.Candidates.ToList() ?? [];
            Assert.True(list.Contains(expected), keys + ": " + string.Join("|", list));
        }
    }

    [Test]
    public static void SettingOff_DoesNotOfferResult()
    {
        var k = new CompositionTests.Keyboard();
        k.Type("1+2*3 ");
        var candidates = k.Host.View?.Candidates.ToList() ?? [];
        Assert.True(!candidates.Any(c => c.Contains('=')), "設定 OFF では式=結果を出さない: " + string.Join("|", candidates));
        Assert.True(!candidates.Contains("7"), "結果も出さない: " + string.Join("|", candidates));

        // / を含む式も、設定 OFF なら今までどおり (確定して空白)
        var divided = new CompositionTests.Keyboard();
        divided.Type("10/4 ");
        var dividedCandidates = divided.Host.View?.Candidates.ToList() ?? [];
        Assert.True(!dividedCandidates.Any(c => c.Contains('=')), "設定 OFF では 10/4 も式=結果を出さない: " + string.Join("|", dividedCandidates));
    }

    [Test]
    public static void InvalidExpression_IsNotTreatedAsCalculator()
    {
        foreach (var keys in new[] { "1/0 ", "(1+2 ", "1+ " })
        {
            var k = new CompositionTests.Keyboard { CalculatorCandidates = true };
            k.Type(keys);
            var candidates = k.Host.View?.Candidates.ToList() ?? [];
            Assert.True(!candidates.Any(c => c.Contains('=')), keys + ": " + string.Join("|", candidates));
        }
    }

    [Test]
    public static void KanaInput_DoesNotUseCalculator()
    {
        var k = new CompositionTests.Keyboard { CalculatorCandidates = true, Kana = true };
        k.Type("1+2*3 ");
        var candidates = k.Host.View?.Candidates.ToList() ?? [];
        Assert.True(!candidates.Any(c => c.Contains('=')), "かな入力では出さない: " + string.Join("|", candidates));
    }

    [Test]
    public static void ChosenResult_IsNotLearned()
    {
        var history = new ConversionHistory(null);
        var k = new CompositionTests.Keyboard(history: history) { CalculatorCandidates = true };
        k.Type("1+2*3 ");
        for (var i = 0; i < 10 && k.Showing != "7"; i++) k.Type(" ");
        Assert.Equal("7", k.Showing, "7 まで進む: " + string.Join("|", k.Host.View?.Candidates ?? []));
        k.Type("\n");
        Assert.Equal("7", k.Host.Document);
        Assert.True(history.Get("1+2*3") is null, "計算結果は学習しない: " + history.Get("1+2*3"));
    }
}
