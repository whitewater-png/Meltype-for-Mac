// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Diagnostics;
using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>
/// 変換ボックスの文章が長くなっても、1 キーごとの処理が重くならないことのテスト (P16)。
/// 時間だけだと CI の環境差で揺れるので、変換エンジン・予測に渡した読みの長さ (打つたびに全体を渡していないか) も数える。
/// </summary>
internal static class LongInputTests
{
    /// <summary>呼ばれた回数と、渡された読みの長さを数える変換エンジン。</summary>
    private sealed class CountingEngine : IKanjiConverter
    {
        public int Calls;
        public long Chars;
        public int MaxLength;
        public int MaxPredictionLength;
        public int PredictionCalls;

        public string? Convert(string hiragana) => hiragana;

        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null)
        {
            Calls++;
            Chars += hiragana.Length;
            MaxLength = Math.Max(MaxLength, hiragana.Length);
            var list = new List<ConversionClause>();
            for (var i = 0; i < hiragana.Length; i += 4)
            {
                var piece = hiragana.Substring(i, Math.Min(4, hiragana.Length - i));
                list.Add(new ConversionClause(piece, "字" + piece));
            }
            return list;
        }
    }

    private const string Sentence = "kyouhanihongonobunshouwonagakuutsutokinougokigaokuninarumonndaigaarunodeshirabetemimasumeetingnojikanwokakuninshitekudasaiashitanohirumadeniokurimasu";

    private static (MeltypeSession Session, CountingEngine Engine) Create()
    {
        var engine = new CountingEngine();
        var withPrediction = new CompositionOptions
        {
            LiveConversion = () => true,
            Predictions = reading =>
            {
                engine.PredictionCalls++;
                engine.MaxPredictionLength = Math.Max(engine.MaxPredictionLength, reading.Length);
                return [reading + "あ"];
            },
        };
        return (new MeltypeSession(CompositionTests.Detector, engine, withPrediction, () => new Settings { LiveConversion = true }), engine);
    }

    private static SessionResult Press(MeltypeSession session, char c) =>
        session.HandleKey(char.ToUpperInvariant(c), c, false, false, false, false, null);

    [Test]
    public static void LongText_EngineIsNeverGivenTheWholeText()
    {
        var (session, engine) = Create();
        var text = string.Concat(Enumerable.Repeat(Sentence, 3))[..400];
        foreach (var c in text) Press(session, c);
        Assert.True(engine.MaxLength <= LongInputBound(), $"変換エンジンに渡した読みの最大 ({engine.MaxLength} 文字) が区切りの上限を超えた (打つたびに全体を渡している)");
        // 区切らない場合、1 キーごとに読み全体を渡すので、渡した文字数の合計は 400 文字で 2 万を超える (区切ると数千)。
        Assert.True(engine.Chars < 10000, $"変換エンジンに渡した文字数の合計が多すぎる: {engine.Chars}");
        Assert.True(engine.MaxPredictionLength <= CompositionController.MaxPredictionReadingLength, $"予測に渡した読みが長すぎる: {engine.MaxPredictionLength}");
    }

    private static int LongInputBound() => CompositionController.ConvertChunkLength;

    [Test]
    public static void LongText_TotalTimeStaysBounded()
    {
        var (session, _) = Create();
        var text = string.Concat(Enumerable.Repeat(Sentence, 3))[..400];
        var stopwatch = Stopwatch.StartNew();
        foreach (var c in text) Press(session, c);
        stopwatch.Stop();
        // Release で 0.5 秒前後。直す前は 10 秒を超えた (文章の長さの 2 乗〜3 乗で重くなっていた)。CI の遅さを見込んだ余裕のある上限。
        Assert.True(stopwatch.Elapsed.TotalSeconds < 8, $"400 文字を打つのに {stopwatch.Elapsed.TotalSeconds:F1} 秒かかった");
    }

    [Test]
    public static void LongText_ViewMatchesCommit()
    {
        var (session, _) = Create();
        // 母音で終わる所まで (語末の n は、確定すると ん になって表示と変わるため)
        var text = string.Concat(Enumerable.Repeat(Sentence, 2))[..200].TrimEnd('n', 'k', 'r', 's', 't', 'd', 'm', 'h', 'g', 'w', 'y', 'b', 'p', 'z', 'j', 'f');
        SessionResult last = null!;
        foreach (var c in text) last = Press(session, c);
        var shown = last.View?.Text;
        Assert.True(!string.IsNullOrEmpty(shown), "長い文章でも変換ボックスに表示が出る");
        var enter = session.HandleKey(VirtualKeys.Return, null, false, false, false, false, null);
        Assert.Equal(shown, enter.Commits.Single().Text, "表示していたとおりに確定する");
    }

    [Test]
    public static void Detector_LongLetterRun_FindsEnglishWordsInTheMiddle()
    {
        // 区間の長さの上限 (MaxEnglishSpanLength) で、長い文の途中の英単語を見落とさない
        var text = new CompositionText(CompositionTests.Detector);
        foreach (var c in "kyouhanihongonobunshouwonagakuutsutokinougokigaokuninarumonndaigaarunodeshirabetemimasuGithubnopushwosuru") text.Append(c);
        var english = text.Segments(final: true).Where(s => s.IsEnglish).Select(s => s.Raw).ToList();
        Assert.True(english.Any(e => e.Contains("Github")), "長い日本語の途中でも、大文字で始めた英単語は英字のまま");
    }

    [Test]
    public static void Detector_ContractionIsNotSkippedByLengthLimit()
    {
        // 短縮形 (don't) は語の長さを見ずに英語にする判定なので、長さの上限で飛ばさない (直す前と同じ結果)
        var text = new CompositionText(CompositionTests.Detector);
        foreach (var c in "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaadon't") text.Append(c);
        Assert.True(text.Segments(final: true).Any(s => s.IsEnglish), "長い語 + don't は英語の区間を含む");
    }

    // ---- 査読後の修正 ----

    private static string Segmented(CompositionDetector detector, string input, bool final, DetectionLevel level = DetectionLevel.Balanced)
    {
        var text = new CompositionText(detector) { LevelOverride = level };
        foreach (var c in input) text.Append(c);
        return string.Join("|", text.Segments(final).Select(x => (x.IsEnglish ? "E" : "J") + x.Raw + "=" + x.Kana));
    }

    [Test]
    public static void Detector_SkippingSpans_DoesNotChangeResults()
    {
        // 区間を飛ばす最適化・判定の覚えを使わない素朴な全探索 (ReferenceMode) と、1 キーずつ打った途中経過まで全部照合する。
        string[] inputs =
        [
            "kyouhanihongonobunshouwonagakuutsu", "meetingnojikanwokakuninshitekudasai", "Githubnopushwosuru", "OCRwoshita", "dontwo", "don't", "it'sOK",
            "stackoverflow", "alcoholicdrink", "taro@gmail.com", "user_namewo", "e-mailde", "ps5wokaitai", "win11pro", "2026nen3gatsu", "tetr.io", "Wakatte.TV",
            "sushiwotabeta", "iPhonedeshashin", "characteristicallyyoi", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaadon't",
            "kyouhaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "!?,.-nihongo#@", "tomatowotabeta", "kameranoremotedeoukuru",
        ];
        var detector = CompositionDetector.CreateDefault();
        var optimized = new List<string>();
        var reference = new List<string>();
        foreach (var level in new[] { DetectionLevel.Aggressive, DetectionLevel.Balanced, DetectionLevel.Conservative })
        {
            foreach (var input in inputs)
            {
                for (var length = 1; length <= input.Length; length += 3)
                {
                    foreach (var final in new[] { false, true })
                    {
                        optimized.Add(Segmented(detector, input[..length], final, level));
                    }
                }
            }
        }
        CompositionDetector.ReferenceMode = true;
        try
        {
            foreach (var level in new[] { DetectionLevel.Aggressive, DetectionLevel.Balanced, DetectionLevel.Conservative })
            {
                foreach (var input in inputs)
                {
                    for (var length = 1; length <= input.Length; length += 3)
                    {
                        foreach (var final in new[] { false, true })
                        {
                            reference.Add(Segmented(detector, input[..length], final, level));
                        }
                    }
                }
            }
        }
        finally
        {
            CompositionDetector.ReferenceMode = false;
        }
        Assert.Equal(reference.Count, optimized.Count);
        for (var i = 0; i < reference.Count; i++) Assert.True(reference[i] == optimized[i], $"最適化で結果が変わった: {reference[i]} / {optimized[i]}");
    }

    [Test]
    public static void Detector_LearnedLongWord_IsStillEnglish()
    {
        // 学習した語 (Memory) は、48 文字を超えても今までどおり英語
        var word = string.Concat(Enumerable.Repeat("ka", 30));
        var memory = new LanguageMemory(null);
        var detector = CompositionDetector.CreateDefault();
        detector.Memory = memory;
        var before = Segmented(detector, word, final: true);
        memory.Remember(word, english: true, explicitChoice: true);
        var after = Segmented(detector, word, final: true);
        CompositionDetector.ReferenceMode = true;
        string reference;
        try { reference = Segmented(detector, word, final: true); }
        finally { CompositionDetector.ReferenceMode = false; }
        Assert.Equal(reference, after, "学習した長い語は、飛ばす最適化を使わない判定と同じ");
        Assert.True(after.StartsWith("E"), "学習した長い語は英字のまま");
        Assert.True(before != after, "学習で判定の覚えが捨てられ、結果が変わる");
        memory.Remove([word]);
        Assert.Equal(before, Segmented(detector, word, final: true), "忘れたら元に戻る (判定の覚えも捨てられる)");
    }

    [Test]
    public static void SpanCache_InvalidatedByMemoryChanges()
    {
        var memory = new LanguageMemory(null);
        var detector = CompositionDetector.CreateDefault();
        detector.Memory = memory;
        var first = Segmented(detector, "sushi", final: true);
        memory.Remember("sushi", english: true, explicitChoice: true);
        var learned = Segmented(detector, "sushi", final: true);
        Assert.True(first != learned && learned.StartsWith("E"), "学習した語は英字にする");
        memory.Clear();
        Assert.Equal(first, Segmented(detector, "sushi", final: true), "学習を消したら元に戻る");
    }

    [Test]
    public static void Usage_CacheFollowsHistoryChanges()
    {
        var now = new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc);
        var history = new ConversionHistory(null) { Clock = () => now };
        history.Remember("よみ", "語");
        Assert.True(history.Usage().ContainsKey("語"), "覚えた語が入る");
        history.Remember("よみに", "別");
        Assert.True(history.Usage().ContainsKey("別"), "Remember で覚えを更新");
        var score = history.Usage()["語"];
        history.Touch("よみ", "語");
        Assert.True(history.Usage()["語"] > score, "Touch で回数が増えたら反映");
        history.Forget("よみ");
        Assert.True(!history.Usage().ContainsKey("語"), "Forget で消える");
        history.Clear();
        Assert.Equal(0, history.Usage().Count, "Clear で空");
        history.Remember("よみ", "語");
        now = now.AddDays(30);
        Assert.True(Math.Abs(history.Usage()["語"] - 0.5) < 0.01, "1 分を超えて時間が進んだら減衰を計算し直す");
    }

    /// <summary>1 回目は null (失敗)、2 回目から成功する変換エンジン。</summary>
    private sealed class FlakyEngine : IKanjiConverter
    {
        public int Calls;
        public string? Convert(string hiragana) => null;

        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) =>
            ++Calls == 1 ? null : [new ConversionClause(hiragana, "字" + hiragana)];
    }

    [Test]
    public static void EngineFailure_IsNotCached()
    {
        var engine = new FlakyEngine();
        var session = new MeltypeSession(CompositionTests.Detector, engine, new CompositionOptions(), () => new Settings());
        foreach (var c in "kakikukeko") Press(session, c);
        var first = session.HandleKey(VirtualKeys.Space, null, false, false, false, false, null);
        Assert.Equal("かきくけこ", first.View?.Text, "1 回目は変換できず読みのまま");
        session.HandleKey(VirtualKeys.Escape, null, false, false, false, false, null);
        var second = session.HandleKey(VirtualKeys.Space, null, false, false, false, false, null);
        Assert.Equal("字かきくけこ", second.View?.Text, "失敗は覚えないので、2 回目は変換できる");
    }

    [Test]
    public static void EngineCache_ClearedWhenBoxCloses()
    {
        var (session, engine) = Create();
        foreach (var c in "kakikukeko") Press(session, c);
        session.HandleKey(VirtualKeys.Space, null, false, false, false, false, null);
        session.HandleKey(VirtualKeys.Return, null, false, false, false, false, null);
        var calls = engine.Calls;
        foreach (var c in "kakikukeko") Press(session, c);
        session.HandleKey(VirtualKeys.Space, null, false, false, false, false, null);
        Assert.True(engine.Calls > calls, "変換ボックスを閉じたら覚えを捨てるので、もう一度エンジンに尋ねる");
    }

    [Test]
    public static void EngineCache_NoRecomputeSpikeOnVeryLongText()
    {
        var (session, engine) = Create();
        var text = string.Concat(Enumerable.Repeat(Sentence, 8))[..1100];
        var worst = 0;
        foreach (var c in text)
        {
            var before = engine.Calls;
            Press(session, c);
            worst = Math.Max(worst, engine.Calls - before);
        }
        // 覚えが 512 件でいっぱいになっても、全部が一度に変換し直しにならない (1 キーのエンジン呼び出しは数回)
        Assert.True(worst <= 8, $"1 キーのエンジン呼び出しが多すぎる: {worst}");
    }

    [Test]
    public static void Prediction_LimitAppliesOnlyToEngine()
    {
        var engine = new CountingEngine();
        var history = new ConversionHistory(null);
        var reading = new string('あ', 45);
        history.Remember(reading + "い", "履歴の語");
        var options = new CompositionOptions
        {
            History = history,
            Predictions = r => { engine.PredictionCalls++; engine.MaxPredictionLength = Math.Max(engine.MaxPredictionLength, r.Length); return ["エンジンの語"]; },
        };
        var session = new MeltypeSession(CompositionTests.Detector, engine, options, () => new Settings());
        SessionResult last = null!;
        foreach (var c in new string('a', 45)) last = Press(session, c);
        Assert.True(engine.MaxPredictionLength <= CompositionController.MaxPredictionReadingLength, "エンジンの予測は 40 文字までしか呼ばない");
        Assert.True(last.View?.Predictions?.Contains("履歴の語") == true, "変換履歴の予測は長い読みでも出す");
        Assert.True(last.View?.Predictions?.Contains("エンジンの語") != true, "40 文字を超えたらエンジンの予測は出さない");
    }
}
