// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Config;
using Meltype.Detection;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>
/// P46: 本家追従 (2026-10-16) の取り込みの回帰テスト。各テストは、取り込む前は赤・取り込んだ後は緑
/// (経緯と数字は docs/IMPROVEMENT_PLAN.md の P46、進み具合は docs/UPSTREAM_PR_TRACKER.md)。
/// </summary>
internal static class UpstreamP46Tests
{

    /// <summary>
    /// 同梱の英単語表のスペルチェッカー (アプリ・--repro と同じ) で打って Enter で確定した結果を、(打つ文字, 期待する文字) の表で確かめる。
    /// </summary>
    private static void ExpectCommitted(params (string Typed, string Expected)[] cases)
    {
        CompositionTests.Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try
        {
            foreach (var (typed, expected) in cases)
            {
                var k = new CompositionTests.Keyboard();
                k.Type(typed + "\n");
                Assert.Equal(expected, k.Host.Document, typed);
            }
        }
        finally
        {
            CompositionTests.Detector.SpellChecker = null;
        }
    }

    [Test]
    public static void P46_1_Infobesity_StaysEnglish()
    {
        // 本家 #609 (#538): infobesity (情報過多) が、ty が残った いんふぉべしty (確定は いんふぉべしつ) になっていた。
        ExpectCommitted(("infobesity", "infobesity"), ("infobesitywo", "infobesityを"), ("infobesityda", "infobesityだ"), ("kyouhainfobesity", "きょうはinfobesity"));
    }

    [Test]
    public static void P46_2_BioBreakAndBluRay_StayEnglish()
    {
        // 本家 #603 (#533): bio-break が びおーbreak に、#435 の Blu-ray が Bぅーらy になっていた。
        // 接頭辞の規則では拾えない、- の入ったよく使う英単語の一覧 (HyphenatedWords) に足した。
        ExpectCommitted(("bio-break", "bio-break"), ("bio-breakdesu", "bio-breakです"), ("bio-breakwotoru", "bio-breakをとる"), ("Blu-ray", "Blu-ray"), ("blu-ray", "blu-ray"));
    }

    [Test]
    public static void P46_2_JapaneseLongVowels_AreUnchanged()
    {
        // 守り: 日本語の長音 (e-to・su-pa-・o-bun・bio-ka) は今までどおりかな。
        ExpectCommitted(("e-to", "えーと"), ("su-pa-", "すーぱー"), ("o-bun", "おーぶん"), ("bio-ka", "びおーか"));
    }

    [Test]
    public static void P46_3_Ime_IsOfferedForIme()
    {
        // 本家 #534: 「いめ」(ime) で IME を候補に出す (dictionaries/brands.txt)。
        foreach (var (keys, expected) in new[] { ("ime ", "IME"), ("imede ", "IMEで") })
        {
            var k = new CompositionTests.Keyboard();
            k.Type(keys);
            var candidates = k.Host.View?.Candidates ?? [];
            Assert.True(candidates.Contains(expected), keys + ": " + string.Join(" | ", candidates));
        }
    }

    [Test]
    public static void P46_4_VibeCoding_StaysEnglish()
    {
        // 本家 #412: vibecoding (vibe coding) が ゔぃべcoding になっていた。coding と vibe を英語の一覧に足した。
        ExpectCommitted(("vibecoding", "vibecoding"), ("vibecodingwo", "vibecodingを"));
    }

    [Test]
    public static void P46_5_Oem_StaysEnglish()
    {
        // 本家 #34 (4a5a62b): oem3 が おえm3、oemnopasokon が おえものぱそこん になっていた。技術用語の一覧に oem を足した。
        ExpectCommitted(("oem3", "oem3"), ("oemnopasokon", "oemのぱそこん"), ("oemwo", "oemを"), ("kyouhaoem", "きょうはoem"));
    }

    [Test]
    public static void P46_6_XcodeDeepSeekMozc_StayEnglish()
    {
        // 本家 #34 (Xcode が Xcoで)、#347 (deepseek が でえpseek)、#249 (mozc が もzc)。固有名詞の一覧に足した。
        ExpectCommitted(("Xcode", "Xcode"), ("xcode", "xcode"), ("kyouhaXcodewotukau", "きょうはXcodeをつかう"),
            ("kyouhadeepseekwotukau", "きょうはdeepseekをつかう"), ("DeepSeek", "DeepSeek"),
            ("mozcnohensyuu", "mozcのへんしゅう"), ("kyouhamozcwotukau", "きょうはmozcをつかう"), ("Mozc", "Mozc"));
    }

    [Test]
    public static void P46_7_RareEnglishWords_StayEnglish()
    {
        // 本家の誤判定報告 10 件 (#683 guesstimation・#692 greedflation・#730 adtech・#731 robonomics・#733 netizenry・#689 finfluencer・
        // #726 malvertising・#686 enshittification・#690 datamation・#725 OpenStreetMap)。稀な英単語がローマ字として読まれて崩れていた。
        // 全体がローマ字として読めてしまう語 (datamation・enshittification) は english-readable.txt、大文字小文字を持つ名前は propernouns.txt にも足した。
        ExpectCommitted(
            ("guesstimation", "guesstimation"), ("guesstimationwo", "guesstimationを"), ("kyouhaguesstimation", "きょうはguesstimation"),
            ("greedflation", "greedflation"), ("greedflationwo", "greedflationを"),
            ("adtech", "adtech"), ("kyouhaadtech", "きょうはadtech"),
            ("robonomics", "robonomics"), ("robonomicswo", "robonomicsを"),
            ("netizenry", "netizenry"), ("netizenrywo", "netizenryを"),
            ("finfluencer", "finfluencer"), ("kyouhafinfluencer", "きょうはfinfluencer"),
            ("malvertisingwo", "malvertisingを"), ("kyouhamalvertising", "きょうはmalvertising"),
            ("enshittification", "enshittification"), ("enshittificationwo", "enshittificationを"),
            ("datamation", "datamation"), ("Datamation", "Datamation"), ("datamationwo", "datamationを"),
            ("openstreetmap", "openstreetmap"), ("openstreetmapwo", "openstreetmapを"));
    }

    [Test]
    public static void P46_8_Aonishiki_IsOffered()
    {
        // 本家 #706: 安青錦 (大相撲の力士) を あおにしき の候補に出す。
        foreach (var keys in new[] { "aonishiki ", "aonisiki " })
        {
            var k = new CompositionTests.Keyboard();
            k.Type(keys);
            var candidates = k.Host.View?.Candidates ?? [];
            Assert.True(candidates.Contains("安青錦"), keys + ": " + string.Join(" | ", candidates));
        }
    }
}
