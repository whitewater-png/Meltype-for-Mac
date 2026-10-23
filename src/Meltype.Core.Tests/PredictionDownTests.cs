// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Input;

namespace Meltype.Tests;

/// <summary>
/// 本家 #993: 入力中に ↓ を押したとき、予測候補を選ぶか (設定「↓ で予測候補を選ぶ」)。
/// 既定は OFF (今までどおり Space と同じく変換を始める)。
/// </summary>
internal static class PredictionDownTests
{
    [Test]
    public static void DownSelectsPrediction_EntersPredictionSelection()
    {
        var k = new CompositionTests.Keyboard(predictions: _ => ["今日", "京都"]) { DownSelectsPrediction = true };
        k.Type("kyou");
        Assert.True(k.Host.View?.Predictions is { Count: > 0 }, "前提: 予測候補が出ている");
        Assert.Equal(-1, k.Host.View!.SelectedPrediction, "最初は選んでいない");
        k.Press(VirtualKeys.Down);
        Assert.Equal(0, k.Host.View!.SelectedPrediction, "↓ で予測の選択に入る");
        Assert.True(!k.Host.View!.Converting, "変換は始めない");
    }

    [Test]
    public static void DownWithoutSetting_StillStartsConversion()
    {
        var k = new CompositionTests.Keyboard(predictions: _ => ["今日", "京都"]);
        k.Type("kyou");
        Assert.True(k.Host.View?.Predictions is { Count: > 0 }, "前提: 予測候補が出ている");
        k.Press(VirtualKeys.Down);
        Assert.Equal(-1, k.Host.View!.SelectedPrediction, "↓ では予測を選ばない");
        Assert.True(k.Host.View!.Converting, "今までどおり変換を始める");
    }

    [Test]
    public static void DownWithShift_SelectsLastPrediction()
    {
        var k = new CompositionTests.Keyboard(predictions: _ => ["今日", "京都"]) { DownSelectsPrediction = true };
        k.Type("kyou");
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Down);
        k.Key(VirtualKeys.LShift, up: true);
        Assert.Equal(1, k.Host.View!.SelectedPrediction, "Shift+↓ で最後の予測");
        Assert.True(!k.Host.View!.Converting, "変換は始めない");
    }

    [Test]
    public static void DownThenDown_SelectsNextPrediction()
    {
        var k = new CompositionTests.Keyboard(predictions: _ => ["今日", "京都", "きょうと"]) { DownSelectsPrediction = true };
        k.Type("kyou");
        k.Press(VirtualKeys.Down);
        Assert.Equal(0, k.Host.View!.SelectedPrediction, "1 回目で先頭");
        k.Press(VirtualKeys.Down);
        Assert.Equal(1, k.Host.View!.SelectedPrediction, "2 回目で次");
    }

    [Test]
    public static void DownThenEnter_CommitsPrediction()
    {
        var k = new CompositionTests.Keyboard(predictions: _ => ["今日", "京都"]) { DownSelectsPrediction = true };
        k.Type("kyou");
        k.Press(VirtualKeys.Down);
        k.Press(VirtualKeys.Return);
        Assert.Equal("今日", k.Host.Document, "Enter で予測が確定する");
        Assert.True(k.Host.View is null, "確定したら変換ボックスは閉じる");
    }

    [Test]
    public static void DownWithoutPredictions_StartsConversion()
    {
        var k = new CompositionTests.Keyboard(predictions: _ => []) { DownSelectsPrediction = true };
        k.Type("kyou");
        Assert.True(k.Host.View?.Predictions is not { Count: > 0 }, "前提: 予測候補が出ていない");
        k.Press(VirtualKeys.Down);
        Assert.Equal(-1, k.Host.View!.SelectedPrediction, "予測を選ばない");
        Assert.True(k.Host.View!.Converting, "予測が無ければ今までどおり変換を始める");
    }
}
