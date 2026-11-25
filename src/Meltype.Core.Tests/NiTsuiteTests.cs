// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Tests;

/// <summary>
/// 母音 1 つ + に + つ… (2026-11-25、033 の掃引で見つかった外れ)。
/// にほんい|について・せつめい|について の い|に|t を英単語 init と読み、t と s を分けて にほんinitsuite になっていた。
/// 英単語の最後の t + s を つ にしない決まり (commitする) の例外に、「い + に + t で、かな (を以外) の後ろ、に + つ が日本語の語の頭 (につ…)」を足した。
/// </summary>
internal static class NiTsuiteTests
{
    private static string Committed(string typed)
    {
        var k = new CompositionTests.Keyboard();
        k.Type(typed + "\n");
        return k.Host.Document;
    }

    [Test]
    public static void Japanese_VowelNiTsu_ReadsKana()
    {
        foreach (var (typed, expected) in new[]
        {
            ("nihonninitsuite", "にほんいについて"), ("nihonninitsukimashite", "にほんいにつきまして"), ("nihonninitsumeru", "にほんいにつめる"),
            ("setsumeinitsuite", "せつめいについて"), ("mondainitsuite", "もんだいについて"), ("mirainitsuite", "みらいについて"),
            ("omoinitsuite", "おもいについて"), ("kainitsuite", "かいについて"), ("zeinitsuite", "ぜいについて"),
            ("kyounitsuite", "きょうについて"), ("sorenitsuite", "それについて"), ("nihonnnitsuite", "にほんについて"),
        })
            Assert.Equal(expected, Committed(typed), typed);
    }

    [Test]
    public static void English_Init_Unchanged()
    {
        // 語の頭の init・unit、空白の後ろ、tsu の続きが無い形 (initshita)、同じ形の別の語は今までどおり
        foreach (var (typed, expected) in new[]
        {
            ("init", "init"), ("initsuite", "initsuite"), ("unitsuite", "unitsuite"), ("npminit", "npminit"),
            ("kyouhainitshita", "きょうはinitした"), ("kyouhaunitsuru", "きょうはunitする"), ("kyouhaunits", "きょうはunits"), ("kyouhaunitshita", "きょうはunitした"),
            ("korewoinitsuru", "これをinitする"), ("korewounitsuru", "これをunitする"), ("sorewounitsuika", "それをunitすいか"),
            ("commitsuru", "commitする"), ("kyouhacommitsuru", "きょうはcommitする"), ("shutshita", "shutした"), ("reflectsareta", "reflectされた"),
        })
            Assert.Equal(expected, Committed(typed), typed);
    }

    [Test]
    public static void KnownLoss_InitAfterKana()
    {
        // 受け入れた損 (要件 040): かなで終わる語 (助詞に限らない。reinit の れ も) に空白なしで続けた init + tsu… (initする・initsuru) は、に + つ と読む。
        // 期待値は今の出力で、望ましい出力ではない (変更前は れinitする・にほんいinitする など)。
        // 期待値は今の出力 (望ましい出力ではない)。この挙動が変わったら、要件 040 の「得と損」を見直す
        foreach (var (typed, expected) in new[]
        {
            ("kyouhainitsuru", "きょうはいにつる"), ("kokodeinitsuru", "ここでいにつる"), ("kyouhainits", "きょうはいにて"),
            ("reinitsuru", "れいにつる"), ("nihonninitsuru", "にほんいにつる"),
        })
            Assert.Equal(expected, Committed(typed), typed);
    }
}
