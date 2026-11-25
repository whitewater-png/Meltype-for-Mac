// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>
/// 要件 024: サロゲートペアの漢字 (𠮷・𩸽) だけの文節を「記号だけ」とみなさない。変換エンジンに渡す文脈を、サロゲートペアの途中で始めない。
/// 偽の変換エンジンは「読み → 文節」を返す (再現用。実際の azooKey が返すかは実機で確かめる)。
/// </summary>
internal static class SurrogatePairTests
{
    private sealed class MapConverter(Dictionary<string, IReadOnlyList<(string Reading, string Text)>> map) : IKanjiConverter
    {
        public List<string> Contexts { get; } = [];

        public string? Convert(string hiragana) => null;

        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null)
        {
            if (context is not null) Contexts.Add(context);
            return map.TryGetValue(hiragana, out var parts) ? parts.Select(p => new ConversionClause(p.Reading, p.Text)).ToList() : null;
        }

        public static MapConverter Single(params (string Reading, string Text)[] pairs) =>
            new(pairs.ToDictionary(p => p.Reading, p => (IReadOnlyList<(string, string)>)[p]));
    }

    private static string Hex(string text) => string.Join(" ", text.Select(c => $"U+{(int)c:X4}"));

    [Test]
    public static void Req024_SurrogateKanjiClause_StaysFirst()
    {
        foreach (var (typed, reading, text) in new[] { ("yoshi", "よし", "\U00020BB7"), ("hokke", "ほっけ", "\U00029E3D"), ("yoshiyoshi", "よしよし", "\U00020BB7\U00020BB7") })
        {
            var k = new CompositionTests.Keyboard(converter: MapConverter.Single((reading, text)));
            k.Type(typed + " ");
            Assert.Equal(text, string.Join("|", k.Host.View!.Clauses!), $"{typed} + Space の文節");
            Assert.Equal(text, k.Host.View.Candidates[0], $"{typed} の先頭の候補");
            k.Type("\n");
            Assert.Equal(text, k.Host.Document, $"{typed} + Enter の確定");
        }
        // 複数の文節: [よし → 𠮷][さん]
        var map = new Dictionary<string, IReadOnlyList<(string, string)>> { ["よしさん"] = [("よし", "\U00020BB7"), ("さん", "さん")] };
        var multi = new CompositionTests.Keyboard(converter: new MapConverter(map));
        multi.Type("yoshisan ");
        Assert.Equal("\U00020BB7|さん", string.Join("|", multi.Host.View!.Clauses!), "文節");
        multi.Type("\n");
        Assert.Equal("\U00020BB7さん", multi.Host.Document, "Enter");
    }

    [Test]
    public static void Req024_SurrogateKanjiClause_LiveConversion()
    {
        var k = new CompositionTests.Keyboard(live: true, converter: MapConverter.Single(("よしよし", "\U00020BB7\U00020BB7")));
        k.Type("yoshiyoshi");
        Assert.Equal("\U00020BB7\U00020BB7", k.Showing, "ライブ変換の表示");
        k.Type("\n");
        Assert.Equal("\U00020BB7\U00020BB7", k.Host.Document, "Enter");
    }

    [Test]
    public static void Req024_SymbolOnlyClause_StaysBehindHiragana()
    {
        foreach (var (typed, reading, symbol) in new[] { ("kara", "から", "～"), ("nikoniko", "にこにこ", "\U0001F60A") })
        {
            var k = new CompositionTests.Keyboard(converter: MapConverter.Single((reading, symbol)));
            k.Type(typed + " ");
            Assert.Equal($"{reading},{symbol}", string.Join(",", k.Host.View!.Candidates.Take(2)), $"{typed} の候補の先頭 2 つ");
            k.Type("\n");
            Assert.Equal(reading, k.Host.Document, $"{typed} + Enter");
        }
    }

    [Test]
    public static void Req024_BmpAndMixedClauses_Unchanged()
    {
        foreach (var (typed, reading, text) in new[] { ("yoshi", "よし", "吉"), ("yoshinoya", "よしのや", "\U00020BB7野家") })
        {
            var k = new CompositionTests.Keyboard(converter: MapConverter.Single((reading, text)));
            k.Type(typed + " \n");
            Assert.Equal(text, k.Host.Document, typed);
        }
    }

    [Test]
    public static void Req024_VariationSelectorsAndCombiningMarks()
    {
        // 異体字セレクタ・結合文字が付いた漢字は、漢字があるので記号だけではない。絵文字 (+ U+FE0F・ZWJ・肌色) は記号のまま。
        foreach (var (text, kanji) in new[]
        {
            ("\U00020BB7\uFE0F", true),            // 𠮷 + 異体字セレクタ U+FE0F
            ("\U00020BB7\U000E0100", true),        // 𠮷 + 異体字セレクタ補助 U+E0100 (IVS)
            ("\U00029E3D\u3099", true),            // 𩸽 + 結合文字 (濁点)
            ("吉\U000E0100", true),                // BMP の漢字 + IVS (今までどおり)
            ("\U0001F60A\uFE0F", false),           // 😊 + U+FE0F
            ("\U0001F44D\U0001F3FB", false),       // 👍 + 肌色
            ("\U0001F468\u200D\U0001F469", false), // ZWJ で繋いだ絵文字
        })
        {
            var k = new CompositionTests.Keyboard(converter: MapConverter.Single(("よし", text)));
            k.Type("yoshi \n");
            Assert.Equal(kanji ? text : "よし", k.Host.Document, $"{Hex(text)} を返す変換エンジン + Enter");
        }
    }

    [Test]
    public static void Req024_Context_FromTextBeforeCaret_DoesNotSplitSurrogatePair()
    {
        foreach (var (before, expected) in new[]
        {
            ("\U00020BB7あいうえおかきくけ", "あいうえおかきくけ"),                 // 11 単位: 切り目が下位サロゲートに当たる → 落とす
            ("\U00020BB7あいうえおかき", "\U00020BB7あいうえおかき"),             // 9 単位: 切らない
            ("あ\U00020BB7あいうえおかきく", "\U00020BB7あいうえおかきく"),       // 11 単位: 切り目が上位サロゲートに当たる → 落としすぎない (10 単位)
            ("あ\U00020BB7\U000E0100いうえおかきく", "\U000E0100いうえおかきく"),  // 切り目が 𠮷 の下位サロゲートに当たる。IVS (サロゲート 2 つ) は残る
        })
        {
            var converter = MapConverter.Single(("かわ", "川"));
            var k = new CompositionTests.Keyboard(converter: converter);
            k.Host.PrecedingText = before;
            k.Type("kawa ");
            Assert.Equal(expected, converter.Contexts.LastOrDefault() ?? "(なし)", $"キャレットの前 [{Hex(before)}] の文脈");
        }
    }

    [Test]
    public static void Req024_Context_FromOwnCommits_DoesNotSplitSurrogatePair()
    {
        var converter = new MapConverter(new Dictionary<string, IReadOnlyList<(string, string)>>
        {
            ["よしのや"] = [("よしのや", "\U00020BB7野家")],
            ["かわ"] = [("かわ", "川")],
        });
        var session = new MeltypeSession(CompositionTests.Detector, converter, new CompositionOptions(), () => new Meltype.Config.Settings());
        void Keys(string text)
        {
            foreach (var c in text) session.HandleKey(char.ToUpperInvariant(c), c, false, false, false, false);
        }
        Keys("yoshinoya");
        session.HandleKey(VirtualKeys.Space, ' ', false, false, false, false);
        session.HandleKey(VirtualKeys.Return, '\r', false, false, false, false);
        Keys("aiueokaki");
        session.HandleKey(VirtualKeys.Return, '\r', false, false, false, false);
        for (var i = 0; i < 10; i++) session.HandleKey(VirtualKeys.Space, ' ', false, false, false, false);
        Keys("kawa");
        session.HandleKey(VirtualKeys.Space, ' ', false, false, false, false);
        var context = converter.Contexts.LastOrDefault() ?? "(なし)";
        Assert.Equal("野家あいうえおかき", context, $"自分の確定の記録から作った文脈 [{Hex(context)}]");
    }
}
