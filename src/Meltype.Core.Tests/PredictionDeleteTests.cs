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

    [Test]
    public static void DeleteOnPrediction_RemovesEnginePredictionThatKeepsReturningIt()
    {
        // azooKey の forgetMemory は学習メモリだけを消し、辞書の語は同じ読みで何度でも返す。
        // 履歴から消しても一覧に戻ってこないこと (実機で「今日」が残った原因)。
        var forgotten = new List<string>();
        var k = new CompositionTests.Keyboard(predictions: _ => ["今日", "京都"])
        {
            ForgetPrediction = forgotten.Add,
        };
        k.Type("kyou");
        Assert.True(k.Host.View!.Predictions!.SequenceEqual(["今日", "京都"]), "エンジンの予測が出る");
        k.Press(VirtualKeys.Tab);
        Assert.Equal(0, k.Host.View!.SelectedPrediction, "Tab で先頭を選ぶ");
        k.Press(0x2E); // Delete
        Assert.True(forgotten.SequenceEqual(["今日"]), "エンジンにも忘れるよう伝える: " + string.Join("|", forgotten));
        Assert.True(k.Host.View?.Predictions?.Contains("今日") != true,
            "エンジンが同じ語を返し続けても一覧から消える: " + string.Join("|", k.Host.View?.Predictions ?? []));
        Assert.True(k.Host.View?.Predictions?.Contains("京都") == true, "ほかの予測は残る");
        Assert.Equal(-1, k.Host.View!.SelectedPrediction, "Delete の後は選択から抜ける");
    }

    [Test]
    public static void DeleteOnPrediction_KeepsUserDictionaryWord()
    {
        // ユーザー辞書・専門用語集の語は消さない方針。同じ語をエンジンも返しても、辞書由来として残す。
        var dictionary = new Composition.UserDictionary(null);
        dictionary.Add("きょう", "今日");
        var k = new CompositionTests.Keyboard(userDictionary: dictionary, predictions: _ => ["今日"]);
        k.Type("kyou");
        k.Press(VirtualKeys.Tab);
        Assert.Equal(0, k.Host.View!.SelectedPrediction, "Tab で選ぶ");
        k.Press(0x2E); // Delete
        Assert.True(k.Host.View?.Predictions?.Contains("今日") == true,
            "ユーザー辞書の語は残る: " + string.Join("|", k.Host.View?.Predictions ?? []));
        Assert.Equal(-1, k.Host.View!.SelectedPrediction, "選択からは抜ける");
    }

    [Test]
    public static void DeleteOnPrediction_StaysHiddenWhenTypingTheReadingAgain()
    {
        // 実機の 7-4: Delete のあと、同じ読みをもう一度打っても出ない (IME を再起動するまで)。
        var k = new CompositionTests.Keyboard(predictions: _ => ["今日", "京都"]);
        k.Type("kyou");
        k.Press(VirtualKeys.Tab);
        k.Press(0x2E); // 今日 を消す
        k.Press(VirtualKeys.Return); // 今の読みを確定して、もう一度打てるようにする
        k.Type("kyou");
        Assert.True(k.Host.View?.Predictions?.Contains("今日") != true,
            "同じ読みを打ち直しても消した語は出ない: " + string.Join("|", k.Host.View?.Predictions ?? []));
        Assert.True(k.Host.View?.Predictions?.Contains("京都") == true, "ほかの予測は出る");
    }

    [Test]
    public static void DeleteAfterSelectionReset_DoesNotForgetAnotherWord()
    {
        var forgotten = new List<string>();
        var k = new CompositionTests.Keyboard(predictions: _ => ["今日", "京都"])
        {
            ForgetPrediction = forgotten.Add,
        };
        k.Type("kyou");
        k.Press(VirtualKeys.Tab);
        k.Press(0x2E); // 今日 を消す
        Assert.True(k.Host.View?.Predictions?.Contains("京都") == true, "京都 は残る");
        k.Press(0x2E); // 選択から抜けているので、京都 は消さない (暗黙の確定になる)
        Assert.True(forgotten.SequenceEqual(["今日"]), "2 回目の Delete は何もしない: " + string.Join("|", forgotten));
    }
}
