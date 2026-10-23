// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

// 本家 #972: 変換中の文の一部だけを消して打ち直す。
// 変換中に ← → で文節を選んでから BackSpace を押したら、その文節だけを消して、
// 消した位置から打ち直せる (前の文節と後ろの文節は変換したまま残る)。
//
// 変換ボックスはキャレットを 1 つ (末尾) しか持てないので、後ろの文節は表示に出すだけで、
// 打ち直した文節の後ろに付けて確定する。

using Meltype.Composition;
using Meltype.Input;

namespace Meltype.Tests;

internal static class DeleteClauseOnBackspaceTests
{
    [Test]
    public static void Off_CancelsConversion_AsBefore()
    {
        var k = new CompositionTests.Keyboard { DeleteClauseOnBackspace = false };
        k.Type("tanniwotoru ");
        Assert.True(k.Host.View!.Converting, "前提: 変換中");
        Assert.Equal(2, k.Host.View.Clauses!.Count, "前提: 2 文節");
        k.Press(VirtualKeys.Right);
        Assert.Equal(1, k.Host.View!.SelectedClause, "前提: 2 番目の文節を選んだ");
        k.Press(VirtualKeys.Back);
        Assert.True(!k.Host.View!.Converting, "OFF: 今までどおり変換を取り消す");
        Assert.Equal("たんいをとる", k.Showing, "OFF: かなに戻る (文節は消えない)");
    }

    [Test]
    public static void On_DeletesTheSelectedClause_AndKeepsPrefixAndSuffix()
    {
        var k = new CompositionTests.Keyboard { DeleteClauseOnBackspace = true };
        k.Type("tanniwotoru ");
        Assert.Equal("単位を|取る", Clauses(k), "前提: 2 文節に分かれる");
        k.Press(VirtualKeys.Right);
        Assert.Equal(1, k.Host.View!.SelectedClause, "前提: 2 番目の文節を選んだ");
        k.Press(VirtualKeys.Back);
        Assert.Equal("単位を", k.Host.View!.Text, "消した文節 (取る) が消え、前の文節は残る");
        Assert.True(!k.Host.View!.Converting, "打ち直しの入力中 (変換中ではない)");
    }

    [Test]
    public static void On_DeletesTheMiddleClause_AndKeepsTheTail()
    {
        // 3 文節の真ん中を消す: 後ろの文節は表示に残り、打ち直した文節の後ろに付く。
        var k = new CompositionTests.Keyboard { DeleteClauseOnBackspace = true };
        k.Type("e,shiran ");
        Assert.Equal("え|、|知らん", Clauses(k), "前提: 3 文節");
        k.Press(VirtualKeys.Right);
        Assert.Equal(1, k.Host.View!.SelectedClause, "前提: 真ん中を選んだ");
        k.Press(VirtualKeys.Back);
        Assert.Equal("え知らん", k.Host.View!.Text, "前の文節と後ろの文節はそのまま、真ん中 (、) だけ消える");
        Assert.True(k.Host.View!.Text!.Contains("知らん"), "後ろの文節が表示に残る");
    }

    [Test]
    public static void On_Enter_CommitsPrefixNewTextAndTail()
    {
        var k = new CompositionTests.Keyboard { DeleteClauseOnBackspace = true };
        k.Type("e,shiran ");
        k.Press(VirtualKeys.Right);
        k.Press(VirtualKeys.Back);
        // 消した位置に打ち直す (「、」の代わりに「か」)
        k.Type("ka");
        k.Type(" ");
        Assert.True(k.Host.View!.Converting, "前提: 打ち直した文節を変換した");
        k.Type("\n");
        Assert.Equal("えか知らん", k.Host.Output.Single(), "前 + 打ち直し + 後ろ の順で確定する");
    }

    [Test]
    public static void On_Escape_RestoresReadingWithoutTheDeletedClause()
    {
        var k = new CompositionTests.Keyboard { DeleteClauseOnBackspace = true };
        k.Type("e,shiran ");
        k.Press(VirtualKeys.Right);
        k.Press(VirtualKeys.Back);
        k.Press(VirtualKeys.Escape);
        Assert.True(!k.Host.View!.Converting, "Esc で変換前の読みに戻る");
        Assert.Equal("えしらん", k.Showing, "消した文節 (、) は消えたまま、前と後ろは読みに戻る");
    }

    [Test]
    public static void On_BackspaceAgainWithNothingTyped_DoesNotCrash()
    {
        // 消した直後 (まだ何も打っていない) にもう一度 BackSpace: 変換前の読みに戻る。
        var k = new CompositionTests.Keyboard { DeleteClauseOnBackspace = true };
        k.Type("e,shiran ");
        k.Press(VirtualKeys.Right);
        k.Press(VirtualKeys.Back);
        k.Press(VirtualKeys.Back);
        Assert.True(!k.Host.View!.Converting, "Esc と同じく変換前の読みに戻る");
        Assert.Equal("えしらん", k.Showing, "消した文節は消えたまま");
        Assert.Equal(0, k.Host.Output.Count, "まだ確定しない");
    }

    [Test]
    public static void FirstClause_FallsBackToCancellingConversion()
    {
        // 先頭の文節 (まだ動かしていない) では消さない: Space の直後に間違えて BackSpace を押しても、
        // 打った読みが消えない (安全側)。Esc と同じく変換を取り消す。
        var k = new CompositionTests.Keyboard { DeleteClauseOnBackspace = true };
        k.Type("tanniwotoru ");
        Assert.Equal(0, k.Host.View!.SelectedClause, "前提: 先頭の文節を選んでいる");
        k.Press(VirtualKeys.Back);
        Assert.True(!k.Host.View!.Converting, "変換を取り消す");
        Assert.Equal("たんいをとる", k.Showing, "かなに戻る (文節は消えない)");
    }

    [Test]
    public static void On_WithSingleClause_FallsBackToCancellingConversion()
    {
        var k = new CompositionTests.Keyboard { DeleteClauseOnBackspace = true };
        k.Type("kyouha ");
        Assert.Equal(1, k.Host.View!.Clauses!.Count, "前提: 1 文節");
        k.Press(VirtualKeys.Back);
        Assert.Equal("きょうは", k.Showing, "1 文節のときは、今までどおり変換を取り消してかなに戻る");
        Assert.True(!k.Host.View!.Converting, "変換中ではない");
    }

    [Test]
    public static void On_AfterContinueAfterConversion_DeletesUnconvertedClause()
    {
        // 変換後も続けて入力 (固定した文節がある) ときは、今までどおり未変換の文節の変換だけを取り消す。
        var k = new CompositionTests.Keyboard { DeleteClauseOnBackspace = true, Continue = true };
        k.Type("tanniwotoru ");
        k.Type("koto ");
        Assert.Equal("単位を|取る|こと", Clauses(k), "前提: 固定 + 未変換");
        k.Press(VirtualKeys.Back);
        Assert.True(!k.Host.View!.Converting, "未変換の文節の変換だけを取り消す (今までどおり)");
        Assert.Equal("単位を取ること", k.Host.View!.Text, "固定した文節は消さない");
    }

    [Test]
    public static void Caret_IsSetToTheRetypePosition()
    {
        var k = new CompositionTests.Keyboard { DeleteClauseOnBackspace = true };
        k.Type("e,shiran ");
        k.Press(VirtualKeys.Right);
        k.Press(VirtualKeys.Back);
        Assert.Equal(1, k.Host.View!.Caret, "キャレットは消した文節の位置 (前の文節「え」の直後)");
        k.Type("ka");
        Assert.Equal(2, k.Host.View!.Caret, "打ち直した文字 (か) の後ろに進む");
    }

    private static string Clauses(CompositionTests.Keyboard k) =>
        string.Join("|", k.Host.View!.Clauses ?? []);
}
