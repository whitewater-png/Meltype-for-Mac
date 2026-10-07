// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Diagnostics;
using System.Text;
using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>専門用語集 (dictionaries/terms-*.txt) の読み込み・強制型 / 候補追加型の振り分け・優先順位・予測・規模。</summary>
internal static class TermDictionaryTests
{
    private const string Sample =
        "# 出典: テスト\n# ライセンス: 自作\n\n" +
        "ぎょうれつしき\t行列式\t数学\n" +   // 7 文字: 強制型
        "すう\t数\t読みが短い: 候補追加型\n" + // 2 文字: 候補追加型
        "かいほうてい\t開放定理\n" +          // 6 文字: 強制型
        "ギョウレツ\t行列\n";                 // カタカナの読み (ひらがなにそろえる)。4 文字: 強制型

    private static UserDictionary WithTerms(string text, string? path = null)
    {
        var dictionary = new UserDictionary(path, builtIn: false);
        dictionary.LoadTerms([text]);
        return dictionary;
    }

    [Test]
    public static void Terms_ClassifiedByReadingLength()
    {
        var terms = TermDictionary.Parse([Sample]);
        Assert.Equal(3, terms.ForcedCount, "4 文字以上は強制型");
        Assert.Equal(1, terms.CandidateCount, "3 文字以下は候補追加型");
        Assert.Equal(4, TermDictionary.ForcedMinReadingLength, "しきい値は 4");
        Assert.Equal("行列", terms.LookupForced("ぎょうれつ")[0], "カタカナの読みはひらがなにそろえる");
        var three = TermDictionary.Parse(["ほげほ\t語A\nほげほげ\t語B\n"]);
        Assert.Equal(1, three.CandidateCount, "3 文字ちょうどは候補追加型");
        Assert.Equal(1, three.ForcedCount, "4 文字ちょうどは強制型");
    }

    [Test]
    public static void Terms_AsciiWords_AreCandidatesOnly()
    {
        // 語が ASCII だけの語は、読みが 4 文字以上でも強制しない (「あいこんをくりっく」が「icon|を|click」にならない)
        var text = "あいこん\ticon\nくりっく\tclick\nぎょうれつしき\t行列式\nえーぴーあい\tAPI v2.0\nまざった\tweb3 技術\n";
        var terms = TermDictionary.Parse([text]);
        Assert.Equal(2, terms.ForcedCount, "強制型は日本語を含む語だけ (行列式・web3 技術)");
        Assert.Equal(3, terms.CandidateCount, "ASCII だけの語は候補追加型");
        var dictionary = WithTerms(text);
        Assert.True(dictionary.Split("あいこんをくりっく") is null, "強制しない");
        Assert.Equal(0, dictionary.Lookup("あいこん").Count, "強制型の Lookup にも出ない");
        Assert.True(dictionary.LookupTermCandidates("あいこん").Contains("icon"), "候補には出る");
        Assert.True(dictionary.LookupTermCandidates("あいこんを").Contains("iconを"), "助詞付きでも候補に出る");
        Assert.True(dictionary.PredictTerms("くりっ").Contains("click"), "予測にも出る");
        Assert.True(dictionary.Split("ぎょうれつしきをもとめる") is not null, "日本語を含む語は今までどおり強制");
        Assert.True(TermDictionary.IsAscii("API v2.0") && !TermDictionary.IsAscii("web3 技術") && !TermDictionary.IsAscii("Ａ"), "ASCII の判定");
    }

    [Test]
    public static void Terms_InvalidLinesAreSkipped()
    {
        var text = string.Join("\n",
            "# コメント", "", "   ",
            "あ\t一文字の読み",           // 読みが短すぎる
            "ただのぶんしょう",             // タブが無い
            "ぶんしょう\t",                // 語が空
            "ながいよみ\t" + new string('語', 101), // 語が長すぎる
            new string('あ', 101) + "\t語",        // 読みが長すぎる
            "せいじょう\t正常\t注記",
            "ぜんかくＡＢ\t全角英数");
        var terms = TermDictionary.Parse([text]);
        Assert.Equal(2, terms.Count, "正常な 2 行だけ読む");
        Assert.Equal(5, terms.Skipped, "不正な 5 行は飛ばす (例外にしない)");
        Assert.Equal("全角英数", terms.LookupForced("ぜんかくab")[0], "全角英数は半角にそろえる");
        Assert.Equal(0, TermDictionary.Parse([]).Count, "テキストが無くても空");
    }

    [Test]
    public static void Terms_DuplicatesAndFileOrder()
    {
        var terms = TermDictionary.Parse(["ふくすう\t語1\nふくすう\t語2\nふくすう\t語1\n", "ふくすう\t語3\n"]);
        Assert.Equal("語1,語2,語3", string.Join(",", terms.LookupForced("ふくすう")), "同じ読み・同じ語は 1 つ。順はファイル順");
    }

    [Test]
    public static void Terms_ForcedSplitsLikeBuiltIn()
    {
        var dictionary = WithTerms(Sample);
        var pieces = dictionary.Split("ぎょうれつしきをもとめる");
        Assert.True(pieces is not null && pieces[0] == ("ぎょうれつしき", "行列式") && pieces[1] == ("をもとめる", null), "強制型は読みの中の部分を語にする");
        Assert.True(dictionary.Split("ぎょうれつ")![0].Word == "行列", "最長一致 (ぎょうれつしき が無ければ ぎょうれつ)");
        Assert.True(dictionary.Split("すうがく") is null, "候補追加型は強制しない (Split に出ない)");
        Assert.True(dictionary.LookupTermCandidates("すう").Contains("数"), "候補追加型は候補に出る");
        Assert.True(dictionary.LookupTermCandidates("すうを").Contains("数を"), "助詞付きでも候補に出る");

        var k = new CompositionTests.Keyboard(userDictionary: dictionary);
        k.Type("gyouretsushikiwomotomeru ");
        Assert.Equal("行列式", k.Host.View!.Clauses![0], "変換で強制される");
        k = new CompositionTests.Keyboard(userDictionary: dictionary);
        k.Type("suu ");
        Assert.True(k.Host.View!.Candidates.Contains("数"), "読みの短い語は変換候補に加わる: " + string.Join(",", k.Host.View.Candidates));
    }

    [Test]
    public static void Terms_UserDictionaryWins()
    {
        var dictionary = new UserDictionary(null, builtIn: false);
        dictionary.LoadTerms(["ぎょうれつしき\t専門の行列式\n"]);
        dictionary.Add("ぎょうれつしき", "自分の行列式");
        Assert.Equal("自分の行列式", dictionary.Split("ぎょうれつしき")![0].Word, "同じ読みはユーザー辞書が先");
        Assert.Equal("自分の行列式,専門の行列式", string.Join(",", dictionary.Lookup("ぎょうれつしき")), "候補はユーザー辞書 → 専門用語集");
        var k = new CompositionTests.Keyboard(userDictionary: dictionary);
        k.Type("gyouretsushiki ");
        Assert.Equal("自分の行列式", k.Host.View!.Clauses![0], "変換でもユーザー辞書が先");
        // 長い読みは専門用語集が勝つ (最長一致)
        dictionary.LoadTerms(["ぎょうれつしきのてんかい\t行列式の展開\n"]);
        Assert.Equal("行列式の展開", dictionary.Split("ぎょうれつしきのてんかい")![0].Word, "より長い専門用語は、短いユーザー辞書の語より先 (最長一致)");
    }

    [Test]
    public static void Terms_NotSavedOrListed()
    {
        var path = Path.Combine(Path.GetTempPath(), "meltype-terms-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            var dictionary = WithTerms(Sample, path);
            Assert.Equal(0, dictionary.Count, "ユーザー辞書の語数に入らない");
            Assert.Equal(0, dictionary.Words.Count, "表示・書き出し (Words) に入らない");
            Assert.True(dictionary.TermCount == 4, "専門用語は別に数える");
            dictionary.Add("ゆーざーご", "ユーザー語");
            var saved = File.ReadAllText(path);
            Assert.True(saved.Contains("ユーザー語") && !saved.Contains("行列式") && !saved.Contains("開放定理"), "保存したファイルに専門用語は混ざらない");
            Assert.True(!Encoding.Unicode.GetString(UserDictionaryFile.Export(dictionary.Words)).Contains("行列式"), "書き出しにも混ざらない");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public static void Terms_AppearInPredictions()
    {
        var dictionary = WithTerms("ぎょうれつしき\t行列式\nぎょうざ\t餃子\nぎょうれつ\t行列\n");
        dictionary.Add("ぎょうむ", "業務");
        var k = new CompositionTests.Keyboard(userDictionary: dictionary, predictions: r => ["行列式", "ぎょう変換"]);
        k.Type("gyou");
        var predictions = k.Host.View!.Predictions!.ToList();
        Assert.True(predictions.Contains("行列式") && predictions.Contains("餃子") && predictions.Contains("行列"), "専門用語 (候補追加型を含む) が予測に出る: " + string.Join(",", predictions));
        Assert.Equal("業務", predictions[0], "ユーザー辞書が先");
        Assert.True(predictions.IndexOf("行列式") < predictions.IndexOf("ぎょう変換"), "専門用語集はエンジンより先");
        Assert.Equal(1, predictions.Count(p => p == "行列式"), "重複は除く");
    }

    [Test]
    public static void Terms_EmptyMeansSameAsBefore()
    {
        var plain = new UserDictionary(null, builtIn: false);
        var loaded = WithTerms("# 空\n");
        Assert.True(plain.Split("ぎょうれつしき") is null && loaded.Split("ぎょうれつしき") is null, "語が 0 件なら何も起きない");
        Assert.Equal(0, loaded.Lookup("ぎょうれつしき").Count, "Lookup も空");
        Assert.Equal(0, loaded.PredictTerms("ぎょう").Count(), "予測も空");
        // 組み込み語句 (phrases.txt) は今までどおり
        var builtIn = new UserDictionary(null);
        Assert.True(builtIn.Count == 0 && builtIn.Lookup("しょせん").Contains("所詮"), "phrases.txt の語句は変わらない");
    }

    [Test]
    public static void Terms_TemplateReadsAsEmpty()
    {
        var text = Detection.DictionarySource.ReadEmbedded("terms-template.txt");
        var terms = TermDictionary.Parse([text]);
        Assert.Equal(0, terms.Count, "テンプレートは語を含まない");
        Assert.Equal(0, terms.Skipped, "テンプレートの行はすべてコメント");
        Assert.True(Detection.DictionarySource.ListEmbedded("terms-").Contains("terms-template.txt"), "terms-*.txt の一覧を取れる (ファイルを足すだけで分野に出る)");
        Assert.True(Detection.DictionarySource.ReadEmbeddedWithPrefix("terms-").Any(), "terms-*.txt が埋め込まれている");
    }

    /// <summary>合成の 5 万語。読みは 3〜12 文字 (短い読みは候補追加型)、同じ読みが複数の語を持つものも混ぜる。</summary>
    private static string Synthetic(int count)
    {
        const string kana = "あいうえおかきくけこさしすせそたちつてとなにぬねのはひふへほまみむめもやゆよらりるれろわ";
        var random = new Random(12345);
        var builder = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            var length = 3 + random.Next(10);
            var reading = new char[length];
            for (var j = 0; j < length; j++) reading[j] = kana[random.Next(kana.Length)];
            builder.Append(new string(reading)).Append('\t').Append("語").Append(i).Append("\t注記\n");
        }
        return builder.ToString();
    }

    [Test]
    public static void Terms_FiftyThousandWords_StayFast()
    {
        var text = Synthetic(50000);
        GC.Collect();
        var before = GC.GetTotalMemory(true);
        var stopwatch = Stopwatch.StartNew();
        var dictionary = new UserDictionary(null, builtIn: false);
        dictionary.LoadTerms([text]);
        var load = stopwatch.ElapsedMilliseconds;
        var after = GC.GetTotalMemory(true);
        GC.KeepAlive(dictionary);
        Assert.True(dictionary.TermCount >= 49000, "5 万行 (同じ読み・語の重複を除く) を読めた: " + dictionary.TermCount);
        Assert.True(load < 3000, $"5 万語の読み込みと索引作成が遅すぎる: {load} ms");
        Assert.True(after - before < 100L * 1024 * 1024, $"5 万語のメモリが大きすぎる: {(after - before) / 1024 / 1024} MB");

        // 1 キーごとの処理 (Split・Lookup・予測) を 1000 回ずつ
        var sentence = string.Concat(Enumerable.Repeat("きょうはてんきがよいのでさんぽにいきます", 2)); // 38 文字
        var empty = new UserDictionary(null, builtIn: false);
        long Time(Action action)
        {
            stopwatch.Restart();
            for (var i = 0; i < 1000; i++) action();
            return stopwatch.ElapsedTicks * 1000000 / Stopwatch.Frequency / 1000; // 1 回あたりのマイクロ秒
        }
        var split = Time(() => dictionary.Split(sentence));
        var splitEmpty = Time(() => empty.Split(sentence));
        var predict = Time(() => dictionary.PredictTerms("あ").ToList());
        var lookup = Time(() => { dictionary.Lookup("あいうえお"); dictionary.LookupTermCandidates("あいう"); });
        Console.WriteLine($"  [規模] 5 万語 読み込み {load} ms / メモリ増 {(after - before) / 1024.0 / 1024:F1} MB / Split {split} µs (語 0 件 {splitEmpty} µs) / 予測 {predict} µs / Lookup {lookup} µs");
        // 実際のキー入力 (ライブ変換 + 予測) で、1 キーあたりの平均時間を語 0 件と比べる
        double PerKey(UserDictionary d)
        {
            var keys = "kyouhatenkigayoinodesanponiikimasu";
            var sw = Stopwatch.StartNew();
            for (var round = 0; round < 20; round++)
            {
                var k = new CompositionTests.Keyboard(live: true, userDictionary: d);
                k.Type(keys);
            }
            return sw.Elapsed.TotalMilliseconds / (20 * keys.Length);
        }
        PerKey(dictionary); // 暖機
        var keyTerms = PerKey(dictionary);
        var keyEmpty = PerKey(empty);
        Console.WriteLine($"  [規模] 1 キー (ライブ変換 + 予測) 平均 5 万語 {keyTerms:F3} ms / 語 0 件 {keyEmpty:F3} ms");
        Assert.True(keyTerms < keyEmpty + 5, "5 万語でも 1 キーの処理が数 ms 以上増えない");
        Assert.True(split < 2000 && predict < 2000 && lookup < 2000, "1 キーごとの処理が遅い");
    }
}
