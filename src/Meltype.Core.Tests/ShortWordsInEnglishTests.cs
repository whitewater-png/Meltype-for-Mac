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
        foreach (var s in new[] { "Where to", "Going to", "Want to", "Used to", "Ready to", "Due to", "Need to", "Have to" })
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
        foreach (var s in new[] { "What to do", "Ready to go", "Where to go", "Have to go", "Going to be", "Want to be", "Need to do", "Able to do", "Try to go", "How to do" })
            Assert.Equal(s, Typed(s + "\n"), s);
    }

    [Test]
    public static void ToGo_ContinuesIntoEnglish()
    {
        Assert.Equal("Have to go home", Typed("Have to go home\n"));
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
        foreach (var s in new[] { "Welcome to Tokyo", "Welcome to Meltype", "Thanks to Google", "Due to Google", "Going to Tokyo" })
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

    // ---- 査読 (W12-2): 一覧の語を前に置いた日本語を壊さない ----

    /// <summary>名詞・代名詞・接続詞・副詞・形容詞。後ろに日本語が続く「と」のつもりのことがある。</summary>
    private static readonly string[] JapaneseSideWords = ["free", "good", "it", "and", "not", "thing", "place", "way", "up", "hard", "about", "get", "Happy", "back", "next", "plan", "talk", "chance", "easy", "hope", "something", "Trip", "things", "nothing"];

    [Test]
    public static void ListedWord_To_Do_Go_Be_FollowedByJapanese_StaysJapanese()
    {
        foreach (var w in JapaneseSideWords)
        {
            Assert.Equal(w + " とどこにいく", Typed(w + " to do ko ni iku\n"), w);
            Assert.Equal(w + " とごはんをたべる", Typed(w + " to go han wo taberu\n"), w);
            Assert.Equal(w + " とべつに", Typed(w + " to be tsu ni\n"), w);
        }
    }

    [Test]
    public static void InfinitiveVerb_To_Do_FollowedByJapanese_ReturnsToJapanese()
    {
        // 「What to do」と同じ形でも、後ろに日本語 (ko ni・助詞) が続いたら、英語に書き換えた to do を日本語に戻す
        Assert.Equal("What とどこにいく", Typed("What to do ko ni iku\n"));
        Assert.Equal("What とごじにあう", Typed("What to go ji ni au\n"));
        Assert.Equal("have とべつに", Typed("have to be tsu ni\n"));
        Assert.Equal("want とどこにいく", Typed("want to do ko ni iku\n"));
        Assert.Equal("What とどもだめ", Typed("What to do mo dame\n"));
        Assert.Equal("What とどがすき", Typed("What to do ga suki\n"));
    }

    [Test]
    public static void PairedRevert_DoesNotEatUserTypedText_AfterEnterBackspaceOrOtherKey()
    {
        // 「What to do」を Enter で確定した後、1.5 秒以内に改行・Backspace・別の文字を挟んでも、
        // 古い記録のまま「to do 」を消してユーザーが打った文字を消してはならない
        // Enter で確定 → 改行 → 日本語
        Assert.Equal("What to doこにいく", Typed("What to do\n\nko ni iku\n"));
        // Enter で確定 → Backspace (確定済みの文字が 1 字消える) → 日本語: 残りの文字を取り消しで消さない
        var afterBackspace = Typed("What to do\n\bko ni iku\n");
        Assert.True(afterBackspace.StartsWith("What to d") && afterBackspace.EndsWith("こにいく"), afterBackspace);
        // 確定 → 別の文字 (Space) を挟む
        var afterSpace = Typed("What to do\n ko ni iku\n");
        Assert.True(afterSpace.StartsWith("What to do") && afterSpace.EndsWith("こにいく"), afterSpace);
    }

    [Test]
    public static void PairedRevert_YoIsNotAParticle_StaysEnglishPair()
    {
        // yo は英語の語でもあるので、取り消しの助詞にしない (半分だけ戻って「What とどyo」にならない)
        foreach (var s in new[] { "what to do yo", "have to go yo", "tried to be yo", "going to do yo" })
        {
            var doc = Typed(s + "\n");
            Assert.True(!(doc.Contains("とど") && doc.Contains("yo")), s + " → " + doc);
        }
    }

    [Test]
    public static void ListedWord_To_AtSentenceEnd_StaysJapanese()
    {
        foreach (var w in JapaneseSideWords)
            Assert.Equal(w + " と", Typed(w + " to\n"), w);
        // 「What と」「try と」も日本語のまま (対照の表と同じ)
        Assert.Equal("What と", Typed("What to\n"));
        Assert.Equal("try と", Typed("try to\n"));
    }

    [Test]
    public static void ListedWord_To_BeforeProperNoun_StaysJapanese()
    {
        foreach (var w in new[] { "Up", "Back", "Next", "Free", "Good", "Thing", "Place", "Way", "Hard", "Close", "Trip", "Train", "Key", "Answer", "Reply", "Talk", "Time", "Plan" })
            Assert.Equal(w + " とZoom", Typed(w + " to Zoom\n"), w);
        Assert.Equal("Up とDown", Typed("Up to Down\n"));
        Assert.Equal("Back とFront", Typed("Back to Front\n"));
        Assert.Equal("Next とPrev", Typed("Next to Prev\n"));
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
