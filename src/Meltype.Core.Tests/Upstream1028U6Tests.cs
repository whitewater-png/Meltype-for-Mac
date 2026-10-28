// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Tests;

/// <summary>
/// U6: 本家追従 (2026-10-28) の取り込み。本家の english-readable にある語 (printenv・nohup・vocaloid・doomscroll は本家 #1148、
/// tmux・opencode・claudecode は以前から) が、フォークではかなに割れていた。
/// </summary>
internal static class Upstream1028U6Tests
{
    private static readonly string[] Words = { "printenv", "nohup", "vocaloid", "doomscroll", "tmux", "opencode", "claudecode" };

    private static string Committed(string typed)
    {
        var k = new CompositionTests.Keyboard();
        k.Type(typed + "\n");
        return k.Host.Document;
    }

    [Test]
    public static void U6_1_Words_StayEnglish()
    {
        foreach (var w in Words) Assert.Equal(w, Committed(w), w);
    }

    [Test]
    public static void U6_2_CapitalizedClaudeCode_StaysEnglish()
    {
        Assert.Equal("ClaudeCode", Committed("ClaudeCode"), "ClaudeCode");
    }

    [Test]
    public static void U6_3_Words_InsideJapanese_KeepNeighbours()
    {
        foreach (var w in Words)
        {
            var got = Committed("kyouha" + w + "wotsukau");
            Assert.Equal("きょうは" + w + "をつかう", got, w);
        }
        // 語の後ろの助詞も巻き込まない (opencode の de・nohup の ha は助詞の綴りで終わる / 続く)
        Assert.Equal("claudecodeでしらべた", Committed("claudecodedeshirabeta"), "claudecode de");
        Assert.Equal("opencodeは", Committed("opencodeha"), "opencode ha");
        Assert.Equal("printenvでしらべた", Committed("printenvdeshirabeta"), "printenv de");
        Assert.Equal("vocaloidについて", Committed("vocaloidnitsuite"), "vocaloid ni");
    }

    /// <summary>
    /// 査読 R-B 必須 1: english-readable にしか無い語は、確定直前の打ち間違いの補正 (FixTypos) に守られず、
    /// 表示 vocaloidは のまま Enter で ゔぉかぉいしゃ (d→s) が確定していた。english.txt にも置いて守る。
    /// </summary>
    [Test]
    public static void U6_5_ParticleAfterReadableWord_CommitsAsShown()
    {
        Assert.Equal("vocaloidは", Committed("vocaloidha"), "vocaloid ha");
        Assert.Equal("vocaloidも", Committed("vocaloidmo"), "vocaloid mo");
        Assert.Equal("きょうはvocaloidはすきです", Committed("kyouhavocaloidhasukidesu"), "kyouha vocaloid ha");
        Assert.Equal("opencodeも", Committed("opencodemo"), "opencode mo");
        Assert.Equal("きょうはopencodeはべんり", Committed("kyouhaopencodehabenri"), "kyouha opencode ha");
    }

    [Test]
    public static void U6_4_NearbyJapanese_Unchanged()
    {
        Assert.Equal("おぺん", Committed("open"), "open");
        Assert.Equal("おぺんこうど", Committed("openkoudo"), "openkoudo");
    }
}
