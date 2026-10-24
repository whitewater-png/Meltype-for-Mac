// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Detection;

namespace Meltype.Tests;

/// <summary>
/// 本家 #1021 (AZIK) の「仕組み」: カスタムローマ字表の非英字キー (; : [ ] @ - , . / など) と、英語判定との衝突 (C1)。
/// 使う表は自作の小さなサンプル (docs/samples/romaji-sample.tsv)。AZIK の定義ではない。
/// 絶対条件: 表が無い・OFF・記号入りのキーが無い・キーに届かない記号は、従来と完全に同じ。
/// </summary>
internal static class AzikSymbolKeyTests
{
    private static void WithTable(string? tsv, Action body)
    {
        var romaji = CompositionTests.Detector.Romaji;
        var previous = romaji.CustomTable;
        var table = tsv is null ? null : RomajiTable.Parse(tsv);
        romaji.CustomTable = () => table;
        try { body(); }
        finally { romaji.CustomTable = previous; }
    }

    private static string SampleTsv()
    {
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "docs", "samples", "romaji-sample.tsv");
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "docs", "samples", "romaji-sample.tsv");
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new FileNotFoundException("docs/samples/romaji-sample.tsv");
    }

    /// <summary>打った途中の表示 | Enter で確定した結果。</summary>
    private static string Run(string keys)
    {
        var k = new CompositionTests.Keyboard();
        k.Type(keys);
        var showing = k.Showing;
        k.Type("\n");
        return showing + "|" + k.Host.Document;
    }

    // ---- 読み込み ----

    [Test]
    public static void Parse_AcceptsSafeSymbolKeys_RejectsOthers()
    {
        var warnings = new List<string>();
        var table = RomajiTable.Parse(";\tっ\n:\tー\nk;\tかん\n@\tん\n[\t『\n]\t』\n-\tー\n,\t、\n.\t。\n/\t・\n(\tあ\n1\tあ\n\"\tあ\n!\tあ\n", warnings);
        Assert.True(table is not null, "表ができる");
        Assert.Equal(10, table!.Count, "安全な記号 9 種 + k; の 10 行だけ有効 ( ( 1 \" ! は無視)");
        Assert.Equal(4, warnings.Count, "無視した 4 行の警告");
        Assert.True(table.HasSymbolKeys, "記号入りのキーがある");
        Assert.True(table.TryGet("k;", out var kana) && kana == "かん", "英字 + 記号のキー");
        Assert.True(!RomajiTable.Parse("ry\tらい\n")!.HasSymbolKeys, "英字だけの表は HasSymbolKeys が false");
    }

    [Test]
    public static void Sample_FileParsesAndIsSmall()
    {
        var warnings = new List<string>();
        var table = RomajiTable.Parse(SampleTsv(), warnings);
        Assert.True(table is { HasSymbolKeys: true }, "サンプル表は記号入り");
        Assert.True(table!.Count is >= 1 and <= 10, "1〜10 行: " + table.Count);
        Assert.Equal(0, warnings.Count, "サンプル表に無視される行が無い");
    }

    // ---- OFF / 表なし / 無関係な表 = 従来と完全に同じ ----

    private static readonly string[] SymbolProbes =
    {
        "kyou;wa", "a:b", "[abc]", "tetr.io", "z.", "z[", "z-", "a-b", "ka,ki.", "a@b.c", "x/y/z", "kyouhatenkigaii,", "ng;", "k;", ";", ":", "@", "[", "z;",
        "10,000", "3.14desu", "C:/dev", "foo@example.com", "./a.sh", "kore-wa", "e-mail", "http://a.b/c;d", "--sorekara",
    };

    private static string[] RunAll() => SymbolProbes.Select(Run).ToArray();

    [Test]
    public static void Off_SymbolsBehaveAsBefore()
    {
        var baseline = RunAll();
        // 表が無い / 空 / 英字だけの表 / 記号入りだが入力と無関係なキーだけの表 は、記号の扱いが従来と同じ
        foreach (var tsv in new string?[] { null, "", "ry\tらい\nst\tした\n", "zq;\tX\n" })
        {
            WithTable(tsv, () => Assert.Equal(string.Join("\n", baseline), string.Join("\n", RunAll()), "従来と同じ: " + tsv));
        }
    }

    [Test]
    public static void Off_SettingOff_ProviderReturnsNull_NoSymbolHook()
    {
        // 設定 OFF は CustomTableProvider が null を返す (MeltypeSession)。null の表 = 従来
        var on = false;
        var table = RomajiTable.Parse(";\tっ\n");
        var provider = Meltype.Composition.MeltypeSession.CustomTableProvider(() => on, _ => table, "/nonexistent/romaji.tsv");
        var romaji = CompositionTests.Detector.Romaji;
        var previous = romaji.CustomTable;
        romaji.CustomTable = provider;
        try
        {
            var off = Run("a;b");
            on = true;
            var withTable = Run("a;b");
            on = false;
            Assert.Equal(off, Run("a;b"), "OFF に戻したら元の結果");
            Assert.True(off != withTable, "ON では ; が っ になる (対照): " + off + " / " + withTable);
        }
        finally { romaji.CustomTable = previous; }
    }

    // ---- 記号キー ----

    [Test]
    public static void SymbolKeys_ReplaceWhenKeyCompletes()
    {
        WithTable(SampleTsv(), () =>
        {
            Assert.Equal("っ|っ", Run(";"), "; = っ (従来は ; のまま)");
            Assert.Equal("かん|かん", Run("k;"), "k; = かん");
            Assert.Equal("あかん|あかん", Run("ak;"), "ak; = あ + かん");
            Assert.Equal("ん|ん", Run("@"), "@ = ん");
            Assert.Equal("『あ|『あ", Run("[a"), "[ = 『 の後ろに あ");
            Assert.Equal("ー|ー", Run(":"), ": = ー");
            Assert.Equal("きょうさん|きょうさん", Run("kyous;"), "日本語の語の後ろでも s; = さん");
        });
    }

    [Test]
    public static void SymbolKeys_ComposeWithOrdinaryRomaji()
    {
        WithTable(SampleTsv(), () =>
        {
            Assert.Equal("さんかん|さんかん", Run("s;k;"), "s; k; が続けて揃う");
            Assert.Equal("かっ|かっ", Run("ka;"), "ka の後の ; は単独のキー (っ): 保留中の ka は標準で確定してから ;");
            Assert.Equal("あさんた|あさんた", Run("as;ta"), "記号入りキーの後ろは標準の綴り");
        });
    }

    [Test]
    public static void SymbolKeys_UnmatchedSymbolFallsBackToOrdinaryRules()
    {
        // 表のキーが ;a だけ。; 単独は途中として待つが、b が来て届かなければ従来の記号の扱い (; のまま) に戻る
        WithTable(";a\tあっ\n", () =>
        {
            Assert.Equal("あっ|あっ", Run(";a"), ";a は表どおり");
        });
        // 従来の結果 (表なし) を先に取り、キーに届かない / 途中のまま確定する入力が同じになることを見る
        string? plain = null, plainEnd = null, plainBracket = null;
        WithTable(null, () => { plain = Run("a;b"); plainEnd = Run("a;"); plainBracket = Run("["); });
        WithTable(";a\tあっ\n", () =>
        {
            Assert.Equal(plain, Run("a;b"), "a;b: キーに届かないので従来と同じ");
            Assert.Equal(plainEnd!.Split('|')[1], Run("a;").Split('|')[1], "a; (表のキーの途中のまま Enter) の確定結果も従来と同じ");
        });
        // [ は従来 「 。キーの途中のまま確定しても 「 (Symbol) になる (待っている間の表示は [ のまま)
        WithTable("[a\tあ\n", () => Assert.Equal(plainBracket!.Split('|')[1], Run("[").Split('|')[1], "[ だけで確定 = 従来と同じ"));
    }

    [Test]
    public static void SymbolKeys_ZSymbolKeepsWorkUnlessTableHasZKey()
    {
        WithTable(";\tっ\n", () =>
        {
            Assert.Equal("…|…", Run("z."), "z. は従来どおり … (キーに . が無い)");
            Assert.Equal("『|『", Run("z["), "z[ は従来どおり 『");
            Assert.Equal("zっ|zっ", Run("z;"), "z + ; は z を確定してから ; (キー z; が無いので)");
        });
        WithTable("z;\tずっと\n", () => Assert.Equal("ずっと|ずっと", Run("z;"), "z; が表のキーなら表が勝つ"));
    }

    [Test]
    public static void SymbolKeys_BackSpaceUndoesOneKey()
    {
        WithTable(SampleTsv(), () =>
        {
            var k = new CompositionTests.Keyboard();
            k.Type("ak;");
            Assert.Equal("あかん", k.Showing, "ak;");
            k.Type("\b");
            Assert.Equal("あk", k.Showing, "k; は 1 打鍵だけ戻る");
            k.Type("\b");
            Assert.Equal("あ", k.Showing, "k を消す");
        });
    }

    // ---- C1: 表でしか読めない綴りの英語判定 ----

    [Test]
    public static void C1_TableOnlySpellingBecomesJapanese()
    {
        WithTable(SampleTsv(), () =>
        {
            Assert.Equal("したこと|したこと", Run("stkt"), "st + kt: 標準では読めず英字のままだった綴りが、表で全部読めるなら日本語");
            Assert.Equal("きょうはしたこと|きょうはしたこと", Run("kyouhastkt"), "日本語の文の中でも同じ");
        });
        // 表 OFF では今までどおり (対照)
        WithTable(null, () => Assert.Equal("stkt", Run("stkt").Split('|')[1], "表なしは英字のまま"));
    }

    /// <summary>実アプリ (BuiltIn / NSSpellChecker) と同じく英単語チェッカーを付けて回す。付けないと辞書に無い英語が表で変わって見える (査読)。</summary>
    private static T WithWords<T>(Func<T> body)
    {
        var previous = CompositionTests.Detector.SpellChecker;
        CompositionTests.Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try { return body(); }
        finally { CompositionTests.Detector.SpellChecker = previous; }
    }

    [Test]
    public static void C1_RealisticSpellChecker_KeepsEnglishAndStaysOnlyWhereFullyCovered()
    {
        WithWords(() =>
        {
            string[] english = { "strike", "kingdom", "steel", "artist", "western", "ringing", "string", "last", "must", "stop", "strong", "street", "kitten", "spring", "stdin", "ngrok", "nginx", "stdout" };
            var off = english.Select(w => Run(w + "\n")).ToArray();
            WithTable("st\tした\nkt\tこと\nng\tんぐ\nry\tらい\n", () =>
            {
                foreach (var (w, expected) in english.Zip(off))
                    Assert.Equal(expected, Run(w + "\n"), $"英単語 {w} は表 ON でも OFF と同じ (BuiltIn 付き)");
                // 表のキーだけで最後まで読める語は日本語、最後まで読めない語 (余りが出る) は変えない。
                Assert.Equal("したこと", Run("stkt\n").Split('|')[1], "stkt は表で全部読める → したこと");
                Assert.Equal("したことあ", Run("stkta\n").Split('|')[1], "stkta も st + kt + a で全部読める → したことあ (査読で文書の誤りを訂正)");
                Assert.Equal(Run("stktx\n"), WithOff(() => Run("stktx\n")), "stktx は最後まで読めない → 変えない");
            });
            return 0;
        });
    }

    private static string WithOff(Func<string> body)
    {
        var romaji = CompositionTests.Detector.Romaji;
        var previous = romaji.CustomTable;
        romaji.CustomTable = () => null;
        try { return body(); }
        finally { romaji.CustomTable = previous; }
    }

    [Test]
    public static void C1_EnglishSpellingsStayEnglish_WithTableOn()
    {
        string[] words = { "string", "ring", "apple", "stop", "test", "stay", "star", "stock", "last", "must", "list", "street", "strong", "best", "fast", "kitten", "market", "ketchup", "stkt" };
        var off = words.Select(w => Run(w + "\n")).ToArray();
        WithTable(SampleTsv(), () =>
        {
            foreach (var (w, expected) in words.Zip(off))
            {
                if (w == "stkt") continue; // 表でしか読めない綴りは上のテスト
                Assert.Equal(expected, Run(w + "\n"), $"英語の綴り {w} は表 ON でも OFF と同じ");
            }
            foreach (var w in new[] { "string", "ring", "apple", "stop", "test" })
                Assert.Equal(w, Run(w + "\n").Split('|')[1], $"{w} は英字のまま");
        });
    }

    [Test]
    public static void C1_KnownEnglishWordIsNotTakenByTable()
    {
        // 表のキーと標準の綴りで全部読めるが、英単語でもある語 (stereo = st + e + re + o) は英語判定のまま
        var off = Run("stereo\n");
        WithTable("st\tした\n", () => Assert.Equal(off, Run("stereo\n"), "知っている英単語 stereo は表 ON でも OFF と同じ"));
    }

    // ---- 測定: eval/cases を サンプル表 ON / OFF で回して差を見る ----

    [Test]
    public static void Measure_EvalCases_SampleTableOnVsOff_NoDiffWithoutKeys()
    {
        var table = RomajiTable.Parse(SampleTsv())!;
        var keys = new List<string>();
        foreach (var raw in SampleTsv().Split('\n'))
        {
            if (raw.StartsWith('#') || !raw.Contains('\t')) continue;
            keys.Add(raw[..raw.IndexOf('\t')].Trim().ToLowerInvariant());
        }
        string root = "";
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "eval", "cases"))) { root = Path.Combine(dir.FullName, "eval", "cases"); break; }
        Assert.True(root.Length > 0, "eval/cases が見つかる");

        var cases = new List<Checks.EvalCase>();
        foreach (var file in Directory.GetFiles(root, "*.tsv").OrderBy(f => f, StringComparer.Ordinal))
            cases.AddRange(Checks.ParseCases(File.ReadLines(file), Path.GetFileName(file), fourColumns: true));
        var ascii = cases.Where(c => c.Typed.All(ch => ch is >= ' ' and <= '~' || ch == '\n')).ToList();

        string Typed(string keysTyped)
        {
            var previous = CompositionTests.Detector.SpellChecker;
            CompositionTests.Detector.SpellChecker = BuiltInWordChecker.Shared;
            try
            {
                var k = new CompositionTests.Keyboard();
                k.Type(keysTyped);
                return k.Host.Document + "|" + k.Showing;
            }
            finally { CompositionTests.Detector.SpellChecker = previous; }
        }

        var off = ascii.Select(c => Typed(c.Typed)).ToList();
        var on = new List<string>();
        WithTable(SampleTsv(), () => on.AddRange(ascii.Select(c => Typed(c.Typed))));

        var withoutKeys = 0; var diffWithout = new List<string>(); var withKeys = 0; var diffWith = 0;
        for (var i = 0; i < ascii.Count; i++)
        {
            var lower = ascii[i].Typed.ToLowerInvariant();
            var hasKey = keys.Any(k => lower.Contains(k));
            if (hasKey) { withKeys++; if (off[i] != on[i]) { diffWith++; Console.WriteLine($"[差] {ascii[i].Typed} : {off[i]} → {on[i]}"); } }
            else { withoutKeys++; if (off[i] != on[i]) diffWithout.Add(ascii[i].Typed + " : " + off[i] + " → " + on[i]); }
        }
        Console.WriteLine($"[測定] 英字だけの正解 {ascii.Count} 件 (全 {cases.Count} 件): 表のキーを含まない {withoutKeys} 件の差 {diffWithout.Count}、含む {withKeys} 件の差 {diffWith}");
        Assert.Equal(0, diffWithout.Count, "表のキーを含まない入力は表 ON でも OFF と同じ: " + string.Join(" / ", diffWithout.Take(5)));
    }
}
