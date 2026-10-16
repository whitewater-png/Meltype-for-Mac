// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>
/// いま / なう / きょう の日時の候補 (本家 #208)。書式・並びは本家の今の形 (c211d24・aef35bc・1745a92) に合わせる。
/// 時刻は CompositionOptions.Now で固定する。
/// </summary>
internal static class DateTimeCandidatesTests
{
    /// <summary>2026/10/9 は金曜日。</summary>
    private static readonly DateTime Friday = new(2026, 10, 9, 17, 22, 0);

    private static List<string> CandidatesOf(string keys, DateTime now, Func<string, IReadOnlyList<string>>? more = null)
    {
        var k = new CompositionTests.Keyboard(now: () => now, moreCandidates: more);
        k.Type(keys);
        return k.Host.View!.Candidates.ToList();
    }

    /// <summary>list の中の日時の候補 (want に含まれるもの) を、list に並んだ順で。</summary>
    private static string Picked(List<string> list, string[] want) => string.Join("|", list.Where(want.Contains));

    private static readonly string[] NowAt1722 =
        ["17:22", "17時22分", "午後5時22分", "2026年10月9日(金) 17時22分", "10月9日(金) 17時22分", "2026年10月9日(金) 17:22", "2026/10/09 17:22"];

    private static readonly string[] TodayFriday =
        ["2026年10月9日", "2026年10月9日(金)", "10月9日", "10月9日(金)", "2026/10/09", "2026-10-09", "金曜日"];

    [Test]
    public static void Now_Formats()
    {
        foreach (var keys in new[] { "ima ", "nau " })
        {
            var list = CandidatesOf(keys, Friday);
            Assert.Equal(string.Join("|", NowAt1722), Picked(list, NowAt1722), keys + ": " + string.Join(",", list));
        }
    }

    [Test]
    public static void Today_Formats()
    {
        var list = CandidatesOf("kyou ", Friday);
        Assert.Equal(string.Join("|", TodayFriday), Picked(list, TodayFriday), string.Join(",", list));
    }

    [Test]
    public static void Now_MorningMidnightNoon()
    {
        // 時は 2 桁 (09:05)。午前 / 午後: 0 時台は午前 0 時、12 時台は午後 0 時
        var morning = CandidatesOf("ima ", new DateTime(2026, 10, 9, 9, 5, 0));
        Assert.True(morning.Contains("09:05") && morning.Contains("9時5分") && morning.Contains("午前9時5分"), string.Join(",", morning));
        var midnight = CandidatesOf("ima ", new DateTime(2026, 10, 9, 0, 5, 0));
        Assert.True(midnight.Contains("00:05") && midnight.Contains("午前0時5分"), string.Join(",", midnight));
        var noon = CandidatesOf("ima ", new DateTime(2026, 10, 9, 12, 30, 0));
        Assert.True(noon.Contains("午後0時30分"), string.Join(",", noon));
        var late = CandidatesOf("ima ", new DateTime(2026, 10, 9, 23, 0, 0));
        Assert.True(late.Contains("午後11時0分") && late.Contains("23:00"), "ちょうどの時刻も 0分 と書く (本家と同じ): " + string.Join(",", late));
    }

    [Test]
    public static void Weekdays_SundayToSaturday()
    {
        var sunday = new DateTime(2026, 10, 11, 8, 0, 0);
        var days = Enumerable.Range(0, 7).Select(i => CandidatesOf("kyou ", sunday.AddDays(i)).Single(c => c.EndsWith("曜日", StringComparison.Ordinal)));
        Assert.Equal("日曜日月曜日火曜日水曜日木曜日金曜日土曜日", string.Concat(days), "日曜始まりの曜日");
    }

    [Test]
    public static void Conversion_KeepsUsualCandidatesFirst()
    {
        // いつもの候補 (今日・今) と かな・カタカナ の並びは変えず、日時の候補はその後ろ
        var list = CandidatesOf("kyou ", Friday);
        Assert.Equal("今日", list[0], "最初の候補は動かさない: " + string.Join(",", list));
        Assert.Equal("きょう", list[1], "2 番目 (Space 2 回目) は前と同じ: " + string.Join(",", list));
        Assert.True(list.IndexOf("2026年10月9日") > list.IndexOf("ｷｮｳ"), "半角カタカナの後ろ: " + string.Join(",", list));

        var more = CandidatesOf("ima ", Friday, reading => reading == "いま" ? ["今", "居間", "イマ"] : []);
        var k = new CompositionTests.Keyboard(now: () => Friday, moreCandidates: reading => reading == "いま" ? ["今", "居間", "イマ"] : []);
        k.Type("ima ");
        k.Type(" ");
        var expanded = k.Host.View!.Candidates.ToList();
        Assert.True(expanded.IndexOf("居間") >= 0 && expanded.IndexOf("居間") < expanded.IndexOf("17:22"),
            "変換エンジンの候補 (居間) を足しても、日時の候補は後ろ: " + string.Join(",", expanded));
        Assert.True(more.Contains("17:22"), "広げる前から出ている: " + string.Join(",", more));
    }

    [Test]
    public static void OtherReadings_GiveNothing()
    {
        foreach (var keys in new[] { "imada ", "kyouha ", "imakara ", "ashita " })
        {
            var list = CandidatesOf(keys, Friday);
            Assert.True(!list.Any(c => c.Contains("17:22") || c.Contains("2026") || c == "金曜日"), keys + " では出さない: " + string.Join(",", list));
        }
    }

    /// <summary>確定した文節を記録する変換エンジン (学習に渡るものを調べる)。</summary>
    private sealed class LearningProbe : IKanjiConverter, ILearningConverter
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

    [Test]
    public static void ChosenDateTime_IsNotLearned()
    {
        // 日時の候補は覚えさせない (変換の記録・変換エンジン・登録の提案のどれにも)。覚えると、次に古い時刻・日付が最初に出る。
        foreach (var (keys, pick) in new[] { ("ima ", "17:22"), ("kyou ", "2026年10月9日"), ("kyou ", "金曜日"), ("nau ", "17時22分") })
        {
            var probe = new LearningProbe();
            var history = new ConversionHistory(null);
            var suggestions = new DictionarySuggestions(null);
            var k = new CompositionTests.Keyboard(history: history, converter: probe, suggestions: suggestions, suggestThreshold: 1, now: () => Friday);
            k.Type(keys);
            var first = k.Showing;
            for (var i = 0; i < 40 && k.Showing != pick; i++) k.Type(" ");
            Assert.Equal(pick, k.Showing, keys + ": 日時の候補まで進む");
            k.Type("\n");
            Assert.Equal(pick, string.Concat(k.Host.Output), keys + ": 確定");
            Thread.Sleep(100);
            lock (probe.Learned) Assert.Equal(0, probe.Learned.Count, keys + ": 変換エンジンに渡さない: " + string.Join(",", probe.Learned));
            Assert.Equal(0, suggestions.Pending(1).Count, keys + ": 登録の提案に数えない");

            var next = new CompositionTests.Keyboard(history: history, now: () => Friday.AddHours(1));
            next.Type(keys);
            Assert.Equal(first, next.Showing, keys + ": 次の最初の候補は前と同じ (日時を覚えていない)");
        }
    }

    [Test]
    public static void ChosenUsualCandidate_IsStillLearned()
    {
        // 日時でない候補 (きょう → キョウ) は今までどおり覚える (学習しない条件を広げすぎていない)
        var history = new ConversionHistory(null);
        var k = new CompositionTests.Keyboard(history: history, now: () => Friday, moreCandidates: r => r == "きょう" ? ["今日", "京"] : []);
        k.Type("kyou ");
        for (var i = 0; i < 10 && k.Showing != "京"; i++) k.Type(" ");
        Assert.Equal("京", k.Showing, "京 まで進む");
        k.Type("\n");
        Assert.Equal("京", history.Get("きょう"), "京 は覚える");
    }
}
