// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>
/// B2-7 (本家 #788): 予測候補を選んでいる間の Delete で、その語を学習から消す。
/// ユーザー辞書・専門用語集は消さない。
/// </summary>
internal static class PredictionDeleteTests
{
    [Test]
    public static void ForgetText_RemovesTheText()
    {
        var history = new ConversionHistory(null);
        history.Remember("きょう", "今日");
        history.Remember("きょうと", "京都");
        history.ForgetText("今日");
        Assert.True(!history.StartingWith("きょう").Any(e => e.Text == "今日"), "今日 は消える");
        Assert.True(history.StartingWith("きょう").Any(e => e.Text == "京都"), "京都 は残る");
    }

    [Test]
    public static void DeleteWithoutSelection_IsSafe()
    {
        var history = new ConversionHistory(null);
        history.Remember("きょう", "今日");
        var k = new CompositionTests.Keyboard(history: history);
        k.Type("kyou");
        Assert.Equal(-1, k.Host.View!.SelectedPrediction, "最初は選んでいない");
        k.Press(0x2E); // 例外にならない
        Assert.True(history.StartingWith("きょう").Any(e => e.Text == "今日"), "選んでいなければ消さない");
    }

    [Test]
    public static void DeleteOnPrediction_RemovesIt()
    {
        var history = new ConversionHistory(null);
        history.Remember("きょう", "今日");
        string? forgotten = null;
        var k = new CompositionTests.Keyboard(history: history) { ForgetPrediction = text => forgotten = text };
        k.Type("kyou");
        Assert.True(k.Host.View?.Predictions?.Contains("今日") == true, "予測: " + string.Join("|", k.Host.View?.Predictions ?? []));
        Assert.Equal(-1, k.Host.View!.SelectedPrediction, "最初は選んでいない");
        k.Press(VirtualKeys.Tab);
        Assert.Equal(0, k.Host.View!.SelectedPrediction, "Tab で選ぶ");
        k.Press(0x2E); // Delete
        Assert.Equal("今日", forgotten, "エンジンの予測を消す");
        Assert.True(!history.StartingWith("きょう").Any(e => e.Text == "今日"), "学習から消える");
        Assert.True(k.Host.View?.Predictions?.Contains("今日") != true, "一覧から消える");
    }
}
