// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>
/// U7: 本家追従 (2026-10-28) の辞書の小さな追加の回帰テスト。
/// 本家 #1191 (bookoff)・#1131 (adb)・#1190 (くだしあ → ください)。取り込む前は赤。
/// </summary>
internal static class Upstream1028U7Tests
{
    private static string Committed(string typed)
    {
        var k = new CompositionTests.Keyboard();
        k.Type(typed + "\n");
        return k.Host.Document;
    }

    [Test]
    public static void BookoffStaysLatin() => Assert.Equal("bookoff", Committed("bookoff"), "bookoff");

    [Test]
    public static void BookoffBetweenJapanese() => Assert.Equal("ちかくのbookoffにいく", Committed("tikakunobookoffniiku"), "bookoff 前後");

    [Test]
    public static void AdbBetweenJapanese()
    {
        // adbyuukounisuru (本家 #1131) のように英語の直後を yu で続けると、b + yu が びゅ と読めるので辞書では直らない (報告に記載)。
        // 助詞などが挟まる形は英字のまま。
        Assert.Equal("adbをゆうこうにする", Committed("adbwoyuukounisuru"), "adb + を");
        Assert.Equal("adbでゆうこう", Committed("adbdeyuukou"), "adb + で");
    }

    [Test]
    public static void AdbAlone() => Assert.Equal("adb", Committed("adb"), "adb");

    [Test]
    public static void KudasiaIsCorrected()
    {
        var dictionary = MisspellingDictionary.Load(null);
        // 辞書の正しい形は読みのカタカナで持つ (直すときにひらがなへ戻す: CompositionText.ReplaceReading)
        Assert.True(dictionary.Find("くだしあ") is { Right: "クダサイ" }, "くだしあ → ください");
        Assert.True(dictionary.Find("おしえてくだしあ") is { Right: "クダサイ" }, "文末の くだしあ");
    }

    // ガード: 近い入力は変わらない
    [Test]
    public static void NearbyInputsUnchanged()
    {
        Assert.Equal("ください", Committed("kudasai"), "kudasai");
        Assert.Equal("ぶっく", Committed("bukku"), "bukku");
        Assert.Equal("あどびゅう", Committed("adobyuu"), "adobyuu");
        Assert.Equal("ぶっくおふ", Committed("bukkuofu"), "bukkuofu");
        Assert.True(MisspellingDictionary.Load(null).Find("ください") is null, "ください は誤りではない");
    }
}
