// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Detection;

namespace Meltype.Tests;

/// <summary>
/// B-1: 本家 2026-10-18 追従のうち、追加型で悪化リスクが低い項目の回帰テスト。
/// 各テストは、取り込む前は赤・取り込んだ後は緑 (要件と設計は docs/UPSTREAM_B1_PLAN.md、
/// 進み具合は docs/UPSTREAM_PR_TRACKER.md)。P48 は辞書語。
/// </summary>
internal static class UpstreamB1Tests
{
    /// <summary>同梱の英単語表のスペルチェッカー (アプリ・--repro と同じ) で打って Enter で確定した結果を確かめる。</summary>
    private static void ExpectCommitted(params (string Typed, string Expected)[] cases) =>
        ExpectCommitted(BuiltInWordChecker.Shared, cases);

    /// <summary>判定器 checker を差し込んで、上と同じことをする (実機の NSSpellChecker に近い模擬を渡すときに使う)。</summary>
    private static void ExpectCommitted(IWordChecker checker, params (string Typed, string Expected)[] cases)
    {
        foreach (var (typed, expected) in cases) Assert.Equal(expected, Committed(checker, typed), typed);
    }

    /// <summary>判定器 checker を差し込んで、打って Enter で確定した結果を返す。終わったら元の判定器に戻す。</summary>
    private static string Committed(IWordChecker checker, string typed)
    {
        var previous = CompositionTests.Detector.SpellChecker;
        CompositionTests.Detector.SpellChecker = checker;
        try
        {
            var k = new CompositionTests.Keyboard();
            k.Type(typed + "\n");
            return k.Host.Document;
        }
        finally
        {
            CompositionTests.Detector.SpellChecker = previous;
        }
    }

    /// <summary>打ったあとの候補 (変換ボックス) を返す。</summary>
    private static List<string> CandidatesOf(string keys)
    {
        var k = new CompositionTests.Keyboard();
        k.Type(keys);
        return k.Host.View?.Candidates.ToList() ?? [];
    }

    [Test]
    public static void P48_Whois_StaysEnglish()
    {
        // 本家 #760 (#616): whois が うぉis になっていた (who + is に割れる)。
        // 守り: is は今までどおり (who は同梱の表の判定で うぉ になるので、この形では守りにしない)。
        ExpectCommitted(("whois", "whois"), ("whoiswo", "whoisを"), ("kyouhawhois", "きょうはwhois"));
        ExpectCommitted(("is", "is"));
    }

    [Test]
    public static void P48_Tver_StaysEnglish()
    {
        // 本家 #753 (#735): tver が tゔぇr になっていた。助詞が続く形 (tverno) は今までどおり。
        ExpectCommitted(("tver", "tver"));
        ExpectCommitted(("tverno", "tverの"));
    }

    [Test]
    public static void P48_Mozc_StaysEnglish()
    {
        // 本家 #791 (#249): mozc の後ろのローマ字を日本語にする。
        // Mac は propernouns の Mozc で既に期待どおり (guard)。本家とそろえるために english.txt にも足す。
        ExpectCommitted(("mozc", "mozc"), ("mozcde", "mozcで"), ("mozcniha", "mozcには"));
    }

    [Test]
    public static void P48_AutoclickerAndClicker_StayEnglish()
    {
        // 本家 #830 (#822): autoclicker・clicker が あうとclicけr・clicけr になっていた。
        ExpectCommitted(("autoclicker", "autoclicker"), ("autoclickerwo", "autoclickerを"),
            ("clicker", "clicker"), ("clickerwo", "clickerを"));
    }

    [Test]
    public static void P48_Docs_StaysEnglish()
    {
        // 本家 #840 (#835): docs が どか になっていた。守り: ./docs/ は保護区間で今までどおり。
        ExpectCommitted(("docs", "docs"), ("docswo", "docsを"), ("kyouhadocs", "きょうはdocs"));
        ExpectCommitted(("./docs/", "./docs/"));
    }

    [Test]
    public static void P48_Ext_StaysEnglish()
    {
        // 本家 #831 (#826): ext が えxt になっていた。守り: ext を含む英単語は今までどおり。
        // extwo は追加前 extwo (1 語)・追加後 extを (ext + 助詞) に変わる。英単語 ext として自然な分割なので記録する。
        ExpectCommitted(("ext", "ext"), ("extwo", "extを"), ("text", "text"), ("next", "next"), ("context", "context"));
    }
}
