// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>P50 (本家 #769・#786): 数字 → 漢数字・元号、元号の年 → 西暦。</summary>
internal static class NumberCandidatesTests
{
    [Test]
    public static void Digits_OfferKanjiAndEra()
    {
        Assert.Equal("二〇二六|二千二十六|弐千弐拾六|令和8年", string.Join("|", NumberCandidates.For("2026")));
        Assert.Equal("二〇二六|二千二十六|弐千弐拾六|令和8年", string.Join("|", NumberCandidates.For("２０２６")), "全角");
        Assert.Equal("二〇二六|二千二十六|弐千弐拾六|令和8年", string.Join("|", NumberCandidates.For("2,026")), "カンマ");
        Assert.Equal("一|壱", string.Join("|", NumberCandidates.For("1")));
        Assert.Equal("〇〇七", string.Join("|", NumberCandidates.For("007")), "先頭が 0 なら漢数字だけ");
        Assert.Equal("一〇〇〇〇|一万|壱万", string.Join("|", NumberCandidates.For("10000")));
        Assert.Equal("一八六八|千八百六十八|壱千八百六拾八|明治元年", string.Join("|", NumberCandidates.For("1868")));
    }

    [Test]
    public static void NotDigits_GiveNothing()
    {
        foreach (var text in new[] { "", "abc", "12a", "12345678901234567" })
            Assert.Equal("", string.Join("|", NumberCandidates.For(text)), text);
    }

    [Test]
    public static void EraToYear_Ranges()
    {
        Assert.Equal("2026年|2026", string.Join("|", CompositionController.EraToYear("れいわ8ねん")!));
        Assert.Equal("2019年|2019", string.Join("|", CompositionController.EraToYear("れいわがんねん")!));
        Assert.Equal("1989年|1989", string.Join("|", CompositionController.EraToYear("へいせいがんねん")!));
        Assert.Equal("1989年|1989", string.Join("|", CompositionController.EraToYear("しょうわ64ねん")!));
        Assert.Equal("1868年|1868", string.Join("|", CompositionController.EraToYear("めいじ1ねん")!));
        foreach (var reading in new[] { "しょうわ65ねん", "れいわ0ねん", "れいわ99ねん", "れいわ100ねん", "へいせい" })
            Assert.True(CompositionController.EraToYear(reading) is null, reading);
    }
}
