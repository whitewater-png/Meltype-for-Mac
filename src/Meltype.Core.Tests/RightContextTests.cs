// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>
/// 要件 051: 広い文脈が ON (Zenzai が ON で Mozc でない) で、変換エンジンが右文脈を受け取れる (<see cref="IRightContextConverter"/>) とき、
/// キャレットの後ろの文字 (最初の改行まで・先頭 20 単位) を右文脈として渡す。OFF・右が空・英字で始まるときは今までどおり。
/// </summary>
internal static class RightContextTests
{
    /// <summary>読みをかなのまま 1 文節にし、呼ばれた (読み, 左文脈, 右文脈) を覚える変換エンジン。</summary>
    private sealed class RecordingConverter : IKanjiConverter, IRightContextConverter
    {
        public List<(string Kana, string? Context, string? Right)> Calls { get; } = [];

        public string? Convert(string hiragana) => null;

        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null)
        {
            Calls.Add((hiragana, context, null));
            return [new(hiragana, hiragana)];
        }

        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context, string rightContext)
        {
            Calls.Add((hiragana, context, rightContext));
            return [new(hiragana, hiragana)];
        }
    }

    private static RecordingConverter Type(string? before, string? after, bool wide = true, string romaji = "hashi ")
    {
        var converter = new RecordingConverter();
        var k = new CompositionTests.Keyboard(converter: converter) { WideContext = wide };
        k.Host.PrecedingText = before;
        k.Host.FollowingText = after;
        k.Type(romaji);
        return converter;
    }

    /// <summary>その読みの要求に付いた右文脈 (重複なし、"|" でつなぐ。右文脈なしは "-")。</summary>
    private static string RightsFor(RecordingConverter converter, string kana) =>
        string.Join("|", converter.Calls.Where(c => c.Kana == kana).Select(c => c.Right ?? "-").Distinct());

    [Test]
    public static void RightContext_IsPassedWhenOn()
    {
        var converter = Type(null, "でご飯を食べる");
        Assert.Equal("でご飯を食べる", RightsFor(converter, "はし"), "キャレットの後ろの文字を右文脈として渡す");
    }

    [Test]
    public static void RightContext_IsNotPassedWhenOffOrEmpty()
    {
        Assert.Equal("-", RightsFor(Type(null, "でご飯を食べる", wide: false), "はし"), "広い文脈が OFF (Zenzai OFF・Mozc) なら渡さない");
        Assert.Equal("-", RightsFor(Type(null, null), "はし"), "後ろに文字が無ければ今までどおり");
        Assert.Equal("-", RightsFor(Type(null, ""), "はし"), "空なら今までどおり");
        Assert.Equal("-", RightsFor(Type(null, "  "), "はし"), "空白だけなら渡さない");
        Assert.Equal("-", RightsFor(Type(null, "\nでご飯を食べる"), "はし"), "最初が改行 (次の行は別の文) なら渡さない");
        Assert.Equal("-", RightsFor(Type(null, "GitHub で"), "はし"), "英字で始まるなら渡さない");
    }

    [Test]
    public static void RightContext_StopsAtNewlineAndTwentyUnits()
    {
        Assert.Equal("でご飯", RightsFor(Type(null, "でご飯\n食べる"), "はし"), "最初の改行より前だけ");
        Assert.Equal("でご飯", RightsFor(Type(null, "でご飯\u2028食べる"), "はし"), "行区切り (U+2028) でも切る");
        var after = "あいうえおかきくけこさしすせそたちつてとなにぬねの"; // 25 文字
        Assert.Equal(after[..20], RightsFor(Type(null, after), "はし"), "先頭 20 単位だけ");
        var surrogate = new string('い', 19) + "\U00020BB7"; // 21 単位。先頭 20 単位の最後が 𠮷 の上位サロゲートになる
        Assert.Equal(new string('い', 19), RightsFor(Type(null, surrogate), "はし"), "最後の上位サロゲート単独は落とす");
    }

    [Test]
    public static void RightContext_IsPassedToBothRequestsWithLeftContext()
    {
        // 左文脈があるときは、左文脈ありとなしの 2 つを比べる。両方に同じ右文脈を付ける
        var converter = Type("今日は", "でご飯を食べる");
        var calls = converter.Calls.Where(c => c.Kana == "はし").ToList();
        Assert.True(calls.Any(c => c.Context == "今日は"), "左文脈ありの要求がある");
        Assert.True(calls.Any(c => c.Context is null), "左文脈なしの要求がある");
        Assert.True(calls.All(c => c.Right == "でご飯を食べる"), "どちらにも同じ右文脈を付ける");
    }

    [Test]
    public static void RightContext_OnlyForTheLastChunkOfALongReading()
    {
        // 50 文字を超える読みは区切って変換する。途中の区切りの右は続きの読みなので、入力欄の後ろの文字は最後の区切りにだけ渡す
        var converter = Type(null, "でご飯を食べる", romaji: string.Concat(Enumerable.Repeat("aiueo", 11)) + "sasisuseso "); // 65 文字
        var withRight = converter.Calls.Where(c => c.Right is not null).Select(c => c.Kana).Distinct().ToList();
        Assert.True(withRight.Count > 0, "最後の区切りには渡す");
        Assert.True(withRight.All(kana => kana.Length < 65 && kana.EndsWith("さしすせそ")), $"右文脈を付けたのは最後の区切りだけ ({string.Join(",", withRight)})");
        Assert.True(converter.Calls.Any(c => c.Right is null && c.Kana.Length == 50 && c.Kana.StartsWith("あいうえお")), "前の区切りには付けない");
    }

    [Test]
    public static void RightContext_PlainConverterIsUnchanged()
    {
        // IRightContextConverter を持たない変換エンジン (Windows・Mozc・テストの偽物) は、今までどおり 2 引数で呼ばれる
        var k = new CompositionTests.Keyboard { WideContext = true };
        k.Host.FollowingText = "を取る";
        k.Type("tanniwotoru ");
        Assert.Equal("単位を取る", k.Host.View?.Text ?? string.Concat(k.Host.Output), "右文脈を受け取れないエンジンでも変換できる");
    }

    [Test]
    public static void RightContextOf_Rules()
    {
        Assert.Equal("を取る", CompositionController.RightContextOf("を取る"), "そのまま");
        Assert.True(CompositionController.RightContextOf(null) is null, "null");
        Assert.True(CompositionController.RightContextOf("abc の") is null, "英字で始まる");
        Assert.Equal("、それで", CompositionController.RightContextOf("、それで"), "句読点で始まってもよい");
    }
}
