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
    /// 「入力 → 期待」の一覧 (1 行に「入力<Tab>期待」) を確かめて JSON で出す。
    ///   入力がかな (しゃおみ) … 変換の候補に期待した語 (Xiaomi) が出るか
    ///   入力が英字 (nihongowohanasu) … 打って Enter した結果が期待どおりか。期待に漢字が入っていれば、日本語 / 英語の分かれ方だけを比べる
    ///   (テストでは変換エンジンを使わず漢字にしないため)。
    /// </summary>
    public static void Expect(string input, string output)
    {
        CompositionTests.Detector.SpellChecker ??= Detection.BuiltInWordChecker.Shared;
        var candidates = Composition.CandidateDictionary.Load(null);
        var results = new List<object>();
        foreach (var line in File.ReadAllLines(input))
        {
            var parts = line.Split('\t');
            if (parts.Length != 2) continue;
            var (typed, expected) = (parts[0].Trim(), parts[1].Trim());
            if (typed.Length == 0 || expected.Length == 0) continue;
            if (typed.All(c => c is >= 'ぁ' and <= 'ゖ' or 'ー' or >= 'ァ' and <= 'ヺ'))
            {
                var reading = new string(typed.Select(c => c is >= 'ァ' and <= 'ヶ' ? (char)(c - 0x60) : c).ToArray());
                var list = candidates.Lookup(reading);
                results.Add(new { typed, expected, kind = "候補", actual = string.Join(" ", list.Take(12)), ok = list.Contains(expected) });
                continue;
            }
            var k = new CompositionTests.Keyboard();
            k.Type(new string(typed.Where(c => c is >= ' ' and <= '~').Take(300).ToArray()) + "\n");
            var actual = k.Host.Document;
            var kanji = expected.Any(c => c is >= '㐀' and <= '鿿');
            var ok = kanji ? Words(actual) == Words(expected) : actual == expected;
            results.Add(new { typed, expected, kind = kanji ? "判定 (英字の部分だけ比べる)" : "入力", actual, ok });
        }
        File.WriteAllText(output, JsonSerializer.Serialize(results, Json));
    }

    /// <summary>英字の語だけを取り出す (日本語 / 英語の分かれ方を比べる)。</summary>
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
