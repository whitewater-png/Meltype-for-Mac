// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>
/// 短いユーザー辞書の語 + 付属語 (敬称など) が、変換エンジンの 1 文節 (しゅうさん → 集散) を割ったとき、
/// その文節の候補の 2 番目に、エンジンの文節の変換 (集散) を置く。選ぶと文節が元の区切りに戻る。選んだ変換は学習する (要件 044)。
/// 登録語は しゅう → 秋 (実際の語はここに書かない)。
/// </summary>
internal static class UserWordFallbackTests
{
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

    private static (string, string)[] C(params (string, string)[] clauses) => clauses;

    private static readonly (string Reading, (string Reading, string Text)[] Clauses)[] Engine =
    [
        ("しゅうさん", C(("しゅうさん", "集散"))),
        ("しゅうさんがきた", C(("しゅうさん", "集散"), ("がきた", "が来た"))),
        ("しゅうくん", C(("しゅうくん", "習君"))),
        ("しゅうが", C(("しゅうが", "秋が"))),
        ("しゅうがきた", C(("しゅうが", "秋が"), ("きた", "来た"))),
        ("しゅうを", C(("しゅうを", "週を"))),
        ("しゅう", C(("しゅう", "週"))),
    ];

    private static CompositionTests.Keyboard NewKeyboard(ConversionHistory? history = null)
    {
        var dictionary = new UserDictionary(null, builtIn: false);
        dictionary.Add("しゅう", "秋");
        return new CompositionTests.Keyboard(converter: new MapConverter(Engine), history: history, userDictionary: dictionary);
    }

    private static string Clauses(CompositionTests.Keyboard k) => string.Join("|", k.Host.View!.Clauses!);

    [Test]
    public static void ShortWordWithTitle_SecondCandidateIsTheEngineClause_AndPickingItRestoresTheClause()
    {
        var k = NewKeyboard();
        k.Type("shuusan ");
        Assert.Equal("秋|さん", Clauses(k), "Space 1 回目は今までどおり登録語 + さん");
        Assert.Equal("集散", k.Host.View!.Candidates[1], "候補の 2 番目にエンジンの文節の変換: " + string.Join(",", k.Host.View.Candidates));
        k.Type(" ");
        Assert.Equal("集散", Clauses(k), "Space 2 回目で文節が しゅうさん に戻り 集散");
        k.Press(VirtualKeys.Return);
        Assert.Equal("集散", string.Concat(k.Host.Output));
    }

    [Test]
    public static void ShortWordWithTitle_PickingTheEngineClause_KeepsTheRestOfTheSentence()
    {
        var k = NewKeyboard();
        k.Type("shuusangakita ");
        Assert.Equal("秋|さんがきた", Clauses(k), "前提 (偽のエンジンは さんがきた を 1 文節で返す)");
        k.Type(" ");
        Assert.Equal("集散|がきた", Clauses(k), "後ろの文節は読みのまま変換し直す");
        k.Press(VirtualKeys.Return);
        Assert.Equal("集散がきた", string.Concat(k.Host.Output));
    }

    [Test]
    public static void ShortWordWithTitle_PickingByNumber_RestoresTheClauseToo()
    {
        var k = NewKeyboard();
        k.Type("shuusan ");
        var n = k.Host.View!.Candidates.ToList().IndexOf("集散") + 1;
        k.Key(0x30 + n);
        Assert.Equal("集散", string.Concat(k.Host.Output));
    }

    [Test]
    public static void ShortWordWithParticle_StaysOneSpace_AndGetsNoExtraCandidate()
    {
        foreach (var typed in new[] { "shuuga", "shuuwo", "shuu" })
        {
            var k = NewKeyboard();
            k.Type(typed + " ");
            Assert.True(Clauses(k).StartsWith("秋"), typed + " は 1 回で登録語: " + Clauses(k));
            k.Press(VirtualKeys.Return);
            Assert.True(string.Concat(k.Host.Output).StartsWith("秋"), typed + " の確定: " + string.Concat(k.Host.Output));
        }
        var title = NewKeyboard();
        title.Type("shuukun ");
        Assert.Equal("習君", title.Host.View!.Candidates[1], "くん も敬称なので同じ (習君)");
        var particle = NewKeyboard();
        particle.Type("shuuga ");
        Assert.True(!particle.Host.View!.Candidates.Contains("秋が"), "登録語 + 助詞と同じ形のエンジンの文節 (秋が) は足さない: " + string.Join(",", particle.Host.View.Candidates));
    }

    [Test]
    public static void ChosenEngineClause_IsLearned_AndComesFirstNextTime()
    {
        var history = new ConversionHistory(null);
        var first = NewKeyboard(history);
        first.Type("shuusan  ");
        first.Press(VirtualKeys.Return);
        Assert.Equal("集散", string.Concat(first.Host.Output));
        Assert.Equal("集散", history.Get("しゅうさん"), "選んだ変換は学習する");
        var second = NewKeyboard(history);
        second.Type("shuusan ");
        Assert.Equal("集散", Clauses(second), "次の しゅうさん は 1 回で 集散");
        // 学習しても、名前 + 助詞は 1 回で登録語のまま
        var other = NewKeyboard(history);
        other.Type("shuuga ");
        Assert.True(Clauses(other).StartsWith("秋"), Clauses(other));
    }

    [Test]
    public static void UnlearnedTitle_StaysRegisteredWordFirst_WhenSomeoneElseLearnedAnotherReading()
    {
        var history = new ConversionHistory(null);
        history.Remember("しゅうくん", "習君");
        var k = NewKeyboard(history);
        k.Type("shuusan ");
        Assert.Equal("秋|さん", Clauses(k), "別の読みの学習は しゅうさん に影響しない");
    }

    [Test]
    public static void EngineClause_StaysSecond_EvenWhenTheEngineAddsManyCandidates()
    {
        var dictionary = new UserDictionary(null, builtIn: false);
        dictionary.Add("しゅう", "秋");
        var k = new CompositionTests.Keyboard(converter: new MapConverter(Engine), userDictionary: dictionary,
            moreCandidates: reading => reading == "しゅう" ? ["しゅう", "週", "州", "集"] : []);
        k.Type("shuusan ");
        Assert.Equal("秋,集散", string.Join(",", k.Host.View!.Candidates.Take(2)), "エンジンの候補が増えても 2 番目: " + string.Join(",", k.Host.View.Candidates));
        Assert.True(k.Host.View.Candidates.Contains("週"), "エンジンの候補も出る");
        k.Type(" ");
        Assert.Equal("集散", Clauses(k));
    }

    [Test]
    public static void AfterLearningTheEngineClause_TheRegisteredWordIsStillReachableByShrinking()
    {
        var history = new ConversionHistory(null);
        history.Remember("しゅうさん", "集散");
        var k = NewKeyboard(history);
        k.Type("shuusan ");
        Assert.Equal("集散", Clauses(k), "前提: 学習したので 1 回で 集散");
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Left);
        k.Press(VirtualKeys.Left);
        k.Key(VirtualKeys.LShift, up: true);
        Assert.True(Clauses(k).StartsWith("しゅう") || k.Host.View!.Candidates.Contains("秋"), "縮めれば登録語が出る: " + Clauses(k) + " / " + string.Join(",", k.Host.View!.Candidates));
        Assert.True(k.Host.View!.Candidates.Contains("秋"), string.Join(",", k.Host.View.Candidates));
    }
}
