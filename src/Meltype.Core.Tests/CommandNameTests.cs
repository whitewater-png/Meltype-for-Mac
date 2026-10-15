// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>
/// K79: コード系アプリ (ターミナル) の語の頭で、コマンド名 (cd・ls・make・sudo) は、打ち終わり (Space・Enter・Tab) に英字で確定する。
/// 一般アプリ・語の頭でない・一覧と完全一致しない・判定の強さ「手動」・かな入力では今までどおり。
/// </summary>
internal static class CommandNameTests
{
    /// <summary>英単語の判定にスペルチェッカー (同梱の単語表) を使う。Mac の製品は使えれば macOS のスペルチェッカーを使うので、同じ答えとは限らない。終わったら元に戻す。</summary>
    private static void WithSpellChecker(Action body)
    {
        var previous = CompositionTests.Detector.SpellChecker;
        CompositionTests.Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try { body(); }
        finally { CompositionTests.Detector.SpellChecker = previous; }
    }

    private static (string Document, string? Composing) Type(string typed, bool codeApp, Action<CompositionTests.Keyboard>? setup = null, Func<string, IReadOnlyList<string>>? predictions = null)
    {
        var result = ("", (string?)null);
        WithSpellChecker(() =>
        {
            var k = new CompositionTests.Keyboard(predictions: predictions) { CodeApp = codeApp };
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

    [Test]
    public static void CodeApp_SecondLineAfterEnterThatNeverReachedTheIme_IsStillAtWordStart()
    {
        // Terminal.app は変換中の文字が無いときの Enter を IME に渡さない: 自分の確定の記録の末尾は ls のまま (空白も改行も入らない)
        Assert.Equal("lscd ~/Downloads", Type("ls\ncd ~/Downloads\n", codeApp: true).Document, "2 行目の cd は語の頭 (英字)・~/Downloads は守る");
        Assert.Equal("./build.sh--no-install", Type("./build.sh\n--no-install\n", codeApp: true).Document, "2 行目の --no-install は語の頭 (守る)");
        // 日本語を確定した直後の /fuka・make は今までどおり
        Assert.Equal("まけ", Type("make\n", codeApp: true, k => k.Host.PrecedingText = "今日は").Document, "日本語の直後の make は日本語");
        // 一般アプリは今までどおり (語の頭でない)
        Assert.True(!Type("ls\nmake\n", codeApp: false).Document.EndsWith("make", StringComparison.Ordinal), "一般アプリは緩めない");
    }

    [Test]
    public static void CodeApp_CommandCommit_IsNotCorrectedLater()
    {
        // open を英字にした確定は、後の「単位」(tanni) で おぺん に書き戻されない
        Assert.Equal("open 単位", Type("open tanni \n", codeApp: true).Document, "open 単位");
    }

    [Test]
    public static void CodeApp_CommandWithPredictions_TabCommitsEnglishAndPassesTab()
    {
        var (document, composing) = Type("make\t", codeApp: true, predictions: reading => ["まけた", "まけて"]);
        Assert.True(document.StartsWith("make", StringComparison.Ordinal) && !document.Contains('ま'), $"予測が出ていても make + Tab は英字 (実際: {document})");
        Assert.True(string.IsNullOrEmpty(composing), "予測候補に入らない");
        WithSpellChecker(() =>
        {
            var k = new CompositionTests.Keyboard(predictions: reading => ["まけた", "まけて"]) { CodeApp = true };
            k.Type("make\t");
            Assert.True(k.Host.Events.Contains("down:09"), "Tab はアプリに渡る: " + string.Join("|", k.Host.Events));
        });
    }

    [Test]
    public static void SegmentsCache_DoesNotReuseAcrossAppKinds()
    {
        // 同じ打った内容・同じ前後のまま、アプリの種類だけが変わっても、前の区間分けを使い回さない (SegmentsKey に CodeApp)
        WithSpellChecker(() =>
        {
            foreach (var firstCode in new[] { true, false })
            {
                var k = new CompositionTests.Keyboard { CodeApp = firstCode };
                k.Type("make");
                string Show() => string.Join("|", k.Controller.AutomaticSegments().Select(s => s.IsEnglish ? "EN:" + s.Raw : "JA:" + s.Kana));
                var first = Show();
                k.CodeApp = !firstCode;
                var second = Show();
                Assert.Equal(firstCode ? "EN:make" : "JA:まけ", first, "1 回目");
                Assert.Equal(firstCode ? "JA:まけ" : "EN:make", second, "アプリの種類を切り替えたあと");
            }
        });
    }

    [Test]
    public static void CodeApp_AfterDigit_DecidedByCharBeforeDigits()
    {
        // 数字の後ろ (予算は10 の後ろ) の man は語の頭にしない: 今までどおり「万」側
        void Before(CompositionTests.Keyboard k) => k.Host.PrecedingText = "予算は10";
        Assert.Equal(Type("man ", codeApp: false, Before).Document, Type("man ", codeApp: true, Before).Document, "コード系アプリでも、数字の後は一般アプリと同じ");
        Assert.Equal("man ", Type("man ", codeApp: true, k => k.Host.PrecedingText = "予算は10 ").Document, "数字のあとに空白が入れば語の頭 (英字)");
        Assert.Equal("man ", Type("man ", codeApp: true, k => k.Host.PrecedingText = "sleep 10").Document, "ASCII の語のあとの数字は語の頭 (英字)");
        Assert.True(CompositionDetector.IsWordStartAfter("sleep 10", codeApp: true) && CompositionDetector.IsWordStartAfter("v2", codeApp: true) && CompositionDetector.IsWordStartAfter("10", codeApp: true), "sleep 10・v2・10 は語の頭");
        Assert.True(!CompositionDetector.IsWordStartAfter("予算は10", codeApp: true) && !CompositionDetector.IsWordStartAfter("第3", codeApp: true), "予算は10・第3 は語の頭ではない");
        Assert.True(!CompositionDetector.IsWordStartAfter("sleep 10", codeApp: false), "一般アプリは今までどおり");
    }

    [Test]
    public static void CodeApp_SecondLineAfterCommandWithTrailingDigits_IsAtWordStart()
    {
        // sleep 10⏎ は Enter が IME に渡らず、自分の確定の記録の末尾が数字のまま
        Assert.Equal("sleep 10cd ~/Downloads", Type("sleep 10\ncd ~/Downloads\n", codeApp: true).Document, "2 行目の cd は語の頭 (英字)・~/Downloads は守る");
        Assert.Equal("kill 1234./build.sh", Type("kill 1234\n./build.sh\n", codeApp: true).Document, "kill 1234 のあとの ./build.sh は守る");
    }

    [Test]
    public static void CodeApp_AfterOwnJapaneseCommit_SlashFukaIsNotProtected()
    {
        // 自分の確定の記録の経路: kyou Enter のあとの /fuka は、日本語の直後なので守らない
        var (document, _) = Type("kyou\n/fuka\n", codeApp: true);
        Assert.True(!document.EndsWith("/fuka", StringComparison.Ordinal), "/fuka は守らない (実際: " + document + ")");
    }

    [Test]
    public static void CodeApp_AfterF6_TabChoosesPrediction()
    {
        WithSpellChecker(() =>
        {
            var k = new CompositionTests.Keyboard(predictions: reading => ["まけた", "まけて"]) { CodeApp = true };
            k.Type("make");
            k.Press(VirtualKeys.F6);
            k.Press(VirtualKeys.Tab);
            Assert.True(!k.Host.Events.Contains("down:09"), "F6 でかなにしたあとの Tab は予測の選択に使う (アプリに渡さない): " + string.Join("|", k.Host.Events));
            Assert.Equal(0, k.Host.View!.SelectedPrediction, "予測の選択に入った (最初の予測)");
        });
    }

    [Test]
    public static void CodeApp_EveryCommandName_CommitsEnglishOnSpaceEnterAndTab()
    {
        foreach (var name in CompositionDetector.CommandNames)
        {
            Assert.True(Type(name + " ", codeApp: true).Document == name + " ", $"{name} + Space");
            Assert.True(Type(name + "\n", codeApp: true).Document == name, $"{name} + Enter");
            var tab = Type(name + "\t", codeApp: true).Document;
            Assert.True(tab.StartsWith(name, StringComparison.Ordinal), $"{name} + Tab (実際: {tab})");
        }
    }
}
