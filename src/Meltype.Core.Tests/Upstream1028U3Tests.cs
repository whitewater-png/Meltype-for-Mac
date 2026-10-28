// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>
/// 本家追従 (2026-10-28) U3: 語句辞書の分割で、拗音・小さいかなの途中では切らない (本家 #1160、issue #823)。
/// </summary>
internal static class Upstream1028U3Tests
{
    private static bool HasWord(string kana, string word) =>
        new UserDictionary(null).Split(kana)?.Any(p => p.Word == word) == true;

    [Test]
    public static void Split_DoesNotCutInsideYoon()
    {
        // いしょくじょうたい は しょくじ|ょうたい で い食事ょうたい になっていた
        Assert.True(!HasWord("いしょくじょうたい", "食事"),"いしょくじょうたい に 食事 を当てない");
    }

    [Test]
    public static void Split_StillSplitsBeforeOrdinaryKana()
    {
        // ガード: 次が小さいかなでなければ、これまでどおり切る
        Assert.True(HasWord("しょくじをする", "食事"), "しょくじをする");
        Assert.True(HasWord("しょくじ", "食事"), "しょくじ (末尾)");
        Assert.True(HasWord("きょうしょくじ", "食事"), "きょう|しょくじ");
        Assert.True(HasWord("しょくじっ", "食事"), "しょくじっ (っ は別の拍)");
        Assert.True(HasWord("はくばのおうじさま", "白馬の王子様"), "既存の語句");
        Assert.True(HasWord("しりーず", "シリーズ"), "既存の語句 (長音)");
    }
}
