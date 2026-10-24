// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

// 格子状の候補 (Tab で広げた 5 列) では、矢印キーを格子の向きに動かす。
// ← → は 1 つ前後、↑ ↓ は 1 行 (5 つ) 前後。

using Meltype.Composition;
using Meltype.Input;

namespace Meltype.Tests;

internal static class GridNavigationTests
{
    private static IReadOnlyList<string> Many(string reading) =>
        reading == "かわ" ? Enumerable.Range(1, 32).Select(i => "候補" + i).ToList() : [];

    private static CompositionTests.Keyboard Grid()
    {
        var k = new CompositionTests.Keyboard(moreCandidates: Many) { TabGrid = true };
        k.Type("kawa ");
        k.Press(VirtualKeys.Tab);
        Assert.True(k.Host.View?.Grid == true, "前提: 格子");
        Assert.True(k.Host.View!.Candidates!.Count > 12, "前提: 3 行以上ある");
        return k;
    }

    private static int Index(CompositionTests.Keyboard k) => k.Host.View!.SelectedIndex;

    [Test]
    public static void Down_MovesOneRow()
    {
        var k = Grid();
        var at = Index(k);
        k.Press(VirtualKeys.Down);
        Assert.Equal(at + 5, Index(k), "↓ で 1 行下 (5 つ先)");
    }

    [Test]
    public static void Up_MovesOneRowBack()
    {
        var k = Grid();
        k.Press(VirtualKeys.Down);
        k.Press(VirtualKeys.Down);
        var at = Index(k);
        k.Press(VirtualKeys.Up);
        Assert.Equal(at - 5, Index(k), "↑ で 1 行上");
    }

    [Test]
    public static void RightAndLeft_MoveOneCell()
    {
        var k = Grid();
        var at = Index(k);
        k.Press(VirtualKeys.Right);
        Assert.Equal(at + 1, Index(k), "→ で 1 つ右");
        k.Press(VirtualKeys.Left);
        Assert.Equal(at, Index(k), "← で 1 つ左");
        Assert.True(k.Host.View!.Converting, "変換のまま");
        Assert.True(k.Host.View!.Grid == true, "格子のまま");
    }

    [Test]
    public static void Down_WrapsToTheTopOfTheSameColumn()
    {
        var k = Grid();
        var n = k.Host.View!.Candidates!.Count;
        var column = Index(k) % 5;
        for (var i = 0; i < n; i++) k.Press(VirtualKeys.Down); // 最下段を越えるまで下へ
        var at = Index(k);
        Assert.True(at >= 0 && at < n, "範囲内: " + at);
        Assert.Equal(column, at % 5, "列は保つ");
    }

    [Test]
    public static void Up_AtTheTopWrapsToTheBottomOfTheSameColumn()
    {
        var k = Grid();
        // Tab で 1 つ目 (index 1) にいる。↑ で同じ列の最下段へ。
        var column = Index(k) % 5;
        var n = k.Host.View!.Candidates!.Count;
        k.Press(VirtualKeys.Up);
        var at = Index(k);
        Assert.True(at > 5 && at < n, "最下段に回る: " + at);
        Assert.Equal(column, at % 5, "列を保つ");
        Assert.True(at + 5 >= n, "その列の最後の行");
    }

    [Test]
    public static void WithoutGrid_ArrowsStayAsBefore()
    {
        var k = new CompositionTests.Keyboard(moreCandidates: Many) { TabGrid = false };
        k.Type("kawa ");
        var at = Index(k);
        k.Press(VirtualKeys.Down);
        Assert.Equal(at + 1, Index(k), "格子でなければ ↓ は 1 つ次 (今までどおり)");
    }

    [Test]
    public static void Grid_RightStillMovesClauseWhenNotGrid()
    {
        var k = new CompositionTests.Keyboard { TabGrid = true };
        k.Type("tanniwotoru ");
        k.Press(VirtualKeys.Right);
        Assert.Equal(1, k.Host.View!.SelectedClause, "Tab を押すまでは → は文節移動");
    }
}
