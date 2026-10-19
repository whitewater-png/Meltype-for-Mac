// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>
/// P55 (本家 #801): 数字・記号 1 文字の選び直しも学習する。
/// 1 文字のかな (き → 記) は、これまでどおり学習しない (き + ごうとう への巻き込みを防ぐ)。
/// </summary>
internal static class NumberSymbolLearningTests
{
    /// <summary>keys を打って pick が選ばれるまで Space を押し、Enter で確定する。候補の一覧をメッセージに使う。</summary>
    private static string Pick(CompositionTests.Keyboard k, string keys, string pick)
    {
        k.Type(keys);
        for (var i = 0; i < 20 && k.Showing != pick; i++) k.Type(" ");
        Assert.Equal(pick, k.Showing, keys + " の候補: " + string.Join("|", k.Host.View?.Candidates ?? []));
        k.Type("\n");
        return k.Host.Document;
    }

    [Test]
    public static void DigitOneChar_IsLearned()
    {
        var history = new ConversionHistory(null);
        var k = new CompositionTests.Keyboard(history: history, moreCandidates: reading => reading == "1" ? ["①"] : []);
        Assert.Equal("①", Pick(k, "1 ", "①"));
        Assert.Equal("①", history.Get("1"), "数字 1 文字の選び直しを覚える");
    }

    [Test]
    public static void SymbolOneChar_IsLearned()
    {
        var history = new ConversionHistory(null);
        var k = new CompositionTests.Keyboard(history: history);
        Assert.Equal("（", Pick(k, "( ", "（"));
        Assert.Equal("（", history.Get("("), "記号 1 文字の選び直しを覚える");
    }

    [Test]
    public static void KanaOneChar_IsStillNotLearned()
    {
        var history = new ConversionHistory(null);
        var k = new CompositionTests.Keyboard(history: history, moreCandidates: reading => reading == "き" ? ["記"] : []);
        Assert.Equal("記", Pick(k, "ki ", "記"));
        Assert.True(history.Get("き") is null, "1 文字のかなは学習しない: " + history.Get("き"));
    }

    [Test]
    public static void MultiChar_IsStillLearned()
    {
        var history = new ConversionHistory(null);
        var k = new CompositionTests.Keyboard(history: history, moreCandidates: reading => reading == "きごう" ? ["記号"] : []);
        Assert.Equal("記号", Pick(k, "kigou ", "記号"));
        Assert.Equal("記号", history.Get("きごう"), "2 文字以上は今までどおり学習する");
    }
}
