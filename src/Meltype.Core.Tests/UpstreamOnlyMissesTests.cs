// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Tests;

/// <summary>
/// 要件 038: 本家 main では当たり Mac 版だけ外れていた入力のうち、悪化 0 と測れたものだけを直した分。
/// 直る入力と、同じ綴りを含む日本語 (悪化の監視) を同じ場所に置く。直さなかった語 (game・image・oss・app) の「今のまま」も固定する。
/// 文書: docs/requirements/038-upstream-only-misses.md
/// </summary>
internal static class UpstreamOnlyMissesTests
{
    /// <summary>実機 (と --repro) と同じく、同梱の英単語の一覧をスペルチェッカーとして使って打つ。</summary>
    private static string Typed(string keys)
    {
        var previous = CompositionTests.Detector.SpellChecker;
        CompositionTests.Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try
        {
            var k = new CompositionTests.Keyboard();
            k.Type(keys);
            return k.Host.Document.TrimEnd();
        }
        finally { CompositionTests.Detector.SpellChecker = previous; }
    }

    private static void Check(Dictionary<string, string> cases)
    {
        foreach (var (typed, expected) in cases) Assert.Equal(expected, Typed(typed + "\n"), typed);
    }

    // ---- cache・code (english-readable.txt) ----

    [Test]
    public static void Cache_AndCode_AreEnglish()
    {
        Check(new()
        {
            ["cachewokeshitemite"] = "cacheをけしてみて",
            ["codereviewwoonegaishimasu"] = "codereviewをおねがいします",
            ["codewoharu"] = "codeをはる",
            ["vscodedecodewokaku"] = "vscodedecodeをかく",
        });
    }

    [Test]
    public static void KanaNeighborsOfCacheAndCode_StayJapanese()
    {
        // こで・かちぇ を含む日本語の語 (ここで・そこで・こども) は変わらない。
        Check(new()
        {
            ["kokodemo"] = "ここでも",
            ["sokode"] = "そこで",
            ["kodomo"] = "こども",
            ["kodawari"] = "こだわり",
            // c 綴りの日本語 (タコ・チョコ + で/です)。code が日本語のすぐ後ろでは英語にならない (NotAfterJapanese)。boundary-sweep が見つけた悪化
            ["tacodesu"] = "たこです",
            ["chocodesu"] = "ちょこです",
            ["tacode"] = "たこで",
            ["sa-bisugyoucdemou"] = "さーびすぎょうこでもう",
            // 日本語のすぐ後ろの cache・code は、今までどおり かな (直せない。損)
            ["kyouhacachewo"] = "きょうはかちぇを",
            ["kyouhacodewokaita"] = "きょうはこでをかいた",
        });
    }

    // ---- 直さなかった語は今のまま (過去の決定との関係) ----

    [Test]
    public static void GameImageOssApp_AreNotAddedToTheDictionary()
    {
        // game: がめん (画面)・がめつい・とがめる・ながめ を壊すので辞書に足さない (要件 038 の測定: gamenwomiru が gameんをみる になった)。
        // image: tadaimagenzai (ただいま現在) を守るため意図して足さない (W1・W18-1)。
        // oss・app: ossna → おっさん・appwo → あっぷを (打ち間違いの補正) を守るため足さない (W18-1)。
        Check(new()
        {
            ["gamen"] = "がめん",
            ["gamenwomiru"] = "がめんをみる",
            ["gametsui"] = "がめつい",
            ["togameru"] = "とがめる",
            ["nagame"] = "ながめ",
            ["tadaimagenzai"] = "ただいまげんざい",
            ["ossna"] = "おっさん",
            ["ossan"] = "おっさん",
            ["appwo"] = "あっぷを",
            ["appurodo"] = "あっぷろど",
            ["kappa"] = "かっぱ",
            ["shats"] = "しゃつ",
        });
    }

    // ---- gpt-oss・create-next-app (ハイフン入りの語の一覧) ----

    [Test]
    public static void HyphenatedProductNames_StayEnglish()
    {
        Check(new()
        {
            ["gpt-oss"] = "gpt-oss",
            ["gpt-oss wo tsukau"] = "gpt-oss をつかう",
            ["npx create-next-app"] = "npx create-next-app",
            ["npx create-react-app"] = "npx create-react-app",
        });
    }

    [Test]
    public static void HyphenatedProductNames_DoNotTouchTypoCorrections()
    {
        // 語単位の一覧なので、oss・app の打ち間違いの補正には影響しない
        Check(new()
        {
            ["ossna"] = "おっさん",
            ["gpt-ossna"] = "gpt-おっさん",
            ["appwo"] = "あっぷを",
        });
    }

    // ---- me・do・we (前の語が英語のとき、1 語だけでも英語にする) ----

    [Test]
    public static void ShortWord_AfterEnglishWord_StaysEnglish()
    {
        Check(new()
        {
            ["Tell me more"] = "Tell me more",
            ["Not me"] = "Not me",
            ["We do"] = "We do",
            ["Let me go"] = "Let me go",
            ["Why do we go"] = "Why do we go",
            ["How do we do"] = "How do we do",
            ["I do"] = "I do",
        });
    }

    [Test]
    public static void ShortWord_AfterJapaneseOrWithJapanese_StaysJapanese()
    {
        // 前が日本語、または前の英単語の後ろに日本語が続く形は今までどおり (直す条件は「前が英語」だけ)
        Check(new()
        {
            ["me wo tojiru"] = "めをとじる",
            ["kyouha do suru"] = "今日はどする",
            ["kyouha me"] = "今日はめ",
            ["Slack me wo"] = "Slack めを",
            ["iPhone no me"] = "iPhone のめ",
            ["Slack de hanasu"] = "Slack ではなす",
            ["Slack to"] = "Slack と",
            ["dokusho"] = "どくしょ",
            ["mewotojiru"] = "めをとじる",
            ["sushi me"] = "すしめ",
            ["Let me know"] = "Let me know",
        });
    }
}
