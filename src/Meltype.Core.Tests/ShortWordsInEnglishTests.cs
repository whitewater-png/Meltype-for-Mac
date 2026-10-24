// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Tests;

/// <summary>
/// 本家 #1029 の (3): 英文の中の to / do / go を、前の英単語を根拠に英語のまま残す (W12-1)。
/// 英語側 (直る型) と、同じ形の日本語 (Slack と…、Zoom とどこに…) が壊れないことの対照を、同じ場所に置く。
/// 正解表: eval/cases/short-words-in-english.tsv。
/// </summary>
internal static class ShortWordsInEnglishTests
{
    /// <summary>実機 (と --repro) と同じく、同梱の英単語の一覧をスペルチェッカーとして使って打つ。</summary>
    private static string Typed(string keys)
    {
        var previous = CompositionTests.Detector.SpellChecker;
        CompositionTests.Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try
        {
            var k = new CompositionTests.Keyboard();
            k.Type(keys);
            return k.Host.Document.TrimEnd();
        }
        finally { CompositionTests.Detector.SpellChecker = previous; }
    }

    // ---- (a) 文の終わりの to ----

    [Test]
    public static void SentenceEnd_To_AfterFunctionWord_StaysEnglish()
    {
        foreach (var s in new[] { "Where to", "Going to", "Want to", "Used to", "Back to", "Next to", "Ready to", "Nothing to" })
            Assert.Equal(s, Typed(s + "\n"), s);
    }

    [Test]
    public static void SentenceEnd_To_AfterNoun_StaysJapanese()
    {
        // 名詞 (製品名・サービス名) のあとの to は「と」のつもり
        Assert.Equal("Slack と", Typed("Slack to\n"));
        Assert.Equal("Chrome と", Typed("Chrome to\n"));
        Assert.Equal("Meeting と", Typed("Meeting to\n"));
        Assert.Equal("Gmail と", Typed("Gmail to\n"));
    }

    // ---- (b) to + do / go / be ----

    [Test]
    public static void ToDo_ToGo_ToBe_AfterFunctionWord_StaysEnglish()
    {
        foreach (var s in new[] { "What to do", "Nothing to do", "Time to go", "Ready to go", "Where to go", "Have to go", "Going to be", "Want to be" })
            Assert.Equal(s, Typed(s + "\n"), s);
    }

    [Test]
    public static void ToGo_ContinuesIntoEnglish()
    {
        Assert.Equal("Time to go home", Typed("Time to go home\n"));
        Assert.Equal("Ready to go now", Typed("Ready to go now\n"));
    }

    [Test]
    public static void ToDo_AfterNoun_FollowedByJapanese_StaysJapanese()
    {
        Assert.Equal("Slack とどこにいく", Typed("Slack to do ko ni iku\n"));
        Assert.Equal("Zoom とごじにあう", Typed("Zoom to go ji ni au\n"));
        Assert.Equal("Notion とべつに", Typed("Notion to be tsu ni\n"));
    }

    // ---- (c) 大文字始まりの固有名詞の前の to ----

    [Test]
    public static void To_BeforeProperNoun_AfterTravelOrWelcomeWord_StaysEnglish()
    {
        foreach (var s in new[] { "Welcome to Tokyo", "Welcome to Meltype", "Trip to London", "Train to Paris", "Flight to Osaka", "Back to Tokyo", "Go to Kyoto", "Thanks to Google", "Next to Google" })
            Assert.Equal(s, Typed(s + "\n"), s);
    }

    [Test]
    public static void To_BeforeProperNoun_AfterNoun_StaysJapanese()
    {
        // 「Google と Apple」を守る条件はそのまま
        Assert.Equal("Google とApple", Typed("Google to Apple\n"));
        Assert.Equal("Slack とZoom", Typed("Slack to Zoom\n"));
        Assert.Equal("Windows とMac", Typed("Windows to Mac\n"));
        Assert.Equal("Docker とKubernetes", Typed("Docker to Kubernetes\n"));
    }

    // ---- 日本語の文は変わらない ----

    [Test]
    public static void JapaneseSentences_WithToDoGo_AreUnchanged()
    {
        Assert.Equal("ともだちとはなす", Typed("tomodachi to hanasu\n"));
        Assert.Equal("これとあれ", Typed("kore to are\n"));
        Assert.Equal("どこにいく", Typed("doko ni iku\n"));
        Assert.Equal("ごはんをたべる", Typed("gohan wo taberu\n"));
        Assert.Equal("Slack ではなす", Typed("Slack de hanasu\n"));
    }
}
