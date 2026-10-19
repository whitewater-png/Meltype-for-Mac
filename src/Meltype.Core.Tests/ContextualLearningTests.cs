// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>
/// B2-4 (本家 #800): 英語 / 日本語の学習を、直前の文脈 (英語の後か日本語の後か) ごとに分ける。
/// 既定 OFF。ON にする前に覚えた学習は、どちらの文脈でも使う。
/// </summary>
internal static class ContextualLearningTests
{
    [Test]
    public static void SameWord_IsLearnedPerContext()
    {
        var memory = new LanguageMemory(null) { ContextSensitive = true };
        memory.Context = true;
        memory.Remember("new", english: true, explicitChoice: true);
        memory.Context = false;
        memory.Remember("new", english: false, explicitChoice: true);

        memory.Context = true;
        Assert.True(memory.Get("new") == true, "英語の後: 英語");
        memory.Context = false;
        Assert.True(memory.Get("new") == false, "日本語の後: 日本語");
    }

    [Test]
    public static void OldEntry_IsUsedInBothContexts()
    {
        var memory = new LanguageMemory(null);
        memory.ContextSensitive = false;
        memory.Remember("api", english: true, explicitChoice: true);

        memory.ContextSensitive = true;
        memory.Context = true;
        Assert.True(memory.Get("api") == true, "英語の後: 古い学習");
        memory.Context = false;
        Assert.True(memory.Get("api") == true, "日本語の後: 古い学習");
    }

    [Test]
    public static void Off_UsesTheOldBehavior()
    {
        var memory = new LanguageMemory(null) { ContextSensitive = false, Context = true };
        memory.Remember("api", english: true, explicitChoice: true);
        memory.Context = false;
        Assert.True(memory.Get("api") == true, "OFF では文脈を見ない");
    }

    [Test]
    public static void Entries_StripTheContextSuffix()
    {
        var memory = new LanguageMemory(null) { ContextSensitive = true };
        memory.Context = true;
        memory.Remember("new", english: true, explicitChoice: true);
        memory.Context = false;
        memory.Remember("new", english: false, explicitChoice: true);

        var entries = memory.Entries();
        Assert.Equal(2, entries.Count, "2 つの文脈");
        Assert.True(entries.All(e => e.Word == "new"), "語は new (印を付けない)");
        Assert.True(entries.Any(e => e.Context == true) && entries.Any(e => e.Context == false), "文脈を持つ");
    }

    [Test]
    public static void Undo_RemovesTheContextEntry()
    {
        var memory = new LanguageMemory(null) { ContextSensitive = true, Context = true };
        var undo = memory.RememberUndoable("new", english: true, explicitChoice: true);
        Assert.True(undo is not null, "覚えた");
        Assert.True(memory.Get("new") == true, "覚えた");
        memory.Undo(undo!);
        Assert.True(memory.Get("new") is null, "元に戻る");
    }

    [Test]
    public static void Reload_KeepsTheContextEntries()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-context-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "languages.json");
        try
        {
            var memory = new LanguageMemory(path) { ContextSensitive = true };
            memory.Context = true;
            memory.Remember("new", english: true, explicitChoice: true);
            memory.Context = false;
            memory.Remember("new", english: false, explicitChoice: true);

            var reloaded = new LanguageMemory(path) { ContextSensitive = true };
            reloaded.Context = true;
            Assert.True(reloaded.Get("new") == true, "読み直しても英語の後");
            reloaded.Context = false;
            Assert.True(reloaded.Get("new") == false, "読み直しても日本語の後");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
