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

    /// <summary>
    /// Q18: 打ち直しが英語で終わる (英語だけ / 日本語 + 英語 / パス / 保護区間) と、Space は変換ではなく
    /// 「確定して空白」になる (Commit() を通らず CommitText を直接呼ぶ経路がある)。その経路でも、
    /// 後ろの文節 (知らん) が Enter と同じく確定に残ること。表示は「え + 打ち直し + 知らん」で、確定もそのとおり。
    /// SpaceCommits = false の行 (-) は Space で変換が始まる (今までどおり) ことの守り。
    /// </summary>
    private static readonly (string Retype, string Shown, bool SpaceCommits)[] EnglishEndingRetypes =
    [
        ("github", "えgithub知らん", true),          // 英語だけ
        ("wogithub", "えをgithub知らん", true),      // 日本語 + 英語 (Q18 の再現手順)
        ("my", "えmy知らん", true),                  // 英語の短い語 (IsEnglishAtWordEnd の経路。査読 F2-1)
        ("./build.sh", "え。/build.sh知らん", true), // パス。日本語 (え) の直後なので保護区間にはならず . は 。 だが、英語で終わる確定の経路
        ("src/main.rs", "えsrc/main.rs知らん", true), // 保護区間 (/ を含む英字の並び)
        ("-", "えー知らん", false),                  // 箇条書きの - は日本語の直後では ー (Space は変換)
    ];

    private static CompositionTests.Keyboard RetypeAfterDeletingTheMiddleClause(string retype)
    {
        var k = new CompositionTests.Keyboard { DeleteClauseOnBackspace = true };
        k.Type("e,shiran ");
        Assert.Equal("え|、|知らん", Clauses(k), "前提: 3 文節");
        k.Press(VirtualKeys.Right);
        k.Press(VirtualKeys.Back);
        k.Type(retype);
        return k;
    }

    [Test]
    public static void On_Enter_AfterRetypeEndingWithEnglish_KeepsTheTail()
    {
        foreach (var (retype, shown, _) in EnglishEndingRetypes)
        {
            var k = RetypeAfterDeletingTheMiddleClause(retype);
            Assert.Equal(shown, k.Host.View!.Text, $"前提 ({retype}): 表示は 前 + 打ち直し + 後ろ");
            k.Type("\n");
            Assert.Equal(shown, k.Host.Output.Single(), $"Enter ({retype}): 後ろの文節 (知らん) も確定に残る");
        }
    }

    [Test]
    public static void On_Space_AfterRetypeEndingWithEnglish_KeepsTheTail()
    {
        foreach (var (retype, shown, spaceCommits) in EnglishEndingRetypes)
        {
            var k = RetypeAfterDeletingTheMiddleClause(retype);
            Assert.Equal(shown, k.Host.View!.Text, $"前提 ({retype}): 表示は 前 + 打ち直し + 後ろ");
            k.Type(" ");
            if (spaceCommits)
            {
                Assert.Equal(1, k.Host.Output.Count, $"Space ({retype}): 英語で終わるので変換ではなく確定する");
                Assert.Equal(shown + " ", k.Host.Output.Single(), $"Space ({retype}): 後ろの文節 (知らん) も確定に残り、空白はその後ろ (Enter の確定 + 空白)");
            }
            else
            {
                Assert.True(k.Host.View!.Converting, $"Space ({retype}): 日本語で終わるので今までどおり変換が始まる");
                Assert.Equal(0, k.Host.Output.Count, $"Space ({retype}): まだ確定しない");
                k.Type("\n");
                Assert.Equal(shown, k.Host.Output.Single(), $"Space → Enter ({retype}): 後ろの文節 (知らん) も確定に残る");
            }
        }
    }

    [Test]
    public static void On_Space_AfterRetypeEndingWithEnglish_DoesNotCountAsAnEnglishCommit()
    {
        // 後ろの文節 (知らん) が日本語なので、この確定は「英語で終わった確定」ではない (Commit() と同じ旗の補正)。
        // 英語で終わった確定の直後だけ、数字だけ (2) の Space は変換せずに確定する (I have 2 cats)。
        // 日本語で終わったあとの 2 + Space は、今までどおり変換して ② 二 などの候補を出す。
        var k = RetypeAfterDeletingTheMiddleClause("wogithub");
        k.Type(" ");
        Assert.Equal("えをgithub知らん ", k.Host.Output.Single(), "前提: 確定した");
        k.Type("2 ");
        Assert.Equal(1, k.Host.Output.Count, "日本語 (知らん) で終わった確定の後なので、2 + Space は確定せず変換する");
        Assert.True(k.Host.View!.Converting, "2 の候補 (② 二 など) を出す変換中");
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
    public static void On_AfterContinueAfterConversion_DeletesTheMiddleClause()
    {
        // 変換後も続けて入力 (ContinueAfterConversion) で前の文節が固定された状態でも、
        // ← → で真ん中の文節を選んで BackSpace を押したら、その文節だけを消せる (全体を取り消さない)。
        var k = new CompositionTests.Keyboard { DeleteClauseOnBackspace = true, Continue = true };
        k.Type("tanniwotoru ");
        Assert.Equal("単位を|取る", Clauses(k), "前提: 2 文節");
        k.Type("koto "); // Continue: 「単位を取る」を固定して「こと」を変換
        Assert.Equal("単位を|取る|こと", Clauses(k), "前提: 固定 + 未変換");
        // 変換を始めた直後は「こと」(index 2) を選んでいる。← で真ん中の「取る」を選ぶ。
        k.Press(VirtualKeys.Left);
        Assert.Equal(1, k.Host.View!.SelectedClause, "前提: 真ん中 (取る) を選んだ");
        k.Press(VirtualKeys.Back);
        Assert.Equal("単位をこと", k.Host.View!.Text, "前の文節 (単位を) と後ろの文節 (こと) はそのまま、真ん中 (取る) だけ消える");
        Assert.True(!k.Host.View!.Converting, "打ち直しの入力中 (変換中ではない)");
    }

    [Test]
    public static void On_AfterContinueAfterConversion_DeletesTheMiddleFixedClause_WhenAllFixed()
    {
        // 全部の文節が固定された状態 (未変換の文節が無い) でも、真ん中の文節を選んで BackSpace で 1 つだけ消せる。
        var k = new CompositionTests.Keyboard { DeleteClauseOnBackspace = true, Continue = true };
        k.Type("e,shiran ");
        Assert.Equal("え|、|知らん", Clauses(k), "前提: 3 文節");
        k.Type("ka"); // Continue: 「え、知らん」を固定して「か」を未変換で持つ
        k.Press(VirtualKeys.Back); // 「か」を消して、最後の固定した文節の選択に戻す
        Assert.True(k.Host.View!.Converting, "前提: 固定した文節の選択に戻った");
        Assert.Equal(2, k.Host.View!.SelectedClause, "前提: 最後の文節を選んでいる");
        k.Press(VirtualKeys.Left); // 真ん中 (、) を選ぶ
        Assert.Equal(1, k.Host.View!.SelectedClause, "前提: 真ん中を選んだ");
        k.Press(VirtualKeys.Back);
        Assert.Equal("え知らん", k.Host.View!.Text, "前の文節 (え) と後ろの文節 (知らん) はそのまま、真ん中 (、) だけ消える");
        Assert.True(!k.Host.View!.Converting, "打ち直しの入力中 (変換中ではない)");
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
