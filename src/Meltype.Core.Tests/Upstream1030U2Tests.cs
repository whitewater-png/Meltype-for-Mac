// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>
/// 本家追従 U2 (2026-10-30): 本家の辞書だけの修正を取り込む。
/// 本家 #1294 (#1231 を Wakatte を残したまま直す。#1268 #1278 #1286)、#1280 (mixi)、issue #1318 / #1319 (iZotope、本家は未修正)。
/// </summary>
internal static class Upstream1030U2Tests
{
    private static string Committed(string typed)
    {
        var k = new CompositionTests.Keyboard();
        k.Type(typed + "\n");
        return k.Host.Document;
    }

    /// <summary>W16-1 で外した Wakatte を戻し、wakatte は japanese.txt の語にした: wakatte は日本語、Wakatte.TV は英字のまま。</summary>
    [Test]
    public static void WakatteIsJapaneseAndWakatteTvStaysEnglish()
    {
        Assert.Equal("わかって", Committed("wakatte"), "wakatte");
        Assert.Equal("それわかって", Committed("sorewakatte"), "sorewakatte");
        Assert.Equal("わかってください", Committed("wakattekudasai"), "wakattekudasai");
        Assert.Equal("Wakatte", Committed("Wakatte"), "Wakatte (Shift で打つと英字のまま)");
        Assert.Equal("Wakatte.TV", Committed("Wakatte.TV"), "Wakatte.TV (K108 の損が無くなった)");
        Assert.Equal("Wakatte.TVで", Committed("Wakatte.TVde"), "Wakatte.TVde");
    }

    [Test]
    public static void MixiStaysEnglish()
    {
        Assert.Equal("mixi", Committed("mixi"), "mixi");
        Assert.Equal("mixi2", Committed("mixi2"), "mixi2");
        Assert.Equal("mixiでともだち", Committed("mixidetomodachi"), "mixidetomodachi");
        Assert.Equal("mixi2をはじめた", Committed("mixi2wohajimeta"), "mixi2wohajimeta");
    }

    /// <summary>iZotope (本家 #1319): 先頭の i が い になっていた。大文字を含む綴りで打った形だけが対象。</summary>
    [Test]
    public static void IZotopeStaysEnglish()
    {
        Assert.Equal("iZotope", Committed("iZotope"), "iZotope");
        Assert.Equal("iZotopeで", Committed("iZotopede"), "iZotopede");
    }

    /// <summary>語句辞書 (phrases.txt) と補助辞書 (candidates.txt)。Mac は読みを複数の文節に分けるので、両方を確かめる。</summary>
    [Test]
    public static void PhrasesAndCandidates()
    {
        var dictionary = new UserDictionary(null);
        Assert.True(dictionary.Split("ふぉろばく")?.Any(p => p.Word == "フォロ爆") == true, "ふぉろばく → フォロ爆 (#1268)");
        Assert.True(dictionary.Split("きゅうくらりん")?.Any(p => p.Word == "きゅうくらりん") == true, "きゅうくらりん → きゅうくらりん (#1278)");
        Assert.True(dictionary.Split("ぶいすぽ")?.Any(p => p.Word == "ぶいすぽ") == true, "ぶいすぽ → ぶいすぽ (#1286)");
        Assert.True(CandidateDictionary.Load(null).Lookup("ぶいすぽ").Contains("ぶいすぽっ！"), "ぶいすぽ → ぶいすぽっ！ (#1286)");
    }
}
