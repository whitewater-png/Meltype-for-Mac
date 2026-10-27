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
    public static void DateAndPhoneLikeHyphens_CommitWithoutConversion()
    {
        // Q24: 設定 ON でも、- だけの列 (日付・電話番号) は式ではないので、Space で変換に入らず確定して空白 (設定 OFF と同じ)。
        foreach (var keys in new[] { "2026-10-27", "03-1234-5678" })
        {
            var k = new CompositionTests.Keyboard { CalculatorCandidates = true };
            k.Type(keys + " ");
            var candidates = k.Host.View?.Candidates.ToList() ?? [];
            Assert.Equal(keys + " ", k.Host.Document, keys + ": 確定して空白。候補: " + string.Join("|", candidates));
            Assert.True(!candidates.Any(c => c.Contains('=')), keys + ": 式=結果を出さない: " + string.Join("|", candidates));
        }

        // - が 1 個 (10-3) と = つき (10-3-2=) は今までどおり結果を出す。
        foreach (var (keys, expected) in new[] { ("10-3 ", "7"), ("10-3-2= ", "5") })
        {
            var k = new CompositionTests.Keyboard { CalculatorCandidates = true };
            k.Type(keys);
            var list = k.Host.View?.Candidates.ToList() ?? [];
            Assert.True(list.Contains(expected), keys + ": " + string.Join("|", list));
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

    [Test]
    public static void AfterPreviousExpression_StillCalculates()
    {
        // 計算式の文節は学習しないために英語扱い (IsEnglish = true) で作る。そのため確定直後は
        // PrecedingEnglish = true になるが、直前の文字が数字なら「英文の後ろ」ではないので計算を続ける。
        var k = new CompositionTests.Keyboard { CalculatorCandidates = true };
        k.Type("10/4 ");
        for (var i = 0; i < 10 && k.Showing != "2.5"; i++) k.Type(" ");
        Assert.Equal("2.5", k.Showing, "10/4 の結果: " + string.Join("|", k.Host.View?.Candidates ?? []));
        k.Type("\n");
        Assert.Equal("2.5", k.Host.Document, "10/4 を確定");

        k.Type("1/3 ");
        var candidates = k.Host.View?.Candidates.ToList() ?? [];
        Assert.True(candidates.Contains("0.3333333333"), "続けて 1/3: " + string.Join("|", candidates));
    }

    [Test]
    public static void AfterJapanese_StillCalculates()
    {
        // 日本語の後ろは今までどおり計算する (直前が英字でない)。
        var k = new CompositionTests.Keyboard { CalculatorCandidates = true };
        k.Host.PrecedingText = "今日は";
        k.Type("1/3 ");
        var candidates = k.Host.View?.Candidates.ToList() ?? [];
        Assert.True(candidates.Contains("0.3333333333"), "日本語の後ろ: " + string.Join("|", candidates));
    }

    [Test]
    public static void AfterEnglishWord_DoesNotCalculate()
    {
        // 英文の後ろは今までどおり (計算しない)。
        var k = new CompositionTests.Keyboard { CalculatorCandidates = true };
        k.Host.PrecedingText = "I have ";
        k.Type("10/4 ");
        var candidates = k.Host.View?.Candidates.ToList() ?? [];
        Assert.True(!candidates.Any(c => c.Contains('=')), "英文の後ろでは計算しない: " + string.Join("|", candidates));
    }

    [Test]
    public static void AfterEnglishSentenceWithPeriod_DoesNotCalculate()
    {
        // 句点を挟んでも、直前の語が英字なら英文の後ろ。
        var k = new CompositionTests.Keyboard { CalculatorCandidates = true };
        k.Host.PrecedingText = "I have. ";
        k.Type("10/4 ");
        var candidates = k.Host.View?.Candidates.ToList() ?? [];
        Assert.True(!candidates.Any(c => c.Contains('=')), "英文＋句点の後ろでは計算しない: " + string.Join("|", candidates));
    }
}
