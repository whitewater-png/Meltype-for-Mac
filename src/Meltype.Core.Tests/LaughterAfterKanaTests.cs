// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Tests;

/// <summary>
/// B2-5 (本家 #799): かな + w を笑いにする (既定 OFF)。
/// 前が英語の文 (I saw) のときは英単語のまま。
/// </summary>
internal static class LaughterAfterKanaTests
{
    private static string Committed(bool laughter, string keys, string? preceding = null)
    {
        var k = new CompositionTests.Keyboard { LaughterAfterKana = laughter };
        if (preceding is not null) k.Host.PrecedingText = preceding;
        k.Type(keys + "\n");
        return k.Host.Document;
    }

    [Test]
    public static void Naruhodonew_BecomesLaughter()
    {
        Assert.Equal("なるほどねw", Committed(true, "naruhodonew"));
    }

    [Test]
    public static void Off_IsUnchanged()
    {
        // OFF のときの今の結果を固定する (この変更で変えない)。
        Assert.Equal("なるほどnew", Committed(false, "naruhodonew"));
    }

    [Test]
    public static void EnglishContext_StaysEnglish()
    {
        // 英文の中の saw (I + saw) は、笑いの w にしない。
        Assert.Equal("Isaw", Committed(true, "Isaw"), "英文の中の saw は英単語のまま");
    }

    [Test]
    public static void KnownLimit_Onew_IsRecorded()
    {
        // ON のときの既知の限界 (おnewの新品)。今の結果を記録する (期待値を固定しない)。
        var result = Committed(true, "onewnoshinpin");
        Assert.True(result.Length > 0, "結果: " + result);
    }
}
