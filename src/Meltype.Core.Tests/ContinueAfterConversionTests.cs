// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>
/// 変換後も続けて入力 (Settings.ContinueAfterConversion)。Space で変換したあとに文字を打っても、
/// ON のときは確定せず、選んだ候補のまま固定して続きを未変換の文節にする。OFF は今までどおり確定する。
/// </summary>
internal static class ContinueAfterConversionTests
{
    /// <summary>「単位を|取る」に変換した状態 (擬似エンジン)。</summary>
    private static CompositionTests.Keyboard Converted(bool on, bool live = false, ConversionHistory? history = null)
    {
        var k = new CompositionTests.Keyboard(live: live, history: history) { Continue = on };
        k.Type("tanniwotoru ");
        Assert.Equal("単位を|取る", string.Join("|", k.Host.View!.Clauses!), "前提: Space で文節に区切って変換");
        return k;
    }

    private static string Clauses(CompositionTests.Keyboard k) => string.Join("|", k.Host.View!.Clauses!);

    [Test]
    public static void Off_TypingAfterConversion_CommitsAsBefore()
    {
        var k = Converted(on: false);
        k.Type("ta");
        Assert.Equal("単位を取る", k.Host.Output.Single(), "OFF: 変換中に文字を打つと、今の候補で確定する (従来どおり)");
        Assert.Equal("た", k.Showing, "新しい入力が始まる");
        Assert.True(!k.Host.View!.Converting && k.Host.View.Clauses is null or { Count: 0 }, "固定した文節は無い");
    }

    [Test]
    public static void On_TypingAfterConversion_KeepsClausesAndAppendsUnconverted()
    {
        var k = Converted(on: true);
        k.Type("ta");
        Assert.Equal(0, k.Host.Output.Count, "ON: 確定しない");
        Assert.Equal("単位を取るた", k.Showing, "選んだ変換結果 + 未変換のかな");
        Assert.True(!k.Host.View!.Converting, "未変換の文節は変換していない");
        Assert.Equal("単位を|取る|た", Clauses(k), "最後の 1 つが未変換の文節");
        Assert.True(k.Gate.IsCaptured, "変換ボックスは開いたまま");
        k.Type("nni");
        Assert.Equal("単位を取るたんい", k.Showing, "続けて打つと未変換の文節が伸びる");
        Assert.Equal("単位を|取る|たんい", Clauses(k));
    }

    [Test]
    public static void On_SpaceConvertsOnlyTheUnconvertedClause()
    {
        var k = Converted(on: true);
        // 前の文節の選び直しも保つ: 取る → 次の候補
        k.Press(VirtualKeys.Right);
        k.Type(" ");
        var chosen = k.Host.View!.Clauses![1];
        Assert.True(chosen != "取る", "前提: 候補が変わった");
        k.Type("tanni");
        Assert.Equal($"単位を|{chosen}|たんい", Clauses(k), "選び直した文節のまま固定される");
        k.Type(" ");
        Assert.True(k.Host.View!.Converting, "Space で未変換の文節を変換");
        Assert.Equal($"単位を|{chosen}|単位", Clauses(k), "未変換の文節だけが変換される");
        Assert.Equal(2, k.Host.View.SelectedClause, "変換した文節を選んでいる");
        k.Type("\n");
        Assert.Equal($"単位を{chosen}単位", k.Host.Output.Single(), "Enter で全体を確定");
        Assert.True(k.Showing is null && !k.Gate.IsCaptured, "確定したら閉じる");
    }

    [Test]
    public static void On_ArrowsReachPreviousClauses_AndCanReselect()
    {
        var k = Converted(on: true);
        k.Type("tanni ");
        Assert.Equal("単位を|取る|単位", Clauses(k));
        Assert.Equal(2, k.Host.View!.SelectedClause);
        k.Press(VirtualKeys.Left);
        k.Press(VirtualKeys.Left);
        Assert.Equal(0, k.Host.View!.SelectedClause, "← で前の文節にも戻れる");
        k.Type(" ");
        var first = k.Host.View!.Clauses![0];
        Assert.True(first != "単位を", "前の文節の候補を選び直せる: " + first);
        k.Press(VirtualKeys.Right);
        k.Press(VirtualKeys.Right);
        Assert.Equal(2, k.Host.View!.SelectedClause);
        k.Type("\n");
        Assert.Equal($"{first}取る単位", k.Host.Output.Single(), "選び直した結果を含めて確定");
    }

    [Test]
    public static void On_ReselectingFixedClause_ThenTypingKeepsSelection()
    {
        // 固定した文節を選び直してから、さらに続けて打っても、選び直した結果は保たれる。
        var k = Converted(on: true);
        k.Type("ta");
        k.Press(VirtualKeys.Left); // 未変換の文節を変換して、最後の文節を選ぶ
        k.Press(VirtualKeys.Left);
        k.Type(" ");
        var chosen = k.Host.View!.Clauses![1];
        Assert.True(chosen != "取る", "前提: 取る の候補が変わった");
        k.Type("ka");
        Assert.True(!k.Host.View!.Converting, "もう一度続けて打つと、また固定される");
        Assert.Equal($"単位を|{chosen}|た|か", Clauses(k));
        k.Type("\n");
        Assert.Equal($"単位を{chosen}たか", k.Host.Output.Single());
    }

    [Test]
    public static void On_Backspace_RemovesUnconvertedTail_ThenBackToPreviousClause()
    {
        var k = Converted(on: true);
        k.Type("tann");
        Assert.Equal("単位を取るたん", k.Showing);
        k.Type("\b");
        Assert.Equal("単位を取るた", k.Showing, "Backspace は未変換の文節の末尾を消す");
        Assert.Equal(0, k.Host.Output.Count);
        k.Type("\b");
        Assert.True(k.Host.View!.Converting, "空になったら前の文節の選択状態に戻る");
        Assert.Equal("単位を取る", k.Showing);
        Assert.Equal(1, k.Host.View.SelectedClause, "最後の文節を選んでいる");
        // その状態から、もう一度打てば固定して続けられる
        k.Type("ka");
        Assert.Equal("単位を|取る|か", Clauses(k));
        // その状態で Enter
        k.Type("\n");
        Assert.Equal("単位を取るか", k.Host.Output.Single());
    }

    [Test]
    public static void On_Backspace_AfterConvertingTail_CancelsOnlyTailConversion()
    {
        var k = Converted(on: true);
        k.Type("tanni ");
        k.Type("\b");
        Assert.True(!k.Host.View!.Converting, "未変換の文節の変換だけが取り消される");
        Assert.Equal("単位を取るたんい", k.Showing, "固定した文節はそのまま");
        k.Type("\n");
        Assert.Equal("単位を取るたんい", k.Host.Output.Single());
    }

    [Test]
    public static void On_Escape_RestoresReadingThenCancels()
    {
        var k = Converted(on: true);
        k.Type("ta");
        k.Press(VirtualKeys.Escape);
        Assert.Equal("たんいをとるた", k.Showing, "Esc: 固定した文節も変換前の読みに戻る");
        Assert.True(!k.Host.View!.Converting && k.Host.View.Clauses is null or { Count: 0 }, "今までの変換前と同じ状態");
        Assert.Equal(0, k.Host.Output.Count);
        k.Press(VirtualKeys.Escape);
        Assert.True(k.Showing is null && k.Host.Output.Count == 0, "もう一度 Esc で入力を取り消す (従来どおり)");

        // 未変換の文節を変換している最中の Esc も、全体が読みに戻る
        k = Converted(on: true);
        k.Type("tanni ");
        k.Press(VirtualKeys.Escape);
        Assert.Equal("たんいをとるたんい", k.Showing, "変換中の Esc");
        // 読みに戻したあとは、従来どおり Space で全体を変換できる
        k.Type(" ");
        Assert.True(k.Host.View!.Converting, "もう一度変換できる");
        Assert.Equal(0, k.Host.Output.Count);
    }

    [Test]
    public static void On_EscapeWithOnlyFixedClauses_RestoresReading()
    {
        var k = Converted(on: true);
        k.Type("ta");
        k.Type("\b"); // 未変換が空 → 前の文節の選択
        k.Press(VirtualKeys.Escape);
        Assert.Equal("たんいをとる", k.Showing, "固定した文節だけのときの Esc も読みに戻る");
        Assert.True(!k.Host.View!.Converting, "確認");
    }

    [Test]
    public static void On_EnterCommitsEverything_AndLearnsAllClauses()
    {
        var history = new ConversionHistory(null);
        var k = new CompositionTests.Keyboard(history: history) { Continue = true };
        k.Type("hashiwo ");
        var index = k.Host.View!.Candidates.ToList().IndexOf("箸を");
        Assert.True(index > 0, "前提: 箸を は候補にある");
        for (var i = 0; i < index; i++) k.Type(" ");
        k.Type("tanni ");
        Assert.Equal("箸を|単位", Clauses(k), "前提: 選び直した候補のまま固定された");
        k.Type("\n");
        Assert.Equal("箸を単位", k.Host.Output.Single(), "全体を確定");
        Assert.Equal("箸を", history.Get("はしを"), "固定した文節の選び直しも、従来どおり学習する");
    }

    [Test]
    public static void On_EnterWithoutConvertingTail_LearnsFixedClauses()
    {
        var history = new ConversionHistory(null);
        var k = new CompositionTests.Keyboard(history: history) { Continue = true };
        k.Type("hashiwo ");
        var index = k.Host.View!.Candidates.ToList().IndexOf("箸を");
        for (var i = 0; i < index; i++) k.Type(" ");
        k.Type("ta");
        k.Type("\n"); // 未変換の文節を変換せずに Enter
        Assert.Equal("箸をた", k.Host.Output.Single());
        Assert.Equal("箸を", history.Get("はしを"), "未変換の文節が付いたままの確定でも学習する");

        // 英単語で終わる確定 (Space) でも、固定した文節を学習する
        history = new ConversionHistory(null);
        k = new CompositionTests.Keyboard(history: history) { Continue = true };
        k.Type("hashiwo ");
        for (var i = 0; i < index; i++) k.Type(" ");
        k.Type("google ");
        Assert.Equal("箸をgoogle ", k.Host.Output.Single());
        Assert.Equal("箸を", history.Get("はしを"), "英単語で終わる確定でも学習する");
    }

    [Test]
    public static void On_FunctionKeys_ApplyToUnconvertedClauseOnly()
    {
        var k = Converted(on: true);
        k.Type("ta");
        k.Press(VirtualKeys.F7);
        Assert.Equal("単位を取るタ", k.Showing, "F7: 未変換の文節だけカタカナ。固定した文節はそのまま");
        k.Type("\n");
        Assert.Equal("単位を取るタ", k.Host.Output.Single());

        // 未変換の文節が無いとき (固定した文節だけを選んでいる状態) は、表示モードを変えない
        k = Converted(on: true);
        k.Type("ta");
        k.Type("\b");
        k.Press(VirtualKeys.F7);
        Assert.True(k.Host.View!.Converting, "変換中のまま");
        Assert.Equal("単位を取る", k.Showing, "変わらない");
    }

    [Test]
    public static void On_Digit_StillSelectsCandidate()
    {
        var k = Converted(on: true);
        k.Press(0x31);
        Assert.Equal("単位を取る", k.Host.Output.Single(), "数字による候補選択は今までどおり (確定)");
    }

    [Test]
    public static void On_SymbolAfterConversion_AppendsToo()
    {
        var k = Converted(on: true);
        k.Type(",");
        Assert.Equal(0, k.Host.Output.Count, "句読点も確定せずに続く");
        Assert.Equal("単位を取る、", k.Showing);
        k.Type("\n");
        Assert.Equal("単位を取る、", k.Host.Output.Single());
    }

    [Test]
    public static void On_English_TailIsDetectedSeparately()
    {
        var k = Converted(on: true);
        k.Type("google");
        Assert.Equal("単位を取るgoogle", k.Showing, "続けて打った英単語は英字のまま");
        k.Type(" ");
        Assert.Equal("単位を取るgoogle ", k.Host.Output.Single(), "英単語で終わっていれば、全体を確定して空白 (従来の英単語 + Space と同じ)");
        Assert.True(k.Showing is null, "確認");

        // 英単語を打ったあと、Enter で確定
        k = Converted(on: true);
        k.Type("google\n");
        Assert.Equal("単位を取るgoogle", k.Host.Output.Single());

        // 変換中の文節が英単語のときも、続けて打てる
        k = new CompositionTests.Keyboard { Continue = true };
        k.Type("google ");
        Assert.True(k.Host.Output.Count == 1, "前提: 英単語は Space で確定して空白 (変換中にはならない)");
    }

    [Test]
    public static void On_ShiftSpace_ConvertsUnconvertedEnglishAsRomaji()
    {
        var k = Converted(on: true);
        k.Type("go");
        k.Host.PhysicalShift = true;
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Space);
        k.Key(VirtualKeys.LShift, up: true);
        k.Host.PhysicalShift = false;
        Assert.True(k.Host.View!.Converting, "Shift+Space で未変換の英語をローマ字として変換");
        Assert.Equal("単位を|取る", string.Join("|", k.Host.View.Clauses!.Take(2)), "固定した文節は変わらない");
        Assert.True(k.Host.View.Clauses!.Count >= 3, "確認");
        k.Type("\n");
        Assert.Equal(1, k.Host.Output.Count);
        Assert.True(k.Host.Output[0].StartsWith("単位を取る"), "確認");
    }

    [Test]
    public static void On_LiveConversion_UnconvertedClauseShownConverted()
    {
        var k = Converted(on: true, live: true);
        k.Type("kyouha");
        Assert.Equal("単位を取る今日は", k.Showing, "ライブ変換が ON なら、続けて打った部分は漢字で見える");
        Assert.Equal(0, k.Host.Output.Count);
        k.Type("\n");
        Assert.Equal("単位を取る今日は", k.Host.Output.Single(), "見えているとおりに確定");
    }

    [Test]
    public static void On_ResizeDoesNotCrossFixedBoundary()
    {
        var k = Converted(on: true);
        k.Type("tanni ");
        k.Press(VirtualKeys.Left); // 取る
        var before = Clauses(k);
        k.Host.PhysicalShift = true;
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Right);
        k.Press(VirtualKeys.Left);
        k.Key(VirtualKeys.LShift, up: true);
        k.Host.PhysicalShift = false;
        Assert.Equal(before, Clauses(k), "固定した文節と未変換の文節の境目は、Shift+←→ でも動かさない");
        // 固定した文節どうし (単位を|取る) の境目は動かせる
        k.Press(VirtualKeys.Left);
        k.Host.PhysicalShift = true;
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Right);
        k.Key(VirtualKeys.LShift, up: true);
        k.Host.PhysicalShift = false;
        Assert.True(Clauses(k) != before, "固定した文節どうしなら区切りを変えられる: " + Clauses(k));
        k.Type("\n");
        Assert.Equal(1, k.Host.Output.Count);
    }

    [Test]
    public static void On_FocusLoss_CommitsEverything()
    {
        var k = Converted(on: true);
        k.Type("ta");
        k.Controller.CommitPending();
        Assert.Equal("単位を取るた", k.Host.Output.Single(), "フォーカスが外れたときは、固定した文節ごと確定する");
        Assert.True(!k.Controller.IsComposing, "確認");
    }

    [Test]
    public static void On_ClickCandidate_AfterContinuing()
    {
        var k = Converted(on: true);
        k.Type("tanni ");
        k.Press(VirtualKeys.Left);
        k.Press(VirtualKeys.Left);
        k.Controller.SelectCandidate(1);
        Assert.True(k.Host.View!.Clauses![0] != "単位を", "固定した文節でも、候補ウィンドウのクリックで選べる");
    }

    [Test]
    public static void On_NoPredictionWhileFixedClausesExist()
    {
        var k = new CompositionTests.Keyboard(predictions: reading => reading.StartsWith("た") ? ["たんい", "たんじょうび"] : [], history: null) { Continue = true };
        k.Type("tanniwotoru ");
        k.Type("tan");
        Assert.True(k.Host.View!.Predictions is null or { Count: 0 }, "固定した文節があるときは予測を出さない (確定すると固定した文節が消えるため)");
        k.Press(VirtualKeys.Tab);
        Assert.Equal("単位を取るたん", k.Host.Output.Single(), "Tab は予測を確定せず、従来の Tab と同じ (確定して Tab を通す)");
    }

    [Test]
    public static void SettingFlag_ChangesLiveWithoutRecreatingController()
    {
        // Func で毎回見るので、同じコントローラーのまま切り替えが次のキーから効く
        var k = new CompositionTests.Keyboard();
        k.Type("tanniwotoru ");
        k.Continue = true;
        k.Type("ta");
        Assert.Equal(0, k.Host.Output.Count, "ON にしたら次のキーから新しい挙動");
        k.Type("\n");
        k.Continue = false;
        k.Type("tanniwotoru ta");
        Assert.Equal("単位を取るた|単位を取る", string.Join("|", k.Host.Output), "OFF に戻したら従来どおり");
    }

    private static void ShiftArrow(CompositionTests.Keyboard k, int vk)
    {
        k.Host.PhysicalShift = true;
        k.Key(VirtualKeys.LShift);
        k.Press(vk);
        k.Key(VirtualKeys.LShift, up: true);
        k.Host.PhysicalShift = false;
    }

    /// <summary>きょうは|google|で検索 のように、英語の文節をはさんだ変換 → 続けて ta (固定) → 文節 0 を選んだ状態。</summary>
    private static CompositionTests.Keyboard FixedWithEnglish(out string fixedText)
    {
        var k = new CompositionTests.Keyboard { Continue = true };
        k.Type("kyouhagoogledekensaku ");
        fixedText = string.Concat(k.Host.View!.Clauses!);
        Assert.True(k.Host.View.Clauses!.Count >= 3 && k.Host.View.Clauses.Any(c => c == "google"), "前提: 英語の文節をはさんで変換: " + Clauses(k));
        k.Type("ta");
        for (var i = 0; i < 4; i++) k.Press(VirtualKeys.Left);
        Assert.Equal(0, k.Host.View!.SelectedClause, "前提: 文節 0 を選んだ");
        return k;
    }

    [Test]
    public static void On_ShiftLeftOnFixedClauseBeforeEnglish_DoesNotLoseClauses()
    {
        // 査読の指摘: 固定した文節の Shift+← で新しい文節を足すとき、固定した側に数えず、最後の固定した文節 (で検索) が消えた。
        var k = FixedWithEnglish(out var fixedText);
        var reading = k.Host.View!.Clauses!.Count;
        ShiftArrow(k, VirtualKeys.Left);
        Assert.Equal(reading + 1, k.Host.View!.Clauses!.Count, "文節が 1 つ増える");
        Assert.True(string.Concat(k.Host.View.Clauses!).Contains("で検索") || k.Host.View.Clauses!.Any(c => c.Contains("検索")), "後ろの文節が残る: " + Clauses(k));
        k.Type("\b"); // 変換の取り消し (未変換の文節の変換を取り消す)
        Assert.True(k.Showing!.EndsWith("た") && k.Showing.Contains("検索"), "Backspace のあとも固定した文節が欠けない: " + k.Showing);
        k.Press(VirtualKeys.F7);
        Assert.True(k.Showing!.Contains("検索") && k.Showing.EndsWith("タ"), "F7 のあとも欠けない: " + k.Showing);
        k.Press(VirtualKeys.Escape);
        Assert.True(k.Showing!.Contains("けんさく"), "Esc で、「で検索」の読みも含めて戻る: " + k.Showing);
        _ = fixedText;
    }

    [Test]
    public static void On_ShiftLeftOnFixedClause_ThenSpaceAndEsc_KeepEverything()
    {
        var k = FixedWithEnglish(out _);
        ShiftArrow(k, VirtualKeys.Left);
        k.Press(VirtualKeys.Escape);
        Assert.Equal("きょうはgoogleでけんさくた", k.Showing, "Esc: 打った英字も含めて、全体が変換前に戻る");
        k = FixedWithEnglish(out _);
        ShiftArrow(k, VirtualKeys.Left);
        k.Type(" ");
        Assert.True(k.Host.View!.Converting, "続けて Space で未変換の文節を変換できる");
        k.Type("\n");
        Assert.True(k.Host.Output.Single().Contains("検索") && k.Host.Output[0].Contains("google"), "確定した文字列に欠けがない: " + k.Host.Output[0]);
    }

    [Test]
    public static void On_ShiftArrowsOnFixedClauses_NeverChangeReadingOrFixedText()
    {
        // 固定した文節への Shift+←→ を、いろいろな位置・順序で行い、読みの連結が変わらない・文節が欠けないことを確かめる。
        foreach (var typed in new[] { "kyouhagoogledekensaku ", "googlekyouha ", "tanniwotoru ", "kyouhagoogle ", "googletanniwotorugoogle "})
        {
            var random = new Random(typed.Length * 31);
            for (var round = 0; round < 30; round++)
            {
                var k = new CompositionTests.Keyboard { Continue = true };
                k.Type(typed);
                if (k.Host.View?.Clauses is not { Count: > 0 }) continue;
                k.Type("ta");
                var expectedReading = ReadingAfterEsc(typed) + "た";
                var steps = random.Next(1, 14);
                for (var i = 0; i < steps; i++)
                {
                    switch (random.Next(4))
                    {
                        case 0: k.Press(VirtualKeys.Left); break;
                        case 1: k.Press(VirtualKeys.Right); break;
                        case 2: ShiftArrow(k, VirtualKeys.Left); break;
                        default: ShiftArrow(k, VirtualKeys.Right); break;
                    }
                    var view = k.Host.View!;
                    Assert.True(view.Clauses![^1] == "た" || !view.Converting, $"{typed} 最後の未変換の文節は動かない: " + Clauses(k));
                }
                // どの経路で抜けても、読みが欠けない
                var exit = random.Next(3);
                if (exit == 0) k.Press(VirtualKeys.Escape);
                else if (exit == 1)
                {
                    // Backspace 2 回: 未変換の文節の変換の取り消し → 「た」を消す (固定した文節だけの選択に戻る)。そこで Esc
                    k.Type("\b");
                    k.Type("\b");
                    k.Press(VirtualKeys.Escape);
                    expectedReading = expectedReading[..^1];
                }
                else { k.Type("\n"); continue; }
                Assert.Equal(expectedReading, k.Showing, $"{typed} 経路 {exit} 手順 {steps}: 読みの連結が変わった");
            }
        }
    }

    /// <summary>OFF で同じように打って Esc (変換前の読みに戻す) したときの表示 = Esc で戻るべき読み。</summary>
    private static string ReadingAfterEsc(string typed)
    {
        var k = new CompositionTests.Keyboard();
        k.Type(typed);
        k.Press(VirtualKeys.Escape);
        return k.Showing!;
    }

    [Test]
    public static void On_Escape_RestoresTypedLetters_LikeOff()
    {
        // OFF の Space → Esc → F10 は、打ったままの英字 (tanniwotoru) になる。ON の固定後の Esc → F10 も同じ。
        var off = new CompositionTests.Keyboard();
        off.Type("tanniwotoru ");
        off.Press(VirtualKeys.Escape);
        off.Press(VirtualKeys.F10);
        var expected = off.Showing;
        Assert.Equal("tanniwotoru", expected, "前提: OFF の結果");
        var k = Converted(on: true);
        k.Type("ta");
        k.Type("\b");
        k.Press(VirtualKeys.Escape);
        k.Press(VirtualKeys.F10);
        Assert.Equal(expected, k.Showing, "ON でも打った英字に戻せる");
    }

    [Test]
    public static void On_Escape_ResetsDisplayModeOfUnconvertedClause()
    {
        var k = Converted(on: true);
        k.Type("ta");
        k.Press(VirtualKeys.F7);
        k.Press(VirtualKeys.Escape);
        Assert.Equal("たんいをとるた", k.Showing, "未変換の文節だけに効かせていたカタカナを、全体に引き継がない");
    }

    // ---- 設定の切り替え (Mac の入力メニュー → Core → FFI の meltype_*_continue_after_conversion の中身) ----

    private static string UseTempConfig(out Func<string> restore)
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "config.json");
        var previous = ContinueAfterConversionSetting.ConfigPath;
        ContinueAfterConversionSetting.ConfigPath = () => path;
        ContinueAfterConversionSetting.Reset();
        restore = () =>
        {
            ContinueAfterConversionSetting.ConfigPath = previous;
            ContinueAfterConversionSetting.Reset();
            try { Directory.Delete(directory, recursive: true); } catch { }
            return "";
        };
        return path;
    }

    [Test]
    public static void Setting_DefaultOff_AndHasAttributes()
    {
        Assert.True(!new Settings().ContinueAfterConversion, "既定は OFF");
        var property = typeof(Settings).GetProperty(nameof(Settings.ContinueAfterConversion))!;
        var category = (System.ComponentModel.CategoryAttribute)Attribute.GetCustomAttribute(property, typeof(System.ComponentModel.CategoryAttribute))!;
        var name = (System.ComponentModel.DisplayNameAttribute)Attribute.GetCustomAttribute(property, typeof(System.ComponentModel.DisplayNameAttribute))!;
        var description = (System.ComponentModel.DescriptionAttribute)Attribute.GetCustomAttribute(property, typeof(System.ComponentModel.DescriptionAttribute))!;
        Assert.Equal("1. 全般", category.Category);
        Assert.Equal("変換後も続けて入力できる", name.DisplayName);
        Assert.True(description.Description.Contains("Space で変換したあと、続けて文字を打っても確定せず、打った文字を含めて編集・変換を続ける"), "説明文");
        // 古い config.json (この項目が無い) を読むと OFF のまま
        var path = UseTempConfig(out var restore);
        try
        {
            File.WriteAllText(path, "{ \"SettingsVersion\": 5, \"LiveConversion\": false }");
            Assert.True(!Settings.Load(path).ContinueAfterConversion, "項目が無ければ OFF");
            Assert.True(!ContinueAfterConversionSetting.IsOn, "確認");
        }
        finally { restore(); }
    }

    [Test]
    public static void Setting_Toggle_SavesToConfig_AndKeepsOtherValues()
    {
        var path = UseTempConfig(out var restore);
        try
        {
            File.WriteAllText(path, "{ \"SettingsVersion\": 5, \"LiveConversion\": false, \"PredictionMinLength\": 4 }");
            Assert.True(!ContinueAfterConversionSetting.IsOn, "最初は OFF");
            Assert.True(ContinueAfterConversionSetting.Set(true), "保存できる");
            Assert.True(ContinueAfterConversionSetting.IsOn, "確認");
            var saved = Settings.Load(path);
            Assert.True(saved.ContinueAfterConversion, "config.json に保存される");
            Assert.True(!saved.LiveConversion && saved.PredictionMinLength == 4, "ほかの設定はそのまま");
            // 次の起動 (覚えた値を捨てて読み直す) でも ON
            ContinueAfterConversionSetting.Reset();
            Assert.True(ContinueAfterConversionSetting.IsOn, "再起動後も ON");
            Assert.True(ContinueAfterConversionSetting.Set(false), "確認");
            Assert.True(!Settings.Load(path).ContinueAfterConversion && !ContinueAfterConversionSetting.IsOn, "確認");
        }
        finally { restore(); }
    }

    [Test]
    public static void Setting_Toggle_DoesNotOverwriteUnreadableConfig()
    {
        var path = UseTempConfig(out var restore);
        try
        {
            File.WriteAllText(path, "{ これは壊れた設定");
            Assert.True(!ContinueAfterConversionSetting.Set(true), "読めない設定には保存しない");
            Assert.Equal("{ これは壊れた設定", File.ReadAllText(path), "元のファイルはそのまま");
            Assert.True(!ContinueAfterConversionSetting.IsOn, "値も変えない");
        }
        finally { restore(); }
    }

    [Test]
    public static void Setting_Toggle_NoConfigFile_CreatesIt()
    {
        var path = UseTempConfig(out var restore);
        try
        {
            Assert.True(!File.Exists(path), "確認");
            Assert.True(ContinueAfterConversionSetting.Set(true), "確認");
            Assert.True(Settings.Load(path).ContinueAfterConversion, "ファイルが無くても保存できる");
        }
        finally { restore(); }
    }

    private static List<SessionResult> TypeKeys(MeltypeSession session, string text)
    {
        var results = new List<SessionResult>();
        foreach (var c in text)
        {
            var vk = c switch { ' ' => VirtualKeys.Space, '\n' => VirtualKeys.Return, _ when char.IsAsciiLetter(c) => char.ToUpperInvariant(c), _ => c };
            char? ch = c is ' ' or '\n' ? null : c;
            results.Add(session.HandleKey(vk, ch, false, false, false, false));
        }
        return results;
    }

    private static MeltypeSession NewSession() => new(CompositionTests.Detector, new CompositionTests.FakeConverter(),
        new CompositionOptions { ContinueAfterConversion = () => ContinueAfterConversionSetting.IsOn, History = new ConversionHistory(null) }, () => new Settings());

    [Test]
    public static void Setting_SwitchReachesAllRunningSessions()
    {
        var path = UseTempConfig(out var restore);
        try
        {
            var a = NewSession();
            var b = NewSession();
            // OFF: どちらも従来どおり、変換中に打つと確定する
            TypeKeys(a, "tanniwotoru ");
            var off = TypeKeys(a, "t");
            Assert.Equal("単位を取る", off[0].Commits.Single().Text, "OFF: 確定する");
            TypeKeys(a, "\n");

            // 入力メニューでの切り替え (meltype_set_continue_after_conversion の中身)。作り直さなくても、すでに動いている両方に効く
            Assert.True(ContinueAfterConversionSetting.Set(true), "確認");
            foreach (var session in new[] { a, b })
            {
                TypeKeys(session, "tanniwotoru ");
                var on = TypeKeys(session, "ta");
                Assert.True(on.All(r => r.Commits.Count == 0), "ON: 確定されない");
                Assert.Equal("単位を取るた", on[^1].View?.Text);
                var enter = TypeKeys(session, "\n")[0];
                Assert.Equal("単位を取るた", enter.Commits.Single().Text, "Enter で全体を確定");
            }
            Assert.True(File.Exists(path), "確認");

            // 戻す
            Assert.True(ContinueAfterConversionSetting.Set(false), "確認");
            TypeKeys(b, "tanniwotoru ");
            Assert.Equal("単位を取る", TypeKeys(b, "t")[0].Commits.Single().Text, "OFF に戻した");
        }
        finally { restore(); }
    }

    [Test]
    public static void AbiVersion_MatchesBetweenCSharpAndSwift()
    {
        // FFI を足したので、C# (Exports.AbiVersion) と Swift (NativeCore.expectedAbiVersion) を同じ値に上げている。
        // リポジトリの外 (配布物) で動かしたときはファイルが無いので、何も確かめない。
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "mac", "Sources", "MeltypeIME", "NativeCore.swift"))) directory = directory.Parent;
        if (directory is null) return;
        var swift = File.ReadAllText(Path.Combine(directory.FullName, "mac", "Sources", "MeltypeIME", "NativeCore.swift"));
        var exports = File.ReadAllText(Path.Combine(directory.FullName, "src", "Meltype.Mac.Native", "Exports.cs"));
        var swiftVersion = System.Text.RegularExpressions.Regex.Match(swift, @"expectedAbiVersion: Int32 = (\d+)").Groups[1].Value;
        var csharpVersion = System.Text.RegularExpressions.Regex.Match(exports, @"AbiVersion = (\d+);").Groups[1].Value;
        Assert.Equal("4", swiftVersion, "Swift 側");
        Assert.Equal("4", csharpVersion, "C# 側");
        Assert.True(exports.Contains("meltype_get_continue_after_conversion") && exports.Contains("meltype_set_continue_after_conversion"), "FFI の入口");
        Assert.True(swift.Contains("meltype_get_continue_after_conversion") && swift.Contains("meltype_set_continue_after_conversion"), "Swift 側の呼び出し");
    }
}
