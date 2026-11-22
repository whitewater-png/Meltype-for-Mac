// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>
/// 要件 018: ライブ変換の表示のまま (Space で変換せずに) Enter で確定したとき、確定した文節のうち学習済みの語を、
/// 予測用の回数 (history.Touch) と登録提案の回数に、Space → Enter と同じく数える。語末の n や Enter の打ち間違いの直しで
/// 確定の形が表示と変わる場合も、確定した語を数える。azooKey の学習 (変換エンジンへの学習) と選び直しの記録はしない。
/// 暗黙の確定・Shift+Enter・英語で終わる Space・かなのまま確定した場合は数えない。
/// </summary>
internal static class LiveEnterLearningTests
{
    /// <summary>変換エンジンに渡った学習を、1 回分ごとに文節の語を | でつないで覚える偽の変換エンジン。</summary>
    private sealed class Probe : IKanjiConverter, ILearningConverter
    {
        private readonly CompositionTests.FakeConverter _inner = new();
        public List<string> Learned { get; } = [];
        public string? Convert(string hiragana) => _inner.Convert(hiragana);
        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) => _inner.ConvertClauses(hiragana, context);
        public void Learn(string? context, IReadOnlyList<ConversionClause> clauses)
        {
            lock (Learned) Learned.Add(string.Join("|", clauses.Select(c => c.Text)));
        }
    }

    private sealed class Setup
    {
        private static readonly DateTime Now = new(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc);

        public Probe Probe { get; } = new();
        // 時計を止める (予測用のスコアは 回数 × 経過日数の減衰 なので、止めればスコアがそのまま回数になる)
        public ConversionHistory History { get; } = new(null) { Clock = () => Now };
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

        /// <summary>予測用の回数 (学習済みでない語は 0)。</summary>
        public int Count(string word) => (int)History.Usage().GetValueOrDefault(word);

        /// <summary>登録提案に数えた回数。</summary>
        public int Suggested(string word)
        {
            var n = 0;
            while (Suggestions.Pending(n + 1).Any(w => w.Word == word)) n++;
            return n;
        }

        /// <summary>
        /// 変換エンジンに渡った学習 (1 回分ごとに文節の語を | でつなぎ、/ で区切ったもの。無ければ空)。
        /// 学習は裏のスレッドで後から届くので、最後に対照 (hiruka を Space → Enter。必ず「昼|か」を渡す) を確定し、
        /// 対照が届くまで 10 ms ずつ最大 1 秒待つ。それより前に渡した学習は、裏のスレッドの列で対照より先に並ぶので、ふつうは先に届く
        /// (固定の時間だけ待つより、遅い環境でも「届いていない」と「渡していない」を取り違えにくい)。返すのは対照を除いた分。
        /// 対照の確定で入力欄も変わるので、ほかを確かめた後で最後に呼ぶ。
        /// </summary>
        public string Taught()
        {
            const string control = "昼|か";
            Keyboard.Type("hiruka ");
            Keyboard.Press(VirtualKeys.Return);
            var arrived = false;
            for (var i = 0; i < 100 && !arrived; i++)
            {
                lock (Probe.Learned) arrived = Probe.Learned.Contains(control);
                if (!arrived) Thread.Sleep(10);
            }
            lock (Probe.Learned)
            {
                Assert.True(arrived, "対照の学習が届く (裏のスレッドの学習が動いている): " + string.Join(" / ", Probe.Learned));
                return string.Join(" / ", Probe.Learned.Where(l => l != control));
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
        Assert.Equal("", s.Taught(), "azooKey の学習はしない");
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
        Assert.Equal("単位を|取る", s.Taught(), "azooKey の学習をする (単位を・取る の 2 文節)");
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
        Assert.Equal("", s.Taught(), "学習しない");
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
    public static void LiveEnter_TrailingN_CountsTheCommittedWord()
    {
        // 設定が既定のとき、語末の n は Enter で ん になってライブ変換される (表示 こうえn → 確定 講演。今までの Enter の動き)。
        // 表示と確定の形は違うが、確定した語のうち学習済みのものは数える (数えるのは学習済みの語だけなので、見ずに通した誤変換を強めない)
        var s = new Setup(learned: false);
        s.History.Remember("こうえん", "講演");
        s.Keyboard.Type("kouen");
        Assert.Equal("こうえn", s.Keyboard.Showing, "前提: 表示はかなと語末の n");
        s.Keyboard.Type("\n");
        Assert.Equal("講演", s.Keyboard.Host.Document, "確定はライブ変換の結果");
        Assert.Equal(2, s.Count("講演"), "講演 の回数は 1 → 2");
        Assert.Equal(1, s.Suggested("講演"), "登録提案に 1 回数える");
        Assert.Equal("", s.Taught(), "azooKey の学習はしない");
    }

    [Test]
    public static void LiveEnter_FixedTypo_CountsTheCommittedWord()
    {
        // Enter の打ち間違いの直し (onegaishimsu → おねがいします) で確定の形が表示と変わる場合も、確定した学習済みの語を数える
        var s = new Setup(learned: false);
        s.History.Remember("おねがいします", "お願いします");
        s.Keyboard.Type("onegaishimsu");
        Assert.Equal("おねがいしmす", s.Keyboard.Showing, "前提: 表示は打ち間違いのまま");
        s.Keyboard.Type("\n");
        Assert.Equal("お願いします", s.Keyboard.Host.Document, "確定は直した読みのライブ変換");
        Assert.Equal(2, s.Count("お願いします"), "お願いします の回数は 1 → 2");
        Assert.Equal(1, s.Suggested("お願いします"), "登録提案に 1 回数える");
        Assert.Equal("", s.Taught(), "azooKey の学習はしない");
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
        Assert.Equal("", s.Taught(), "azooKey の学習はしない");
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
        Assert.Equal("", s.Taught(), "azooKey の学習はしない");
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
        Assert.Equal("", s.Taught(), "azooKey の学習はしない");
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
        // 設定「Enter は見えているとおりに確定」で語末の n を ん にしてかなのまま確定する形 (かんたn → かんたん) は、
        // ライブ変換の結果 (確定のときの読み かんたん → 簡単) を確定しないので数えない。
        // 読みが学習済みの語でないと、守りを外しても数える語が無くて通ってしまうので、学習済みの 簡単 で確かめる
        var s = new Setup();
        s.History.Remember("かんたん", "簡単");
        s.Keyboard.EnterCommitsAsShown = true;
        var before = s.History.Usage().GetValueOrDefault("簡単");
        s.Keyboard.Type("kantan");
        Assert.Equal("かんたn", s.Keyboard.Showing, "前提: 表示はかなと語末の n");
        s.Keyboard.Type("\n");
        Assert.Equal("かんたん", s.Keyboard.Host.Document, "確定はかなのまま");
        Assert.Equal(before, s.History.Usage().GetValueOrDefault("簡単"), "簡単 の回数は変わらない");
        Assert.Equal(0, s.Pending, "登録提案に数えない");
        Assert.Equal("", s.Taught(), "azooKey の学習はしない");
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
        Assert.Equal("", s.Taught(), "azooKey の学習はしない");
    }

    [Test]
    public static void FirstSoundCommit_ThenLiveEnter_DoesNotCount()
    {
        // 1 音確定 (ATOK スタイルの Shift+↓) のあとの残りは語の途中から始まるので、数えない (Space → Enter でも学習しない。R46 任意 1)
        var s = new Setup();
        s.Keyboard.TokMode = true;
        s.Keyboard.Type("atanniwotoru");
        s.Keyboard.Key(VirtualKeys.LShift);
        s.Keyboard.Press(VirtualKeys.Down);
        s.Keyboard.Key(VirtualKeys.LShift, up: true);
        Assert.Equal("あ", s.Keyboard.Host.Document, "前提: 先頭の 1 音だけ確定");
        Assert.True(s.Keyboard.Host.View is { Converting: false, Text: "単位を取る" }, "前提: 残りはライブ表示: " + s.Keyboard.Showing);
        s.Keyboard.Type("\n");
        Assert.Equal("あ単位を取る", s.Keyboard.Host.Document, "確定はする");
        Assert.Equal(1, s.Count("単位を"), "単位を の回数は変わらない");
        Assert.Equal(0, s.Suggested("単位を"), "登録提案に数えない");
        Assert.Equal("", s.Taught(), "azooKey の学習はしない");
    }

    [Test]
    public static void ToggleBox_LiveEnter_DoesNotCount()
    {
        // 確定後の F6 で開いた変換ボックス (英字で確定した語を、かなの読みにして開き直したもの) は、
        // 変換をやめてライブ表示のまま Enter で確定しても数えない (Enter で日本語として覚えるのは打った英字のほう)
        var s = new Setup();
        s.Keyboard.Type("tanniwotoru");
        s.Keyboard.Press(VirtualKeys.F10);
        s.Keyboard.Type("\n");
        Assert.Equal("tanniwotoru", s.Keyboard.Host.Document, "前提: 英字で確定");
        Assert.True(s.Keyboard.Controller.TryToggleLastWord(toEnglish: false, s.Keyboard.Host.Document), "前提: 確定後の F6 で変換ボックスが開く");
        s.Keyboard.Press(VirtualKeys.Back);
        Assert.True(s.Keyboard.Host.View is { Converting: false, Text: "単位を取る" }, "前提: 変換をやめて、ライブ表示に戻る: " + s.Keyboard.Showing);
        s.Keyboard.Type("\n");
        Assert.Equal("単位を取る", s.Keyboard.Host.Document, "確定はする");
        Assert.Equal(1, s.Count("単位を"), "単位を の回数は変わらない");
        Assert.Equal(0, s.Suggested("単位を"), "登録提案に数えない");
        Assert.Equal("", s.Taught(), "azooKey の学習はしない");
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
