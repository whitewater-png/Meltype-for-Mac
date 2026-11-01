// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>
/// テストが共有する判定器 (CompositionTests.Detector) が、本番 (Mac の MeltypeSession.CreateDefault) と同じくスペルチェッカーつきであること。
/// スペルチェッカー無しだと、本番でだけ違う結果になる回帰 (英単語の判定) をテストが捕まえられない。
/// </summary>
internal static class SharedDetectorConfigTests
{
    [Test]
    public static void SharedDetector_HasSpellChecker_LikeProduction()
    {
        // meeting は同梱の英語の一覧 (english.txt) に無く、スペルチェッカーで英単語とわかる。無いと めえちんっが になる。
        var text = new CompositionText(CompositionTests.Detector);
        foreach (var c in "meetingga") text.Append(c);
        var segments = string.Join(" | ", text.Segments(final: true).Select(s => (s.IsEnglish ? "EN:" : "JA:") + s.Raw));
        Assert.Equal("EN:meeting | JA:ga", segments, "meetingga の区間分け");

        var k = new CompositionTests.Keyboard();
        k.Type("meetingga\n");
        Assert.Equal("meetingが", k.Host.Document, "meetingga の確定");

        Assert.True(CompositionTests.Detector.SpellChecker is { IsAvailable: true }, "共有の判定器にスペルチェッカーが付いている");
        Assert.True(CompositionTests.Detector.Memory is null, "共有の判定器の学習は空 (利用者の学習を読まない)");
    }

    [Test]
    public static void WithoutSpellChecker_RestoresTheSharedChecker()
    {
        var before = CompositionTests.Detector.SpellChecker;
        using (CompositionTests.WithoutSpellChecker())
        {
            Assert.True(CompositionTests.Detector.SpellChecker is null, "スコープの中はスペルチェッカー無し");
        }
        Assert.True(ReferenceEquals(before, CompositionTests.Detector.SpellChecker), "スコープを出たら元のスペルチェッカーに戻る");
    }
}
