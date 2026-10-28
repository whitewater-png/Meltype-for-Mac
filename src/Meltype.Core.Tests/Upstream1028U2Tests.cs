// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>
/// 本家 #1187 #1214 (PR #974 の一部): 予測候補を Tab で選んだまま文字を打つと、選んだ語で確定してから続きを打つ。
/// BackSpace・Esc・Space・Enter・矢印は今までどおり。選んでいないときも今までどおり。
/// </summary>
internal static class Upstream1028U2Tests
{
    private static CompositionTests.Keyboard Selected()
    {
        var history = new ConversionHistory(null);
        history.Remember("きょう", "今日");
        var k = new CompositionTests.Keyboard(history: history);
        k.Type("kyou");
        k.Press(VirtualKeys.Tab);
        Assert.Equal(0, k.Host.View!.SelectedPrediction, "Tab で予測を選ぶ");
        return k;
    }

    [Test]
    public static void LetterAfterTab_CommitsSelectedPredictionAndStartsNewInput()
    {
        var k = Selected();
        k.Type("a");
        Assert.Equal("今日", k.Host.Document, "選んだ語で確定する");
        Assert.True(k.Controller.IsComposing, "打った a から新しい入力が始まる");
        Assert.Equal(-1, k.Host.View?.SelectedPrediction ?? -1, "予測の選択は終わる");
        k.Press(VirtualKeys.Return);
        Assert.Equal("今日あ", k.Host.Document, "続きは a (あ) の入力");
    }

    [Test]
    public static void DigitAndSymbolAfterTab_CommitAndStartNewInput()
    {
        foreach (var typed in new[] { "1", "!", "-", "/" })
        {
            var k = Selected();
            k.Type(typed);
            Assert.Equal("今日", k.Host.Document, $"{typed}: 選んだ語で確定する");
            Assert.True(k.Controller.IsComposing, $"{typed}: 新しい入力が始まる");
        }
    }

    [Test]
    public static void UpperCaseAfterTab_CommitsAndStartsNewInput()
    {
        var k = Selected();
        k.Type("A");
        Assert.Equal("今日", k.Host.Document, "Shift つきの英字でも確定する");
        Assert.True(k.Controller.IsComposing, "新しい入力が始まる");
    }

    [Test]
    public static void BackSpace_DoesNotCommit()
    {
        var k = Selected();
        k.Press(VirtualKeys.Back);
        Assert.Equal("", k.Host.Document, "BackSpace は確定しない");
        Assert.True(k.Controller.IsComposing, "入力は続く");
    }

    [Test]
    public static void EscSpaceEnter_KeepExistingBehavior()
    {
        var esc = Selected();
        esc.Press(VirtualKeys.Escape);
        Assert.Equal("", esc.Host.Document, "Esc は確定しない");
        Assert.Equal(-1, esc.Host.View?.SelectedPrediction ?? -1, "Esc は予測の選択から抜ける");

        var enter = Selected();
        enter.Press(VirtualKeys.Return);
        Assert.Equal("今日", enter.Host.Document, "Enter は選んだ予測で確定");

        var space = Selected();
        space.Press(VirtualKeys.Space);
        Assert.Equal("", space.Host.Document, "Space は確定せず変換を始める");
        Assert.True(space.Controller.IsComposing, "変換中");
    }

    [Test]
    public static void WithoutSelection_TypingContinuesTheReading()
    {
        var history = new ConversionHistory(null);
        history.Remember("きょう", "今日");
        var k = new CompositionTests.Keyboard(history: history);
        k.Type("kyou");
        Assert.Equal(-1, k.Host.View!.SelectedPrediction, "選んでいない");
        k.Type("a");
        Assert.Equal("", k.Host.Document, "選んでいなければ確定しない");
        k.Press(VirtualKeys.Return);
        Assert.Equal("きょうあ", k.Host.Document, "読みの続きとして入る");
    }

    [Test]
    public static void TabAroundToUnselect_ThenTypingDoesNotCommit()
    {
        var k = Selected();
        k.Press(VirtualKeys.Tab); // 候補は 1 つ: もう一度 Tab で一巡して選ばない状態に戻る
        Assert.Equal(-1, k.Host.View!.SelectedPrediction, "選ばない状態に戻る");
        k.Type("a");
        Assert.Equal("", k.Host.Document, "選んでいないので確定しない");
    }

    [Test]
    public static void KanaInput_KanaKeyAfterTab_CommitsSelectedAndStartsNewInput()
    {
        var history = new ConversionHistory(null);
        history.Remember("こんにちは", "こんにちは世界");
        var k = new CompositionTests.Keyboard(history: history) { Kana = true };
        k.TypeKanaKeys("by"); // こん
        Assert.True(k.Host.View?.Predictions is { Count: > 0 }, "前提: 予測候補が出ている");
        k.Press(VirtualKeys.Tab);
        Assert.Equal(0, k.Host.View!.SelectedPrediction, "Tab で予測を選ぶ");
        k.TypeKanaKeys("i"); // に
        Assert.Equal("こんにちは世界", k.Host.Document, "選んだ語で確定する");
        Assert.True(k.Controller.IsComposing, "打ったかなから新しい入力が始まる");
        Assert.Equal("に", k.Showing, "新しい入力は打ったかな");
    }

    [Test]
    public static void DownSelectedPrediction_LetterCommitsSelected()
    {
        var k = new CompositionTests.Keyboard(predictions: _ => ["今日", "京都"]) { DownSelectsPrediction = true };
        k.Type("kyou");
        k.Press(VirtualKeys.Down);
        Assert.Equal(0, k.Host.View!.SelectedPrediction, "↓ で予測を選ぶ");
        k.Type("a");
        Assert.Equal("今日", k.Host.Document, "↓ で選んだ予測も、文字を打つと確定する");
        Assert.True(k.Controller.IsComposing, "打った a から新しい入力が始まる");
        Assert.Equal("あ", k.Showing, "新しい入力は a");
    }

    [Test]
    public static void LeftRightAfterTab_LeavePredictionWithoutCommitting()
    {
        foreach (var vk in new[] { VirtualKeys.Left, VirtualKeys.Right })
        {
            var k = Selected();
            k.Press(vk);
            Assert.Equal("", k.Host.Document, $"{vk:X}: ← → は確定しない");
            Assert.Equal(-1, k.Host.View?.SelectedPrediction ?? -1, $"{vk:X}: 予測の選択から抜ける");
            Assert.True(k.Controller.IsComposing, $"{vk:X}: 入力は続く (確定していない)");
            Assert.True(k.Host.View!.Converting, $"{vk:X}: 今までどおり、予測から抜けて文節の選択に入る");
        }
    }

    [Test]
    public static void CommitByTyping_LearnsAsFromPrediction_AndDoesNotHijackConversion()
    {
        var history = new ConversionHistory(null);
        Func<string, IReadOnlyList<string>> predictions = r => r.StartsWith("たんい", StringComparison.Ordinal) ? ["単位を取得する"] : [];
        var k = new CompositionTests.Keyboard(history: history, predictions: predictions);
        k.Type("tanniwo");
        k.Press(VirtualKeys.Tab);
        k.Type("a");
        Assert.Equal("単位を取得する", k.Host.Document, "文字で確定する");
        Assert.True(history.Get("たんいを") is null, "「予測から」の印つきで覚える: 変換の最初の候補 (Get) には入らない");
        Assert.True(history.StartingWith("たんい").Any(e => e.Text == "単位を取得する"), "予測の並びには出る");
        Assert.True(history.Usage().ContainsKey("単位を取得する"), "使用頻度にも入る");

        k = new CompositionTests.Keyboard(history: history, predictions: predictions);
        k.Type("tanniwotoru ");
        Assert.Equal("単位を|取る", string.Join("|", k.Host.View!.Clauses!), "同じ読みの変換の最初の候補が予測の語に乗っ取られない (Q17)");
    }
}
