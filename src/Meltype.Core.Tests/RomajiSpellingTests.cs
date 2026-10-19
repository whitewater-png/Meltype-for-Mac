// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Tests;

/// <summary>
/// B2-1 (本家 #848): ローマ字入力表に拗音・外来音の綴りを足す。
/// Mac では、既存の fya・fyu・fyo と同じく入力用の CompositionTable に足し、英語 / 日本語の判定は変えない。
/// 本家が悪化で外した *yi・hw*・hye・sw* は入れない。
/// </summary>
internal static class RomajiSpellingTests
{
    private static string Committed(string keys)
    {
        var k = new CompositionTests.Keyboard();
        k.Type(keys + "\n");
        return k.Host.Document;
    }

    [Test]
    public static void NewSpellings_AreReadAsKana()
    {
        Assert.Equal("きぇ", Committed("kye"));
        Assert.Equal("ぎぇ", Committed("gye"));
        Assert.Equal("ぴぇ", Committed("pye"));
        Assert.Equal("みぇ", Committed("mye"));
        Assert.Equal("くぃ", Committed("kwi"));
        Assert.Equal("くぅ", Committed("kwu"));
        Assert.Equal("くぇ", Committed("kwe"));
        Assert.Equal("ぐぃ", Committed("gwi"));
        Assert.Equal("ぐぅ", Committed("gwu"));
        Assert.Equal("ぐぇ", Committed("gwe"));
        Assert.Equal("ずぁ", Committed("zwa"));
        Assert.Equal("ずぃ", Committed("zwi"));
        Assert.Equal("ずぅ", Committed("zwu"));
        Assert.Equal("ずぇ", Committed("zwe"));
    }

    [Test]
    public static void EnglishWordsWithExcludedSpellings_AreUnchanged()
    {
        // 本家が悪化で外した綴り (*yi・hw*・hye・sw*) を含む英単語は、今までどおり。
        // swan・hyena は同梱の英単語表に無いため、今の Keyboard テストでは sわん・hいぇな になる。
        // ここで固定するのは「新しい綴りを足しても、その今の結果が変わらない」こと。
        Assert.Equal("trying", Committed("trying"));
        Assert.Equal("highway", Committed("highway"));
        Assert.Equal("sわん", Committed("swan"));
        Assert.Equal("hいぇな", Committed("hyena"));
    }
}
