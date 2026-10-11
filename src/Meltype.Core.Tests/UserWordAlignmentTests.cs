// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>ユーザー辞書の語を、変換エンジンの文節の途中には割り込ませない (本家 #374・PR #385 / Mac 版 P30)。</summary>
internal static class UserWordAlignmentTests
{
    /// <summary>
    /// 小さな語彙を最長一致で区切る偽の変換エンジン。呼ばれた回数と中身を Calls に残す (1 キーあたりの呼び出し数を数えるため)。
    /// 語彙にない文字は、続くものを 1 つの文節にまとめて、そのまま返す。
    /// </summary>
    internal sealed class LexiconConverter : IKanjiConverter
    {
        public static readonly Dictionary<string, string> Lexicon = new()
        {
            ["わたしの"] = "私の", ["ことが"] = "事が", ["すき"] = "好き", ["わた"] = "綿", ["しの"] = "篠", ["が"] = "が", ["きた"] = "来た",
            ["に"] = "に", ["を"] = "を", ["は"] = "は", ["の"] = "の", ["もの"] = "物", ["ひと"] = "人", ["あ"] = "あ",
            ["ことしの"] = "今年の", ["ほし"] = "星", ["あしょ"] = "足所", ["せんだ"] = "仙台", ["あぎょう"] = "ア行", ["れつしき"] = "列式",
        };

        public List<string> Calls { get; } = [];

        public string? Convert(string hiragana) => ConvertClauses(hiragana) is { } clauses ? string.Concat(clauses.Select(c => c.Text)) : null;

        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null)
        {
            Calls.Add(hiragana + "|" + context);
            var clauses = new List<ConversionClause>();
            var unknown = "";
            for (var i = 0; i < hiragana.Length;)
            {
                var length = Math.Min(hiragana.Length - i, Lexicon.Keys.Max(k => k.Length));
                while (length > 0 && !Lexicon.ContainsKey(hiragana.Substring(i, length))) length--;
                if (length == 0)
                {
                    unknown += hiragana[i++];
                    continue;
                }
                if (unknown.Length > 0) clauses.Add(new ConversionClause(unknown, unknown));
                unknown = "";
                var reading = hiragana.Substring(i, length);
                clauses.Add(new ConversionClause(reading, Lexicon[reading]));
                i += length;
            }
            if (unknown.Length > 0) clauses.Add(new ConversionClause(unknown, unknown));
            return clauses;
        }
    }

    private static UserDictionary Dictionary(params (string Reading, string Word)[] words)
    {
        var dictionary = new UserDictionary(null, builtIn: false);
        foreach (var (reading, word) in words) dictionary.Add(reading, word);
        return dictionary;
    }

    private static string Clauses(CompositionTests.Keyboard k) => string.Join("|", k.Host.View!.Clauses!);

    // 本家 PR #385 のテスト (Mac 版の Keyboard に合わせて移したもの)。
    private sealed class WatashiConverter : IKanjiConverter
    {
        public string? Convert(string hiragana) => ConvertClauses(hiragana) is { } clauses ? string.Concat(clauses.Select(c => c.Text)) : null;
        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) => hiragana switch
        {
            "わたしのことがすき" => [new("わたしの", "私の"), new("ことが", "事が"), new("すき", "好き")],
            "わた" => [new("わた", "綿")],
            "ことがすき" => [new("ことが", "事が"), new("すき", "好き")],
            "しのがきた" => [new("しの", "篠"), new("が", "が"), new("きた", "来た")],
            "がきた" => [new("が", "が"), new("きた", "来た")],
            _ => null,
        };
    }

    [Test]
    public static void UserDictionary_WordInsideAnEngineClause_IsNotSplitOut()
    {
        // #374: しの → 紫乃 を登録すると、わたしのことがすき が 綿紫乃事が好き になっていた。
        // 変換エンジンの文節 (わたしの) の途中から始まる語では区切らない。文節の頭から始まるなら今までどおり使う
        var dictionary = new UserDictionary(null, builtIn: false);
        dictionary.Add("しの", "紫乃");
        var k = new CompositionTests.Keyboard(converter: new WatashiConverter(), userDictionary: dictionary);
        k.Type("watashinokotogasuki ");
        Assert.Equal("私の事が好き", string.Concat(k.Host.View!.Clauses!));

        k = new CompositionTests.Keyboard(converter: new WatashiConverter(), userDictionary: dictionary);
        k.Type("shinogakita ");
        Assert.Equal("紫乃", k.Host.View!.Clauses![0], "文節の頭から始まる登録した語は使う");

        k = new CompositionTests.Keyboard(live: true, converter: new WatashiConverter(), userDictionary: dictionary);
        k.Type("watashinokotogasuki");
        Assert.True(!k.Showing!.Contains("紫乃"), "ライブ変換でも割り込まない: " + k.Showing);
    }

    [Test]
    public static void UserDictionary_UnusedWord_CanStillBePickedByResizingClauses()
    {
        var k = new CompositionTests.Keyboard(converter: new LexiconConverter(), userDictionary: Dictionary(("しの", "紫乃")));
        k.Type("watashinokotogasuki ");
        Assert.Equal("私の|事が|好き", Clauses(k), "前提: 割り込まない");
        // Shift+← で わたしの を わた|しのことが にして、しのことが を しの|ことがすき まで縮める
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Left);
        k.Press(VirtualKeys.Left);
        k.Key(VirtualKeys.LShift, up: true);
        k.Press(VirtualKeys.Right);
        k.Key(VirtualKeys.LShift);
        for (var i = 0; i < 3; i++) k.Press(VirtualKeys.Left);
        k.Key(VirtualKeys.LShift, up: true);
        var view = k.Host.View!;
        Assert.True(view.SelectedClause >= 0 && view.Clauses![view.SelectedClause] != null, "文節を選んでいる");
        Assert.True(view.Candidates.Contains("紫乃"), "区切りを変えれば、登録した語を候補から選べる: " + string.Join(",", view.Candidates) + " / " + Clauses(k));
        // 候補の中から選んで確定できる
        var index = view.Candidates.ToList().IndexOf("紫乃");
        for (var i = view.SelectedIndex; i < index; i++) k.Press(VirtualKeys.Down);
        k.Type("\n");
        Assert.True(k.Host.Output.Single().Contains("紫乃"), "確定した文に 紫乃 が入る: " + k.Host.Output.Single());
    }

    [Test]
    public static void UserDictionary_WordAtTheHead_AndWholeReading_KeepWorking()
    {
        var dictionary = Dictionary(("しの", "紫乃"), ("わたし", "私"));
        // (a) 語だけを打つ
        var k = new CompositionTests.Keyboard(converter: new LexiconConverter(), userDictionary: dictionary);
        k.Type("shino ");
        Assert.Equal("紫乃", Clauses(k), "語だけ");
        // (b) 語 + 助詞
        k = new CompositionTests.Keyboard(converter: new LexiconConverter(), userDictionary: dictionary);
        k.Type("shinoga ");
        Assert.Equal("紫乃|が", Clauses(k), "語 + 助詞");
        // (c) 変換エンジンが辞書の語と同じ区切りで返す文
        k = new CompositionTests.Keyboard(converter: new LexiconConverter(), userDictionary: dictionary);
        k.Type("shinogakita ");
        Assert.Equal("紫乃|が|来た", Clauses(k), "エンジンと区切りが同じ文");
        // (d) 語が複数ある文: 文節の途中から始まる しの は使わず、文節の頭から始まる ほし は使う
        dictionary = Dictionary(("しの", "紫乃"), ("ほし", "星野"));
        k = new CompositionTests.Keyboard(converter: new LexiconConverter(), userDictionary: dictionary);
        k.Type("kotoshinohoshiga ");
        Assert.Equal("今年の|星野|が", Clauses(k), "語が複数: 途中の しの は使わず、頭の ほし は使う");
        // 語が複数あって、どちらも文節の頭 (わたしの の しの は途中、2 つ目の しの は文節の頭)
        dictionary = Dictionary(("しの", "紫乃"));
        k = new CompositionTests.Keyboard(converter: new LexiconConverter(), userDictionary: dictionary);
        k.Type("watashinoshinogakita ");
        Assert.Equal("私の|紫乃|が|来た", Clauses(k), "途中の しの は使わず、文節の頭の しの は使う");
    }

    [Test]
    public static void BuiltInPhrases_AndBundledForcedTerms_AreStillUsedInsideAnEngineClause()
    {
        // 同梱の語句 しょせん → 所詮 は、変換エンジンが あしょ|せんだ と区切る途中からでも使う (従来どおり)
        var dictionary = new UserDictionary(null);
        Assert.True(!dictionary.IsUserReading("しょせん"), "同梱の語句は、ユーザーの語ではない");
        var converter = new LexiconConverter();
        var k = new CompositionTests.Keyboard(converter: converter, userDictionary: dictionary);
        k.Type("ashosenda ");
        Assert.True(string.Concat(k.Host.View!.Clauses!).Contains("所詮"), "同梱の語句は文節の途中でも使う: " + Clauses(k));
        Assert.True(!converter.Calls.Any(c => c.StartsWith("あしょせんだ|", StringComparison.Ordinal)), "同梱の語句は、文節の頭の判定のための変換エンジンの呼び出しをしない: " + string.Join(",", converter.Calls));

        // 同梱の専門用語集の強制語 (ここでは強制型の ぎょうれつしき) も同じ
        var terms = new UserDictionary(null, builtIn: false);
        terms.LoadTerms(["ぎょうれつしき\t行列式\t\t強制\n"]);
        Assert.True(!terms.IsUserReading("ぎょうれつしき"), "同梱の専門用語集の語は、ユーザーの語ではない");
        converter = new LexiconConverter();
        k = new CompositionTests.Keyboard(converter: converter, userDictionary: terms);
        k.Type("agyouretsushiki ");
        Assert.True(Clauses(k).EndsWith("|行列式"), "同梱の専門用語集の強制語は文節の途中でも使う: " + Clauses(k));
        Assert.True(!converter.Calls.Any(c => c.StartsWith("あぎょうれつしき|", StringComparison.Ordinal)), "同梱の専門用語集の強制語も、判定のための呼び出しをしない: " + string.Join(",", converter.Calls));
    }

    [Test]
    public static void IsUserReading_UserWordsAndOwnTerms_AreUser_BundledAreNot()
    {
        var dictionary = new UserDictionary(null);
        dictionary.Add("しの", "紫乃");
        Assert.True(dictionary.IsUserReading("しの"), "ユーザー辞書の語");
        Assert.True(!dictionary.IsUserReading("おうじさま"), "同梱の語句 (phrases.txt)");
        dictionary.Add("おうじさま", "王子さま");
        Assert.True(dictionary.IsUserReading("おうじさま"), "同梱の語句と同じ読みでも、ユーザーが登録すればユーザーの語 (Split もそちらを先に使う)");
        Assert.True(!dictionary.IsUserReading("ありません"), "どこにもない読み");
    }

    [Test]
    public static void NoExtraEngineCalls_WhenNoUserWordIsInsideTheReading()
    {
        // 余分な変換エンジンの呼び出しが増えるのは、先頭以外から始まるユーザーの語が入っているときだけ
        const string typed = "watashinokotogasuki";
        int Calls(UserDictionary? dictionary, string text)
        {
            var converter = new LexiconConverter();
            var k = new CompositionTests.Keyboard(live: true, converter: converter, userDictionary: dictionary);
            k.Type(text);
            return converter.Calls.Count;
        }
        var none = Calls(null, typed);
        Assert.Equal(none, Calls(Dictionary(("ぜんぜん", "全然")), typed), "辞書の語が入らない読みは、辞書なしと同じ回数");
        // 語が読みの先頭にあるだけなら、判定のための呼び出しは無い (先頭は必ず文節の頭)。13 は修正前に測った回数で、修正後も増えない
        Assert.Equal(13, Calls(Dictionary(("わたし", "私")), typed), "先頭の語だけなら、修正前と同じ回数");
        // 途中に語が入るときは判定のために全体を変換するが、使わない語で区切らなくなるので、残りの変換が減って全体は増えない (修正前は 13)
        Assert.True(Calls(Dictionary(("しの", "紫乃")), typed) <= 13, "途中の語は、修正前より多くは呼ばない");
    }

    // ---- 変換エンジンが失敗したとき (ConvertClauses が null) は、登録語を落とさない ----

    /// <summary>文節に区切れない変換エンジン。Mac では、azooKey が空を返すと NativeCore が nil を返し、Core には null として届く。</summary>
    private sealed class FailingConverter(string kind) : IKanjiConverter
    {
        public string? Convert(string hiragana) => kind == "convert-only" ? "変換:" + hiragana : null;
        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) => kind == "empty" ? [] : null;
    }

    [Test]
    public static void FailingEngine_KeepsEveryUserWord_LikeBeforeTheAlignment()
    {
        // 失敗した変換は「読み全体が 1 文節」として返るので、文節の頭を位置 0 だけと読むと、先頭以外の登録語が全部落ちる
        // (きょうしのがきた、わたしのことがすき が登録語なしの変換になる)。失敗なら、判定せずに登録語を使う (本家も engine が null なら元に戻す)。
        foreach (var kind in new[] { "null", "convert-only" })
        {
            var k = new CompositionTests.Keyboard(converter: new FailingConverter(kind), userDictionary: Dictionary(("しの", "紫乃")));
            k.Type("kyoushinogakita ");
            Assert.Equal(kind == "null" ? "きょう|紫乃|がきた" : "変換:きょう|紫乃|変換:がきた", Clauses(k), $"{kind}: 変換エンジンが失敗しても、登録語は使う");
            k = new CompositionTests.Keyboard(converter: new FailingConverter(kind), userDictionary: Dictionary(("しの", "紫乃")));
            k.Type("watashinokotogasuki ");
            Assert.Equal(kind == "null" ? "わた|紫乃|ことがすき" : "変換:わた|紫乃|変換:ことがすき", Clauses(k), $"{kind}: 変換エンジンが失敗しても、登録語は使う");
        }
        // ライブ変換でも同じ
        var live = new CompositionTests.Keyboard(live: true, converter: new FailingConverter("null"), userDictionary: Dictionary(("しの", "紫乃")));
        live.Type("kyoushinogakita");
        Assert.True(live.Showing!.Contains("紫乃"), "ライブ変換でも登録語は使う: " + live.Showing);
    }

    [Test]
    public static void FailingEngine_InALongReading_KeepsTheUserWord()
    {
        // 50 文字を超える読みは区切って変換する (ConvertWithEngine)。区切りの変換が失敗しても同じ
        Assert.True(60 > CompositionController.ConvertChunkLength, "前提: 読みは区切って変換する長さ");
        var k = new CompositionTests.Keyboard(converter: new FailingConverter("null"), userDictionary: Dictionary(("しの", "紫乃")));
        k.Type(new string('a', 60) + "shinoga ");
        Assert.True(Clauses(k).Contains("紫乃"), "長い読みでも、失敗なら登録語は使う: " + Clauses(k));
    }

    [Test]
    public static void EmptyEngineResult_DoesNotDropUserWords()
    {
        // 文節が 0 個 (空のリスト) の結果も、文節の頭を求める手がかりにならない。登録語は落とさない
        var k = new CompositionTests.Keyboard(converter: new FailingConverter("empty"), userDictionary: Dictionary(("しの", "紫乃")));
        k.Type("kyoushinogakita ");
        Assert.True(Clauses(k).Contains("紫乃"), "空の結果でも登録語は使う: " + Clauses(k));
    }

    /// <summary>Fail の間だけ失敗する変換エンジン。</summary>
    private sealed class FlakyConverter : IKanjiConverter
    {
        private readonly LexiconConverter _inner = new();
        public bool Fail;
        public string? Convert(string hiragana) => Fail ? null : _inner.Convert(hiragana);
        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) => Fail ? null : _inner.ConvertClauses(hiragana, context);
    }

    [Test]
    public static void FailedEngineResult_IsNotRemembered_SoALaterKeyUsesTheAlignment()
    {
        // 一度失敗しても、次のキーでエンジンが戻れば、文節の頭の判定を使う (失敗を覚え込まない)
        var converter = new FlakyConverter();
        var k = new CompositionTests.Keyboard(live: true, converter: converter, userDictionary: Dictionary(("しの", "紫乃")));
        converter.Fail = true;
        k.Type("watashinokotogasu");
        Assert.True(k.Showing!.Contains("紫乃"), "失敗中は登録語を使う: " + k.Showing);
        converter.Fail = false;
        k.Type("ki");
        Assert.True(!k.Showing!.Contains("紫乃"), "エンジンが戻れば、文節の途中の語は使わない: " + k.Showing);
    }

    // ---- 長い語は、文節の途中から始まっても使う (しきい値 CompositionController.InsideClauseMinReadingLength) ----

    /// <summary>
    /// 実際の azooKey (d59a28e・学習なし・MeltypeIME --serve) が返した文節を記録したもの (P30b の比較で使った文のうちの数文と、本家の問題例)。
    /// 記録に無い読みは null (変換できない) を返す。呼ばれた読みを Calls に残す。
    /// </summary>
    private sealed class RecordedAzooConverter : IKanjiConverter
    {
        private static readonly Dictionary<string, (string Reading, string Text)[]> Recorded = new()
        {
            ["きょうはめるたいぷをつかう"] = [("きょう", "今日"), ("はめる", "ハメる"), ("たいぷ", "タイプ"), ("をつかう", "を使う")],
            ["きょうは"] = [("きょうは", "今日は")],
            ["をつかう"] = [("をつかう", "を使う")],
            ["おとうとのさくらです"] = [("おとうと", "弟"), ("のさくらです", "の桜です")],
            ["おとうとの"] = [("おとうとの", "弟の")],
            ["です"] = [("です", "です")],
            ["きょうはひなとかいものにいく"] = [("きょうはひなと", "今日はひなと"), ("かいもの", "買い物"), ("にいく", "に行く")],
            ["わたしのことがすき"] = [("わたし", "私"), ("のことがすき", "のことが好き")],
        };

        public List<string> Calls { get; } = [];

        public string? Convert(string hiragana) => ConvertClauses(hiragana) is { } c ? string.Concat(c.Select(x => x.Text)) : null;

        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null)
        {
            Calls.Add(hiragana);
            return Recorded.TryGetValue(hiragana, out var clauses) ? clauses.Select(c => new ConversionClause(c.Reading, c.Text)).ToList() : null;
        }
    }

    [Test]
    public static void LongUserWord_AfterAParticle_IsUsedEvenInsideAnEngineClause()
    {
        // 実際の azooKey は きょう|はめる|たいぷ|をつかう と区切る。めるたいぷ は文節 はめる の途中から始まるが、長い語なので使う
        // (文節の頭かどうかだけで決めると、助詞の後ろの登録語が 今日|ハメる|タイプ|を使う のように落ちる)
        var converter = new RecordedAzooConverter();
        var k = new CompositionTests.Keyboard(converter: converter, userDictionary: Dictionary(("めるたいぷ", "Meltype")));
        k.Type("kyouhamerutaipuwotsukau ");
        Assert.Equal("今日は|Meltype|を使う", Clauses(k));
        Assert.True(!converter.Calls.Contains("きょうはめるたいぷをつかう"), "長い語だけなら、文節の頭を調べるための全体の変換はしない: " + string.Join(",", converter.Calls));

        k = new CompositionTests.Keyboard(converter: new RecordedAzooConverter(), userDictionary: Dictionary(("さくら", "桜良")));
        k.Type("otoutonosakuradesu ");
        Assert.Equal("弟の|桜良|です", Clauses(k), "3 文字の語も、文節 のさくらです の途中から始まっても使う");
    }

    [Test]
    public static void ShortUserWord_InsideAnEngineClause_IsNotUsed_WithRecordedAzooKey()
    {
        // 本家 PR の問題例を、実際の azooKey の区切りで確かめる。azooKey は わたし|のことがすき と区切るので、しの は文節をまたぐ
        var k = new CompositionTests.Keyboard(converter: new RecordedAzooConverter(), userDictionary: Dictionary(("しの", "紫乃")));
        k.Type("watashinokotogasuki ");
        Assert.Equal("私|のことが好き", Clauses(k), "しの (2 文字) は文節をまたぐので使わない");
        // 2 文字の語は、助詞の後ろで文節の途中になるときも使わない (この版の割り切り。使いたいときは文節の区切りを Shift+←→ で変える)
        var converter = new RecordedAzooConverter();
        k = new CompositionTests.Keyboard(converter: converter, userDictionary: Dictionary(("ひな", "陽菜")));
        k.Type("kyouhahinatokaimononiiku ");
        Assert.Equal("今日はひなと|買い物|に行く", Clauses(k), "ひな (2 文字) は文節の途中なので使わない");
        Assert.Equal(1, converter.Calls.Count(c => c == "きょうはひなとかいものにいく"), "文節の頭を調べる全体の変換は 1 回だけ");
    }

    [Test]
    public static void MinReadingLength_IsTheBoundaryBetweenUsedAndNotUsed()
    {
        // しきい値の長さちょうどの語は使い、1 文字短い語は使わない (どちらも わたしの の末尾で、文節 わたしの の途中から始まる語)
        var n = CompositionController.InsideClauseMinReadingLength;
        Assert.True(n is >= 2 and <= 3, "このテストは わたしの の末尾の語を使う (しきい値が 4 以上なら作り直す): " + n);
        string Convert(string reading)
        {
            var k = new CompositionTests.Keyboard(converter: new LexiconConverter(), userDictionary: Dictionary((reading, "★")));
            k.Type("watashinokotogasuki ");
            return Clauses(k);
        }
        var atThreshold = "わたしの"[^n..];
        var below = "わたしの"[^(n - 1)..];
        Assert.True(Convert(atThreshold).Contains('★'), $"{atThreshold} ({n} 文字) は使う: " + Convert(atThreshold));
        Assert.True(!Convert(below).Contains('★'), $"{below} ({n - 1} 文字) は使わない: " + Convert(below));
    }

    /// <summary>おとうと|のさくらとしのです と区切る変換エンジン (他の読みは区切らずそのまま返す)。短い語と長い語の両方が同じ文節の途中にある形。</summary>
    private sealed class OneBigClauseConverter : IKanjiConverter
    {
        private const string Full = "おとうとのさくらとしのです";
        public string? Convert(string hiragana) => ConvertClauses(hiragana) is { } c ? string.Concat(c.Select(x => x.Text)) : null;
        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) =>
            hiragana == Full
                ? [new("おとうと", "弟"), new("のさくらとしのです", "の桜と詩のです")]
                : [new(hiragana, hiragana)];
    }

    [Test]
    public static void LongAndShortUserWords_InTheSameClause_OnlyTheLongOneIsUsed()
    {
        // さくら (3 文字) と しの (2 文字) が、どちらも文節 のさくらとしのです の途中にある。長い方だけ使う
        // (しきい値の判定を外す・厳しくする変更で、どちらかが逆になって赤になる)
        var k = new CompositionTests.Keyboard(converter: new OneBigClauseConverter(), userDictionary: Dictionary(("さくら", "桜良"), ("しの", "紫乃")));
        k.Type("otoutonosakuratoshinodesu ");
        var clauses = Clauses(k);
        Assert.True(clauses.Contains("桜良"), "3 文字の さくら は使う: " + clauses);
        Assert.True(!clauses.Contains("紫乃"), "2 文字の しの は文節の途中なので使わない: " + clauses);
    }

    [Test]
    public static void ShortUserWordAtTheHead_DoesNotNeedTheWholeReadingConverted()
    {
        // 文節の頭を調べるための全体の変換は、先頭以外にしきい値より短いユーザーの語があるときだけ。先頭の短い語は必ず文節の頭なので呼ばない
        var converter = new LexiconConverter();
        var k = new CompositionTests.Keyboard(converter: converter, userDictionary: Dictionary(("しの", "紫乃")));
        k.Type("shinogakita ");
        Assert.Equal("紫乃|が|来た", Clauses(k));
        Assert.True(!converter.Calls.Any(c => c.StartsWith("しのがきた|", StringComparison.Ordinal)), "先頭の語だけなら全体を変換しない: " + string.Join(",", converter.Calls));
    }

    // ---- 自作の専門用語集 (ユーザー辞書の語と同じ扱い) ----

    [Test]
    public static void OwnTermDomain_IsTreatedLikeUserDictionary()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var previousConfig = TermDomains.ConfigPath;
        var previousExclusion = TermDomains.ExclusionPath;
        var previousSource = TermDomains.Source;
        var previousUser = TermDomains.UserDirectory;
        var previousInterval = TermDomains.ExternalCheckIntervalMs;
        var previousFileInterval = UserDictionary.FileCheckIntervalMs;
        TermDomains.ConfigPath = () => Path.Combine(directory, "config.json");
        TermDomains.ExclusionPath = () => Path.Combine(directory, "terms-excluded.txt");
        TermDomains.UserDirectory = () => Path.Combine(directory, "terms");
        TermDomains.Source = () => [];
        TermDomains.ExternalCheckIntervalMs = 0;
        UserDictionary.FileCheckIntervalMs = 0;
        TermDomains.Reset();
        try
        {
            var source = new UserDictionary(Path.Combine(directory, "userdict.txt"), builtIn: false);
            source.Add("しの", "紫乃");
            Assert.Equal<string?>(null, TermDomains.CreateUserDomain("自作", out var id));
            Assert.Equal<string?>(null, TermDomains.MoveFromUserDictionary(source, id, [.. source.Words], out _));
            Assert.Equal(0, source.Count, "ユーザー辞書は空になった");
            var after = new UserDictionary(null);
            Assert.True(after.IsUserReading("しの"), "自作の専門用語集の語は、ユーザーの語");
            foreach (var typed in new[] { "watashinokotogasuki ", "shinogakita " })
            {
                var before = new CompositionTests.Keyboard(converter: new LexiconConverter(), userDictionary: Dictionary(("しの", "紫乃")));
                before.Type(typed);
                var moved = new CompositionTests.Keyboard(converter: new LexiconConverter(), userDictionary: after);
                moved.Type(typed);
                Assert.Equal(Clauses(before), Clauses(moved), $"語を移しても「{typed}」の変換は同じ");
            }
            var k = new CompositionTests.Keyboard(converter: new LexiconConverter(), userDictionary: after);
            k.Type("watashinokotogasuki ");
            Assert.Equal("私の|事が|好き", Clauses(k), "自作の専門用語集の語も、文節の途中には割り込まない");
        }
        finally
        {
            TermDomains.ConfigPath = previousConfig;
            TermDomains.ExclusionPath = previousExclusion;
            TermDomains.Source = previousSource;
            TermDomains.UserDirectory = previousUser;
            TermDomains.ExternalCheckIntervalMs = previousInterval;
            UserDictionary.FileCheckIntervalMs = previousFileInterval;
            TermDomains.Reset();
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }
}
