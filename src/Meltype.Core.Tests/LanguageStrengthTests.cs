// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>1-6 学習の強度: 候補から選んだだけの逆向きは、続けて 2 回になるまで向きを変えない。F10 / F6 ではっきり直したら 1 回で変える。</summary>
internal static class LanguageStrengthTests
{
    private static string TempFile(out Action cleanup)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"meltype-strength-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        cleanup = () =>
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        };
        return Path.Combine(directory, "languages.json");
    }

    [Test]
    public static void Strength_ImplicitReverseOnce_DoesNotFlip_TwiceDoes()
    {
        var memory = new LanguageMemory(null);
        memory.Remember("apix", english: true);
        memory.Remember("apix", english: true);
        Assert.Equal(true, memory.Get("apix"), "前提: 英語");
        memory.Remember("apix", english: false);
        Assert.Equal(true, memory.Get("apix"), "候補から選んだだけの逆向きが 1 回では、向きを変えない");
        memory.Remember("apix", english: false);
        Assert.Equal(false, memory.Get("apix"), "続けて 2 回で反転");
    }

    [Test]
    public static void Strength_SameDirectionResetsTheCount()
    {
        var memory = new LanguageMemory(null);
        memory.Remember("apix", english: true);
        memory.Remember("apix", english: true);
        memory.Remember("apix", english: false); // 逆向き 1 回
        memory.Remember("apix", english: true);  // 同じ向きに戻ったら数え直し
        memory.Remember("apix", english: false); // また 1 回目
        Assert.Equal(true, memory.Get("apix"), "続いていないので反転しない");
    }

    [Test]
    public static void Strength_AfterFlipping_NoExtraRepetitionIsNeeded_ForTwiceRuleWords()
    {
        // 日本語として覚えた語を、暗黙に 2 回英語にして反転。よく使う日本語 (2 回ルールの語) でも、反転した時点で効く (Count = 2 で作る。R41-7)
        var memory = new LanguageMemory(null) { IsCommonJapanese = word => word == "kyouhax" };
        memory.Remember("kyouhax", english: false);
        memory.Remember("kyouhax", english: true);
        Assert.Equal(false, memory.Get("kyouhax"), "1 回では日本語のまま");
        memory.Remember("kyouhax", english: true);
        Assert.Equal(true, memory.Get("kyouhax"), "2 回で反転し、そのまま英語として効く (さらに 1 回は要らない)");
    }

    [Test]
    public static void Strength_ExplicitFlipsAtOnce()
    {
        var memory = new LanguageMemory(null);
        memory.Remember("apix", english: true);
        memory.Remember("apix", english: true);
        memory.Remember("apix", english: false, explicitChoice: true);
        Assert.Equal(false, memory.Get("apix"), "F10 / F6 ではっきり直したら 1 回で反転");
        var twice = new LanguageMemory(null) { IsCommonJapanese = word => word == "kyouhax" };
        twice.Remember("kyouhax", english: false);
        twice.Remember("kyouhax", english: true, explicitChoice: true);
        Assert.Equal(true, twice.Get("kyouhax"), "反転は Count = 2 なので、2 回ルールの語も効く");
    }

    [Test]
    public static void Strength_ReverseCountIsSavedAndReloaded()
    {
        var path = TempFile(out var cleanup);
        try
        {
            var memory = new LanguageMemory(path);
            memory.Remember("apix", english: true);
            memory.Remember("apix", english: true);
            memory.Remember("apix", english: false); // 逆向き 1 回
            var reloaded = new LanguageMemory(path);
            Assert.Equal(true, reloaded.Get("apix"), "読み直しても、まだ英語");
            Assert.True(File.ReadAllText(path).Contains("Reverse"), "逆向きの回数をファイルに書く");
            reloaded.Remember("apix", english: false);
            Assert.Equal(false, reloaded.Get("apix"), "保存した逆向きの回数が残っているので、もう 1 回で反転");
        }
        finally { cleanup(); }
    }

    [Test]
    public static void Strength_OldFileWithoutReverse_StillLoads()
    {
        var path = TempFile(out var cleanup);
        try
        {
            File.WriteAllText(path, "{\"apix\":{\"English\":true,\"Used\":\"2026-01-01T00:00:00Z\",\"Count\":2,\"Explicit\":false}}");
            var memory = new LanguageMemory(path);
            Assert.Equal(true, memory.Get("apix"), "Reverse の無い古い形式も読める");
            memory.Remember("apix", english: false);
            Assert.Equal(true, memory.Get("apix"), "逆向きの回数は 0 から数える (1 回では反転しない)");
            memory.Remember("apix", english: false);
            Assert.Equal(false, memory.Get("apix"), "2 回で反転");
        }
        finally { cleanup(); }
    }

    [Test]
    public static void Strength_UndoRestoresTheEntryBeforeRemembering()
    {
        var memory = new LanguageMemory(null);
        var created = memory.RememberUndoable("apix", english: true, explicitChoice: true);
        Assert.Equal(true, memory.Get("apix"), "前提: 覚えた");
        memory.Undo(created!);
        Assert.True(memory.Get("apix") is null && memory.Count == 0, "無かった語は消える");
        memory.Remember("apix", english: false, explicitChoice: true);
        var flipped = memory.RememberUndoable("apix", english: true, explicitChoice: true);
        Assert.Equal(true, memory.Get("apix"), "前提: 反転した");
        memory.Undo(flipped!);
        Assert.Equal(false, memory.Get("apix"), "反転する前 (日本語) に戻る");
        Assert.True(memory.RememberUndoable("x", english: true) is null, "覚えなかった (不正な語) ときは null");
    }
}
