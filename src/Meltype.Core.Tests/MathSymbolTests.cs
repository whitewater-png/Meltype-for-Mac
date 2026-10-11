// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>数字の後ろの かける・わる を × ÷ にする (本家 #321・#293)。変換エンジンの区切り方が数字と かける を 1 つにする場合・別にする場合の両方で確かめる。</summary>
internal static class MathSymbolTests
{
    internal enum Form { Separate, NumberJoinsWord, WordTakesNumber }

    /// <summary>
    /// 文節の区切りを決めて返す偽の変換エンジン (本物の azooKey が 3かける4 をどう区切るかは実機でないと分からないので、両方の形を試す)。
    /// 区切り方は 3 通り: 数字は別の文節 (3|かける|4) / 数字だけの文節を次の文節とつなげる (3かける|4) /
    /// 言葉の文節が後ろの数字から始まる文節を取り込む (3|かける4|は12。実機の azooKey の区切りはこれ)。
    /// 覚えさせた文節も記録する。
    /// </summary>
    internal sealed class SegmentingEngine(IEnumerable<(string Typed, string[] Segments)> cases, Form form) : IKanjiConverter, ILearningConverter
    {
        private readonly Dictionary<string, List<ConversionClause>> _clauses = Build(cases, form);
        private readonly Dictionary<string, string> _words = cases.SelectMany(c => c.Segments).Select(Split)
            .Where(s => !s.Reading.All(ch => char.IsAsciiDigit(ch) || ch == '.')).GroupBy(s => s.Reading).ToDictionary(g => g.Key, g => g.First().Text);

        public List<string> Learned { get; } = [];
        public List<string> Asked { get; } = [];

        private static (string Reading, string Text) Split(string segment)
        {
            var colon = segment.IndexOf(':');
            return colon < 0 ? (segment, segment) : (segment[..colon], segment[(colon + 1)..]);
        }

        private static Dictionary<string, List<ConversionClause>> Build(IEnumerable<(string Typed, string[] Segments)> cases, Form form)
        {
            var result = new Dictionary<string, List<ConversionClause>>();
            foreach (var (_, segments) in cases)
            {
                var list = new List<ConversionClause>();
                foreach (var (reading, text) in segments.Select(Split))
                {
                    var number = reading.All(ch => char.IsAsciiDigit(ch) || ch == '.');
                    if (form == Form.NumberJoinsWord && list.Count > 0 && list[^1].Reading.All(ch => char.IsAsciiDigit(ch) || ch == '.') && !number)
                        list[^1] = new ConversionClause(list[^1].Reading + reading, list[^1].Text + text);
                    else if (form == Form.WordTakesNumber && list.Count > 0 && char.IsAsciiDigit(reading[0]) && !list[^1].Reading.All(ch => char.IsAsciiDigit(ch) || ch == '.'))
                        list[^1] = new ConversionClause(list[^1].Reading + reading, list[^1].Text + text);
                    else list.Add(new ConversionClause(reading, text));
                }
                result[string.Concat(list.Select(c => c.Reading))] = list;
            }
            return result;
        }

        public string? Convert(string hiragana) => _clauses.TryGetValue(hiragana, out var list) ? string.Concat(list.Select(c => c.Text)) : _words.GetValueOrDefault(hiragana);

        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null)
        {
            lock (Asked) Asked.Add(hiragana);
            return _clauses.GetValueOrDefault(hiragana);
        }

        public void Learn(string? context, IReadOnlyList<ConversionClause> clauses)
        {
            lock (Learned) Learned.Add(string.Join("|", clauses.Select(c => $"{c.Reading}={c.Text}")));
        }
    }

    private static readonly Form[] Forms = Enum.GetValues<Form>();

    /// <summary>打つキー と 文節 (読み:文字列。同じなら読みだけ) と 1 番目の候補で確定した結果。</summary>
    internal static readonly (string Typed, string[] Segments, string Expected, string Note)[] Cases =
    [
        // 記号になる: 前が数字で、後ろが数字か文の終わり
        ("3kakeru4", ["3", "かける:掛ける", "4"], "3×4", "3かける4"),
        ("10waru2", ["10", "わる:割る", "2"], "10÷2", "10わる2"),
        ("12kakeru3kakeru4", ["12", "かける:掛ける", "3", "かける:掛ける", "4"], "12×3×4", "12かける3かける4"),
        ("3kakeru4ha12", ["3", "かける:掛ける", "4は12"], "3×4は12", "3かける4は12"),
        ("3kakeru", ["3", "かける:掛ける"], "3×", "3かける (文の終わり)"),
        ("10waru", ["10", "わる:割る"], "10÷", "10わる (文の終わり)"),
        ("1kakeru", ["1", "かける:掛ける"], "1×", "1かける (文の終わり)"),
        ("1waru", ["1", "わる:割る"], "1÷", "1わる (文の終わり)"),
        ("20kakeru5", ["20", "かける:掛ける", "5"], "20×5", "20かける5"),
        ("100waru4", ["100", "わる:割る", "4"], "100÷4", "100わる4"),
        ("kyouha3kakeru4wo", ["きょうは:今日は", "3", "かける:掛ける", "4を"], "今日は3×4を", "今日は3かける4を"),
        ("3kakeru2tasu1", ["3", "かける:掛ける", "2", "たす:足す", "1"], "3×2足す1", "3かける2たす1"),
        // 記号にならない: 小数は 3.5 が別の区間 (英数) になり、変換エンジンには かける2 だけが渡る (対象外。直すなら StartConversion で前の区間を渡す)
        ("3.5kakeru2", ["かける:掛ける", "2"], "3.5掛ける2", "小数 (対象外)"),
        // 記号にならない: 後ろに言葉が続く (動詞)
        ("3kakeruhito", ["3", "かける:掛ける", "ひと:人"], "3掛ける人", "3かける人"),
        ("5kakerunara", ["5", "かける:掛ける", "なら"], "5掛けるなら", "5かけるなら"),
        ("3kakeruyo", ["3", "かける:掛ける", "よ"], "3掛けるよ", "3かけるよ"),
        ("3kakeruto12", ["3", "かけると:掛けると", "12"], "3掛けると12", "3かけると12"),
        ("3kakerukedo", ["3", "かける:掛ける", "けど"], "3掛けるけど", "3かけるけど"),
        ("3kakerukoto", ["3", "かける:掛ける", "こと"], "3掛けること", "3かけること"),
        ("5kakeruniha", ["5", "かける:掛ける", "には"], "5掛けるには", "5かけるには"),
        ("2wareru", ["2", "われる:割れる"], "2割れる", "2われる"),
        ("100kaketa", ["100", "かけた:掛けた"], "100掛けた", "100かけた"),
        ("1kakete", ["1", "かけて:掛けて"], "1掛けて", "1かけて"),
        ("2warui", ["2", "わるい:悪い"], "2悪い", "2わるい"),
        ("10waruno", ["10", "わる:割る", "の"], "10割るの", "10わるの"),
        ("100wari", ["100", "わり:割り"], "100割り", "100わり"),
        // 記号にならない: 数字と かける の間に言葉 (時間・量・回数)
        ("5hunkakeru", ["5", "ふん:分", "かける"], "5分かける", "5分かける"),
        ("3jikankakeru", ["3", "じかん:時間", "かける"], "3時間かける", "3時間かける"),
        ("2kaikaketemiru", ["2", "かい:回", "かけて:掛けて", "みる:見る"], "2回掛けて見る", "2回かけてみる"),
        ("denwawo3kaikakeru", ["でんわを:電話を", "3", "かい:回", "かける"], "電話を3回かける", "電話を3回かける"),
        ("hon3satsukakeru", ["ほん:本", "3", "さつ:冊", "かける"], "本3冊かける", "本3冊かける"),
        ("2026nenkakeru", ["2026", "ねん:年", "かける"], "2026年かける", "2026年かける"),
        // 記号にならない: 数字が無い・漢数字
        ("kakeru", ["かける:掛ける"], "掛ける", "かける"),
        ("warui", ["わるい:悪い"], "悪い", "わるい"),
        ("kagiwokakeru", ["かぎを:鍵を", "かける"], "鍵をかける", "鍵をかける"),
        ("sankakeruyon", ["さん:三", "かける:掛ける", "よん:四"], "三掛ける四", "三かける四 (漢数字)"),
        // たす・ひく: 1 番目は動詞のまま (記号は 2 番目の候補。下の別のテストで確かめる)
        ("3tasu4", ["3", "たす:足す", "4"], "3足す4", "3たす4"),
        ("1tasu1ha2", ["1", "たす:足す", "1は2"], "1足す1は2", "1たす1は2"),
        ("5hiku3", ["5", "ひく:引く", "3"], "5引く3", "5ひく3"),
        ("3tashite4", ["3", "たして:足して", "4"], "3足して4", "3たして4"),
        ("5hiite3", ["5", "ひいて:引いて", "3"], "5引いて3", "5ひいて3"),
    ];

    private static CompositionTests.Keyboard Type(IKanjiConverter engine, string typed, ConversionHistory? history = null, string? preceding = null, string? following = null)
    {
        var k = new CompositionTests.Keyboard(converter: engine, history: history);
        k.Host.PrecedingText = preceding;
        k.Host.FollowingText = following;
        k.Type(typed + " ");
        return k;
    }

    [Test]
    public static void MathWordsAfterDigits_BecomeSymbols_OnlyWhereExpected()
    {
        // 変換エンジンが 3|かける|4 と区切っても、3かける|4 と区切っても同じ結果になる
        var failures = new List<string>();
        foreach (var form in Forms)
        {
            var engine = new SegmentingEngine(Cases.Select(c => (c.Typed, c.Segments)), form);
            foreach (var (typed, _, expected, note) in Cases)
            {
                var k = Type(engine, typed);
                k.Type("\n");
                if (k.Host.Document != expected) failures.Add($"{note} ({form}) 期待値: {expected} / 実際: {k.Host.Document}");
            }
        }
        Assert.True(failures.Count == 0, string.Join(" ; ", failures));
    }

    [Test]
    public static void MathWordsAfterDigits_WholeExpressionInOneClause()
    {
        // 変換エンジンが 3かける4 全体を 1 つの文節にしても (数字が前にも後ろにも付く)
        (string Typed, string[] Segments, string Expected)[] whole =
        [
            ("3kakeru4", ["3かける4:3掛ける4"], "3×4"), ("10waru2", ["10わる2:10割る2"], "10÷2"),
            ("3kakeruhito", ["3かけるひと:3掛ける人"], "3掛ける人"), ("5hunkakeru", ["5ふんかける:5分かける"], "5分かける"),
        ];
        var engine = new SegmentingEngine(whole.Select(c => (c.Typed, c.Segments)), Form.Separate);
        foreach (var (typed, _, expected) in whole)
        {
            var k = Type(engine, typed);
            k.Type("\n");
            Assert.Equal(expected, k.Host.Document, typed);
        }
    }

    [Test]
    public static void MathWordsAfterDigits_SurroundingText()
    {
        var engine = new SegmentingEngine([("kakeru3", ["かける:掛ける", "3"]), ("tasu", ["たす:足す"]), ("kakeru", ["かける:掛ける"]), ("3kakeru", ["3", "かける:掛ける"])], Form.Separate);
        // 変換の前に確定済みの数字 (半角・全角) があれば、それが前の数字
        var k = Type(engine, "kakeru3", preceding: "12");
        k.Type("\n");
        Assert.Equal("×3", k.Host.Document, "確定済みの 12 の後ろ");
        k = Type(engine, "kakeru3", preceding: "答えは");
        k.Type("\n");
        Assert.Equal("掛ける3", k.Host.Document, "前が数字でなければそのまま");
        k = Type(engine, "kakeru3", preceding: "mp3");
        k.Type("\n");
        Assert.Equal("掛ける3", k.Host.Document, "mp3 の 3 は名前の一部");
        k = Type(engine, "tasu", preceding: "１２");
        Assert.Equal("＋", Candidates(k, "＋"), "全角の数字の後ろは全角の ＋ (2 番目)");
        // 変換する語の後ろに文字が続くなら、文の終わりとは見ない
        k = Type(engine, "3kakeru", following: "人");
        k.Type("\n");
        Assert.Equal("3掛ける", k.Host.Document, "後ろに言葉が続く");
        k = Type(engine, "3kakeru", following: "4");
        k.Type("\n");
        Assert.Equal("3×", k.Host.Document, "後ろが数字");
    }

    /// <summary>記号の候補が出ている文節まで Right で進み、その候補の 2 番目 (1 番目は動詞) を調べる。</summary>
    private static string Candidates(CompositionTests.Keyboard k, string symbol)
    {
        for (var i = 0; i < 5 && k.Host.View?.Candidates.Any(c => c.Contains(symbol)) != true; i++) k.Press(VirtualKeys.Right);
        var candidates = k.Host.View!.Candidates;
        Assert.True(candidates.Count > 1, string.Join(",", candidates));
        return candidates[1];
    }

    [Test]
    public static void TasuHiku_OfferSymbolAsSecondCandidate()
    {
        foreach (var form in Forms)
        {
            var engine = new SegmentingEngine(Cases.Select(c => (c.Typed, c.Segments)), form);
            // 数字と同じ文節なら 3+ のように数字ごと、後ろの数字を取り込む文節なら +4 のように後ろごと
            var (plus, minus) = form switch { Form.NumberJoinsWord => ("3+", "5-"), Form.WordTakesNumber => ("+4", "-3"), _ => ("+", "-") };
            Assert.Equal(plus, Candidates(Type(engine, "3tasu4"), "+"), "たす " + form);
            Assert.Equal(minus, Candidates(Type(engine, "5hiku3"), "-"), "ひく " + form);
        }
    }

    [Test]
    public static void MathSymbol_BeatsLearnedHistory_AndIsNotLearned()
    {
        var engine = new SegmentingEngine(Cases.Select(c => (c.Typed, c.Segments)).Append(("honwo3kakeru4", ["ほんを:本を", "3", "かける:掛ける", "4"])), Form.Separate);
        // 前に 掛ける を選んで覚えていても (鍵を掛ける)、数字の間は ×
        var history = new ConversionHistory(null);
        history.Remember("かける", "掛ける");
        var k = Type(engine, "3kakeru4", history);
        k.Type("\n");
        Assert.Equal("3×4", k.Host.Document, "覚えた 掛ける より記号");
        // × を選んでも (そのまま確定しても) 覚えない
        Assert.Equal("掛ける", history.Get("かける") ?? "", "覚えたものは変わらない");
        history = new ConversionHistory(null);
        k = Type(engine, "3kakeru4", history);
        k.Type("\n");
        Assert.True(history.Get("かける") is null, "× を確定しても覚えない");
        // 記号を選び直して確定しても覚えない (たす → +)
        k = Type(engine, "3tasu4", history);
        Candidates(k, "+");
        k.Press(VirtualKeys.Space);
        k.Type("\n");
        Assert.Equal("3+4", k.Host.Document, "たす は 2 番目の候補の + を選べる");
        Assert.True(history.Get("たす") is null, "+ を選んでも覚えない");
        Assert.True(!DictionarySuggestions.IsEligible("たす", "+") && !DictionarySuggestions.IsEligible("かける", "×") && !DictionarySuggestions.IsEligible("わる", "÷") && !DictionarySuggestions.IsEligible("ひく", "-"),
            "辞書登録の提案にも数えない");
        // 記号でない候補 (掛ける) を選んだら、従来どおり覚える。それでも数字の間は ×
        history = new ConversionHistory(null);
        k = Type(engine, "3kakeru4", history);
        k.Press(VirtualKeys.Right);
        Assert.Equal("×", k.Host.View!.Candidates[0], "かける の文節の 1 番目は ×");
        k.Press(VirtualKeys.Space);
        k.Type("\n");
        Assert.Equal("3掛ける4", k.Host.Document, "選び直せる");
        Assert.Equal("掛ける", history.Get("かける") ?? "(なし)", "記号でない候補は覚える");
        k = Type(engine, "3kakeru4", history);
        k.Type("\n");
        Assert.Equal("3×4", k.Host.Document, "覚えていても数字の間は ×");
        k = Type(engine, "kagiwokakeru", history);
        k.Type("\n");
        Assert.Equal("鍵を掛ける", k.Host.Document, "数字の間でない かける は覚えたとおり");
    }

    [Test]
    public static void MathSymbol_IsNotSentToTheConverterToLearn()
    {
        // azooKey に学習させる文節 (Learn の引数) に × ÷ + - が入らない。かける の文節が数字を含まない読みでも (3|かける|4)
        foreach (var form in Forms)
        {
            var engine = new SegmentingEngine(
                Cases.Select(c => (c.Typed, c.Segments)).Concat([
                    ("tanniwotoru3kakeru4", ["たんいを:単位を", "とる:取る", "3", "かける:掛ける", "4"]),
                    ("tanniwotoru10waru2", ["たんいを:単位を", "とる:取る", "10", "わる:割る", "2"]),
                    ("tanniwotoru3tasu4", ["たんいを:単位を", "とる:取る", "3", "たす:足す", "4"]),
                ]), form);
            foreach (var typed in new[] { "tanniwotoru3kakeru4", "tanniwotoru10waru2" })
            {
                var k = Type(engine, typed);
                k.Press(VirtualKeys.Return);
                for (var i = 0; i < 100 && engine.Learned.Count == 0; i++) Thread.Sleep(10);
            }
            // 記号の候補を選び直して確定しても
            var plus = Type(engine, "tanniwotoru3tasu4");
            Candidates(plus, "+");
            plus.Press(VirtualKeys.Space);
            plus.Press(VirtualKeys.Return);
            // 確定済みの文節の学習は裏で行うので、対照 (単位を取る) が全部届くまで待ってから調べる
            for (var i = 0; i < 200; i++)
            {
                lock (engine.Learned) if (engine.Learned.Count(l => l.Contains("たんいを=単位を")) >= 3) break;
                Thread.Sleep(10);
            }
            Thread.Sleep(100);
            string[] learned;
            lock (engine.Learned) learned = engine.Learned.ToArray();
            Assert.True(learned.Count(l => l.Contains("たんいを=単位を")) >= 3, "対照: 数字の前の文節は学習する: " + string.Join(" / ", learned));
            Assert.True(!learned.Any(l => l.IndexOfAny(['×', '÷', '+', '-', '＋', '＝']) >= 0),
                form + ": " + string.Join(" / ", learned));
            Assert.True(!learned.Any(l => l.Contains("かける") || l.Contains("わる") || l.Contains("たす")), "計算の言葉の文節は学習に送らない: " + string.Join(" / ", learned));
        }
    }
}
