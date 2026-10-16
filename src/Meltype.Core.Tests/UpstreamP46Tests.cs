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
}
