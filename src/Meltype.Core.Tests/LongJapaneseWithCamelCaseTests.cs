// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Detection;

namespace Meltype.Tests;

/// <summary>
/// B2-3 (本家 #829): iPhone・YouTube を 2 つ含む長い日本語文。
/// Mac の Core はすでに期待どおりだったため、コードは変えずに回帰テストで固定する (2026-10-19)。
/// </summary>
internal static class LongJapaneseWithCamelCaseTests
{
    private static string Committed(string keys)
    {
        var k = new CompositionTests.Keyboard();
        k.Type(keys + "\n");
        return k.Host.Document;
    }

    private static string Committed(IWordChecker checker, string keys)
    {
        var previous = CompositionTests.Detector.SpellChecker;
        CompositionTests.Detector.SpellChecker = checker;
        try { return Committed(keys); }
        finally { CompositionTests.Detector.SpellChecker = previous; }
    }

    /// <summary>実機の NSSpellChecker が知っている長い英単語 (university・international) を足した模擬。</summary>
    private sealed class NsLikeChecker : IWordChecker
    {
        public bool IsAvailable => true;
        public bool IsWord(string lower) =>
            lower is "university" or "international" || BuiltInWordChecker.Shared.IsWord(lower);
    }

    [Test]
    public static void LongJapaneseWithTwoCamelCaseNames()
    {
        Assert.Equal("これをiPhoneでさつえいし、YouTubeにあっぷろーどしよう",
            Committed("korewoiPhonedesatueisi,YouTubeniappuro-dosiyou"));
    }

    [Test]
    public static void LongEnglishWords_AreUnchanged()
    {
        // 7 文字以上の実在の英単語の頭は、日本語に倒さない (実機の NSSpellChecker に近い判定器で)。
        var checker = new NsLikeChecker();
        Assert.Equal("universityGitHub", Committed(checker, "universityGitHub"));
        Assert.Equal("internationaliPhone", Committed(checker, "internationaliPhone"));
    }
}
