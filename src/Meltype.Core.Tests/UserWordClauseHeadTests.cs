// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>
/// 短い (拍数が 3 未満の) ユーザー辞書の語が、変換エンジンの文節の頭から始まるとき、その文節の読みの一部だけを覆い、
/// 残りが助詞などでなければ使わない (要件 043。しゅう → 秋 を登録していると ごみをしゅうしゅうする が ごみを秋週する になっていた)。
/// </summary>
internal static class UserWordClauseHeadTests
{
    /// <summary>読みごとに、決めた文節の区切りを返す偽の変換エンジン (表に無い読みは 1 文節でそのまま返す)。</summary>
    private sealed class MapConverter(params (string Reading, (string Reading, string Text)[] Clauses)[] map) : IKanjiConverter
    {
        public string? Convert(string hiragana) => ConvertClauses(hiragana) is { } c ? string.Concat(c.Select(x => x.Text)) : null;
        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null)
        {
            foreach (var (reading, clauses) in map)
                if (reading == hiragana) return clauses.Select(c => new ConversionClause(c.Reading, c.Text)).ToList();
            return [new(hiragana, hiragana)];
        }
    }

    private static (string Reading, string Text)[] C(params (string, string)[] clauses) => clauses;

    private static readonly (string Reading, (string Reading, string Text)[] Clauses)[] Engine =
    [
        ("ごみを", C(("ごみを", "ゴミを"))),
        ("ごみをしゅうしゅうする", C(("ごみを", "ゴミを"), ("しゅうしゅうする", "収集する"))),
        ("しゅうしゅうする", C(("しゅうしゅうする", "収集する"))),
        ("ごみをしゅうしゅうした", C(("ごみを", "ゴミを"), ("しゅうしゅうした", "収集した"))),
        ("しゅうかんする", C(("しゅうかんする", "習慣する"))),
        ("しゅうちゅうする", C(("しゅうちゅうする", "集中する"))),
        ("しゅうせいする", C(("しゅうせいする", "修正する"))),
        ("しゅうりょうする", C(("しゅうりょうする", "終了する"))),
        ("しゅうまつ", C(("しゅうまつ", "週末"))),
        ("しゅうさん", C(("しゅうさん", "集散"))),
        ("しゅうくんのいえ", C(("しゅうくん", "習君"), ("のいえ", "の家"))),
        ("しゅうだよ", C(("しゅうだよ", "舟だよ"))),
        ("しゅうとの", C(("しゅうとの", "舅の"))),
        ("しゅうが", C(("しゅうが", "秋が"))),
        ("しゅうを", C(("しゅうを", "秋を"))),
        ("しゅうと", C(("しゅうと", "舅と"))),
        ("しゅうのいえ", C(("しゅうの", "週の"), ("いえ", "家"))),
        ("しゅう", C(("しゅう", "週"))),
    ];

    private static string Type(string typed, bool learned)
    {
        var history = new ConversionHistory(null);
        if (learned) history.Remember("しゅう", "週");
        var dictionary = new UserDictionary(null, builtIn: false);
        dictionary.Add("しゅう", "秋");
        var k = new CompositionTests.Keyboard(converter: new MapConverter(Engine), history: history, userDictionary: dictionary);
        k.Type(typed + " ");
        return string.Join("|", k.Host.View!.Clauses!);
    }

    [Test]
    public static void ShortWord_ThatCoversOnlyPartOfAnEngineClause_IsNotUsed()
    {
        foreach (var learned in new[] { false, true })
        {
            Assert.Equal("ゴミを|収集する", Type("gomiwoshuushuusuru", learned), $"ごみをしゅうしゅうする (学習 {learned})");
            Assert.Equal("収集する", Type("shuushuusuru", learned), $"しゅうしゅうする (学習 {learned})");
            Assert.Equal("ゴミを|収集した", Type("gomiwoshuushuushita", learned), $"ごみをしゅうしゅうした (学習 {learned})");
            Assert.Equal("習慣する", Type("shuukansuru", learned), $"しゅうかんする (学習 {learned})");
            Assert.Equal("集中する", Type("shuuchuusuru", learned), $"しゅうちゅうする (学習 {learned})");
            Assert.Equal("修正する", Type("shuuseisuru", learned), $"しゅうせいする (学習 {learned})");
            Assert.Equal("終了する", Type("shuuryousuru", learned), $"しゅうりょうする (学習 {learned})");
            Assert.Equal("週末", Type("shuumatsu", learned), $"しゅうまつ (学習 {learned})");
        }
    }

    [Test]
    public static void ShortWord_WithParticle_OrAlone_IsStillUsed()
    {
        foreach (var learned in new[] { false, true })
        {
            var word = learned ? "週" : "秋";   // 学習があれば登録語の文節の最初の候補は学習の語
            Assert.True(Type("shuuga", learned).StartsWith(word), $"しゅうが は登録語から始まる (学習 {learned}): " + Type("shuuga", learned));
            Assert.True(Type("shuuwo", learned).StartsWith(word), $"しゅうを は登録語から始まる (学習 {learned}): " + Type("shuuwo", learned));
            Assert.True(Type("shuuto", learned).StartsWith(word), $"しゅうと は登録語から始まる (学習 {learned}): " + Type("shuuto", learned));
            Assert.True(Type("shuunoie", learned).StartsWith(word), $"しゅうのいえ は登録語から始まる (学習 {learned}): " + Type("shuunoie", learned));
            Assert.True(Type("shuudayo", learned).StartsWith(word), $"しゅうだよ は登録語から始まる (学習 {learned}): " + Type("shuudayo", learned));
            Assert.True(Type("shuutono", learned).StartsWith(word), $"しゅうとの (舅の と読めるが、登録語 + との を使う。要件 043 の損) (学習 {learned}): " + Type("shuutono", learned));
            Assert.True(Type("shuusan", learned).StartsWith(word), $"しゅうさん (敬称) は登録語から始まる (学習 {learned}): " + Type("shuusan", learned));
            Assert.True(Type("shuukunnnoie", learned).StartsWith(word), $"しゅうくんのいえ は登録語から始まる (学習 {learned}): " + Type("shuukunnnoie", learned));
            Assert.Equal(word, Type("shuu", learned), $"しゅう だけ (学習 {learned})");
        }
    }

    [Test]
    public static void UnusedShortWord_CanStillBePickedByResizingTheClause()
    {
        // 使わなかった登録語は、文節を しゅう まで縮めれば候補に出る (Shift+←。登録語が候補から消えるわけではない)
        var dictionary = new UserDictionary(null, builtIn: false);
        dictionary.Add("しゅう", "秋");
        var k = new CompositionTests.Keyboard(converter: new MapConverter(Engine), userDictionary: dictionary);
        k.Type("gomiwoshuushuusuru ");
        Assert.Equal("ゴミを|収集する", string.Join("|", k.Host.View!.Clauses!), "前提: 登録語は割り込まない");
        k.Press(VirtualKeys.Right);
        k.Key(VirtualKeys.LShift);
        for (var i = 0; i < 5; i++) k.Press(VirtualKeys.Left);
        k.Key(VirtualKeys.LShift, up: true);
        var view = k.Host.View!;
        Assert.True(view.Candidates.Contains("秋"), "文節を縮めれば登録語を候補から選べる: " + string.Join(",", view.Candidates) + " / " + string.Join("|", view.Clauses!));
    }
}
