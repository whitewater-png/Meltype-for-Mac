// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Input;

namespace Meltype.Tests;

/// <summary>
/// B2-2 (本家 #841): 大文字で打った英字の最後の n を ん と読む（TEN → てん）。
/// 小文字の英文の中の語（pen）は日本語にしない。
/// </summary>
internal static class UpperFinalNTests
{
    [Test]
    public static void UpperTEN_OffersTen()
    {
        // Mac の「日本語として変換」は Shift+Space (Core は JIS の Convert キーを直接扱わない)。
        var k = new CompositionTests.Keyboard();
        k.Type("TEN");
        k.TypeKeys((VirtualKeys.Space, true));
        var candidates = k.Host.View?.Candidates.ToList() ?? [];
        Assert.True(candidates.Contains("てん"), "てん: " + string.Join("|", candidates));
        Assert.Equal("てん", k.Showing, "先頭が てん: " + string.Join("|", candidates));
    }

    [Test]
    public static void LowerPen_IsUnchanged()
    {
        var k = new CompositionTests.Keyboard();
        k.Type("pen");
        k.TypeKeys((VirtualKeys.Space, true));
        var candidates = k.Host.View?.Candidates.ToList() ?? [];
        Assert.True(!candidates.Contains("てん"), "pen に てん は出さない: " + string.Join("|", candidates));
        // 小文字の pen は、今までどおり ぺん が先頭 (この変更では変えない)。
        Assert.Equal("ぺん", k.Showing, "pen の先頭: " + string.Join("|", candidates));
    }
}
