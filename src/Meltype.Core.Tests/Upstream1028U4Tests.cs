// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Tests;

/// <summary>
/// きょう の日付の候補に、/ 区切りの曜日つきと 8 桁を足す (本家 #1189・#1072 (f))。
/// 時刻は CompositionOptions.Now で固定する。2026/10/28 は水曜日。
/// </summary>
internal static class Upstream1028U4Tests
{
    private static readonly DateTime Wednesday = new(2026, 10, 28, 17, 22, 0);

    private static List<string> CandidatesOf(string keys, DateTime now)
    {
        var k = new CompositionTests.Keyboard(now: () => now);
        k.Type(keys);
        return k.Host.View!.Candidates.ToList();
    }

    [Test]
    public static void Today_HasSlashWeekdayAndEightDigits()
    {
        var list = CandidatesOf("kyou ", Wednesday);
        foreach (var want in new[] { "2026/10/28(水)", "10/28(水)", "20261028" })
            Assert.True(list.Contains(want), want + ": " + string.Join(",", list));
        Assert.Equal(list.IndexOf("水曜日") + 1, list.IndexOf("20261028"), "8 桁は日付の候補の最後 (曜日の次): " + string.Join(",", list));
    }

    [Test]
    public static void Today_SlashWeekdayFollowsSlashDate()
    {
        var list = CandidatesOf("kyou ", Wednesday);
        Assert.Equal(list.IndexOf("2026/10/28") + 1, list.IndexOf("2026/10/28(水)"), "/ 区切りの後ろに曜日つき: " + string.Join(",", list));
    }

    [Test]
    public static void Today_ZeroPadding()
    {
        // 月・日が 1 桁のとき: 8 桁と / 区切りの年月日は 0 埋め、月日だけの形は埋めない (本家と同じ)
        var list = CandidatesOf("kyou ", new DateTime(2026, 1, 5, 9, 0, 0));
        foreach (var want in new[] { "2026/01/05(月)", "1/5(月)", "20260105" })
            Assert.True(list.Contains(want), want + ": " + string.Join(",", list));
    }

    [Test]
    public static void Today_ExistingCandidatesKeepOrder()
    {
        var list = CandidatesOf("kyou ", Wednesday);
        var old = new[] { "2026年10月28日", "2026年10月28日(水)", "10月28日", "10月28日(水)", "2026/10/28", "2026-10-28", "水曜日" };
        Assert.Equal(string.Join("|", old), string.Join("|", list.Where(old.Contains)), string.Join(",", list));
    }

    [Test]
    public static void OtherReadings_DoNotGetEightDigits()
    {
        foreach (var keys in new[] { "ima ", "nau ", "ashita ", "kyouha ", "imada " })
        {
            var list = CandidatesOf(keys, Wednesday);
            Assert.True(!list.Contains("20261028") && !list.Contains("2026/10/28(水)") && !list.Contains("10/28(水)"), keys + ": " + string.Join(",", list));
        }
    }
}
