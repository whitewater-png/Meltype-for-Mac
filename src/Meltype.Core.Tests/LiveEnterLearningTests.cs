// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>
/// 要件 018: ライブ変換の表示を、見えたとおりに Enter で確定したとき、予測用の回数 (history.Touch) と登録提案の回数を、
/// Space → Enter と同じく数える。azooKey の学習 (変換エンジンへの学習) と選び直しの記録はしない。
/// 暗黙の確定・Shift+Enter・英語で終わる Space・かなのまま確定した場合は数えない。
/// </summary>
internal static class LiveEnterLearningTests
{
    /// <summary>変換エンジンに渡った学習を数える偽の変換エンジン。</summary>
    private sealed class Probe : IKanjiConverter, ILearningConverter
    {
        private readonly CompositionTests.FakeConverter _inner = new();
        public List<string> Learned { get; } = [];
        public string? Convert(string hiragana) => _inner.Convert(hiragana);
        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) => _inner.ConvertClauses(hiragana, context);
        public void Learn(string? context, IReadOnlyList<ConversionClause> clauses)
        {
            lock (Learned) Learned.AddRange(clauses.Select(c => c.Text));
        }
    }

    private sealed class Setup
    {
        public Probe Probe { get; } = new();
        public ConversionHistory History { get; } = new(null);
        public DictionarySuggestions Suggestions { get; } = new(null);
        public CompositionTests.Keyboard Keyboard { get; }

        public Setup(bool learned = true, bool live = true)
        {
            // 前に 1 回、単位を と選び直して学習した状態
            if (learned) History.Remember("たんいを", "単位を");
            Keyboard = new CompositionTests.Keyboard(live: live, history: History, converter: Probe, suggestions: Suggestions, suggestThreshold: 1);
        }

        public double Used => History.Usage().GetValueOrDefault("単位を");
        public int Pending => Suggestions.Pending(1).Count;
        public int LearnCalls
        {
            get
            {
                Thread.Sleep(100); // 学習は裏のスレッド
                lock (Probe.Learned) return Probe.Learned.Count;
            }
        }
    }

    private static void ShiftEnter(CompositionTests.Keyboard k)
    {
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Return);
        k.Key(VirtualKeys.LShift, up: true);
    }

    [Test]
    public static void LiveEnter_CountsUsageAndSuggestion_ButDoesNotTeachTheEngine()
    {
        // R1a: 学習済みの語を、ライブ表示のまま Enter
        var s = new Setup();
        var before = s.Used;
        s.Keyboard.Type("tanniwotoru");
        Assert.Equal("単位を取る", s.Keyboard.Showing, "前提: ライブ表示は漢字");
        s.Keyboard.Type("\n");
        Assert.Equal("単位を取る", s.Keyboard.Host.Document, "確定");
        Assert.True(s.Used > before, $"予測用の回数が増える: {before} → {s.Used}");
        Assert.Equal(1, s.Pending, "登録提案に数える");
        Assert.Equal(0, s.LearnCalls, "azooKey の学習はしない");
    }

    [Test]
    public static void SpaceThenEnter_StillLearnsAsBefore()
    {
        // R1b (対照): Space → Enter は今までどおり
        var s = new Setup();
        var before = s.Used;
        s.Keyboard.Type("tanniwotoru ");
        s.Keyboard.Press(VirtualKeys.Return);
        Assert.Equal("単位を取る", s.Keyboard.Host.Document);
        Assert.True(s.Used > before, "回数が増える");
        Assert.Equal(1, s.Pending, "登録提案に数える");
        Assert.True(s.LearnCalls > 0, "azooKey の学習をする (単位を・取る の 2 文節)");
    }

    [Test]
    public static void LiveEnter_UnlearnedWord_AddsNothing()
    {
        // 履歴に無い語は、Touch は空振りで、登録提案にも数えない
        var s = new Setup(learned: false);
        s.Keyboard.Type("tanniwotoru\n");
        Assert.Equal("単位を取る", s.Keyboard.Host.Document);
        Assert.Equal(0, s.History.Count, "履歴に覚えない");
        Assert.Equal(0, s.Pending, "登録提案に数えない");
        Assert.Equal(0, s.LearnCalls, "学習しない");
    }

    [Test]
    public static void LiveEnter_CountsEachTime()
    {
        var s = new Setup();
        var before = s.Used;
        s.Keyboard.Type("tanniwotoru\n");
        var once = s.Used;
        s.Keyboard.Type("tanniwotoru\n");
        Assert.True(once > before && s.Used > once, $"確定のたびに増える: {before} → {once} → {s.Used}");
    }

    [Test]
    public static void ShiftEnter_DoesNotCount()
    {
        var s = new Setup();
        var before = s.Used;
        s.Keyboard.Type("tanniwotoru");
        ShiftEnter(s.Keyboard);
        Assert.Equal("単位を取る", s.Keyboard.Host.Document, "確定はする");
        Assert.Equal(before, s.Used, "Shift+Enter は数えない");
        Assert.Equal(0, s.Pending);
        Assert.Equal(0, s.LearnCalls);
    }

    [Test]
    public static void ImplicitCommit_DoesNotCount()
    {
        // フォーカス移動などの暗黙の確定
        var s = new Setup();
        var before = s.Used;
        s.Keyboard.Type("tanniwotoru");
        s.Keyboard.Controller.CommitPending();
        Assert.Equal("単位を取る", s.Keyboard.Host.Document, "確定はする");
        Assert.Equal(before, s.Used, "暗黙の確定は数えない");
        Assert.Equal(0, s.Pending);
        Assert.Equal(0, s.LearnCalls);
    }

    [Test]
    public static void SpaceEndingInEnglish_DoesNotCount()
    {
        // R1e: 英語で終わる Space (日本語の所はライブ表示のまま確定する経路)
        var s = new Setup();
        var before = s.Used;
        s.Keyboard.Type("tanniwotorugithub ");
        Assert.True(s.Keyboard.Host.Document.Contains("github"), "前提: 英語で終わって確定した: " + s.Keyboard.Host.Document);
        Assert.Equal(before, s.Used, "数えない");
        Assert.Equal(0, s.Pending);
        Assert.Equal(0, s.LearnCalls);
    }

    [Test]
    public static void ShortReading_BelowLiveThreshold_DoesNotCount()
    {
        // 4 字未満はライブ変換でも漢字にならず、かなのまま確定する
        var s = new Setup();
        s.History.Remember("きょう", "今日");
        var before = s.History.Usage().GetValueOrDefault("今日");
        s.Keyboard.Type("kyou\n");
        Assert.Equal("きょう", s.Keyboard.Host.Document, "かなのまま確定");
        Assert.Equal(before, s.History.Usage().GetValueOrDefault("今日"), "数えない");
        Assert.Equal(0, s.Pending);
    }

    [Test]
    public static void EnterAsShown_KanaWithTrailingN_DoesNotCount()
    {
        // 設定「Enter は見えているとおりに確定」で語末の n をんにして確定する形は、ライブ表示そのものではないので数えない
        var s = new Setup();
        s.Keyboard.EnterCommitsAsShown = true;
        var before = s.Used;
        s.Keyboard.Type("tanniwotorun\n");
        Assert.True(s.Keyboard.Host.Document.EndsWith("ん"), "前提: 語末の n を ん にして確定: " + s.Keyboard.Host.Document);
        Assert.Equal(before, s.Used, "数えない");
        Assert.Equal(0, s.Pending);
        Assert.Equal(0, s.LearnCalls);
    }

    [Test]
    public static void EnterAsShown_NormalLiveDisplay_Counts()
    {
        // 同じ設定でも、ライブ表示そのままの確定は数える
        var s = new Setup();
        s.Keyboard.EnterCommitsAsShown = true;
        var before = s.Used;
        s.Keyboard.Type("tanniwotoru\n");
        Assert.Equal("単位を取る", s.Keyboard.Host.Document);
        Assert.True(s.Used > before, "数える");
        Assert.Equal(1, s.Pending);
        Assert.Equal(0, s.LearnCalls);
    }

    [Test]
    public static void LiveOff_KanaEnter_DoesNotCount()
    {
        var s = new Setup(live: false);
        var before = s.Used;
        s.Keyboard.Type("tanniwotoru\n");
        Assert.Equal("たんいをとる", s.Keyboard.Host.Document, "ライブ変換 OFF はかなのまま");
        Assert.Equal(before, s.Used);
        Assert.Equal(0, s.Pending);
    }
}
