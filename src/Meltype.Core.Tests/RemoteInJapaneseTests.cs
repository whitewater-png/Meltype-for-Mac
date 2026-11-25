// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Tests;

/// <summary>
/// 「それも + て…」のような日本語が、英単語 remote に取られて割れる (soremoteishutsu → そremoteいしゅつ。要件 039)。
/// english-readable.txt の remote (= れもて) は、日本語の語の途中から始まっていても英語になっていた。
/// 日本語の語 + 助詞 + 日本語の語 (そ|れ + も + て|いしゅつ) と読めるときは英語にしない。
/// </summary>
internal static class RemoteInJapaneseTests
{
    private static string Committed(string typed)
    {
        var k = new CompositionTests.Keyboard();
        k.Type(typed + "\n");
        return k.Host.Document;
    }

    [Test]
    public static void Japanese_WordParticleWord_IsNotRemote()
    {
        foreach (var (typed, expected) in new[]
        {
            ("soremoteishutsu", "それもていしゅつ"), ("soremotetsudau", "それもてつだう"), ("soremotekisetsu", "それもてきせつ"),
            ("soremotetsu", "それもてつ"), ("koremotetsudau", "これもてつだう"), ("aremoteishutsu", "あれもていしゅつ"),
            ("doremotetsudau", "どれもてつだう"),
        })
            Assert.Equal(expected, Committed(typed), typed);
    }

    [Test]
    public static void English_Remote_Unchanged()
    {
        // 語の頭・空白の後ろ・日本語 + remote のあとに 助詞/する/英単語が続く形は今までどおり英単語
        foreach (var (typed, expected) in new[]
        {
            ("remote", "remote"), ("remotes", "remotes"), ("remoteishutsu", "remoteいしゅつ"), ("remotework", "remotework"),
            ("kyouremoteishutsu", "きょうremoteいしゅつ"), ("sore remotetsudau", "それremoteつだう"), ("sorenoremotetsudau", "それのremoteつだう"),
            ("sorekaremote", "それかremote"), ("kyouharemotesuru", "きょうはremoteする"), ("kyouharemoteworkdesu", "きょうはremoteworkです"),
            ("soremote", "そremote"),
        })
            Assert.Equal(expected, Committed(typed), typed);
    }
}
