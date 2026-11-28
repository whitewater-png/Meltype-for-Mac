// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Tests;

/// <summary>
/// 要件 056: ん (語末の n) の後ろに b・h・j・m を 1 文字打って Enter すると、表示は あんb なのに あん が入っていた (本家 #2461)。
/// n の隣のキーが b・h・j・m なので、打ち間違いの直し (語末の余計な 1 文字を消す) がいつも働いていた。表示どおりに確定する。
/// n 以外の後ろの余計な 1 文字 (yoroshikuy → よろしく) と、ほかの打ち間違いの直しは今までどおり。
/// 文書: docs/requirements/056-trailing-consonant-after-n.md
/// </summary>
internal static class TrailingConsonantAfterN056Tests
{
    [Test]
    public static void Req056_ConsonantAfterN_CommitsWhatIsShowing()
    {
        foreach (var (typed, expected) in new[]
        {
            ("anb", "あんb"), ("kanb", "かんb"), ("anm", "あんm"), ("anh", "あんh"), ("anj", "あんj"), ("sanm", "さんm"), ("sanh", "さんh"),
            ("honb", "ほんb"), ("honnb", "ほんb"), ("gohanm", "ごはんm"), ("nihonj", "にほんj"), ("kyouhaanb", "きょうはあんb"),
        })
        {
            var k = new CompositionTests.Keyboard();
            k.Type(typed);
            var showing = k.Showing;
            k.Type("\n");
            Assert.Equal(expected, k.Host.Document, typed);
            // 打っている間に見えていたものと、Enter で入るものが同じ
            Assert.Equal(showing, k.Host.Document, typed + " の表示");
        }
    }

    [Test]
    public static void Req056_ConsonantAfterN_IsNotDroppedBeforeSpace()
    {
        // Space の直前の確定も同じ打ち間違いの直しを通る。b が消えない
        var k = new CompositionTests.Keyboard();
        k.Type("anb \n");
        Assert.True(k.Host.Document.StartsWith("あんb", StringComparison.Ordinal), "anb + Space: " + k.Host.Document);
    }

    [Test]
    public static void Req056_Guards_OtherTyposStillFixed()
    {
        foreach (var (typed, expected) in new[]
        {
            // n の隣でない子音・続きを打った形は、もともと消えない
            ("anp", "あんp"), ("kanz", "かんz"), ("a-nb", "あーんb"), ("kanbu", "かんぶ"), ("honnda", "ほんだ"),
            // n 以外の後ろの余計な 1 文字は今までどおり消す
            ("yoroshikuy", "よろしく"), ("desuh", "です"), ("desuy", "です"),
            // ほかの打ち間違いの直しも今までどおり
            ("onegaishimsu", "おねがいします"), ("arigatpu", "ありがとう"), ("hazkimeru", "はじめる"),
        })
        {
            var k = new CompositionTests.Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, typed);
        }
    }
}
