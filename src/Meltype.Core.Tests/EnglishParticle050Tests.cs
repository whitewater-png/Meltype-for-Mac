// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Tests;

/// <summary>
/// 要件 050: 英字の語のすぐ後ろの助詞 (URL|wo|…PDF、ChatGPT|de|CSV、JavaScript|to|Python)、拡張子 + 助詞 + 次の名前 (dll|to|Cargo.toml)、
/// 数字 + 単位 + 助詞 + 数字 (1.5kg|to|100MB で 1 が前にずれていた)、版の名前の . (preview.4)、小書きの le でしか読めない語 (file|wo) の外れ。
/// 直る入力と、同じ形の守る入力を同じ場所に置く。文書: docs/requirements/050-english-particle-misses.md
/// </summary>
internal static class EnglishParticle050Tests
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

    [Test]
    public static void Req050_ParticleBetweenCapitalizedWords_SplitsAtFirstParticle()
    {
        Check(new()
        {
            ["URLwoshirasetekudasaiPDFwookurimasu"] = "URLをしらせてくださいPDFをおくります",
            ["ChatGPTdeCSVwo3maitsukurimashita"] = "ChatGPTでCSVを3まいつくりました",
            ["JavaScripttoPythonnoAPIwokakunin"] = "JavaScriptとPythonのAPIをかくにん",
            ["MacBookdeYouTubewomiru"] = "MacBookでYouTubeをみる",
            ["NHKtoBSwomiru"] = "NHKとBSをみる",
            ["GPTnoAPI"] = "GPTのAPI",
        });
    }

    [Test]
    public static void Req050_CapitalizedWords_KeepKnownWordsAndNames()
    {
        Check(new()
        {
            ["PhotoShopdehiraku"] = "PhotoShopでひらく",
            ["iPhonenoSEwokau"] = "iPhoneのSEをかう",
            ["GitHubnoissuewo"] = "GitHubのissueを",
            ["AutoIMEnotesuto"] = "AutoIMEのてすと",
            ["LaTeXdekaku"] = "LaTeXでかく",
            ["DeNAnoshiai"] = "DeNAのしあい",
            // 査読で見つけた形: 略語 + 英単語の名前、小文字で始まる識別子
            ["AIdeaLab"] = "AIdeaLab",
            ["AInoteBook"] = "AInoteBook",
            ["parseJSONtoObject"] = "parseJSONtoObject",
            ["loadCSVtoTable"] = "loadCSVtoTable",
        });
    }

    [Test]
    public static void Req050_ExtensionThenParticle_EndsTheName()
    {
        Check(new()
        {
            ["MeltypeTip.dlltoCargo.tomlwoREADME.mdnikirokusuru"] = "MeltypeTip.dllとCargo.tomlをREADME.mdにきろくする",
            ["https://example.com/README.mdwobrowserdehiraku"] = "https://example.com/README.mdをbrowserでひらく",
            ["kyouhagoogle.comdesu"] = "きょうはgoogle.comです",
            ["readme.mdwokesu"] = "readme.mdをけす",
            ["package.jsonwoREADMEni"] = "package.jsonをREADMEに",
            ["kyouhagoogle.desu"] = "きょうはgoogle。です",
            // 査読で見つけた形: . の後ろが読める日本語の頭 (de・me・ai・co) や、ふつうの英単語なら句点のまま
            ["kyouhaMac.dehanai"] = "きょうはMac。ではない",
            ["kyouhaword.ainimiru"] = "きょうはword。あいにみる",
            ["kyouhaMac.cokode"] = "きょうはMac。ここで",
            ["kyouhaMac.goodbye"] = "きょうはMac。goodbye",
            // 査読 2 周目: 拡張子 + 助詞の後ろの、一覧に無い拡張子の名前 (plist・scss)
            ["meltype.appnoInfo.plist"] = "meltype.appのInfo.plist",
            ["index.htmltoStyle.scss"] = "index.htmlとStyle.scss",
            ["main.swiftnoApp.entitlements"] = "main.swiftnoApp.entitlements",
            // 査読 3 周目: 分けた名前の後ろでも、. の後ろが読める日本語なら句点。版の数字は名前.拡張子と見ない
            ["kyouhaREADME.mdnoMac.ainiku"] = "きょうはREADME.mdのMac。あいにく",
            ["korehav1.2toMac.dehanai"] = "これはv1.2とMac。ではない",
            ["video.mp4noInfo.plist"] = "video.mp4のInfo.plist",
        });
    }

    [Test]
    public static void Req050_NumberUnitParticleNumber_KeepsTheOrder()
    {
        Check(new()
        {
            ["1.5kgto100MBnofilewosoufu"] = "1.5kgと100MBのfileをそうふ",
            ["5kgto3kg"] = "5kgと3kg",
            ["5kgde1kai"] = "5kgで1かい",
            ["10cmno3bai"] = "10cmの3ばい",
            ["rta2026"] = "rta2026",
            ["kaibunshorta2026"] = "かいぶんしょrta2026",
            ["ps5wokaitai"] = "ps5をかいたい",
        });
    }

    [Test]
    public static void Req050_DotInVersionName_StaysHalfWidth()
    {
        Check(new()
        {
            ["versionha1.2.3preview.4desu"] = "versionは1.2.3preview.4です",
            ["kyouhaMac.3jikara"] = "きょうはMac。3じから",
            ["arigatou.4jini"] = "ありがとう。4じに",
        });
    }

    [Test]
    public static void Req050_SmallLeSpelledWordBeforeParticle_IsEnglish()
    {
        Check(new()
        {
            ["nofilewo"] = "のfileを",
            ["kyouhafilegaaru"] = "きょうはfileがある",
            ["anoholegaaru"] = "あのholeがある",
            ["hosuthingu"] = "ほすてぃんぐ",
        });
    }
}
