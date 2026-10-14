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
}
