// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>
/// B2-6 (本家 #807): クリップボードの文字を変換候補に出す (既定 OFF)。
/// </summary>
internal static class ClipboardCandidatesTests
{
    [Test]
    public static void ClipboardLine_TakesOneLine()
    {
        Assert.Equal("hello", CompositionController.ClipboardLine(" hello \nworld"));
        Assert.Equal("hello", CompositionController.ClipboardLine("\n\nhello\n"));
        Assert.True(CompositionController.ClipboardLine("   ") is null, "空白だけ");
        Assert.True(CompositionController.ClipboardLine(new string('a', 201)) is null, "201 文字");
        Assert.True(CompositionController.ClipboardLine("a\tb") is null, "制御文字");
        Assert.Equal(new string('a', 200), CompositionController.ClipboardLine(new string('a', 200)));
    }

    [Test]
    public static void ClipboardReading_OffersTheClipboard()
    {
        var k = new CompositionTests.Keyboard { ClipboardCandidates = true };
        k.Host.ClipboardText = "https://example.com";
        k.Type("kurippuboodo ");
        var candidates = k.Host.View?.Candidates.ToList() ?? [];
        Assert.True(candidates.Contains("https://example.com"), "候補: " + string.Join("|", candidates));
    }

    [Test]
    public static void Off_DoesNotRead()
    {
        var k = new CompositionTests.Keyboard { ClipboardCandidates = false };
        k.Host.ClipboardText = "https://example.com";
        k.Type("kurippuboodo ");
        var candidates = k.Host.View?.Candidates.ToList() ?? [];
        Assert.True(!candidates.Contains("https://example.com"), "候補: " + string.Join("|", candidates));
    }
}
