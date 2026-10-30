// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Tests;

/// <summary>
/// 本家追従 W18 (2026-10-30): 本家 PR #1185 の試作 (docs/UPSTREAM_REVIEW_2026-10-30.md) で「本家のほうが良い」だった入力のうち、
/// 辞書を足すだけで直り、日本語の入力が悪化しないと測れた語。期待は本家 main の出力。
/// </summary>
internal static class Upstream1030W18Tests
{
    private static string Committed(string typed)
    {
        var k = new CompositionTests.Keyboard();
        k.Type(typed + "\n");
        return k.Host.Document;
    }

    [Test]
    public static void Env_Into_Instal()
    {
        Assert.Equal("env", Committed("env"), "env");
        Assert.Equal("dive into summer", Committed("dive into summer"), "dive into summer");
        Assert.Equal("instal", Committed("instal"), "instal");
    }

    [Test]
    public static void Ppt_Ffmpeg()
    {
        Assert.Equal("excelとppt", Committed("exceltoppt"), "exceltoppt");
        Assert.Equal("pythonとffmpeg", Committed("pythontoffmpeg"), "pythontoffmpeg");
    }

    [Test]
    public static void HoloX_Dice_Pedia()
    {
        Assert.Equal("holoX", Committed("holoX"), "holoX");
        Assert.Equal("dice", Committed("dice"), "dice");
        foreach (var w in new[] { "pedia", "protopedia", "protopedias", "Protopedia", "Protopedias", "wikipedias", "worldpedia", "conservapedia" })
            Assert.Equal(w, Committed(w), w);
    }

    /// <summary>変わってはいけないもの: 語と同じ綴りを含む日本語 (起点の出力と同じ)。</summary>
    [Test]
    public static void JapaneseNeighborsAreUnchanged()
    {
        var expected = new Dictionary<string, string>
        {
            ["appurodo"] = "あっぷろど", ["kappa"] = "かっぱ", ["ossan"] = "おっさん", ["ossu"] = "おっす",
            ["kippu"] = "きっぷ", ["happyou"] = "はっぴょう", ["enbu"] = "えんぶ", ["intoro"] = "いんとろ",
            ["shatsu"] = "しゃつ", ["kyoushatsuwokiru"] = "きょうしゃつをきる", ["tsumetai"] = "つめたい",
            ["metawo"] = "めたを", ["kyoudaisuki"] = "きょうだいすき",
        };
        foreach (var (typed, want) in expected)
            Assert.Equal(want, Committed(typed), typed);
    }

    /// <summary>
    /// 変わってはいけないもの: 1 文字落とした打ち間違いの補正 (査読 W18 R1)。shat・oss・app を辞書に入れると、
    /// shats → shatsが・ossna → ossな・appwo → appを のように補正が効かなくなった。入れた語の周りでも補正が保たれることを守る。
    /// </summary>
    [Test]
    public static void TypoCorrectionsAreUnchanged()
    {
        var expected = new Dictionary<string, string>
        {
            ["shats"] = "しゃつ", ["shatswokiru"] = "しゃつをきる", ["shatdesu"] = "しゃつです",
            ["osshru"] = "おっしゃる", ["ossna"] = "おっさん", ["appwo"] = "あっぷを", ["appdesu"] = "あっぷです",
        };
        foreach (var (typed, want) in expected)
            Assert.Equal(want, Committed(typed), typed);
    }
}
