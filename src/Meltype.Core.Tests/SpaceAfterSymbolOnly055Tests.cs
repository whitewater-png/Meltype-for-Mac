// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Input;

namespace Meltype.Tests;

/// <summary>
/// 要件 055: ASCII の記号だけ (#・##・//・>・*) を打って Space を押すと、Space が変換のキーになって空白が消えていた (# hello → #hello)。
/// 箇条書きの - と同じく、半角の記号と空白で確定する。Shift+Space の変換、句読点 (. ,) の Space 変換、語の後ろの記号は今までどおり。
/// 文書: docs/requirements/055-space-after-symbol-only.md
/// </summary>
internal static class SpaceAfterSymbolOnly055Tests
{
    private static string Typed(string keys, bool codeApp)
    {
        var k = new CompositionTests.Keyboard { CodeApp = codeApp };
        k.Type(keys);
        return k.Host.Document.TrimEnd();
    }

    private static void Check(Dictionary<string, string> cases)
    {
        foreach (var codeApp in new[] { false, true })
            foreach (var (typed, expected) in cases)
                Assert.Equal(expected, Typed(typed + "\n", codeApp), typed + (codeApp ? " (コード系アプリ)" : " (一般アプリ)"));
    }

    [Test]
    public static void Req055_SymbolOnlyThenSpace_CommitsSymbolAndSpace()
    {
        Check(new()
        {
            ["# hello"] = "# hello",
            ["## hello"] = "## hello",
            ["# parseJSONtoObject"] = "# parseJSONtoObject",
            ["# kyouha"] = "# きょうは",
            ["// hello"] = "// hello",
            ["> hello"] = "> hello",
            ["* hello"] = "* hello",
            ["+ hello"] = "+ hello",
            ["| hello"] = "| hello",
        });
    }

    [Test]
    public static void Req055_SymbolOnlyThenSpace_IsHalfWidthEvenWithFullWidthSymbols()
    {
        // 設定「全角の記号」(テスト用キーボードの既定は ON) でも、空白で区切った記号は半角 (- と同じ)
        var k = new CompositionTests.Keyboard { FullWidthSymbols = true };
        k.Type("> ");
        Assert.Equal("> ", k.Host.Document, "> + Space は半角の > と空白");
        Assert.True(k.Showing is null, "確定したので変換ボックスは閉じている");
    }

    [Test]
    public static void Req055_Guards_KeepCurrentBehavior()
    {
        Check(new()
        {
            ["- hello"] = "- hello",
            ["kyou# hello"] = "今日#hello",
            ["echo hello"] = "echo hello",
        });
    }

    [Test]
    public static void Req055_ShiftSpaceOnSymbol_StillConverts()
    {
        // ＃・♯ などの候補が欲しいときは Shift+Space で変換する
        var k = new CompositionTests.Keyboard();
        k.Type("#");
        k.TypeKeys((VirtualKeys.Space, true));
        Assert.True(k.Host.View is { Converting: true }, "Shift+Space は記号だけでも変換を始める");
        Assert.Equal("", k.Host.Document, "まだ何も確定しない");
    }

    [Test]
    public static void Req055_PunctuationThenSpace_StillConverts()
    {
        // 句読点・括弧になる記号 (. , [ ]) は対象外。今までどおり Space で変換を始める
        foreach (var keys in new[] { ".", ",", "[", "]" })
        {
            var k = new CompositionTests.Keyboard();
            k.Type(keys + " ");
            Assert.True(k.Host.View is { Converting: true }, keys + " + Space は変換を始める");
            Assert.Equal("", k.Host.Document, keys + " はまだ確定しない");
        }
    }
}
