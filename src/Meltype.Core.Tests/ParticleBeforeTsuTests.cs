// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Tests;

/// <summary>
/// 助詞の後ろの あつ・おつ・うつ・いつ・ぶつ (2026-11-25、比較ベンチと実機で見つかった外れ)。
/// とても|も|あつい の mo|a|t を英単語 moat と読み、t と s を分けて とてもatすい にしていた。
/// 英単語の最後の t + s を つ にしない決まり (commitする) の例外に、「助詞の次の 1 音 + つ が日本語の語の頭」を足した。
/// </summary>
internal static class ParticleBeforeTsuTests
{
    private static string Committed(string typed)
    {
        var k = new CompositionTests.Keyboard();
        k.Type(typed + "\n");
        return k.Host.Document;
    }

    [Test]
    public static void Japanese_AfterParticle_ReadsTsu()
    {
        foreach (var (typed, expected) in new[]
        {
            ("totemoatsui", "とてもあつい"), ("kyoumoatsui", "きょうもあつい"), ("kyouhatotemoatsukatta", "きょうはとてもあつかった"),
            ("demoatsui", "でもあつい"), ("kyoumoatsumaru", "きょうもあつまる"), ("soremootsukaresama", "それもおつかれさま"),
            ("kimitoutsukushii", "きみとうつくしい"), ("watashigaitsumodoori", "わたしがいつもどおり"), ("kokodebutsuri", "ここでぶつり"),
            ("kyoumoatsukau", "きょうもあつかう"),
        })
            Assert.Equal(expected, Committed(typed), typed);
    }

    [Test]
    public static void English_TsuRule_Unchanged()
    {
        // 語の頭の英単語、助詞の前に かな が無い形、助詞の次が 2 音以上の英単語 (moment・deceit) は今までどおり
        foreach (var (typed, expected) in new[]
        {
            ("commitsuru", "commitする"), ("heatsuru", "heatする"), ("debutsuru", "debutする"), ("moatsuru", "moatする"),
            ("kyouhacommitsuru", "きょうはcommitする"), ("kyouhamomentsuru", "きょうはmomentする"), ("korewodeceitsuru", "これをdeceitする"),
            ("shutshita", "shutした"), ("reflectsareta", "reflectされた"), ("heats", "heats"),
        })
            Assert.Equal(expected, Committed(typed), typed);
    }
}
