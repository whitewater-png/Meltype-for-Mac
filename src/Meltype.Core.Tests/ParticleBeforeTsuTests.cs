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

    [Test]
    public static void KnownLoss_PluralRightAfterParticle()
    {
        // 受け入れた損 (要件 033 の人の判断 1): かな + 助詞のすぐ後ろに空白なしで続けた、助詞の音で始まる 4 文字の英単語の複数形は割れる。
        // 期待値は今の出力 (望ましい出力ではない)。この挙動が変わったら、要件 033 の「得と損」を見直す
        Assert.Equal("きょうはへあts", Committed("kyouhaheats"), "kyouhaheats");
        Assert.Equal("きょうはもあて", Committed("kyouhamoats"), "kyouhamoats");
        Assert.Equal("heats", Committed("heats"), "語の頭の heats は英単語のまま");
        Assert.Equal("今日はheats", Committed("kyouha heats"), "空白の後ろの heats は英単語のまま (空白は変換で消える)");
    }
}
