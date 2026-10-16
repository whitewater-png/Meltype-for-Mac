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

    [Test]
    public static void P46_9_Japanese_StaysEnglish()
    {
        // 本家 #712: japanese が じゃぱねせ になっていた (english.txt には元からあるが、全体がローマ字として読めるので日本語側が勝っていた)。
        ExpectCommitted(("japanese", "japanese"), ("japanesewo", "japaneseを"), ("kyouhajapanese", "きょうはjapanese"), ("Japanese", "Japanese"));
    }

    /// <summary>やほやほ を 谷保谷保 にする変換エンジン (本家 PR #622 のテストと同じ)。</summary>
    private sealed class YahoyahoConverter : IKanjiConverter
    {
        public string? Convert(string hiragana) => hiragana == "やほやほ" ? "谷保谷保" : null;

        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) =>
            hiragana == "やほやほ" ? [new("やほ", "谷保"), new("やほ", "谷保")] : null;
    }

    /// <summary>ぶいあーるちゃっと を 部位アールチャット にする変換エンジン (本家 bot の Mozc の結果)。</summary>
    private sealed class VrchatConverter : IKanjiConverter
    {
        public string? Convert(string hiragana) => hiragana == "ぶいあーるちゃっと" ? "部位アールチャット" : null;

        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) =>
            hiragana == "ぶいあーるちゃっと" ? [new("ぶい", "部位"), new("あーるちゃっと", "アールチャット")] : null;
    }

    [Test]
    public static void P46_10_Yahoyaho_StaysHiragana_AndVrChatIsKept()
    {
        // 本家 #424 (PR #622): やほやほ が 谷保谷保 になる。同梱の語句 (phrases.txt) にして、1 つの文節として確定する。
        var yahoyaho = new CompositionTests.Keyboard(live: true, converter: new YahoyahoConverter(), userDictionary: new UserDictionary(null));
        yahoyaho.Type("yahoyaho\n");
        Assert.Equal("やほやほ", yahoyaho.Host.Document, "yahoyaho");
        // 本家 #365: VRChat が 部位アールチャット (Vアールチャット) と変換される。
        var vrchat = new CompositionTests.Keyboard(live: true, converter: new VrchatConverter(), userDictionary: new UserDictionary(null));
        vrchat.Type("buia-rutyatto\n");
        Assert.Equal("VRChat", vrchat.Host.Document, "buia-rutyatto");
    }

    [Test]
    public static void P46_11_UchikataKaikata_OfferBothReadings()
    {
        // 本家 PR #615 の辞書の部分だけ: うちかた (打ち方・撃ち方)、かいかた (買い方・飼い方) の同音異義語を候補に出す。
        foreach (var (keys, expected) in new[] { ("uchikata ", "打ち方"), ("uchikata ", "撃ち方"), ("kaikata ", "買い方"), ("kaikata ", "飼い方") })
        {
            var k = new CompositionTests.Keyboard();
            k.Type(keys);
            var candidates = k.Host.View?.Candidates ?? [];
            Assert.True(candidates.Contains(expected), keys + ": " + string.Join(" | ", candidates));
        }
    }

    [Test]
    public static void P46_12_Fyu_IsReadAsSmallYu()
    {
        // 本家 #674: fyu で ふゅ (フュージョン = fyu-jon) が打てない。Microsoft IME・Mozc と同じ綴りの ふゃ・ふゅ・ふょ を足した。
        ExpectCommitted(("fyu", "ふゅ"), ("fyuzyon", "ふゅじょん"), ("fya", "ふゃ"), ("fyo", "ふょ"), ("fyuu", "ふゅう"));
    }

    [Test]
    public static void P46_13_LowercaseSiriZu_StaysJapanese()
    {
        // 本家 #656: siri-zu (シリーズ) の siri が固有名詞 (Siri) と数えられて、しり-ず (長音が - のまま) になっていた。
        // 小文字で打った、ローマ字として最後まで読める固有名詞は、英字の語として数えない。
        ExpectCommitted(("siri-zu", "しりーず"), ("siri-zumodel", "しりーずmodel"), ("siri", "siri"), ("sirinoondoku", "しりのおんどく"));
    }

    [Test]
    public static void P46_14_LeadingDotFiles_StayEnglish()
    {
        // 本家 #34 (803719c): 入力の先頭が . のドットファイル (.exe → 。えぇ、.gitignore → 。ぎちgのれ) を英字のままにする。
        // 途中の 3.14desu や、文末の「です。」の . には使わない (i == 0 のときだけ)。
        ExpectCommitted((".exe", ".exe"), (".gitignore", ".gitignore"), (".env", ".env"), (".zshrc", ".zshrc"), (".exedesu", ".exeです"),
            ("3.14desu", "3.14です"), ("desu.", "です。"));
    }

    [Test]
    public static void P46_15_Data_StaysEnglish_ExceptAfterJapanese()
    {
        // 本家 #34: data が だた になっていた。english-readable.txt に足し、main・domain と同じく日本語のすぐ後ろでは英語にしない
        // (kyouhadatawo = きょうはだたを。日本語の語の終わり + data の綴りが、はだ + た などと読めるため)。
        ExpectCommitted(("data", "data"), ("datawo", "dataを"), ("dataha", "dataは"), ("kyouhadatawo", "きょうはだたを"), ("kyouhadata", "きょうはだた"));
    }

    [Test]
    public static void P46_16_Cye_IsReadAsChe()
    {
        // 本家 #34 (05a1bb5): cyen が cyen のまま (ちぇん と読めない) だった。cye を ちぇ の綴りに足した。
        ExpectCommitted(("cyen", "ちぇん"), ("cye", "ちぇ"));
    }

    [Test]
    public static void P46_17_BrokenMemoryFiles_AreCopiedToBroken()
    {
        // 本家 #551: 読めなかった (壊れた) 学習データは、次の保存で上書きされる前に「名前.broken」に残す。
        // LanguageMemory は元から保存の直前に同じことをする。ConversionHistory・TranslationHistory は読んだ時点で残す。
        var directory = Path.Combine(Path.GetTempPath(), "meltype-p46-broken-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            const string broken = "{\"sig\":\"--\\n雪代\",";
            var languagesPath = Path.Combine(directory, "languages.json");
            var conversionsPath = Path.Combine(directory, "conversions.json");
            var translationsPath = Path.Combine(directory, "translations.json");
            foreach (var path in new[] { languagesPath, conversionsPath, translationsPath }) File.WriteAllText(path, broken);

            var languages = new LanguageMemory(languagesPath);
            var conversions = new ConversionHistory(conversionsPath);
            var translations = new TranslationHistory(translationsPath);
            languages.Remember("cafe", english: false, explicitChoice: true);
            conversions.Remember("くらいど", "クラウド");
            translations.Remember("すし", "sushi");

            foreach (var path in new[] { languagesPath, conversionsPath, translationsPath })
            {
                var backup = path + ".broken";
                Assert.True(File.Exists(backup), Path.GetFileName(path) + ".broken ができている");
                Assert.Equal(broken, File.ReadAllText(backup), Path.GetFileName(path) + " の元の中身が .broken に残っている");
                if (!OperatingSystem.IsWindows())
                    Assert.True(File.GetUnixFileMode(backup) == (UnixFileMode.UserRead | UnixFileMode.UserWrite), Path.GetFileName(backup) + " は本人だけが読み書きできる");
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void P46_17_ValidOrMissingFiles_DoNotCreateBroken()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-p46-valid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var conversionsPath = Path.Combine(directory, "conversions.json");
            var translationsPath = Path.Combine(directory, "translations.json");
            // 無いファイル: .broken を作らない
            _ = new ConversionHistory(conversionsPath);
            _ = new TranslationHistory(translationsPath);
            Assert.True(!File.Exists(conversionsPath + ".broken") && !File.Exists(translationsPath + ".broken"), "ファイルが無いだけでは .broken を作らない");
            // 正しいファイル: 読めて、.broken を作らない
            new ConversionHistory(conversionsPath).Remember("くらいど", "クラウド");
            new TranslationHistory(translationsPath).Remember("すし", "sushi");
            Assert.Equal("クラウド", new ConversionHistory(conversionsPath).Get("くらいど"), "保存した変換を読み直せる");
            Assert.Equal(1, new TranslationHistory(translationsPath).Get("すし").Count, "保存した英訳を読み直せる");
            Assert.True(!File.Exists(conversionsPath + ".broken") && !File.Exists(translationsPath + ".broken"), "正しいファイルでは .broken を作らない");
            // 空の JSON ({}) も壊れていない
            File.WriteAllText(conversionsPath, "{}");
            _ = new ConversionHistory(conversionsPath);
            Assert.True(!File.Exists(conversionsPath + ".broken"), "{} は壊れていない");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void P46_17_ClearLeftovers_RemovesBrokenOfConversionsAndTranslations()
    {
        // 学習データを全消去したとき (入力メニュー) に、今回できる .broken も消える。
        var directory = Path.Combine(Path.GetTempPath(), "meltype-p46-clear-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var conversionsPath = Path.Combine(directory, "conversions.json");
            var translationsPath = Path.Combine(directory, "translations.json");
            File.WriteAllText(conversionsPath, "{\"a\":");
            File.WriteAllText(translationsPath, "{\"a\":");
            _ = new ConversionHistory(conversionsPath);
            _ = new TranslationHistory(translationsPath);
            Assert.True(File.Exists(conversionsPath + ".broken") && File.Exists(translationsPath + ".broken"), ".broken ができている");
            Assert.True(LearningData.ClearLeftovers([conversionsPath, translationsPath], []), "ClearLeftovers が成功する");
            Assert.True(!File.Exists(conversionsPath + ".broken") && !File.Exists(translationsPath + ".broken"), "全消去で .broken も消える");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    /// <summary>
    /// 実機の NSSpellChecker のように、73 文字以上の文字列を (検査せず) 単語と答えるスペルチェッカー (実測: 60〜72 文字は単語でない、73 文字以上は全部単語)。
    /// それ以外は同梱の英単語表。
    /// </summary>
    private sealed class LongStringsAreWordsChecker : Detection.IWordChecker
    {
        public bool IsAvailable => true;

        public bool IsWord(string lower) => lower.Length >= 73 || Detection.BuiltInWordChecker.Shared.IsWord(lower);
    }

    [Test]
    public static void P46_18_LongRomajiSentence_IsNotOneEnglishWord()
    {
        // 本家 PR #709: 73 文字以上のローマ字の文を、macOS の NSSpellChecker が 1 語の英単語と答える (検査せず「誤りなし」を返す) ので、
        // 小書き文字 (xe・xi) を含む文の後ろが ふxえんす・ちxえっくいん のまま崩れていた (長い文を 1 回の変換ボックスに溜めたとき)。
        const string head = "kyounoasagohanwotabeteikaradenshadegakkounimukattatokorodengakkounokoutou"; // 73 文字
        const string headKana = "きょうのあさごはんをたべていからでんしゃでがっこうにむかったところでんがっこうのこうとう";
        CompositionTests.Detector.SpellChecker = new LongStringsAreWordsChecker();
        try
        {
            foreach (var (tail, expected) in new[] { ("oirufuxensu", "おいるふぇんす"), ("chixekkuin", "ちぇっくいん"), ("goru", "ごる") })
            {
                var k = new CompositionTests.Keyboard();
                k.Type(head + tail + "\n");
                Assert.Equal(headKana + expected, k.Host.Document, tail);
            }
            // 短い文は今までどおり
            var shortSentence = new CompositionTests.Keyboard();
            shortSentence.Type("kyouhaiitenkidesune\n");
            Assert.Equal("きょうはいいてんきですね", shortSentence.Host.Document, "短い文");
        }
        finally
        {
            CompositionTests.Detector.SpellChecker = null;
        }
    }

    /// <summary>な～～～ を 名～～～ に、らーめん を ラーメン にする変換エンジン (本家 #505 の報告と同じ)。呼ばれた回数を数える。</summary>
    private sealed class CountingLiveConverter : IKanjiConverter
    {
        public int Calls;

        private static string? Map(string hiragana) => hiragana switch { "な～～～" => "名～～～", "らーめん" => "ラーメン", _ => null };

        public string? Convert(string hiragana)
        {
            Calls++;
            return Map(hiragana);
        }

        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null)
        {
            Calls++;
            var mapped = Map(hiragana);
            return mapped is null ? null : [new(hiragana, mapped)];
        }
    }

    [Test]
    public static void P46_19_LiveConversion_DoesNotConvertTildesOnly()
    {
        // 本家 #505 (PR #632): ライブ変換が ON のとき、な～～～ は かな 1 文字 + 記号 3 つで 4 文字に届き、変換エンジンに渡って 名～～～ になっていた。
        // かなの文字数 (ー を含む) で数える。らーめん のような長音を含む語は今までどおり変換する。
        foreach (var typed in new[] { "na~~~", "sou~~", "sa!!!", "yada~~" })
        {
            var converter = new CountingLiveConverter();
            var k = new CompositionTests.Keyboard(live: true, converter: converter);
            k.Type(typed + "\n");
            Assert.Equal(0, converter.Calls, typed + ": 変換エンジンを呼ばない");
        }
        var nakama = new CountingLiveConverter();
        var k2 = new CompositionTests.Keyboard(live: true, converter: nakama);
        k2.Type("na~~~\n");
        Assert.Equal("な～～～", k2.Host.Document, "na~~~");
        var ramen = new CountingLiveConverter();
        var k3 = new CompositionTests.Keyboard(live: true, converter: ramen);
        k3.Type("ra-men\n");
        Assert.Equal("ラーメン", k3.Host.Document, "ra-men は今までどおり変換する");
        Assert.True(ramen.Calls > 0, "ra-men は変換エンジンを呼ぶ");
    }
}
