// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>
/// 設定「日本語入力中は一覧の語をかなで変換する」(本家 #1023)。
/// dictionaries/japanese-mode.txt の語 (exe) を、日本語の文脈では かな として扱い、
/// 英語の文の中と拡張子 (.exe) は今までどおり英字のままにする。
/// 英数 (直接入力) は変換ボックスを通らないので、この設定に関係なく英字で入る (CompositionTests の Direct で確認)。
/// </summary>
internal static class KanaInJapaneseModeTests
{
    /// <summary>共有の判定器の設定を切り替えて、元に戻す。</summary>
    private static void WithMode(bool on, Action body)
    {
        var previous = CompositionTests.Detector.KanaInJapaneseMode;
        CompositionTests.Detector.KanaInJapaneseMode = () => on;
        try { body(); }
        finally { CompositionTests.Detector.KanaInJapaneseMode = previous; }
    }

    [Test]
    public static void DefaultIsOff()
    {
        Assert.True(!new Settings().KanaInJapaneseMode, "設定の既定は OFF (今までの動きを変えない)");
    }

    [Test]
    public static void Off_ExeStaysEnglish()
    {
        var k = new CompositionTests.Keyboard();
        k.Type("exe");
        Assert.Equal("exe", k.Showing, "OFF のときは今までどおり英字");
        k.Type("\n");
        Assert.Equal("exe", k.Host.Output.Single());
    }

    [Test]
    public static void Off_ExeSpaceCommitsEnglish()
    {
        var k = new CompositionTests.Keyboard();
        k.Type("exe ");
        Assert.Equal("exe ", string.Concat(k.Host.Output), "OFF のときは Space で英字 + 空白");
        Assert.True(k.Host.View is null, "変換は始めない");
    }

    [Test]
    public static void On_ExeAtInputStartBecomesKana()
    {
        WithMode(true, () =>
        {
            var k = new CompositionTests.Keyboard();
            k.Type("exe");
            Assert.Equal("えぇ", k.Showing, "ON のときは入力の先頭 (日本語の文脈) で かな");
            k.Type("\n");
            Assert.Equal("えぇ", k.Host.Output.Single());
        });
    }

    [Test]
    public static void On_ExeAfterJapaneseBecomesKana()
    {
        WithMode(true, () =>
        {
            var k = new CompositionTests.Keyboard();
            k.Type("korehaexe");
            Assert.Equal("これはえぇ", k.Showing, "日本語の後ろでは かな");
            k.Type("\n");
            Assert.Equal("これはえぇ", k.Host.Output.Single());
        });
    }

    [Test]
    public static void On_ExeSpaceStartsConversion()
    {
        WithMode(true, () =>
        {
            var k = new CompositionTests.Keyboard();
            k.Type("exe ");
            Assert.True(k.Host.View?.Converting == true, "ON のときは Space で変換を始める (英字 + 空白で確定しない)");
        });
    }

    [Test]
    public static void On_ExeAfterEnglishStaysEnglish()
    {
        WithMode(true, () =>
        {
            var k = new CompositionTests.Keyboard();
            k.Type("hello ");
            k.Type("exe");
            Assert.Equal("exe", k.Showing, "英語の文の中 (前が英単語) では今までどおり英字");
            k.Type("\n");
            Assert.Equal("hello exe", string.Concat(k.Host.Output));
        });
    }

    [Test]
    public static void On_ExeAfterEnglishSpaceStaysEnglish()
    {
        WithMode(true, () =>
        {
            var k = new CompositionTests.Keyboard();
            k.Type("hello ");
            k.Type("exe ");
            Assert.Equal("hello exe ", string.Concat(k.Host.Output), "英語の文の中では Space でも英字 + 空白で確定する");
            Assert.True(k.Host.View is null, "変換は始めない");
        });
    }

    [Test]
    public static void On_DotExeStaysEnglish()
    {
        WithMode(true, () =>
        {
            var k = new CompositionTests.Keyboard();
            k.Type(".exe");
            Assert.Equal(".exe", k.Showing, "拡張子 (.exe) は英字のまま");
            k.Type("\n");
            Assert.Equal(".exe", k.Host.Output.Single());
        });
    }

    [Test]
    public static void On_OtherEnglishWordsUnchanged()
    {
        WithMode(true, () =>
        {
            var k = new CompositionTests.Keyboard();
            k.Type("hello");
            Assert.Equal("hello", k.Showing, "一覧に無い英単語は今までどおり");
            k.Type("\n");
            Assert.Equal("hello", k.Host.Output.Single());
        });
    }

    [Test]
    public static void ToggleAppliesFromNextKeystroke()
    {
        var k = new CompositionTests.Keyboard();
        k.Type("exe");
        Assert.Equal("exe", k.Showing, "OFF で打った時点は英字");
        CompositionTests.Detector.KanaInJapaneseMode = () => true;
        try
        {
            k.Press(VirtualKeys.Space);
            Assert.True(k.Host.View?.Converting == true, "設定を ON にした直後の Space から変換を始める (再起動不要)");
        }
        finally { CompositionTests.Detector.KanaInJapaneseMode = () => false; }
    }
}
