// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>ユーザー辞書への登録提案 (P12) と、予測変換の使用頻度による重み付け (P13) のテスト。</summary>
internal static class SuggestionTests
{
    private static string TempFile(string name) => Path.Combine(Path.GetTempPath(), $"meltype-{name}-{Guid.NewGuid():N}.json");

    /// <summary>「はしを」の候補から「箸を」を選び直して確定する (変換エンジンの 1 番目は「橋を」)。</summary>
    private static void PickChopsticks(ConversionHistory history, DictionarySuggestions suggestions, int threshold = 3, bool enabled = true)
    {
        var k = new CompositionTests.Keyboard(history: history, suggestions: suggestions, suggestThreshold: threshold) { SuggestEnabled = enabled };
        k.Type("hashiwo ");
        var index = k.Host.View!.Candidates.ToList().IndexOf("箸を");
        Assert.True(index >= 0, "候補に箸を");
        // 学習済みなら最初から 箸を が先頭 (index 0)
        for (var i = 0; i < index; i++) k.Type(" ");
        k.Type("\n");
        Assert.Equal("箸を", k.Host.Output.Single(), "箸を を確定");
    }

    [Test]
    public static void Suggest_ThreeTimesBecomesPending()
    {
        var history = new ConversionHistory(null);
        var suggestions = new DictionarySuggestions(null);
        PickChopsticks(history, suggestions);
        PickChopsticks(history, suggestions);
        Assert.Equal(0, suggestions.Pending(3).Count, "2 回ではまだ提案しない");
        PickChopsticks(history, suggestions);
        var pending = suggestions.Pending(3);
        Assert.Equal(1, pending.Count, "3 回で提案待ち");
        Assert.Equal("はしを", pending[0].Reading, "読み");
        Assert.Equal("箸を", pending[0].Word, "語");
    }

    [Test]
    public static void Suggest_FirstCandidateIsNotCounted()
    {
        var suggestions = new DictionarySuggestions(null);
        for (var i = 0; i < 4; i++)
        {
            var k = new CompositionTests.Keyboard(suggestions: suggestions);
            k.Type("hashiwo \n");
            Assert.Equal("橋を", k.Host.Output.Single(), "変換エンジンの 1 番目をそのまま確定");
        }
        Assert.Equal(0, suggestions.Count, "選び直していない語は数えない");
    }

    [Test]
    public static void Suggest_SettingOffDoesNotCount()
    {
        var history = new ConversionHistory(null);
        var suggestions = new DictionarySuggestions(null);
        for (var i = 0; i < 3; i++) PickChopsticks(history, suggestions, enabled: false);
        Assert.Equal(0, suggestions.Count, "設定で OFF なら数えない");
    }

    [Test]
    public static void Suggest_Threshold()
    {
        var suggestions = new DictionarySuggestions(null);
        suggestions.Record("はしを", "箸を", 2);
        suggestions.Record("はしを", "箸を", 2);
        Assert.Equal(1, suggestions.Pending(2).Count, "閾値 2 なら 2 回で提案待ち");
        Assert.Equal(0, suggestions.Pending(5).Count, "閾値を上げれば待ちではなくなる");
        Assert.Equal(1, suggestions.Pending(2, max: 1).Count, "件数の上限を指定できる");
    }

    [Test]
    public static void Suggest_IneligibleAreNotCounted()
    {
        var suggestions = new DictionarySuggestions(null);
        // ひらがなだけ・読みが短い・読みがかなでない・パスワードらしい英数字の羅列
        Assert.True(!suggestions.Record("ありがとう", "ありがとう", 3), "ひらがなだけの語は数えない");
        Assert.True(!suggestions.Record("ありがとう", "ありがとー", 3), "ひらがなだけの語は数えない (別の綴り)");
        Assert.True(!suggestions.Record("き", "記", 3), "読みが短い");
        Assert.True(!suggestions.Record("abc", "ABC", 3), "読みがひらがなでない");
        Assert.True(!suggestions.Record("ぱすわーど", "Abcd1234Efgh5678", 3), "16 文字以上の英数字だけの語はパスワードかもしれない");
        Assert.True(suggestions.Record("ぱすわーど", "Abcd1234Efgh567", 3), "15 文字なら数える");
        Assert.True(suggestions.Record("ぐーぐる", "Google", 3), "英字を含む語は数える");
        Assert.True(suggestions.Record("こーひー", "コーヒー", 3), "カタカナを含む語は数える");
        Assert.Equal(3, suggestions.Count, "数えた組");
    }

    [Test]
    public static void Suggest_RegisteredAndRejectedAreNotCounted()
    {
        var dictionary = new UserDictionary(null, builtIn: false);
        dictionary.Add("はしを", "箸を");
        var suggestions = new DictionarySuggestions(null);
        for (var i = 0; i < 5; i++) suggestions.Record("はしを", "箸を", 3, dictionary);
        Assert.Equal(0, suggestions.Count, "ユーザー辞書に登録済みの語は数えない");

        suggestions.Reject("きごう", "記号");
        for (var i = 0; i < 5; i++) suggestions.Record("きごう", "記号", 3);
        Assert.Equal(0, suggestions.Pending(3).Count, "「登録しない」にした語は提案しない");
        suggestions.Record("きごう", "奇号", 3);
        suggestions.Record("きごう", "奇号", 3);
        suggestions.Record("きごう", "奇号", 3);
        Assert.Equal("奇号", suggestions.Pending(3).Single().Word, "却下したのは組だけ (同じ読みの別の語は数える)");
    }

    [Test]
    public static void Suggest_LimitDropsOldest()
    {
        var suggestions = new DictionarySuggestions(null);
        for (var i = 0; i < DictionarySuggestions.MaxEntries + 20; i++) suggestions.Record("ぐーぐる", $"語{i}", 3);
        Assert.Equal(DictionarySuggestions.MaxEntries, suggestions.Count, "上限を超えたら古いものを捨てる");
        suggestions.Record("ぐーぐる", "語0", 1);
        Assert.Equal(1, suggestions.Pending(1, max: 1).Count, "最初の語は捨てられていて数え直しになる");
        Assert.Equal("語0", suggestions.Pending(1, max: 1)[0].Word, "捨てられた語は新しく数え始めた扱い (閾値 1 で待ちになる)");
    }

    [Test]
    public static void Suggest_PersistsAndAcceptRemoves()
    {
        var path = TempFile("suggest");
        try
        {
            var first = new DictionarySuggestions(path);
            for (var i = 0; i < 3; i++) first.Record("はしを", "箸を", 3);
            first.Record("ぐーぐる", "Google", 3);
            first.Reject("きごう", "記号");

            var reloaded = new DictionarySuggestions(path);
            Assert.Equal(3, reloaded.Count, "再読み込みで保持");
            Assert.Equal("箸を", reloaded.Pending(3).Single().Word, "提案待ちも保持");
            for (var i = 0; i < 5; i++) reloaded.Record("きごう", "記号", 3);
            Assert.Equal(1, reloaded.Pending(3).Count, "却下も保持");
            // 保存内容は読み・語・回数・状態だけ
            var json = File.ReadAllText(path);
            Assert.True(json.Contains("\"Reading\"") && json.Contains("\"Word\"") && json.Contains("\"Count\"") && json.Contains("\"Rejected\""), "保存するのは読み・語・回数・状態");

            reloaded.Remove("はしを", "箸を");
            Assert.Equal(0, new DictionarySuggestions(path).Pending(3).Count, "登録すると提案待ちから消える");
            reloaded.Clear();
            Assert.Equal(0, new DictionarySuggestions(path).Count, "履歴の消去");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public static void Suggest_BrokenFileIsIgnored()
    {
        var path = TempFile("suggest");
        try
        {
            File.WriteAllText(path, "{ これは壊れている");
            var suggestions = new DictionarySuggestions(path);
            Assert.Equal(0, suggestions.Count, "壊れたファイルでも落ちない");
            Assert.True(suggestions.Record("はしを", "箸を", 3), "そのまま使える");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public static void Suggest_SessionAcceptRejectClearAndHint()
    {
        var dictionary = new UserDictionary(null, builtIn: false);
        var suggestions = new DictionarySuggestions(null);
        var today = new DateTime(2026, 10, 7, 9, 0, 0);
        suggestions.Clock = () => today;
        var session = new MeltypeSession(CompositionTests.Detector, new CompositionTests.FakeConverter(),
            new CompositionOptions { Suggestions = suggestions, UserDictionary = dictionary }, () => new Settings());

        for (var i = 0; i < 3; i++) suggestions.Record("はしを", "箸を", 3);
        for (var i = 0; i < 3; i++) suggestions.Record("ぐーぐる", "Google", 3);
        for (var i = 0; i < 3; i++) suggestions.Record("こーひー", "珈琲", 3);
        for (var i = 0; i < 3; i++) suggestions.Record("きごう", "記号", 3);
        var shown = session.PendingSuggestions();
        Assert.Equal(MeltypeSession.MaxSuggestionsShown, shown.Count, "メニューには最大 3 件");
        Assert.Equal("記号", shown[0].Word, "新しく待ちになったものから");

        Assert.Equal(null, session.AcceptSuggestion("きごう", "記号"), "登録できる");
        Assert.True(dictionary.Lookup("きごう").Contains("記号"), "ユーザー辞書に入る");
        Assert.True(!session.PendingSuggestions().Any(w => w.Word == "記号"), "登録したら提案待ちから消える");
        Assert.True(session.AcceptSuggestion("あ", "亜") is not null, "登録できないときは理由を返す");

        session.RejectSuggestion("こーひー", "珈琲");
        Assert.True(!session.PendingSuggestions().Any(w => w.Word == "珈琲"), "却下したら出ない");

        // ヒントは待ちができた直後に 1 日 1 回だけ
        var fresh = new DictionarySuggestions(null) { Clock = () => today };
        var hinted = new MeltypeSession(CompositionTests.Detector, new CompositionTests.FakeConverter(),
            new CompositionOptions { Suggestions = fresh }, () => new Settings());
        for (var i = 0; i < 3; i++) fresh.Record("はしを", "箸を", 3);
        Assert.True(hinted.TakeSuggestionHint()?.Contains("箸を") == true, "待ちができた直後はヒントが出る");
        Assert.Equal(null, hinted.TakeSuggestionHint(), "2 回目は出ない");
        for (var i = 0; i < 3; i++) fresh.Record("ぐーぐる", "Google", 3);
        Assert.Equal(null, hinted.TakeSuggestionHint(), "同じ日にはもう出さない");
        today = today.AddDays(1);
        for (var i = 0; i < 3; i++) fresh.Record("こーひー", "珈琲", 3);
        Assert.True(hinted.TakeSuggestionHint()?.Contains("珈琲") == true, "翌日は出る");

        session.ClearSuggestions();
        Assert.Equal(0, session.PendingSuggestions().Count, "履歴の消去");
        var off = new MeltypeSession(CompositionTests.Detector, new CompositionTests.FakeConverter(),
            new CompositionOptions { Suggestions = fresh }, () => new Settings { DictionarySuggest = false });
        for (var i = 0; i < 3; i++) fresh.Record("はしを", "箸を", 3);
        Assert.Equal(0, off.PendingSuggestions().Count, "設定で OFF ならメニューに出さない");
    }

    [Test]
    public static void Suggest_SettingsAreClamped()
    {
        Assert.Equal(2, new Settings { DictionarySuggestThreshold = 0 }.Normalize().DictionarySuggestThreshold, "下限 2");
        Assert.Equal(10, new Settings { DictionarySuggestThreshold = 99 }.Normalize().DictionarySuggestThreshold, "上限 10");
        Assert.True(new Settings().DictionarySuggest && new Settings().DictionarySuggestThreshold == 3, "既定は ON・3 回");
    }

    // ---- P13: 予測の使用頻度 ----

    private static readonly DateTime Now = new(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc);

    private static ConversionHistory HistoryFrom(string json, out string path)
    {
        path = TempFile("conversions");
        File.WriteAllText(path, json);
        return new ConversionHistory(path) { Clock = () => Now };
    }

    [Test]
    public static void Prediction_MoreUsedComesFirst()
    {
        var history = HistoryFrom("""
            {"おせわになります":{"Text":"お世話になります","Used":"2026-10-06T00:00:00Z","Count":1},
             "おせちりょうり":{"Text":"おせち料理","Used":"2026-10-01T00:00:00Z","Count":5}}
            """, out var path);
        try
        {
            var k = new CompositionTests.Keyboard(history: history);
            k.Type("ose");
            var predictions = k.Host.View!.Predictions!;
            Assert.Equal("おせち料理", predictions[0], "回数が多い語が上 (昨日 1 回より、6 日前 5 回)");
            Assert.Equal("お世話になります", predictions[1], "次に少ない語");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public static void Prediction_SameCountNewerFirst()
    {
        var history = HistoryFrom("""
            {"おせちりょうり":{"Text":"おせち料理","Used":"2026-09-01T00:00:00Z","Count":2},
             "おせわになります":{"Text":"お世話になります","Used":"2026-10-06T00:00:00Z","Count":2}}
            """, out var path);
        try
        {
            var k = new CompositionTests.Keyboard(history: history);
            k.Type("ose");
            Assert.Equal("お世話になります", k.Host.View!.Predictions![0], "同じ回数なら最近使った語が上");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public static void History_ReadsOldFileWithoutCount()
    {
        // 回数を持たない以前の conversions.json (Text と Used だけ)。読めて、1 回として扱う。
        var history = HistoryFrom("""
            {"おせわになります":{"Text":"お世話になります","Used":"2026-10-07T00:00:00Z"},
             "おせちりょうり":{"Text":"おせち料理","Used":"2026-10-06T00:00:00Z"}}
            """, out var path);
        try
        {
            Assert.Equal(2, history.Count, "古いファイルを読める");
            Assert.Equal(1.0, history.Usage()["お世話になります"], "回数が無ければ 1 回");
            history.Remember("おせわになります", "お世話になります");
            Assert.Equal(2.0, history.Usage()["お世話になります"], "使うと 2 回になる (今使ったので減衰なし)");
            var reloaded = new ConversionHistory(path) { Clock = () => Now };
            Assert.Equal(2.0, reloaded.Usage()["お世話になります"], "回数は保存される");
            var k = new CompositionTests.Keyboard(history: HistoryFrom(File.ReadAllText(path), out var second));
            k.Type("ose");
            Assert.Equal("お世話になります", k.Host.View!.Predictions![0], "新しい形式と古い形式が混ざっていても並べられる");
            File.Delete(second);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public static void History_CountAndTouch()
    {
        var history = new ConversionHistory(null) { Clock = () => Now };
        history.Remember("はしを", "箸を");
        history.Remember("はしを", "箸を");
        Assert.Equal(2.0, history.Usage()["箸を"], "同じ語を選び続けたら回数が増える");
        history.Remember("はしを", "橋を");
        Assert.Equal(1.0, history.Usage()["橋を"], "別の語に変えたら数え直し");
        Assert.True(!history.Usage().ContainsKey("箸を"), "置き換えられた語は残らない");
        var version = history.Version;
        history.Touch("はしを", "橋を");
        Assert.Equal(2.0, history.Usage()["橋を"], "Touch で回数が増える");
        Assert.Equal(version, history.Version, "Touch は変換結果のキャッシュを捨てない");
        history.Touch("はしを", "箸を");
        Assert.Equal(2.0, history.Usage()["橋を"], "別の語の Touch は無視");
    }

    [Test]
    public static void Prediction_DecaysWithTime()
    {
        // 30 日で半分: 60 日前の 4 回 (= 4 × 0.25 = 約 1) より、昨日の 2 回 (約 1.9) が上。
        var history = HistoryFrom("""
            {"おせちりょうり":{"Text":"おせち料理","Used":"2026-08-08T00:00:00Z","Count":4},
             "おせわになります":{"Text":"お世話になります","Used":"2026-10-06T00:00:00Z","Count":2}}
            """, out var path);
        try
        {
            var usage = history.Usage();
            Assert.True(Math.Abs(usage["おせち料理"] - 1.0) < 0.05, $"60 日前の 4 回は約 1 (半減期 30 日): {usage["おせち料理"]}");
            Assert.True(usage["お世話になります"] > usage["おせち料理"], "古い語より最近の語が上");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public static void Prediction_EngineUsedWordsRiseButSourceOrderHolds()
    {
        var history = new ConversionHistory(null) { Clock = () => Now };
        history.Remember("おせわになります", "C予測");
        history.Remember("おせわになります", "C予測");
        history.Remember("おせちりょうり", "履歴の語");
        var dictionary = new UserDictionary(null, builtIn: false);
        dictionary.Add("おせんべい", "お煎餅");
        dictionary.Add("おせろ", "オセロ");
        history.Remember("おせろ", "オセロ");
        var k = new CompositionTests.Keyboard(history: history, userDictionary: dictionary, predictions: _ => ["A予測", "B予測", "C予測"]);
        k.Type("ose");
        var predictions = k.Host.View!.Predictions!;
        Assert.Equal("オセロ", predictions[0], "ユーザー辞書の中では使用実績のある語が上");
        Assert.Equal("お煎餅", predictions[1], "ユーザー辞書の語が先 (出どころの順は崩さない)");
        Assert.Equal("C予測", predictions[2], "エンジンの予測では使った語が上に上がる");
        Assert.Equal("A予測", predictions[3], "実績の無い語は元の順");
        Assert.Equal("B予測", predictions[4], "実績の無い語は元の順");
        Assert.Equal("履歴の語", predictions[5], "履歴由来はエンジンの後");
        Assert.Equal(1, predictions.Count(p => p == "C予測"), "重複は除く");
    }

    [Test]
    public static void Prediction_CommitCountsUp()
    {
        var history = new ConversionHistory(null) { Clock = () => Now };
        for (var i = 0; i < 2; i++)
        {
            var k = new CompositionTests.Keyboard(history: history, predictions: _ => ["お世話になります"]);
            k.Type("osewa");
            k.Press(VirtualKeys.Tab);
            k.Press(VirtualKeys.Return);
        }
        Assert.Equal(2.0, history.Usage()["お世話になります"], "予測で確定した語も回数に数える");
    }

    [Test]
    public static void History_SharedInstanceKeepsBothUpdates()
    {
        var path = TempFile("conversions");
        try
        {
            var a = ConversionHistory.Shared(path);
            var b = ConversionHistory.Shared(path);
            Assert.True(ReferenceEquals(a, b), "同じパスは同じインスタンス");
            a.Remember("はしを", "箸を");
            b.Remember("きごう", "記号");
            b.Touch("はしを", "箸を");
            // Touch の保存は遅れるが、Remember (選び直し) は即保存、Flush で確実に書ける。
            a.Flush();
            var reloaded = new ConversionHistory(path);
            Assert.Equal("箸を", reloaded.Get("はしを"), "片方の更新が残る");
            Assert.Equal("記号", reloaded.Get("きごう"), "もう片方の更新も残る");
            Assert.True(reloaded.Usage()["箸を"] >= 1.9, "Touch の回数も Flush で保存される");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public static void History_TouchDefersSave()
    {
        var path = TempFile("conversions");
        try
        {
            var history = new ConversionHistory(path);
            history.Remember("はしを", "箸を");
            var before = File.ReadAllText(path);
            history.Touch("はしを", "箸を");
            Assert.Equal(before, File.ReadAllText(path), "Touch はその場では書かない");
            history.Flush();
            Assert.True(File.ReadAllText(path) != before, "Flush で書く");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public static void History_BrokenFileEntriesAreSkipped()
    {
        var history = HistoryFrom("""
            {"おせわになります":null,
             "おせちりょうり":{"Text":null,"Used":"2026-10-06T00:00:00Z","Count":3},
             "おせろ":{"Text":"","Used":"2026-10-06T00:00:00Z"},
             "おせんべい":{"Text":"お煎餅","Used":"2026-10-06T00:00:00Z","Count":2}}
            """, out var path);
        try
        {
            Assert.Equal(1, history.Count, "壊れた項目は読み飛ばす");
            Assert.Equal(null, history.Get("おせちりょうり"), "Text が null の項目は無い扱い");
            Assert.True(history.Usage().ContainsKey("お煎餅"), "正常な項目は使える");
            var k = new CompositionTests.Keyboard(history: history);
            k.Type("ose");
            Assert.Equal("お煎餅", k.Host.View!.Predictions!.Single(), "予測でも落ちない");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public static void Suggest_RejectsLineBreaksAndKeepsRejectedOnTrim()
    {
        Assert.True(!DictionarySuggestions.IsEligible("はしを", "箸\nを"), "改行を含む語は数えない");
        Assert.True(!DictionarySuggestions.IsEligible("はしを", "箸\tを"), "タブを含む語は数えない");
        Assert.True(!DictionarySuggestions.IsEligible("は\nしを", "箸を"), "改行を含む読みは数えない");

        var suggestions = new DictionarySuggestions(null);
        suggestions.Reject("きごう", "記号");
        for (var i = 0; i < DictionarySuggestions.MaxEntries + 10; i++) suggestions.Record("ぐーぐる", $"語{i}", 3);
        for (var i = 0; i < 5; i++) suggestions.Record("きごう", "記号", 3);
        Assert.Equal(0, suggestions.Pending(3).Count, "上限で捨てるときも却下は残る (提案が復活しない)");
    }

    [Test]
    public static void Suggest_PendingExcludesRegisteredWords()
    {
        var dictionary = new UserDictionary(null, builtIn: false);
        var suggestions = new DictionarySuggestions(null);
        var session = new MeltypeSession(CompositionTests.Detector, new CompositionTests.FakeConverter(),
            new CompositionOptions { Suggestions = suggestions, UserDictionary = dictionary }, () => new Settings());
        for (var i = 0; i < 3; i++) suggestions.Record("はしを", "箸を", 3);
        Assert.Equal(1, session.PendingSuggestions().Count, "提案待ち");
        dictionary.Add("はしを", "箸を");
        Assert.Equal(0, session.PendingSuggestions().Count, "メニューで先に登録した語は提案から外す");
    }
}
