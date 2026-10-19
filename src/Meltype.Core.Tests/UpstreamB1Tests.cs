// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
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

    [Test]
    public static void P48_DeVito_IsProperNoun()
    {
        // 本家 #792 (#268): devito が でゔぃと になっていた。固有名詞として英字のまま出す。
        // 打ったままの小文字はそのまま (devito)、大文字で打った形は正しい形 (DeVito) を保つ (本家のテストと同じ DeVitode)。
        ExpectCommitted(("devito", "devito"), ("devitowo", "devitoを"), ("kyouhadevito", "きょうはdevito"), ("DeVitode", "DeVitoで"));
    }

    [Test]
    public static void P48_ShotaAndLattice_AreOffered()
    {
        // 本家 #756 (#653) しょた → ショタ、#758 (#654) らてぃす → ラティス。
        // カタカナの候補は JapaneseCandidates が元から出すので、補助辞書 (candidates.txt) に載ったことを直接確かめる
        // (辞書の候補は、かな・カタカナより前の位置に入る)。
        var dictionary = CandidateDictionary.Load(null);
        Assert.Equal("ショタ", string.Join("|", dictionary.Lookup("しょた")));
        Assert.Equal("ラティス", string.Join("|", dictionary.Lookup("らてぃす")));
    }

    /// <summary>
    /// 実機の NSSpellChecker (2026-10-19、language: "en") の答えのうち、P48 の語と断片だけをまねた判定器。
    /// 同梱の表も併せて見る。NS が語の途中の断片 (who・is・t・ver・de・auto・click など) を知っていても、
    /// 辞書に足した語が 1 語として残ることを確かめる。
    /// </summary>
    private sealed class NsLikeChecker : IWordChecker
    {
        private static readonly HashSet<string> Words =
            ["whois", "who", "is", "t", "ver", "mo", "de", "auto", "clicker", "click", "er", "docs", "doc", "ext", "e", "text", "next", "context"];

        public bool IsAvailable => true;

        public bool IsWord(string lower) => Words.Contains(lower) || BuiltInWordChecker.Shared.IsWord(lower);
    }

    [Test]
    public static void P48_DictionaryAdditions_AlsoHoldWithNsLikeChecker()
    {
        // 実機の NSSpellChecker は whois・docs・ext を知っているが、tver・mozc・devito・autoclicker は知らない。
        // NS が断片を知っている語 (whois = who + is、tver = t + ver) でも、追加した語が 1 語のまま残ることを確かめる。
        ExpectCommitted(new NsLikeChecker(),
            ("whois", "whois"), ("whoiswo", "whoisを"),
            ("tver", "tver"), ("mozc", "mozc"), ("mozcde", "mozcで"),
            ("devito", "devito"), ("DeVitode", "DeVitoで"),
            ("autoclicker", "autoclicker"), ("clicker", "clicker"),
            ("docs", "docs"), ("ext", "ext"),
            ("text", "text"), ("next", "next"), ("context", "context"));
    }
}
