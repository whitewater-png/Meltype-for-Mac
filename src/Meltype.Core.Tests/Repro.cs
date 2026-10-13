// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text.Json;

namespace Meltype.Tests;

/// <summary>
/// 報告された「打ったもの」を Meltype キーボードで打ってみる (GitHub の bot が誤判定の報告を再現するのに使う)。
/// 変換エンジンはテスト用の偽物なので、漢字の変換は再現しない。日本語 / 英語の判定 (にほんgo) を見る。
/// </summary>
internal static class Repro
{
    /// <summary>打つ文字の上限 (bot に長い文を渡されても時間がかからないように)。</summary>
    private const int MaxKeys = 300;

    public static void Type(string keys, string last)
    {
        // 英字・数字・記号と空白だけ (それ以外の文字はキーとして打てない)
        // bot が結果を読むので、ほかの環境の文字コードにせず UTF-8 で出す
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        var typed = new string(keys.Where(c => c is >= ' ' and <= '~').Take(MaxKeys).ToArray()).Trim();
        // Windows のテストランナーなら Windows のスペルチェッカー、ほかの環境では同梱の英単語の一覧 (アプリと同じ)
        CompositionTests.Detector.SpellChecker ??= Detection.BuiltInWordChecker.Shared;
        var k = new CompositionTests.Keyboard();
        k.Type(typed);
        var showing = k.Showing;
        switch (last)
        {
            case "space": k.Type(" "); break;
            case "enter": k.Type("\n"); break;
        }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            typed,
            last,
            // 最後のキーの前に変換ボックスに出ていたもの
            showing,
            // 最後のキーを押した後: 入力欄に入ったもの + まだ変換ボックスにあるもの
            committed = k.Host.Document,
            // 英語のスペルチェッカーを使ったか (Windows では使う。使わないと英単語の判定が実際のアプリと違うことがある)
            spellChecker = CompositionTests.Detector.SpellChecker is not null,
            composing = k.Showing,
        }, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }
}

/// <summary>Pull Request のチェック (.github/workflows/pr-checks.yml) で使う結果の出力。</summary>
internal static class Checks
{
    private static readonly JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = true };

    /// <summary>EvalResult を typed・expected・ok のように小文字始まりで出す。</summary>
    private static readonly JsonSerializerOptions ResultJson = new(Json) { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>品質テストの例ごとの結果を JSON で出す (main と Pull Request の結果を比べて、新しく外れた例を見つける)。</summary>
    public static void EvalJson(string path)
    {
        var result = Quality.Run();
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            pass = result.Pass,
            total = result.Total,
            cases = result.Cases.Select(c => new { key = c.Key, ok = c.Ok, detail = c.Detail }),
        }, Json));
    }

    /// <summary>
    /// 正解表 (eval/cases/*.tsv) の 1 件。1 行 = 打鍵・期待・比べ方・出典の 4 列 (Tab 区切り)。
    /// Mode は full / ascii / nojp。古い 2 列だけの表 (Pull Request のチェックが使う) は Mode が空で、
    /// 変更前の Expect と同じに比べる (期待に漢字があれば英字の語の並び、なければ完全一致。カタカナ・大小の正規化なし)。
    /// </summary>
    public sealed record EvalCase(string Typed, string Expected, string Mode, string Source);

    /// <summary>1 件の結果。File は --expect-all のときだけ入る。</summary>
    public sealed record EvalResult(
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? File,
        string Typed, string Expected, string Mode, string Source, string Actual, bool Ok, string Kind);

    /// <summary>
    /// 正解表の行を読む。
    /// # で始まらない空でない行に 4 列 (Tab 3 つ) の行が 1 つも無ければ、古い 2 列の表 (Pull Request のチェック) として、変更前と同じ読み方 (# の行も読み、2 列でない行は黙って捨てる)。
    /// 1 つでもあれば 4 列の表として、# で始まる行と空行を無視し、列の数・比べ方の誤りは例外にする。
    /// fourColumns のときは、判定せず常に 4 列の表として読む (--expect-all)。
    /// </summary>
    public static List<EvalCase> ParseCases(IEnumerable<string> lines, string name = "", bool fourColumns = false)
    {
        var all = lines.ToList();
        var legacy = !fourColumns && !all.Any(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith('#') && l.Split('\t').Length == 4);
        var cases = new List<EvalCase>();
        var number = 0;
        foreach (var line in all)
        {
            number++;
            if (legacy)
            {
                var two = line.Split('\t');
                if (two.Length != 2) continue;
                var (t, e) = (two[0].Trim(), two[1].Trim());
                if (t.Length == 0 || e.Length == 0) continue;
                cases.Add(new EvalCase(t, e, "", ""));
                continue;
            }
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#')) continue;
            var parts = line.Split('\t');
            if (parts.Length != 4)
                throw new FormatException($"{name}:{number} 列が {parts.Length} 個です (打鍵・期待・比べ方・出典の 4 列)");
            var (typed, expected) = (parts[0].Trim(), parts[1].Trim());
            if (typed.Length == 0 || expected.Length == 0) continue;
            var mode = parts[2].Trim();
            var source = parts[3].Trim();
            if (mode is not ("" or "full" or "ascii" or "nojp"))
                throw new FormatException($"{name}:{number} 比べ方 '{mode}' が分かりません (full / ascii / nojp)");
            if (mode != "" && typed.Any(c => c > '~'))
                throw new FormatException($"{name}:{number} かなで打つ行 (候補を見る行) に比べ方 '{mode}' は使えません (比べ方は空にする): {typed}");
            if (mode == "ascii" && AsciiWords(expected).Length == 0)
                throw new FormatException($"{name}:{number} ascii の期待に比べる英字・記号の語がありません: {expected}");
            cases.Add(new EvalCase(typed, expected, mode, source));
        }
        return cases;
    }

    /// <summary>
    /// 期待と実際を比べる。
    ///   full  … 完全一致 (大小も区別)。期待にカタカナがあるときだけ、カタカナをひらがなに直し (ホスティング = ほすてぃんぐ)、英字の大小も無視する (OEM = oem)。
    ///   ascii … 印字可能 ASCII の連続 (英字・数字・記号) の並びだけを比べる (実機の漢字変換の結果を期待に書いても、日本語 / 英語の分かれ方だけを測れる)。
    ///   nojp  … 実際の出力に日本語 (ひらがな・カタカナ・漢字・全角記号・半角カナ) が 1 文字も無ければ合格 (英文が日本語に崩れないことだけを見る)。
    /// Mode が空 (2 列の古い表) のときは変更前の Expect と同じ: 期待に漢字があれば英字の語の並び、無ければ完全一致。
    /// </summary>
    public static bool Judge(string mode, string expected, string actual)
    {
        if (mode == "")
            return expected.Any(c => c is >= '㐀' and <= '鿿') ? Words(actual) == Words(expected) : actual == expected;
        return mode switch
        {
            "ascii" => AsciiWords(actual) == AsciiWords(expected),
            "nojp" => !actual.Any(IsJapanese),
            _ => expected.Any(c => c is >= 'ァ' and <= 'ヶ')
                ? AsciiLower(Hiragana(actual)) == AsciiLower(Hiragana(expected))
                : Hiragana(actual) == Hiragana(expected),
        };
    }

    /// <summary>
    /// 「入力 → 期待」の一覧を確かめて JSON で出す。
    ///   入力がかな (しゃおみ) … 変換の候補に期待した語 (Xiaomi) が出るか
    ///   入力が英字 (nihongowohanasu) … 打って Enter した結果が <see cref="Judge"/> の比べ方どおりか
    ///   (テストでは変換エンジンを使わず漢字にしないため、漢字の変換結果は比べられない)。
    /// </summary>
    public static void Expect(string input, string output)
    {
        CompositionTests.Detector.SpellChecker ??= Detection.BuiltInWordChecker.Shared;
        var candidates = Composition.CandidateDictionary.Load(null);
        var results = Evaluate(ParseCases(File.ReadAllLines(input), Path.GetFileName(input)), null, candidates);
        File.WriteAllText(output, JsonSerializer.Serialize(results, ResultJson));
    }

    /// <summary>--expect-all: フォルダー内の *.tsv をすべて確かめ、ファイル名を file に入れて 1 つの JSON 配列にする。</summary>
    public static void ExpectAll(string directory, string output)
    {
        CompositionTests.Detector.SpellChecker ??= Detection.BuiltInWordChecker.Shared;
        var candidates = Composition.CandidateDictionary.Load(null);
        var results = new List<EvalResult>();
        foreach (var path in Directory.GetFiles(directory, "*.tsv").OrderBy(p => p, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(path);
            results.AddRange(Evaluate(ParseCases(File.ReadAllLines(path), name, fourColumns: true), name, candidates));
        }
        File.WriteAllText(output, JsonSerializer.Serialize(results, ResultJson));
    }

    private static List<EvalResult> Evaluate(List<EvalCase> cases, string? file, Composition.CandidateDictionary candidates)
    {
        var results = new List<EvalResult>();
        foreach (var c in cases)
        {
            var (typed, expected, source) = (c.Typed, c.Expected, c.Source);
            var mode = c.Mode;
            if (typed.All(ch => ch is >= 'ぁ' and <= 'ゖ' or 'ー' or >= 'ァ' and <= 'ヺ'))
            {
                var reading = new string(typed.Select(ch => ch is >= 'ァ' and <= 'ヶ' ? (char)(ch - 0x60) : ch).ToArray());
                var list = candidates.Lookup(reading);
                results.Add(new EvalResult(file, typed, expected, mode, source, string.Join(" ", list.Take(12)), list.Contains(expected), "候補"));
                continue;
            }
            var k = new CompositionTests.Keyboard();
            k.Type(new string(typed.Where(ch => ch is >= ' ' and <= '~').Take(300).ToArray()) + "\n");
            var actual = k.Host.Document;
            var kind = mode switch
            {
                "ascii" => "判定 (英字・記号の部分だけ比べる)",
                "nojp" => "判定 (日本語を含まない)",
                "" => expected.Any(ch => ch is >= '㐀' and <= '鿿') ? "判定 (英字の部分だけ比べる)" : "入力",
                _ => "入力",
            };
            results.Add(new EvalResult(file, typed, expected, mode, source, actual, Judge(mode, expected, actual), kind));
        }
        return results;
    }

    /// <summary>日本語の文字: 全角記号・ひらがな・カタカナ (ー を含む U+3000〜30FF)、漢字 (U+3400〜9FFF)、全角英数・半角カナ (U+FF00〜FFEF)。</summary>
    private static bool IsJapanese(char c) => c is >= '　' and <= 'ヿ' or >= '㐀' and <= '鿿' or >= '＀' and <= '￯';

    /// <summary>カタカナをひらがなに直す (テストの変換エンジンはカタカナにしないため)。</summary>
    // 本家 Meltype src/Meltype.Core.Tests/Repro.cs の Hiragana (a4b6f1a, 176-178 行) と同じ。
    private static string Hiragana(string text) =>
        new(text.Select(c => c is >= 'ァ' and <= 'ヶ' ? (char)(c - 0x60) : c).ToArray());

    /// <summary>英字の大小を無視する (OEM = oem)。</summary>
    // 本家 Meltype src/Meltype.Core.Tests/Repro.cs の AsciiLower (a4b6f1a, 180-182 行) と同じ。
    private static string AsciiLower(string text) =>
        new(text.Select(c => c is >= 'A' and <= 'Z' ? (char)(c + 32) : c).ToArray());

    /// <summary>印字可能 ASCII の連続 (英字・数字・記号) を小文字にして空白でつなぐ (gemma4-27b や python3 -m venv .venv の崩れを見つける)。</summary>
    private static string AsciiWords(string text) =>
        string.Join(" ", System.Text.RegularExpressions.Regex.Matches(text, "[!-~]+").Select(m => m.Value.ToLowerInvariant()));

    /// <summary>英字の語だけを取り出す (変更前の Expect と同じ。2 列の古い表の比べ方)。</summary>
    private static string Words(string text) =>
        string.Join(" ", System.Text.RegularExpressions.Regex.Matches(text, "[A-Za-z][A-Za-z'’-]*").Select(m => m.Value.ToLowerInvariant()));
}

/// <summary>
/// henkan-test: 打ったキーを Meltype キーボードで打ち、変換ボックスの表示・Enter で確定した結果・Space で変換した結果を JSON で返す。
/// 環境変数 MELTYPE_MOZC に Mozc の変換ヘルパーの場所があれば、アプリと同じく Mozc で漢字に変換する。無ければ日本語 / 英語の判定だけ (漢字にしない)。
///   dotnet run --project src/Meltype.Core.Tests -- --henkan kyouhagoogledekensaku
/// </summary>
internal static class Henkan
{
    private const int MaxKeys = 300;

    /// <summary>Mozc の学習データの場所。bot が同時にいくつも動かすので、プロセスごとに分ける (終わったら消す)。</summary>
    private static readonly string ProfileDirectory = Path.Combine(Path.GetTempPath(), $"meltype-henkan-mozc-{Environment.ProcessId}");

    /// <summary>終わるときに、Mozc の変換ヘルパーを止めて、学習データの場所を消す。</summary>
    public static void Shutdown()
    {
        if (Mozc.IsValueCreated) Mozc.Value?.Dispose();
        try { if (Directory.Exists(ProfileDirectory)) Directory.Delete(ProfileDirectory, recursive: true); } catch { }
    }

    /// <summary>続けて試すとき (--jht-batch)、前の文の控えを次の文に持ち越さない (1 文ずつ起動したときと同じ結果にする)。</summary>
    public static void Reset()
    {
        if (Mozc.IsValueCreated) Mozc.Value?.ClearCache();
    }

    private static readonly Lazy<Composition.MozcConverter?> Mozc = new(() =>
        Environment.GetEnvironmentVariable("MELTYPE_MOZC") is { Length: > 0 } helper && File.Exists(helper)
            ? new Composition.MozcConverter(helper, ProfileDirectory)
            : null);
    private static readonly Lazy<Composition.TranslationDictionary> Translations = new(Composition.TranslationDictionary.Load);

    /// <summary>Windows のテストランナーが Microsoft IME を変換エンジンにするとき (Mozc が無いとき) に入れる。</summary>
    public static Composition.IKanjiConverter? FallbackConverter { get; set; }

    public static string EngineName => Mozc.Value is not null ? "Mozc" : FallbackConverter is not null ? "Microsoft IME" : "なし (判定だけ)";

    /// <summary>アプリと同じ設定の Meltype キーボード (ライブ変換 ON、Mozc があれば Mozc で変換)。</summary>
    public static CompositionTests.Keyboard Keyboard()
    {
        CompositionTests.Detector.SpellChecker ??= Detection.BuiltInWordChecker.Shared;
        // Mozc には覚えさせない (覚えさせると、前の打ち方・前の文で確定した変換が次の打ち方の結果を変えてしまう。
        // 確かめたいのは、まだ何も覚えていない人の最初の変換)
        var converter = Mozc.Value is { } engine ? new WithoutLearning(engine) : FallbackConverter;
        return new(live: true, converter: converter, moreCandidates: Mozc.Value is { } mozc ? mozc.Candidates : null,
            userDictionary: new Composition.UserDictionary(null), translations: Translations.Value);
    }

    /// <summary>変換だけを頼み、覚えさせない変換エンジン (ILearningConverter を持たない)。</summary>
    private sealed class WithoutLearning(Composition.IKanjiConverter inner) : Composition.IKanjiConverter
    {
        public string? Convert(string hiragana) => inner.Convert(hiragana);
        public IReadOnlyList<Composition.ConversionClause>? ConvertClauses(string hiragana, string? context = null) => inner.ConvertClauses(hiragana, context);
    }

    public static void Run(string keys)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        var typed = new string(keys.Where(c => c is >= ' ' and <= '~').Take(MaxKeys).ToArray()).Trim();
        CompositionTests.Keyboard Create() => Keyboard();

        // そのまま打った表示と、Enter で確定した結果
        var k = Create();
        k.Type(typed);
        var showing = k.Showing ?? "";
        k.Type("\n");
        var entered = k.Host.Document;

        // Space で変換した結果 (英語で終わっていれば確定して空白)
        var s = Create();
        s.Type(typed + " ");
        var view = s.Host.View;
        var converted = view is { Converting: true } ? string.Concat(view.Clauses ?? [view.Text]) : s.Host.Document;
        var clauses = view is { Converting: true, Clauses: { } c } ? c : [];
        var candidates = view is { Converting: true } ? view.Candidates.Take(9).ToList() : [];

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            typed,
            engine = EngineName,
            showing,
            entered,
            converted,
            clauses,
            candidates,
        }, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }
}
