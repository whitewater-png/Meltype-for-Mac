// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

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
        });
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
