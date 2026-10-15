// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Config;

namespace Meltype.Tests;

/// <summary>
/// K79: コード系アプリ (ターミナル) の行頭で、コマンド名 (cd・ls・make・sudo) は、打ち終わり (Space・Enter・Tab) に英字で確定する。
/// 一般アプリ・語の頭でない・一覧と完全一致しない・判定の強さ「手動」・かな入力では今までどおり。
/// </summary>
internal static class CommandNameTests
{
    /// <summary>製品と同じく、英単語の判定にスペルチェッカーを使う。終わったら元に戻す。</summary>
    private static void WithSpellChecker(Action body)
    {
        var previous = CompositionTests.Detector.SpellChecker;
        CompositionTests.Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try { body(); }
        finally { CompositionTests.Detector.SpellChecker = previous; }
    }

    private static (string Document, string? Composing) Type(string typed, bool codeApp, Action<CompositionTests.Keyboard>? setup = null)
    {
        var result = ("", (string?)null);
        WithSpellChecker(() =>
        {
            var k = new CompositionTests.Keyboard { CodeApp = codeApp };
            setup?.Invoke(k);
            k.Type(typed);
            result = (k.Host.Document, k.Showing);
        });
        return result;
    }

    [Test]
    public static void CodeApp_CommandThenSpace_CommitsEnglishWithSpace()
    {
        foreach (var command in new[] { "cd", "ls", "sudo", "make", "go", "rm", "sh", "open", "node" })
        {
            var (document, composing) = Type(command + " ", codeApp: true);
            Assert.Equal(command + " ", document, $"「{command}」+ Space は英字で確定して空白");
            Assert.True(string.IsNullOrEmpty(composing), $"「{command}」+ Space で変換が始まらない");
        }
    }

    [Test]
    public static void CodeApp_CommandThenEnter_CommitsEnglish()
    {
        foreach (var command in new[] { "make", "go", "sudo", "node", "open", "man", "nano", "bun", "echo", "code" })
        {
            Assert.Equal(command, Type(command + "\n", codeApp: true).Document, $"「{command}」+ Enter は英字");
        }
    }

    [Test]
    public static void CodeApp_CommandThenTab_CommitsEnglish()
    {
        var (document, composing) = Type("make\t", codeApp: true);
        Assert.True(document.StartsWith("make", StringComparison.Ordinal) && !document.Contains('ま'), $"make + Tab は英字 (実際: {document})");
        Assert.True(string.IsNullOrEmpty(composing), "make + Tab で変換が始まらない");
    }

    [Test]
    public static void CodeApp_TypingCommands_FullLine()
    {
        Assert.Equal("cd ~/Downloads", Type("cd ~/Downloads\n", codeApp: true).Document, "cd ~/Downloads");
        Assert.Equal("ls -la", Type("ls -la\n", codeApp: true).Document, "ls -la");
        Assert.Equal("sudo ls", Type("sudo ls\n", codeApp: true).Document, "sudo ls");
    }

    [Test]
    public static void CodeApp_WhileTyping_LooksTheSameAsBefore()
    {
        // 打っている間 (final ではない) の見え方は今までどおり (確定で英字になる)
        Assert.Equal(Type("make", codeApp: false).Composing, Type("make", codeApp: true).Composing, "make を打っている間の見え方");
        Assert.Equal(Type("sudo", codeApp: false).Composing, Type("sudo", codeApp: true).Composing, "sudo を打っている間の見え方");
    }

    [Test]
    public static void GeneralApp_CommandNames_AreUnchanged()
    {
        Assert.Equal("まけ", Type("make\n", codeApp: false).Document, "一般アプリの make Enter は今までどおり");
        Assert.True(string.IsNullOrEmpty(Type("cd ", codeApp: false).Document), "一般アプリの cd Space は今までどおり (変換が始まる)");
    }

    [Test]
    public static void CodeApp_NotAtWordStart_IsUnchanged()
    {
        // 日本語を確定した直後の make は、語の頭ではない
        var after = Type("make\n", codeApp: true, k => k.Host.PrecedingText = "今日は").Document;
        Assert.Equal("まけ", after, "日本語の直後の make は今までどおり");
        var afterSpace = Type("make\n", codeApp: true, k => k.Host.PrecedingText = "$ ").Document;
        Assert.Equal("make", afterSpace, "空白の後 (行頭のプロンプトの後) は語の頭");
    }

    [Test]
    public static void CodeApp_NotInTheList_IsUnchanged()
    {
        Assert.Equal("まける", Type("makeru\n", codeApp: true).Document, "makeru は一覧と完全一致しないので今までどおり");
        Assert.Equal("すどう", Type("sudou\n", codeApp: true).Document, "sudou は今までどおり");
    }

    [Test]
    public static void CodeApp_ManualLevel_IsUnchanged()
    {
        var document = Type("make\n", codeApp: true, k => k.Level = DetectionLevel.Manual).Document;
        Assert.Equal("まけ", document, "判定の強さ「手動」では効かせない");
    }

    [Test]
    public static void CommandNames_AreLowerCaseWordsOnly()
    {
        foreach (var name in CompositionDetector.CommandNames)
            Assert.True(name.Length > 0 && name.All(char.IsAsciiLetterLower), $"一覧の語は小文字の英字だけ: {name}");
    }
}
