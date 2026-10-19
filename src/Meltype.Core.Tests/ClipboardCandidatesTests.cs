// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>
/// B2-6 (本家 #807): クリップボードの文字を変換候補に出す (既定 OFF)。
/// </summary>
internal static class ClipboardCandidatesTests
{
    [Test]
    public static void ClipboardLine_TakesOneLine()
    {
        Assert.Equal("hello", CompositionController.ClipboardLine(" hello \nworld"));
        Assert.Equal("hello", CompositionController.ClipboardLine("\n\nhello\n"));
        Assert.True(CompositionController.ClipboardLine("   ") is null, "空白だけ");
        Assert.True(CompositionController.ClipboardLine(new string('a', 201)) is null, "201 文字");
        Assert.True(CompositionController.ClipboardLine("a\tb") is null, "制御文字");
        Assert.Equal(new string('a', 200), CompositionController.ClipboardLine(new string('a', 200)));
    }

    [Test]
    public static void ClipboardReading_OffersTheClipboard()
    {
        var k = new CompositionTests.Keyboard { ClipboardCandidates = true };
        k.Host.ClipboardText = "https://example.com";
        k.Type("kurippuboodo ");
        var candidates = k.Host.View?.Candidates.ToList() ?? [];
        Assert.True(candidates.Contains("https://example.com"), "候補: " + string.Join("|", candidates));
    }

    [Test]
    public static void Off_DoesNotRead()
    {
        var k = new CompositionTests.Keyboard { ClipboardCandidates = false };
        k.Host.ClipboardText = "https://example.com";
        k.Type("kurippuboodo ");
        var candidates = k.Host.View?.Candidates.ToList() ?? [];
        Assert.True(!candidates.Contains("https://example.com"), "候補: " + string.Join("|", candidates));
    }

    [Test]
    public static void ClipboardChoice_IsNotLearned()
    {
        var history = new ConversionHistory(null);
        var suggestions = new DictionarySuggestions(null);
        var k = new CompositionTests.Keyboard(history: history, suggestions: suggestions) { ClipboardCandidates = true };
        k.Host.ClipboardText = "https://example.com";
        k.Type("kurippuboodo ");
        var index = k.Host.View!.Candidates.ToList().IndexOf("https://example.com");
        Assert.True(index >= 0, "候補: " + string.Join("|", k.Host.View.Candidates));
        k.Controller.SelectCandidate(index);
        k.Type("\n");

        Assert.Equal("https://example.com", k.Host.Output.Single(), "クリップボードの候補を確定");
        Assert.True(!history.Usage().ContainsKey("https://example.com"), "履歴に覚えない: " + string.Join("|", history.Usage().Keys));
        Assert.Equal(0, suggestions.Count, "登録提案に数えない");
    }

    [Test]
    public static void ClipboardChoice_DoesNotTouchExistingHistory()
    {
        var now = new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc);
        var history = new ConversionHistory(null) { Clock = () => now };
        history.Remember("くりっぷぼおど", "https://example.com");
        Assert.Equal(1.0, history.Usage()["https://example.com"]);

        var k = new CompositionTests.Keyboard(history: history) { ClipboardCandidates = true };
        k.Host.ClipboardText = "https://example.com";
        k.Type("kurippuboodo ");
        var index = k.Host.View!.Candidates.ToList().IndexOf("https://example.com");
        Assert.True(index >= 0, "候補: " + string.Join("|", k.Host.View.Candidates));
        k.Controller.SelectCandidate(index);
        k.Type("\n");

        Assert.Equal(1.0, history.Usage()["https://example.com"], "既存の履歴の使用回数も増やさない");
    }

    [Test]
    public static void ClipboardChoice_IsNotLearnedByEngine()
    {
        var converter = new LearningProbe();
        var k = new CompositionTests.Keyboard(converter: converter) { ClipboardCandidates = true };
        k.Host.ClipboardText = "https://example.com";
        k.Type("kurippuboodo ");
        var index = k.Host.View!.Candidates.ToList().IndexOf("https://example.com");
        Assert.True(index >= 0, "候補: " + string.Join("|", k.Host.View.Candidates));
        k.Controller.SelectCandidate(index);
        k.Type("\n");
        Thread.Sleep(100);
        Assert.Equal(0, converter.Learned.Count, "変換エンジンにも覚えさせない: " + string.Join("|", converter.Learned));
    }

    [Test]
    public static void ClipboardChoice_DoesNotComeBackAfterClipboardChanges()
    {
        var history = new ConversionHistory(null);
        var k = new CompositionTests.Keyboard(history: history) { ClipboardCandidates = true };
        k.Host.ClipboardText = "https://example.com";
        k.Type("kurippuboodo ");
        var index = k.Host.View!.Candidates.ToList().IndexOf("https://example.com");
        Assert.True(index >= 0, "候補: " + string.Join("|", k.Host.View.Candidates));
        k.Controller.SelectCandidate(index);
        k.Type("\n");

        // クリップボードが変わったあとも、前の内容が履歴から出てこない。
        var next = new CompositionTests.Keyboard(history: history) { ClipboardCandidates = true };
        next.Host.ClipboardText = "https://example.org";
        next.Type("kurippuboodo ");
        var candidates = next.Host.View?.Candidates.ToList() ?? [];
        Assert.True(!candidates.Contains("https://example.com"), "前のクリップボードの内容が履歴から戻る: " + string.Join("|", candidates));
        Assert.True(candidates.Contains("https://example.org"), "今のクリップボードは候補に出る");
    }

    /// <summary>変換エンジンに覚えさせた文節を記録する (Mozc の代わり)。</summary>
    private sealed class LearningProbe : IKanjiConverter, ILearningConverter
    {
        private readonly CompositionTests.FakeConverter _inner = new();
        public List<string> Learned { get; } = [];

        public string? Convert(string hiragana) => _inner.Convert(hiragana);

        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) => _inner.ConvertClauses(hiragana, context);

        public void Learn(string? context, IReadOnlyList<ConversionClause> clauses)
        {
            lock (Learned) Learned.Add(string.Join("|", clauses.Select(c => $"{c.Reading}={c.Text}")));
        }
    }
}
