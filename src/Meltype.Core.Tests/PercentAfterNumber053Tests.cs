// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Input;

namespace Meltype.Tests;

/// <summary>
/// 要件 053: 日本語の文の中で、数字のすぐ後ろの percent を ％ (打った % と同じ単位) にする。
/// 英文・コード系アプリ・percent で始まる長い語 (percentage・percentile) は英字のまま。文書: docs/requirements/053-percent-after-number.md
/// </summary>
internal static class PercentAfterNumber053Tests
{
    /// <summary>実機 (と --repro) と同じく、同梱の英単語の一覧をスペルチェッカーとして使って打つ。</summary>
    private static string Typed(string keys, Action<CompositionTests.Keyboard>? setup = null)
    {
        var previous = CompositionTests.Detector.SpellChecker;
        CompositionTests.Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try
        {
            var k = new CompositionTests.Keyboard();
            setup?.Invoke(k);
            k.Type(keys);
            return k.Host.Document.TrimEnd();
        }
        finally { CompositionTests.Detector.SpellChecker = previous; }
    }

    private static void Check(Dictionary<string, string> cases, Action<CompositionTests.Keyboard>? setup = null)
    {
        foreach (var (typed, expected) in cases) Assert.Equal(expected, Typed(typed + "\n", setup), typed);
    }

    [Test]
    public static void Req053_PercentAfterNumberInJapanese_BecomesPercentSign()
    {
        Check(new()
        {
            ["1000ennno5percentwo"] = "1000えんの5％を",
            ["5percentnowaribiki"] = "5％のわりびき",
            ["5percentdesu"] = "5％です",
            ["0.5percentnoneage"] = "0.5％のねあげ",
            ["5Percentnowaribiki"] = "5％のわりびき",
            ["5percentijou"] = "5％いじょう",
            ["5percentika"] = "5％いか",
            ["kyouha5percent"] = "きょうは5％",
        });
    }

    [Test]
    public static void Req053_HalfWidthSymbolsSetting_GivesHalfWidthPercent()
    {
        // 打った % と同じく、設定「記号を全角にする」が OFF なら半角
        Check(new() { ["5percentnowaribiki"] = "5%のわりびき" }, k => k.FullWidthSymbols = false);
    }

    [Test]
    public static void Req053_EnglishAndLongerWords_StayAsTyped()
    {
        Check(new()
        {
            ["5percent"] = "5percent",
            ["percentnowaribiki"] = "percentのわりびき",
            ["5percentage"] = "5percentage",
            ["5percents"] = "5percents",
            ["kyouha5percentage"] = "きょうは5percentage",
            ["I got 5 percent"] = "I got 5 percent",
            ["5%nowaribiki"] = "5％のわりびき",
            // 査読で見つけた形: 後ろが英字の語なら、日本語の文ではないので置き換えない
            ["5percentrule"] = "5percentrule",
            ["the 5percentrule"] = "the 5percentrule",
            ["5percentwise"] = "5percentwise",
            ["5percenter"] = "5percenter",
            ["5percentper"] = "5percentper",
            ["5PercentNo"] = "5PercentNo",
        });
    }

    [Test]
    public static void Req053_AfterJapanesePunctuation_BecomesPercentSign()
    {
        // 入力の頭の数字でも、前の確定が日本語の句読点 (。、) で終わっていれば日本語の文
        Check(new() { ["5percent"] = "5％" }, k => k.Host.PrecedingText = "あめ。");
    }

    [Test]
    public static void Req053_FunctionKeys_RestoreTypedSpelling()
    {
        // ％ にしたあとで F10・F9 を押したら、打った percent に戻す (F6 は 5ぺrせんtの。確定した直後の F10 は LastWordToggleTests) (査読: 5%no になっていた)
        foreach (var (key, expected) in new[] { (VirtualKeys.F10, "5percentno"), (VirtualKeys.F9, "５ｐｅｒｃｅｎｔｎｏ") })
        {
            var previous = CompositionTests.Detector.SpellChecker;
            CompositionTests.Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
            try
            {
                var k = new CompositionTests.Keyboard();
                k.Type("5percentno");
                Assert.Equal("5％の", k.Showing, "前提: 打っている途中で ％");
                k.Press(key);
                Assert.Equal(expected, k.Showing, key.ToString());
            }
            finally { CompositionTests.Detector.SpellChecker = previous; }
        }
    }

    [Test]
    public static void Req053_CodeApp_StaysAsTyped()
    {
        Check(new()
        {
            ["5percentnowaribiki"] = "5percentのわりびき",
            ["kyouha5percent"] = "きょうは5percent",
        }, k => k.CodeApp = true);
    }
}
