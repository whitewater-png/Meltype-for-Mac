// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>
/// 要件 017: Zenzai が使える変換エンジンには、文をまたいだ前の日本語 (最大 20 文字・最後の改行まで) を文脈として渡す。
/// OFF (Zenzai を使わない・Mozc・Windows) は今までどおり、同じ文の最大 10 文字。
/// </summary>
internal static class WideContextTests
{
    private static string? ContextAfterTyping(string before, bool wide, string romaji = "atsui ")
    {
        var k = new CompositionTests.Keyboard { WideContext = wide };
        k.Host.PrecedingText = before;
        k.Type(romaji);
        return k.Converter.LastContext;
    }

    [Test]
    public static void PreviousSentence_IsPassedWhenOn()
    {
        Assert.Equal("大学の先生の話を聞いた。", ContextAfterTyping("大学の先生の話を聞いた。", true), "前の文を含む文脈を渡す");
        Assert.True(ContextAfterTyping("大学の先生の話を聞いた。", false) is null, "OFF は今までどおり (前の文が「。」で終わると文脈なし)");
        Assert.Equal("来週の", ContextAfterTyping("来週の", false), "OFF の同じ文の文脈は今までどおり");
    }

    [Test]
    public static void Context_IsLastTwentyUnits()
    {
        var before = "あいうえおかきくけこさしすせそたちつてとなにぬねの"; // 25 文字
        Assert.Equal(before[^20..], ContextAfterTyping(before, true), "末尾 20 文字だけ");
        Assert.Equal(before[^10..], ContextAfterTyping(before, false), "OFF は 10 文字");
        // 先頭が下位サロゲート単独になる切り方はしない
        var surrogate = "\U00020BB7" + new string('い', 19); // 21 単位。末尾 20 単位の先頭が 𠮷 の下位サロゲートになる
        var context = ContextAfterTyping(surrogate, true)!;
        Assert.Equal(new string('い', 19), context, "先頭の下位サロゲート単独は落とす");
    }

    /// <summary>よしの を 𠮷野 (𠮷 はサロゲートペア) に、ほかの読みはかなのまま変換し、渡された文脈を覚える変換エンジン。</summary>
    private sealed class YoshinoConverter : IKanjiConverter
    {
        public string? LastContext { get; private set; }

        public string? Convert(string hiragana) => hiragana == "よしの" ? "\U00020BB7野" : null;

        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null)
        {
            if (context is not null) LastContext = context;
            return [new(hiragana, hiragana == "よしの" ? "\U00020BB7野" : hiragana)];
        }
    }

    [Test]
    public static void Context_DropsLeadingLowSurrogate_EvenWhenNotCut()
    {
        // 自分の確定の記録 (_lastCommitText) は、サロゲートを見ずに末尾 20 単位で切る。確定の直後 (1.5 秒以内) はその記録がキャレットの前の文字になるので、
        // ちょうど 20 単位で下位サロゲートから始まることがある。広い文脈で切ったかどうかに関係なく落とす
        Assert.Equal(new string('い', 19), ContextAfterTyping("\uDFB7" + new string('い', 19), true), "20 単位ちょうどでも、先頭の下位サロゲート単独は落とす");
        var converter = new YoshinoConverter();
        var k = new CompositionTests.Keyboard(converter: converter) { WideContext = true };
        k.Type("yoshino \n" + new string('i', 18) + "\n"); // 𠮷野 (3 単位) + い 18 文字を確定する。記録は 21 単位の末尾 20 単位
        k.Type("atsui ");
        Assert.Equal("野" + new string('い', 18), converter.LastContext, "確定の直後も、記録の先頭の下位サロゲート単独は落とす");
    }

    [Test]
    public static void Context_StopsAtNewline_NotAtSentenceEnd()
    {
        Assert.Equal("今日は雨。明日は晴れ。", ContextAfterTyping("昨日の話\n今日は雨。明日は晴れ。", true), "最後の改行より後ろだけ。「。」では切らない");
        Assert.Equal("次の段落", ContextAfterTyping("前の段落です。\r\n次の段落", true), "CRLF でも最後の改行の後ろ");
        Assert.True(ContextAfterTyping("前の段落です。\n", true) is null, "改行の後ろが空なら文脈なし");
    }

    [Test]
    public static void Context_IsNullAfterEnglish()
    {
        Assert.True(ContextAfterTyping("今日はGitHub", true) is null, "最後が英字なら文脈なし (今までどおり)");
    }

    [Test]
    public static void ClauseRule_StillAppliesToWideContext()
    {
        // 前の文に「記号等」があると、変換エンジンは き|ごうとう と区切りを変える。広い文脈でも、区切りが違えば文脈なしを採る
        var k = new CompositionTests.Keyboard { WideContext = true };
        k.Host.PrecedingText = "文章を「記号等」含め。";
        k.Type("kigoutou ");
        Assert.Equal("文章を「記号等」含め。", k.Converter.LastContext, "広い文脈で変換エンジンに問い合わせている (規則が働く場面)");
        Assert.Equal("記号|等", string.Join("|", k.Host.View!.Clauses!), "区切りが変わるなら文脈なしの 記号|等");
        // 区切りが変わらない語では、広い文脈が効く
        Assert.Equal("この本は。", ContextAfterTyping("この本は。", true), "区切りが同じなら文脈つきで変換する");
    }

    /// <summary>読みをカタカナにして返し、呼ばれた読みと文脈をすべて記録する変換エンジン。</summary>
    private sealed class RecordingConverter : IKanjiConverter
    {
        public List<(string Reading, string? Context)> Calls { get; } = [];

        public string? Convert(string hiragana) => CompositionText.ToKatakana(hiragana);

        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null)
        {
            Calls.Add((hiragana, context));
            return [new(hiragana, CompositionText.ToKatakana(hiragana))];
        }
    }

    [Test]
    public static void LongReading_ChunkContextComesBeforeWideContext()
    {
        // 50 文字を超える読みは区切って変換し、2 つ目以降の区切りには前の区切りの変換結果を文脈にする (_chunkContext)。
        // 広い文脈 (前の文) は最初の区切りにだけ渡す。2 つ目以降で、前の区切りの変換結果より先に効かせない
        var converter = new RecordingConverter();
        var k = new CompositionTests.Keyboard(converter: converter) { WideContext = true };
        k.Host.PrecedingText = "大学の先生の話を聞いた。";
        k.Type(new string('a', CompositionController.ConvertChunkLength + 10) + " ");
        var first = new string('あ', CompositionController.ConvertChunkLength);
        Assert.True(converter.Calls.Contains((first, "大学の先生の話を聞いた。")), "最初の区切りには広い文脈を渡す (広い文脈が効いている場面)");
        var contexts = converter.Calls.Where(c => c.Reading == new string('あ', 10) && c.Context is not null).Select(c => c.Context).Distinct();
        Assert.Equal("アアアアアアアアアア", string.Join(" / ", contexts), "2 つ目の区切りには、前の区切りの変換結果 (の末尾 10 文字) を渡す。広い文脈ではない");
    }

    [Test]
    public static void NiStart_UsesRealContextInsteadOfKore()
    {
        Assert.Equal("これ", ContextAfterTyping("前の文です。", false, "ninattesimau "), "OFF + 前が空: 仮の文脈「これ」(今までどおり)");
        Assert.Equal("前の文です。", ContextAfterTyping("前の文です。", true, "ninattesimau "), "ON + 前の文あり: 仮の文脈ではなく本物の文脈");
        Assert.Equal("これ", ContextAfterTyping("", true, "ninattesimau "), "ON でも前が無ければ仮の文脈");
    }

    [Test]
    public static void CreateDefault_WideContextProvider()
    {
        var fake = new CompositionTests.FakeConverter();
        var mozc = new MozcConverter("/nonexistent/meltype_mozc_helper", null);
        Assert.True(MeltypeSession.WideContextProvider(fake, () => true)(), "Zenzai ON + Mozc でない変換エンジン → 広い文脈");
        Assert.True(!MeltypeSession.WideContextProvider(fake, () => false)(), "Zenzai OFF → 今までどおり");
        Assert.True(!MeltypeSession.WideContextProvider(mozc, () => true)(), "Mozc は Zenzai が ON でも今までどおり");
        var on = false;
        var provider = MeltypeSession.WideContextProvider(fake, () => on);
        on = true;
        Assert.True(provider(), "設定を毎回読む (その場で効く)");
    }
}
