// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

// Shift+←→ で文節を伸縮したあとも、その文節の候補一覧 (エンジンの追加候補) が引かれる。
// 伸縮前に候補を開いていた文節は Expanded のままだったので、作り直した候補が 3〜4 件に固定されていた (Q26)。

using Meltype.Composition;
using Meltype.Input;

namespace Meltype.Tests;

internal static class ResizeCandidatesTests
{
    private static readonly List<string> Asked = [];

    private static IReadOnlyList<string> Twenty(string reading)
    {
        Asked.Add(reading);
        return Enumerable.Range(1, 20).Select(i => $"{reading}候補{i}").ToList();
    }

    private static CompositionTests.Keyboard Converted(string romaji)
    {
        Asked.Clear();
        var k = new CompositionTests.Keyboard(moreCandidates: Twenty);
        k.Type(romaji + " ");
        Assert.True(k.Host.View!.Converting, "前提: 変換中");
        return k;
    }

    private static void Shift(CompositionTests.Keyboard k, int vk)
    {
        k.Host.PhysicalShift = true;
        k.Key(VirtualKeys.LShift);
        k.Press(vk);
        k.Key(VirtualKeys.LShift, up: true);
        k.Host.PhysicalShift = false;
    }

    private static int Count(CompositionTests.Keyboard k) => k.Host.View!.Candidates!.Count;

    private static string Reading(CompositionTests.Keyboard k) => k.Host.View!.Clauses![k.Host.View.SelectedClause];

    [Test]
    public static void ShrinkWithShiftLeft_StillOffersEngineCandidates()
    {
        var k = Converted("kawaga");
        var before = Count(k);
        Assert.True(before >= 20, "前提: 伸縮前は追加候補を含む: " + before);
        Shift(k, VirtualKeys.Left);
        Assert.True(Reading(k).Length < "かわが".Length, "前提: 文節が縮んだ: " + Reading(k));
        Assert.True(Count(k) >= 20, "縮めた文節にも追加候補が出る (3〜4 件に固定されない): " + Count(k));
    }

    [Test]
    public static void GrowWithShiftRight_StillOffersEngineCandidates()
    {
        var k = Converted("kawa");
        Shift(k, VirtualKeys.Left);
        Shift(k, VirtualKeys.Right);
        Assert.Equal("かわ", k.Host.View!.Clauses![0], "前提: 縮めて伸ばすと元の長さ");
        Assert.True(Count(k) >= 20, "伸ばした文節にも追加候補が出る: " + Count(k));
    }

    [Test]
    public static void AfterResize_NextClauseStillOffersEngineCandidates()
    {
        var k = Converted("kawa");
        Shift(k, VirtualKeys.Left);
        k.Press(VirtualKeys.Right);
        Assert.Equal(1, k.Host.View!.SelectedClause, "前提: 隣 (押し出された 1 文字) の文節へ");
        Assert.True(Count(k) >= 20, "伸縮で生まれた隣の文節にも追加候補が出る: " + Count(k));
    }

    [Test]
    public static void Resize_AsksEngineOncePerClause_NotEveryKey()
    {
        var k = Converted("kawa");
        Asked.Clear();
        Shift(k, VirtualKeys.Left);
        var afterResize = Asked.Count;
        Assert.True(afterResize >= 1 && afterResize <= 2, "伸縮で引くのは変わった文節の分だけ: " + string.Join(",", Asked));
        k.Press(VirtualKeys.Down);
        k.Press(VirtualKeys.Down);
        Assert.Equal(afterResize, Asked.Count, "候補を動かすたびには引き直さない");
    }

    [Test]
    public static void UntouchedClause_CandidateCountUnchanged()
    {
        var k = Converted("kawa");
        var before = Count(k);
        var k2 = Converted("kawa");
        Assert.Equal(before, Count(k2), "伸縮しない文節の候補数は今までどおり");
    }
}
