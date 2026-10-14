// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Input;

namespace Meltype.Tests;

/// <summary>
/// Shift+↓ で先頭の 1 音だけ確定する (ATOK の 1 音確定)。入力中だけ。変換中は今までどおり (次の候補)。
/// </summary>
internal static class CommitFirstSoundTests
{
    private static void ShiftDown(CompositionTests.Keyboard k)
    {
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Down);
        k.Key(VirtualKeys.LShift, up: true);
    }

    [Test]
    public static void Composing_CommitsTheFirstSound_AndKeepsTheRest()
    {
        var k = new CompositionTests.Keyboard();
        k.Type("kyouha");
        ShiftDown(k);
        Assert.Equal("きょ", k.Host.Document, "先頭の 1 音 (きょ) だけ確定する");
        Assert.Equal("うは", k.Host.View?.Text, "残りは入力のまま");
        Assert.True(k.Host.View is { Converting: false }, "変換には入らない");
        Assert.True(!k.Host.Events.Contains("down:28"), "↓ はアプリに渡さない: " + string.Join("|", k.Host.Events));

        ShiftDown(k);
        ShiftDown(k);
        Assert.Equal("きょうは", k.Host.Document, "最後の 1 音まで確定できる");
        Assert.True(k.Host.View is null, "全部確定したら変換ボックスを閉じる");
    }

    [Test]
    public static void RestCanStillBeTypedAndConverted()
    {
        var k = new CompositionTests.Keyboard();
        k.Type("kyouha");
        ShiftDown(k);
        k.Type("re");
        Assert.Equal("うはれ", k.Host.View?.Text, "残りに続けて打てる");
        k.Press(VirtualKeys.Return);
        Assert.Equal("きょうはれ", k.Host.Document, "Enter で残りを確定");
        Assert.True(k.Host.View is null, "閉じる");
    }

    [Test]
    public static void PendingConsonantOnly_CommitsIt()
    {
        // 語末の n は、最後に Shift+↓ を押せば ん で確定する。
        var k = new CompositionTests.Keyboard();
        k.Type("kan");
        ShiftDown(k);
        Assert.Equal("か", k.Host.Document, "か");
        ShiftDown(k);
        Assert.Equal("かん", k.Host.Document, "残った n は ん で確定");
        Assert.True(k.Host.View is null, "閉じる");
    }

    [Test]
    public static void Katakana_CommitsInTheShownForm()
    {
        var k = new CompositionTests.Keyboard();
        k.Type("kyouha");
        k.Press(VirtualKeys.F7);
        ShiftDown(k);
        Assert.Equal("キョ", k.Host.Document, "F7 のカタカナのまま確定");
        Assert.Equal("ウハ", k.Host.View?.Text, "残りもカタカナのまま");

        var half = new CompositionTests.Keyboard();
        half.Type("kyouha");
        half.Press(VirtualKeys.F8);
        ShiftDown(half);
        Assert.Equal("ｷｮ", half.Host.Document, "F8 の半角カナのまま確定");
    }

    [Test]
    public static void English_CommitsOneLetter()
    {
        // 英字として見せている部分は 1 文字ずつ (google の go を 1 音として丸ごと確定しない)。
        var k = new CompositionTests.Keyboard();
        k.Type("kyouha");
        k.Press(VirtualKeys.F10);
        ShiftDown(k);
        Assert.Equal("k", k.Host.Document, "F10 の英字は 1 文字");
        Assert.Equal("youha", k.Host.View?.Text, "残りも英字のまま");

        var full = new CompositionTests.Keyboard();
        full.Type("kyouha");
        full.Press(VirtualKeys.F9);
        ShiftDown(full);
        Assert.Equal("ｋ", full.Host.Document, "F9 の全角英字は 1 文字");
        Assert.Equal("ｙｏｕｈａ", full.Host.View?.Text, "残りも全角英字のまま");
        for (var i = 0; i < 5; i++) ShiftDown(full);
        Assert.Equal("ｋｙｏｕｈａ", full.Host.Document, "最後の 1 文字も全角のまま");
        Assert.True(full.Host.View is null, "閉じる");

        var auto = new CompositionTests.Keyboard();
        auto.Type("google");
        Assert.Equal("google", auto.Host.View?.Text, "前提: 英語として見せている");
        ShiftDown(auto);
        Assert.Equal("g", auto.Host.Document, "自動で英語と判定した語も 1 文字");
        Assert.True(auto.Host.View is { Text.Length: > 0 }, "残りは入力のまま: " + auto.Host.View?.Text);
    }

    [Test]
    public static void Converting_KeepsTheCurrentBehavior()
    {
        var k = new CompositionTests.Keyboard();
        k.Type("kyouha ");
        Assert.True(k.Host.View is { Converting: true }, "前提: Space で変換中");
        ShiftDown(k);
        Assert.Equal("", k.Host.Document, "変換中は 1 音確定しない");
        Assert.True(k.Host.View is { Converting: true }, "変換中のまま");
    }

    [Test]
    public static void PlainDown_IsUnchanged()
    {
        // Shift なしの ↓ は、今までどおり文節の選択に入る (確定しない)。
        var k = new CompositionTests.Keyboard();
        k.Type("kyouha");
        k.Press(VirtualKeys.Down);
        Assert.Equal("", k.Host.Document, "確定しない");
        Assert.True(k.Host.View is { Converting: true }, "文節の選択に入る");
    }

    [Test]
    public static void KanaInput_CommitsOneKana()
    {
        var k = new CompositionTests.Keyboard { Kana = true };
        k.TypeKanaKeys("t@u");
        Assert.Equal("がな", k.Showing, "前提: T + ゛ = が, U = な");
        ShiftDown(k);
        Assert.Equal("が", k.Host.Document, "濁点を付けたかなも 1 音");
        Assert.Equal("な", k.Host.View?.Text, "残りは入力のまま");
    }

    [Test]
    public static void LiveConversion_CommitsTheKana()
    {
        // ライブ変換で漢字に見えていても、確定するのは読みの 1 音。残りは読みからライブ変換し直す。
        var k = new CompositionTests.Keyboard(live: true);
        k.Type("nihongo");
        Assert.Equal("日本語", k.Host.View?.Text, "前提: ライブ変換で漢字");
        ShiftDown(k);
        Assert.Equal("に", k.Host.Document, "読みの先頭の 1 音");
        Assert.Equal("ほんご", k.Host.View?.Text, "残りは入力のまま");
    }

    [Test]
    public static void FixedClauses_KeepTheCurrentBehavior()
    {
        // 変換後も続けて入力 (固定した文節がある) ときは、今までどおり (確定しない)。
        var k = new CompositionTests.Keyboard { Continue = true };
        k.Type("kyou ");
        k.Type("ha");
        Assert.Equal("", k.Host.Document, "前提: まだ何も確定していない");
        ShiftDown(k);
        Assert.Equal("", k.Host.Document, "固定した文節があるときは 1 音確定しない");
        Assert.True(k.Host.View is not null, "入力は続いている");
    }

    /// <summary>Shift+↓ を、変換ボックスが閉じるまで押し続ける (ATOK で 1 音ずつ確定していく使い方)。毎回、見えていた文字の頭が確定されたかも見る。</summary>
    private static void ShiftDownUntilClosed(CompositionTests.Keyboard k)
    {
        for (var i = 0; i < 40 && k.Host.View is { } view; i++)
        {
            var document = k.Host.Document;
            ShiftDown(k);
            var committed = k.Host.Document[document.Length..];
            var rest = k.Host.View?.Text ?? "";
            Assert.Equal(view.Text, committed + rest, $"見えていた「{view.Text}」= 確定「{committed}」+ 残り「{rest}」");
        }
        Assert.True(k.Host.View is null, "閉じる");
    }

    [Test]
    public static void English_RestStaysAsShown()
    {
        // 査読 (独立): 英語の頭の 1 文字を確定したあと、残り (ello) を日本語として読み直して えっぉ になっていた。
        foreach (var (typed, shown) in new[]
                 {
                     ("Hello!", "Hello!"), ("GitHub.", null), ("googlede", "googleで"), ("iPhone", null), ("e-mail", null), ("J-core", null),
                     ("tetr.io", null), ("Meltype.amr", null), ("kyouhaGoogle!", null), ("(Google)", null), ("kyouhann", null), ("ok?", null),
                     ("\"Go\"", "\"Go\""), ("10mmde", null), ("\"Go Through!\"", null),
                     ("Japan", "Japan"), ("London", null), ("Kotlin", null), ("Python", null), ("Hellow", null), ("kyouhaJapan", null),
                 })
        {
            var k = new CompositionTests.Keyboard();
            k.Type(typed);
            var view = k.Host.View?.Text ?? "";
            if (shown is not null) Assert.Equal(shown, view, $"前提: {typed} の見え方");
            // 空白を含むもの ("Go Through!") は、空白までが先に確定している。
            var document = k.Host.Document;
            ShiftDownUntilClosed(k);
            Assert.Equal(document + view, k.Host.Document, $"{typed}: 1 音ずつ確定しても、見えていたとおりの文字になる");
        }
    }

    [Test]
    public static void English_TypingAfterTheRest_IsJudgedAsBefore()
    {
        var k = new CompositionTests.Keyboard();
        k.Type("google");
        ShiftDown(k);
        Assert.Equal("oogle", k.Host.View?.Text, "残りは英字のまま");
        k.Type("de");
        Assert.Equal("oogleで", k.Host.View?.Text, "後ろに打った de は日本語");
        k.Press(VirtualKeys.Back);
        k.Press(VirtualKeys.Back);
        k.Press(VirtualKeys.Back);
        Assert.Equal("oog", k.Host.View?.Text, "BackSpace で消しても、残りは英字のまま");
    }

    [Test]
    public static void FullWidthOpener_CommitsAsShown()
    {
        // 査読 (独立): 全角の設定でも、英語の前で半角に見せている ( を、全角 （ で確定していた。
        var k = new CompositionTests.Keyboard { Parentheses = Meltype.Config.ParenthesisWidth.FullWidth };
        k.Type("(Google)");
        var view = k.Host.View?.Text ?? "";
        ShiftDown(k);
        Assert.Equal(view[..1], k.Host.Document, "見えているとおりの幅で確定: " + view);
    }

    [Test]
    public static void Reconversion_EscAfterOneSound_DoesNotDuplicate()
    {
        // 査読 (独立): 再変換 → Esc → Shift+↓ → Esc で、元の文字を丸ごと書き戻して二重になっていた。
        var k = new CompositionTests.Keyboard();
        Assert.True(k.Controller.ReconvertKana("きょうは", "今日は"), "前提: 再変換が始まる");
        k.Press(VirtualKeys.Escape);
        Assert.Equal("きょうは", k.Host.View?.Text, "前提: Esc で読みに戻る");
        ShiftDown(k);
        Assert.Equal("きょ", k.Host.Document, "再変換の読みも、小書きのかなまでで 1 音 (きょ)");
        Assert.Equal("うは", k.Host.View?.Text, "残り");
        k.Press(VirtualKeys.Escape);
        Assert.Equal("きょ", k.Host.Document, "Esc で元の文字を書き戻さない (二重にならない)");
    }

    [Test]
    public static void EnterOrSpace_AfterOneSound_KeepsTheRestAsShown()
    {
        // 再査読: 1 回だけ Shift+↓ を押したあとの Enter で、語末の n が ん に (Japan → Japaん)、
        // 打ち間違いの直しが固定した英字を書き換えて (googlede → gooごえで、Meltype.amr → Meltype.aめ) いた。
        foreach (var typed in new[] { "Japan", "London", "Kotlin", "Amazon", "googlede", "Meltype.amr", "kyouhaJapan", "Hello!" })
        {
            var k = new CompositionTests.Keyboard();
            k.Type(typed);
            var view = k.Host.View?.Text ?? "";
            ShiftDown(k);
            var rest = k.Host.View?.Text ?? "";
            Assert.Equal(view, k.Host.Document + rest, $"{typed}: 1 回目");
            k.Press(VirtualKeys.Return);
            Assert.Equal(view, k.Host.Document, $"{typed}: Enter で見えていたとおりに確定");
        }

        // Space: 英語で終わっているなら、今までどおり確定して空白 (変換に入らない)。
        foreach (var (typed, expected) in new[] { ("Hellow", "Hellow "), ("e-mail", "e-mail "), ("GoogleMeet", "GoogleMeet "), ("Japan", "Japan ") })
        {
            var k = new CompositionTests.Keyboard();
            k.Type(typed);
            ShiftDown(k);
            k.Press(VirtualKeys.Space);
            Assert.Equal(expected, k.Host.Document, $"{typed}: Space で確定して空白");
            Assert.True(k.Host.View is null, $"{typed}: 変換に入らない");
        }
    }
}
