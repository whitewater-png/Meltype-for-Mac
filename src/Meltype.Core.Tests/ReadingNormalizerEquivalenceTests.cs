// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>
/// R3: ReadingNormalizer の各関数が、集める前の実装 (LegacyReadingNormalization) と U+0000〜U+FFFF の全文字・代表的な読みで同じ結果を返すこと。
/// 1 文字だけの文字列に加えて、前後に変換の掛かる文字 (Ａ・カ) を置いた文字列でも比べる (専門用語集の深さは、変える文字が無いと素通りする近道があるため、
/// 近道を通らない側でも全文字を確かめる)。
/// </summary>
internal static class ReadingNormalizerEquivalenceTests
{
    private static IEnumerable<char> AllChars()
    {
        for (var code = 0; code <= 0xFFFF; code++) yield return (char)code;
    }

    private static IEnumerable<string> Inputs() =>
        AllChars().SelectMany(c => new[] { c.ToString(), "Ａ" + c + "カ", " " + c + "あ " }).Concat(LegacyReadingNormalization.Samples);

    [Test]
    public static void IsReadingChar_MatchesBothLegacyImplementations_ForAllChars()
    {
        // 集める前は UserDictionary と UserDictionaryFile に別々の判定があった。書き方は違う (片方は 'ゔ' を別に挙げる) が、範囲は同じ。
        foreach (var c in AllChars())
        {
            var legacyUser = LegacyReadingNormalization.UserDictionaryIsReadingChar(c);
            var legacyFile = LegacyReadingNormalization.UserDictionaryFileIsReadingChar(c);
            Assert.Equal(legacyUser, legacyFile, $"集める前の 2 つの判定が同じ U+{(int)c:X4}");
            Assert.Equal(legacyUser, ReadingNormalizer.IsReadingChar(c), $"IsReadingChar U+{(int)c:X4}");
            Assert.Equal(LegacyReadingNormalization.UserDictionaryIsHiragana(c), ReadingNormalizer.IsHiragana(c), $"IsHiragana U+{(int)c:X4}");
        }
    }

    [Test]
    public static void ForUserDictionary_MatchesLegacy()
    {
        foreach (var text in Inputs())
        {
            Assert.Equal(LegacyReadingNormalization.UserDictionaryNormalizeReading(text), ReadingNormalizer.ForUserDictionary(text), $"ForUserDictionary U+{Codes(text)}");
        }
    }

    [Test]
    public static void KatakanaToHiragana_MatchesLegacy()
    {
        foreach (var text in Inputs())
        {
            Assert.Equal(LegacyReadingNormalization.UserDictionaryFileToHiragana(text), ReadingNormalizer.KatakanaToHiragana(text), $"KatakanaToHiragana U+{Codes(text)}");
        }
    }

    [Test]
    public static void ForTermDictionary_MatchesLegacy()
    {
        foreach (var text in Inputs())
        {
            Assert.Equal(LegacyReadingNormalization.TermDictionaryNormalize(text), ReadingNormalizer.ForTermDictionary(text), $"ForTermDictionary U+{Codes(text)}");
        }
    }

    [Test]
    public static void ForUserTermDomain_MatchesLegacy()
    {
        foreach (var text in Inputs())
        {
            Assert.Equal(LegacyReadingNormalization.TermDomainsUserReading(text), ReadingNormalizer.ForUserTermDomain(text), $"ForUserTermDomain U+{Codes(text)}");
        }
    }

    [Test]
    public static void Depths_DifferAsDocumented()
    {
        // 深さの違いは今のまま残している (変えると保存されている読みとの照合が変わる)。違いがあることを例で固定する。
        Assert.Equal("ABCけい", ReadingNormalizer.ForUserDictionary(" ABCケイ "), "ユーザー辞書は英大文字を残す");
        Assert.Equal(" abcけい ", ReadingNormalizer.ForTermDictionary(" ＡＢＣケイ "), "専門用語集は全角→半角・小文字。空白は取らない");
        Assert.Equal("abcけい", ReadingNormalizer.ForUserTermDomain(" ＡＢＣケイ "), "自作の専門用語集は両方");
        Assert.Equal("ｷｺﾞｳ", ReadingNormalizer.ForUserTermDomain("ｷｺﾞｳ"), "半角カナはどの深さでもそのまま");
        Assert.Equal("ゔぁゕゖ", ReadingNormalizer.KatakanaToHiragana("ヴァヵヶ"), "ヴ・ヵ・ヶ もひらがなに");
    }

    private static string Codes(string text) => string.Join(" ", text.Select(c => ((int)c).ToString("X4")));
}
