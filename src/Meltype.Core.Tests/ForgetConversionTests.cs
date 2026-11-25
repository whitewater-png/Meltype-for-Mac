// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>
/// 要件 031: 変換中に文節の候補を選んでいる間の Delete で、その「読み → 候補の文字」を学習から忘れる。
/// 履歴 (ConversionHistory) の R → T と、変換エンジンの学習 (ForgetConversion) を消し、その文節の候補を引き直して先頭に戻す。
/// 実機の azooKey の学習が消えるかは、Core のテストでは測れない (評価の口 --convert-eval --forget と実機で確かめる)。
/// </summary>
internal static class ForgetConversionTests
{
    /// <summary>
    /// 「がくしゅうを」を がく | しゅう | を に区切る偽のエンジン。しゅう の変換は、学習から忘れさせたあとは 習 を返す
    /// (本物の azooKey が、学習した 週 を忘れたあと辞書の別の語を先頭にするのと同じ形)。
    /// </summary>
    private sealed class ShuuConverter : IKanjiConverter
    {
        /// <summary>true なら、忘れたあとに 習 を返す (学習が消えると別の語が先頭になる実機の Zenzai ON の形)。false のままなら、忘れても 週 を返す (実機の Zenzai OFF で、単位が合わず消えなかった形)。</summary>
        public bool Forgotten { get; set; }
        public int ConvertCalls { get; private set; }
        private string Shuu => Forgotten ? "習" : "週";

        public string? Convert(string hiragana)
        {
            ConvertCalls++;
            return hiragana == "しゅう" ? Shuu : null;
        }

        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) =>
            hiragana == "がくしゅうを" ? [new("がく", "がく"), new("しゅう", Shuu), new("を", "を")] : null;
    }

    /// <summary>「がくしゅうを」を打って Space で変換し、→ で「しゅう」の文節を選ぶ。</summary>
    private static CompositionTests.Keyboard SelectShuu(ConversionHistory history, ShuuConverter converter, Action<string, string>? forget = null)
    {
        var k = new CompositionTests.Keyboard(history: history, converter: converter, moreCandidates: reading => reading == "しゅう" ? ["週", "習", "収", "周"] : [])
        {
            ForgetConversion = forget,
        };
        k.Type("gakushuuwo ");
        Assert.True(k.Host.View?.Converting == true, "変換中になる");
        k.Press(VirtualKeys.Right);
        Assert.Equal(1, k.Host.View!.SelectedClause, "しゅう の文節を選んでいる");
        return k;
    }

    [Test]
    public static void ForgetPair_RemovesOnlyAnExactReadingAndTextMatch()
    {
        var history = new ConversionHistory(null);
        history.Remember("しゅう", "週");
        history.Remember("せんしゅう", "週");
        Assert.True(!history.ForgetPair("しゅう", "習"), "同じ読みでも、別の語の学習は消さない");
        Assert.Equal("週", history.Get("しゅう"), "別の語を指定しても残る");
        Assert.True(!history.ForgetPair("しゅ", "週"), "別の読みは消さない");
        Assert.True(history.ForgetPair("しゅう", "週"), "読みと語が一致すれば消えて true");
        Assert.Equal(null, history.Get("しゅう"), "消えた");
        Assert.Equal("週", history.Get("せんしゅう"), "同じ語の別の読みは残る (ForgetText との違い)");
        Assert.True(!history.ForgetPair("しゅう", "週"), "2 度目は消すものが無いので false (例外にならない)");
        Assert.True(!history.ForgetPair("", "週") && !history.ForgetPair("しゅう", ""), "空の読み・語は何もしない");
    }

    [Test]
    public static void DeleteWhileConverting_ForgetsTheLearnedPairAndRedrawsTheClause()
    {
        var history = new ConversionHistory(null);
        history.Remember("しゅう", "週");
        history.Remember("せんしゅう", "週"); // 同じ語の別の読み: 残る
        var converter = new ShuuConverter();
        var forgotten = new List<(string Reading, string Text)>();
        var k = SelectShuu(history, converter, (reading, text) =>
        {
            forgotten.Add((reading, text));
            converter.Forgotten = true; // エンジンが学習を忘れた
        });
        Assert.Equal("週", k.Host.View!.Candidates[k.Host.View.SelectedIndex], "学習のおかげで 週 が先頭 (忘れさせる前)");
        Assert.Equal("がく週を", k.Host.View.Text, "忘れさせる前の表示");

        k.Press(0x2E); // Delete (fn+delete)

        // (a) 履歴: 読み → 語 が一致する学習だけ消える
        Assert.Equal(null, history.Get("しゅう"), "しゅう → 週 の学習が消える");
        Assert.Equal("週", history.Get("せんしゅう"), "別の読みの同じ 週 は消えない");
        // (b) エンジンの学習も、(読み, 語) で 1 回だけ消す
        Assert.Equal(1, forgotten.Count, "ForgetConversion は 1 回: " + string.Join("|", forgotten));
        Assert.Equal(("しゅう", "週"), forgotten[0], "ForgetConversion の引数は (読み, 表示していた語)");
        // (c) 候補が引き直されて先頭に戻り、まだ変換中
        Assert.True(k.Host.View?.Converting == true, "Delete のあとも変換中 (確定しない)");
        Assert.Equal(0, k.Host.View!.SelectedIndex, "候補は先頭に戻る");
        Assert.Equal("習", k.Host.View.Candidates[0], "引き直した先頭の候補は、忘れたあとの変換結果: " + string.Join("|", k.Host.View.Candidates));
        Assert.Equal("がく習を", k.Host.View.Text, "表示も引き直した候補になる");
        Assert.Equal(1, k.Host.View.SelectedClause, "選んでいる文節は変わらない");
        Assert.True(k.Host.Output.Count == 0, "Delete は確定もアプリへの素通しもしない: " + string.Join("|", k.Host.Output));
        Assert.True(!k.Host.Events.Any(e => e.StartsWith("passed:2E")), "Delete はアプリに渡らない: " + string.Join("|", k.Host.Events));
    }

    [Test]
    public static void DeleteWhileConverting_ForgetsTheCandidateThatIsShownNotTheFirstOne()
    {
        // 候補を ↓ で動かして 収 を選んでいるときの Delete は、(しゅう, 収) だけを忘れる。学習済みの しゅう → 週 は残す。
        var history = new ConversionHistory(null);
        history.Remember("しゅう", "週");
        var converter = new ShuuConverter();
        var forgotten = new List<(string Reading, string Text)>();
        var k = SelectShuu(history, converter, (reading, text) => forgotten.Add((reading, text)));
        k.Press(VirtualKeys.Down);
        var shown = k.Host.View!.Candidates[k.Host.View.SelectedIndex];
        Assert.True(shown != "週", "↓ で別の候補に動いた: " + shown);
        k.Press(0x2E);
        Assert.True(forgotten.SequenceEqual([("しゅう", shown)]), "表示していた語だけを忘れる: " + string.Join("|", forgotten));
        Assert.Equal("週", history.Get("しゅう"), "表示していない語の学習 (しゅう → 週) は消さない");
    }

    [Test]
    public static void DeleteWhileConverting_WithNothingLearned_JustRedrawsWithoutError()
    {
        // (e) 学習が無い語: 履歴は空、エンジンに渡す関数も無い (null)。例外にならず、候補を引き直すだけ
        var history = new ConversionHistory(null);
        var converter = new ShuuConverter();
        var k = SelectShuu(history, converter);
        var before = converter.ConvertCalls;
        k.Press(0x2E);
        Assert.True(k.Host.View?.Converting == true, "変換中のまま");
        Assert.Equal("週", k.Host.View!.Candidates[0], "学習が無ければ、引き直しても同じ先頭の候補");
        Assert.Equal(0, k.Host.View.SelectedIndex, "先頭に戻る");
        Assert.True(converter.ConvertCalls > before, "候補を引き直した (キャッシュを使い回していない)");
        Assert.True(history.Entries().Count == 0, "履歴は空のまま");
        // ForgetConversion が付いていて、履歴に学習が無いときも、エンジンには忘れるよう伝える (エンジン側だけに学習があるのが今回の不具合)
        var calls = 0;
        var k2 = SelectShuu(new ConversionHistory(null), new ShuuConverter(), (_, _) => calls++);
        k2.Press(0x2E);
        Assert.Equal(1, calls, "履歴に無くてもエンジンには伝える");
        k2.Press(0x2E);
        Assert.Equal(2, calls, "続けて押しても例外にならない");
    }

    [Test]
    public static void DeleteWhileConverting_ThenEnter_CommitsTheRedrawnCandidateWithoutRelearningTheForgottenOne()
    {
        var history = new ConversionHistory(null);
        history.Remember("しゅう", "週");
        var converter = new ShuuConverter();
        var k = SelectShuu(history, converter, (_, _) => converter.Forgotten = true);
        k.Press(0x2E);
        k.Press(VirtualKeys.Return);
        Assert.Equal("がく習を", string.Concat(k.Host.Output), "引き直した候補で確定する");
        Assert.Equal(null, history.Get("しゅう"), "確定しても、忘れた しゅう → 週 は学習し直さない");
    }

    [Test]
    public static void Delete_WhenNotConverting_OrOnPrediction_BehavesAsBefore()
    {
        // (d) 変換中でない (入力中)。ForgetConversion は呼ばれず、履歴も消えない
        var history = new ConversionHistory(null);
        history.Remember("きょう", "今日");
        var calls = 0;
        var k = new CompositionTests.Keyboard(history: history) { ForgetConversion = (_, _) => calls++ };
        k.Type("kyou");
        Assert.True(k.Host.View?.Converting != true, "変換中ではない");
        k.Press(0x2E);
        Assert.Equal(0, calls, "変換中でない Delete では ForgetConversion を呼ばない");
        Assert.Equal("今日", history.Get("きょう"), "履歴は消えない");

        // 予測を選んでいる間の Delete は今までどおり ForgetPrediction + ForgetText (ForgetConversion は呼ばない)
        string? forgottenPrediction = null;
        var k2 = new CompositionTests.Keyboard(history: history) { ForgetPrediction = text => forgottenPrediction = text, ForgetConversion = (_, _) => calls++ };
        k2.Type("kyou");
        Assert.True(k2.Host.View?.Predictions?.Contains("今日") == true, "予測が出る: " + string.Join("|", k2.Host.View?.Predictions ?? []));
        k2.Press(VirtualKeys.Tab);
        Assert.Equal(0, k2.Host.View!.SelectedPrediction, "予測を選ぶ");
        k2.Press(0x2E);
        Assert.Equal("今日", forgottenPrediction, "予測の Delete は ForgetPrediction を呼ぶ");
        Assert.Equal(0, calls, "予測の Delete では ForgetConversion を呼ばない");
        Assert.Equal(null, history.Get("きょう"), "予測の Delete は今までどおり履歴から語を消す (ForgetText)");
    }

    [Test]
    public static void DeleteWhileConverting_OnEnglishClause_BehavesAsBefore()
    {
        // 英語の文節 (打った英字の候補) には学習から忘れる語が無い。今までどおり (確定して Delete をアプリに渡す)
        var calls = 0;
        var k = new CompositionTests.Keyboard { ForgetConversion = (_, _) => calls++ };
        k.Type("hello");
        k.Press(VirtualKeys.Space);
        k.Press(0x2E);
        Assert.Equal(0, calls, "英語の文節の Delete は忘れない");
    }

    /// <summary>
    /// 実機の学習データのコピー (conversions.json) を使った確認 (環境変数 MELTYPE_TEST_CONVERSIONS_COPY が指すファイル)。
    /// 指していなければ何もしない (リポジトリには実機の語を置かないため。実行する場所はコミットしない)。
    /// 学習した しゅう → 週 が、変換中の Delete で消え、ほかの学習は 1 件も減らないことを見る。
    /// </summary>
    [Test]
    public static void DeleteWhileConverting_OnACopyOfTheRealHistory_RemovesOnlyThePair()
    {
        var source = Environment.GetEnvironmentVariable("MELTYPE_TEST_CONVERSIONS_COPY");
        if (string.IsNullOrEmpty(source) || !File.Exists(source))
        {
            // 空振りのときは、通ったように見えないよう出す (DictionaryManagementTests のスキップと同じ出し方)
            Console.WriteLine("  DeleteWhileConverting_OnACopyOfTheRealHistory_RemovesOnlyThePair: スキップ (環境変数 MELTYPE_TEST_CONVERSIONS_COPY が実機の conversions.json のコピーを指していない)");
            return;
        }
        var directory = Path.Combine(Path.GetTempPath(), "meltype-forget-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var copy = Path.Combine(directory, "conversions.json");
            File.Copy(source, copy);
            var history = new ConversionHistory(copy);
            var before = history.Entries();
            Assert.Equal("週", history.Get("しゅう"), "コピーには しゅう → 週 の学習がある");
            var converter = new ShuuConverter();
            var k = SelectShuu(history, converter, (_, _) => converter.Forgotten = true);
            k.Press(0x2E);
            Assert.Equal(null, history.Get("しゅう"), "Delete で しゅう → 週 が消える");
            var after = history.Entries();
            Assert.Equal(before.Count - 1, after.Count, "消えたのは 1 件だけ");
            Assert.True(!after.Any(e => e.Reading == "しゅう"), "しゅう の項目は無い");
            Assert.True(before.Where(e => e.Reading != "しゅう").All(e => after.Any(a => a.Reading == e.Reading && a.Text == e.Text)), "ほかの学習は変わらない");
            // 保存した結果も、読み直して同じ (消したままファイルに書かれた)
            var reloaded = new ConversionHistory(copy);
            Assert.Equal(null, reloaded.Get("しゅう"), "保存したファイルからも消えている");
            Assert.Equal(after.Count, reloaded.Entries().Count, "保存したファイルの件数");
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    [Test]
    public static void DeleteWhileConverting_WhenTheEngineStillReturnsTheSameWord_ForgetsTheHistoryAndKeepsTheWord()
    {
        // 実機の Zenzai OFF: 学習した単位が合わないと azooKey は忘れず、引き直しても同じ 週 が先頭に来る。
        // そのとき Core は、履歴から消して引き直すだけで、ほかのことはしない (週 のまま。変換中のまま。選択も先頭)。
        var history = new ConversionHistory(null);
        history.Remember("しゅう", "週");
        var converter = new ShuuConverter(); // Forgotten にしない = 忘れたあとも 週 を返す
        var forgotten = new List<(string Reading, string Text)>();
        var k = SelectShuu(history, converter, (reading, text) => forgotten.Add((reading, text)));
        k.Press(0x2E);
        Assert.Equal(null, history.Get("しゅう"), "履歴からは消える");
        Assert.True(forgotten.SequenceEqual([("しゅう", "週")]), "エンジンには伝える: " + string.Join("|", forgotten));
        Assert.True(k.Host.View?.Converting == true, "変換中のまま");
        Assert.Equal("週", k.Host.View!.Candidates[k.Host.View.SelectedIndex], "エンジンが同じ語を返すなら、先頭は 週 のまま");
        Assert.Equal("がく週を", k.Host.View.Text, "表示も変わらない");
        Assert.Equal(0, k.Host.View.SelectedIndex, "先頭に戻る");
        Assert.Equal(1, k.Host.View.SelectedClause, "選んでいる文節は変わらない");
        k.Press(0x2E); // 続けて押しても、2 度目の ForgetConversion が (しゅう, 週) で呼ばれるだけで例外にならない
        Assert.True(k.Host.View?.Converting == true, "2 度目も変換中のまま");
    }

    [Test]
    public static void ShiftDelete_WhileConverting_DoesNotForget_AndPassesDeleteAsBefore()
    {
        var history = new ConversionHistory(null);
        history.Remember("しゅう", "週");
        var calls = 0;
        var k = SelectShuu(history, new ShuuConverter(), (_, _) => calls++);
        k.Key(VirtualKeys.LShift);
        k.Press(0x2E);
        k.Key(VirtualKeys.LShift, up: true);
        Assert.Equal(0, calls, "Shift+Delete ではエンジンに忘れさせない");
        Assert.Equal("週", history.Get("しゅう"), "履歴も消さない");
        Assert.True(k.Host.Events.Any(e => e.StartsWith("down:2E", StringComparison.Ordinal)), "今までどおり、確定して Delete をアプリに渡す: " + string.Join("|", k.Host.Events));
        Assert.Equal("がく週を", string.Concat(k.Host.Output), "選んでいた候補で確定する");
    }

    [Test]
    public static void Delete_OnRomajiReconversion_DoesNotForget_AndPassesDeleteAsBefore()
    {
        // ローマ字の再変換は読みに戻れない (文節の「読み」は日本語にした文)。忘れる対象が無いので、今までどおり確定して Delete を渡す。
        var history = new ConversionHistory(null);
        var calls = 0;
        var k = new CompositionTests.Keyboard(history: history, converter: new ShuuConverter()) { ForgetConversion = (_, _) => calls++ };
        Assert.True(k.Controller.ReconvertRomaji("kyouha iitenki desu ne"), "前提: ローマ字の再変換が始まる");
        k.Controller.Pump();
        Assert.True(k.Host.View?.Converting == true, "前提: 変換中: " + k.Showing);
        k.Press(0x2E);
        Assert.Equal(0, calls, "ローマ字の再変換の Delete では忘れない");
        Assert.True(k.Host.Events.Any(e => e.StartsWith("down:2E", StringComparison.Ordinal)), "確定して Delete をアプリに渡す: " + string.Join("|", k.Host.Events));
    }

    [Test]
    public static void Delete_InTokMode_ForgetsTheSelectedClause_LikeNormalMode()
    {
        // ATOK スタイル (TokMode) が変えるのは ← → ↓ の意味だけ。Delete は変わらないので、同じく忘れる (Shift+→ で文節を選ぶ)。
        var history = new ConversionHistory(null);
        history.Remember("しゅう", "週");
        var converter = new ShuuConverter();
        var forgotten = new List<(string Reading, string Text)>();
        var k = new CompositionTests.Keyboard(history: history, converter: converter) { TokMode = true, ForgetConversion = (r, t) => { forgotten.Add((r, t)); converter.Forgotten = true; } };
        k.Type("gakushuuwo ");
        Assert.True(k.Host.View?.Converting == true, "前提: 変換中");
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Right); // TokMode: Shift+→ は文節の移動
        k.Key(VirtualKeys.LShift, up: true);
        Assert.Equal(1, k.Host.View!.SelectedClause, "前提: しゅう の文節を選んだ");
        k.Press(0x2E);
        Assert.True(forgotten.SequenceEqual([("しゅう", "週")]), "TokMode でも (読み, 語) を忘れる: " + string.Join("|", forgotten));
        Assert.Equal(null, history.Get("しゅう"), "履歴から消える");
        Assert.Equal("がく習を", k.Host.View!.Text, "引き直した候補");
    }

    [Test]
    public static void Delete_OnAFixedClause_WithContinueAfterConversion_ForgetsOnlyThatClauseAndStaysConsistent()
    {
        // 変換後も続けて入力 (固定した文節 _headCount > 0): 固定した しゅう の文節を選んで Delete しても、
        // その文節の (読み, 語) だけを忘れて引き直し、固定した前後の文節と続けて打った分は壊れない。
        var history = new ConversionHistory(null);
        history.Remember("しゅう", "週");
        var converter = new ShuuConverter();
        var forgotten = new List<(string Reading, string Text)>();
        var k = new CompositionTests.Keyboard(history: history, converter: converter) { Continue = true, ForgetConversion = (r, t) => { forgotten.Add((r, t)); converter.Forgotten = true; } };
        k.Type("gakushuuwo ");
        k.Type("ta"); // 変換した文節を固定して、続けて打つ
        Assert.True(k.Host.View!.Clauses!.Take(3).SequenceEqual(["がく", "週", "を"]), "前提: 固定した文節: " + string.Join("|", k.Host.View.Clauses!));
        k.Type(" "); // 続けて打った分を変換する (固定した文節は保つ)
        Assert.True(k.Host.View?.Converting == true, "前提: 変換中");
        var before = string.Join("|", k.Host.View!.Clauses!);
        for (var i = 0; i < 5 && k.Host.View!.SelectedClause > 1; i++) k.Press(VirtualKeys.Left); // 固定した しゅう まで戻る
        Assert.Equal(1, k.Host.View!.SelectedClause, "前提: しゅう の文節を選んだ: " + before);
        k.Press(0x2E);
        Assert.True(forgotten.Count <= 1 && forgotten.All(f => f == ("しゅう", "週")), "忘れるなら (しゅう, 週) の 1 つだけ: " + string.Join("|", forgotten));
        Assert.True(k.Host.View?.Converting == true, "変換中のまま: " + k.Showing);
        Assert.True(k.Host.View!.Clauses!.Count == before.Split('|').Length, "文節の数は変わらない: " + string.Join("|", k.Host.View.Clauses!));
        Assert.True(k.Host.View.Clauses![0] == "がく" && k.Host.View.Clauses[2] == "を", "前後の固定した文節は変わらない: " + string.Join("|", k.Host.View.Clauses!));
        Assert.True(k.Host.Output.Count == 0, "確定しない: " + string.Join("|", k.Host.Output));
    }
}
