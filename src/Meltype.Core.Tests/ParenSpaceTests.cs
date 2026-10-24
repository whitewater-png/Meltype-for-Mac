// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>閉じ括弧の直後の Space は、変換中のままにせず確定して空白を入れる (W13-3)。</summary>
internal static class ParenSpaceTests
{
    private static CompositionTests.Keyboard NewKeyboard()
    {
        CompositionTests.Detector.SpellChecker ??= Detection.BuiltInWordChecker.Shared;
        return new CompositionTests.Keyboard();
    }

    [Test]
    public static void ClosingParen_Space_CommitsAndKeepsTheSpace()
    {
        var k = NewKeyboard();
        k.Type("Japanese(nihongo) input requires too complex IMEs.\n");
        Assert.Equal("Japanese(日本語) input requires too complex IMEs.", k.Host.Document);
    }

    [Test]
    public static void ClosingParen_JapaneseAndWidthSettings_KeepTheSpace()
    {
        var japanese = NewKeyboard();
        japanese.Type("(kakko) desu\n");
        Assert.Equal("(かっこ) です", japanese.Host.Document);

        var full = NewKeyboard();
        full.Parentheses = ParenthesisWidth.FullWidth;
        full.Type("(kakko) desu\n");
        Assert.Equal("（かっこ） です", full.Host.Document);

        var spaced = NewKeyboard();
        spaced.SpaceAroundEnglish = true;
        spaced.Type("Japanese(nihongo) input\n");
        Assert.Equal("Japanese(日本語) input", spaced.Host.Document);
    }

    [Test]
    public static void OtherClosingBrackets_Space_AlsoKeepsTheSpace()
    {
        var square = NewKeyboard();
        square.Type("test[foo] bar\n");
        Assert.Equal("test「ふぉお」 bar", square.Host.Document);

        var curly = NewKeyboard();
        curly.Type("test{foo} bar\n");
        Assert.Equal("test{ふぉお｝ bar", curly.Host.Document);

        var half = NewKeyboard();
        half.Braces = BraceWidth.HalfWidth;
        half.Type("test{foo} bar\n");
        Assert.Equal("test{ふぉお} bar", half.Host.Document);
    }

    [Test]
    public static void JapaneseDirectlyAfterClosingParen_IsUnchanged()
    {
        var k = NewKeyboard();
        k.Type("(kakko)desu\n");
        Assert.Equal("(かっこ)です", k.Host.Document);
    }

    [Test]
    public static void RepeatedSpace_AfterClosingParen_PassesLaterSpacesToTheHost()
    {
        var k = NewKeyboard();
        k.Type("(kakko)  ");
        Assert.Equal("(かっこ) ", k.Host.Document);
        Assert.Equal("(かっこ) ", k.Host.Output.Single());
        Assert.True(k.Showing is null, "2 回目の Space でも変換を始めない");
        Assert.True(k.Host.Events.Contains("passed:20"), "2 回目の Space はアプリへ渡す");
    }

    [Test]
    public static void EnterAndEscape_DirectlyAfterClosingParen_AreUnchanged()
    {
        var enter = NewKeyboard();
        enter.Type("(kakko)\n");
        Assert.Equal("(かっこ)", enter.Host.Document);

        var escape = NewKeyboard();
        escape.Type("(kakko)");
        escape.Press(VirtualKeys.Escape);
        Assert.Equal("", escape.Host.Document);
        Assert.True(escape.Showing is null, "Esc で入力を取り消す");
    }

    [Test]
    public static void ShiftSpace_AfterClosingParen_StillStartsConversion()
    {
        var k = NewKeyboard();
        k.Type("(nihongo)");
        k.Host.PhysicalShift = true;
        k.Press(VirtualKeys.Space);
        k.Host.PhysicalShift = false;
        Assert.Equal("", k.Host.Document);
        Assert.True(k.Host.View?.Converting == true, "明示の Shift+Space は従来どおり変換を始める");
        Assert.Equal("(日本語)", k.Showing);
    }

    [Test]
    public static void SpaceWithFixedClauses_KeepsContinueAfterConversionBehavior()
    {
        var k = NewKeyboard();
        k.Continue = true;
        k.Type("kyou (nihongo)");
        k.Type(" ");
        Assert.Equal("", k.Host.Document);
        Assert.True(k.Host.View?.Converting == true, "固定した文節があるときは従来の Space 変換");
    }
}
