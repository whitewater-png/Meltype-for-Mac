// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>P53 (本家 #795): 子音の略入力 (ast → 明日)。設定は既定 OFF。</summary>
internal static class ConsonantAbbreviationTests
{
    [Test]
    public static void Dictionary_HasAst()
    {
        var dictionary = CandidateDictionary.Load(null);
        Assert.Equal("明日", string.Join("|", dictionary.Lookup("ast")));
    }

    [Test]
    public static void SettingOn_OffersAbbreviation()
    {
        var k = new CompositionTests.Keyboard { ConsonantAbbreviations = true };
        k.Type("ast ");
        var candidates = k.Host.View?.Candidates.ToList() ?? [];
        Assert.Equal("明日", candidates[0], "略入力の候補が先頭: " + string.Join("|", candidates));
        Assert.True(candidates.Contains("ast"), "打った英字も候補に残る: " + string.Join("|", candidates));
    }

    [Test]
    public static void SettingOff_DoesNotOffer()
    {
        var k = new CompositionTests.Keyboard();
        k.Type("ast ");
        var candidates = k.Host.View?.Candidates.ToList() ?? [];
        Assert.True(!candidates.Contains("明日"), "設定 OFF: " + string.Join("|", candidates));
    }

    [Test]
    public static void KanaInput_DoesNotOffer()
    {
        var k = new CompositionTests.Keyboard { ConsonantAbbreviations = true, Kana = true };
        k.Type("ast ");
        var candidates = k.Host.View?.Candidates.ToList() ?? [];
        Assert.True(!candidates.Contains("明日"), "かな入力: " + string.Join("|", candidates));
    }

    [Test]
    public static void UnknownAbbreviation_DoesNotOffer()
    {
        var k = new CompositionTests.Keyboard { ConsonantAbbreviations = true };
        k.Type("xyz ");
        var candidates = k.Host.View?.Candidates.ToList() ?? [];
        Assert.True(!candidates.Contains("明日"), "xyz: " + string.Join("|", candidates));
    }

    [Test]
    public static void Choice_IsNotLearned()
    {
        var history = new ConversionHistory(null);
        var k = new CompositionTests.Keyboard(history: history) { ConsonantAbbreviations = true };
        k.Type("ast ");
        for (var i = 0; i < 10 && k.Showing != "明日"; i++) k.Type(" ");
        Assert.Equal("明日", k.Showing, "明日 まで進む: " + string.Join("|", k.Host.View?.Candidates ?? []));
        k.Type("\n");
        Assert.True(history.Get("ast") is null, "英語扱いなので学習しない: " + history.Get("ast"));
    }
}
