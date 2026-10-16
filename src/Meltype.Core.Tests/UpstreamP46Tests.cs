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
    private static void ExpectCommitted(params (string Typed, string Expected)[] cases) =>
        ExpectCommitted(Detection.BuiltInWordChecker.Shared, cases);

    /// <summary>判定器 checker を差し込んで、上と同じことをする。実機の NSSpellChecker に近い模擬 (NsLikeChecker) を渡すと、同梱の表だけでは見えない差が見える。</summary>
    private static void ExpectCommitted(Detection.IWordChecker checker, params (string Typed, string Expected)[] cases)
    {
        foreach (var (typed, expected) in cases) Assert.Equal(expected, Committed(checker, typed), typed);
    }

    /// <summary>判定器 checker を差し込んで、打って Enter で確定した結果を返す。終わったら元の判定器に戻す。</summary>
    private static string Committed(Detection.IWordChecker checker, string typed)
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
        var previous = CompositionTests.Detector.SpellChecker;
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
            CompositionTests.Detector.SpellChecker = previous;
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

    /// <summary>
    /// 実機の NSSpellChecker のように、同梱の英単語表にない語 (shat・doit・ment・gat・gatt・mahout・techno) も単語と答えるスペルチェッカー。
    /// それ以外は同梱の英単語表。語の境目の読み方が実機でだけ変わるかを見る。
    /// </summary>
    private sealed class NsLikeChecker : Detection.IWordChecker
    {
        public bool IsAvailable => true;

        public bool IsWord(string lower) =>
            lower is "shat" or "doit" or "ment" or "gat" or "gatt" or "mahout" or "techno" || Detection.BuiltInWordChecker.Shared.IsWord(lower);
    }

    [Test]
    public static void P46_20_TsuAfterEnglishLookingHead_IsReadAsJapanese()
    {
        // 本家 #594: shatsu が、実機では shatす (NSSpellChecker が shat を単語と答える) になる。同梱の表では しゃつ で、実機でだけ崩れる。
        // 英単語の頭の t + s を つ にまとめるかを、「かな + つ」が、よく使う語 (readings.txt) の頭になるか (しゃつ・どいつ・めんつ・がっつ・まほうつ) でも見る。
        var previous = CompositionTests.Detector.SpellChecker;
        CompositionTests.Detector.SpellChecker = new NsLikeChecker();
        try
        {
            foreach (var (typed, expected) in new[]
            {
                ("shatsu", "しゃつ"), ("waishatsu", "わいしゃつ"), ("shatsuwokau", "しゃつをかう"), ("kyouhashatsuwokau", "きょうはしゃつをかう"),
                ("doitsu", "どいつ"), ("mentsu", "めんつ"), ("gattsu", "がっつ"), ("mahoutsukai", "まほうつかい"),
                // 守り: 今までどおり (つ を含む日本語の語、英単語)
                ("tsunami", "つなみ"), ("natsu", "なつ"), ("matsuri", "まつり"), ("outsource", "outsource"),
            })
            {
                var k = new CompositionTests.Keyboard();
                k.Type(typed + "\n");
                Assert.Equal(expected, k.Host.Document, typed);
            }
        }
        finally
        {
            CompositionTests.Detector.SpellChecker = previous;
        }
    }

    [Test]
    public static void P46_23_DictionaryAdditions_AlsoHoldWithNsLikeChecker()
    {
        // 辞書の追加 (P46-1〜15) を、実機の NSSpellChecker のように techno なども単語と答える判定器でも確かめる。
        // 同梱の英単語表だけでは、実機でだけ起きる差 (語の境目の読み方の違い) が見えない。
        ExpectCommitted(new NsLikeChecker(),
            ("infobesity", "infobesity"), ("infobesitywo", "infobesityを"), ("kyouhainfobesity", "きょうはinfobesity"),
            ("vibecoding", "vibecoding"), ("oem3", "oem3"), ("Xcode", "Xcode"), ("DeepSeek", "DeepSeek"), ("Mozc", "Mozc"),
            ("guesstimation", "guesstimation"), ("greedflationwo", "greedflationを"), ("robonomicswo", "robonomicsを"), ("netizenrywo", "netizenryを"),
            ("japanese", "japanese"), ("japanesewo", "japaneseを"), ("data", "data"), ("datawo", "dataを"), ("kyouhadatawo", "きょうはだたを"),
            ("adtech", "adtech"), ("adtechwo", "adtechを"), ("kyouhaadtech", "きょうはadtech"), ("kyouhaadtechwo", "きょうはadtechを"));
    }

    [Test]
    public static void P46_23_Adtech_KnownLimit_NoAfterAdtechIsEnglishWithNsLikeChecker()
    {
        // 既知の限界 (KNOWN_LIMITS): 実機の NSSpellChecker は techno を単語と答えるので、adtech + の (adtechno…) の no が英語側に取り込まれる
        // (今までは adtechのような だった)。adtech 単独・助詞つき (を・は など)・日本語の後ろは直るので、辞書には入れたままにしている。
        // 直ったら、この期待値を adtechのような に変える。
        ExpectCommitted(new NsLikeChecker(), ("adtechnoyouna", "adtechnoyouna"));
        // 同梱の英単語表では adtechのような のまま
        ExpectCommitted(("adtechnoyouna", "adtechのような"));
    }

    [Test]
    public static void P46_24_LiveConversion_StillConvertsReadingsWithDigitsOrLetters()
    {
        // P46-19 の見落とし: 記号 (～ ! ? .) を数えないつもりが、かな以外 (数字・英字) も数えず、12がつ・100えん・iPhone15をかう のライブ変換が止まっていた。
        // 文字として読めるもの (かな・ー・数字・英字) は数え、記号だけを数えない。
        foreach (var typed in new[] { "12gatsu", "100en", "2024nen", "iPhone15wokau", "arigatou!" })
        {
            var converter = new CountingLiveConverter();
            var k = new CompositionTests.Keyboard(live: true, converter: converter);
            k.Type(typed + "\n");
            Assert.True(converter.Calls > 0, typed + ": 変換エンジンを呼ぶ");
        }
    }

    [Test]
    public static void P46_24_LiveConversion_SymbolsStillDoNotCount()
    {
        // 守り: 数字・英字を数えるようにしても、記号で 4 文字に届いただけのものは変換エンジンを呼ばない。
        foreach (var typed in new[] { "na~~~", "1~~~", "sa!!!", "sou~~", "yada~~" })
        {
            var converter = new CountingLiveConverter();
            var k = new CompositionTests.Keyboard(live: true, converter: converter);
            k.Type(typed + "\n");
            Assert.Equal(0, converter.Calls, typed + ": 変換エンジンを呼ばない");
        }
    }

    [Test]
    public static void P46_25_LowercaseProperNoun_BeforeDot_KeepsHalfWidthDot()
    {
        // P46-13 の見落とし: 小文字で打った固有名詞を英字の語に数えない変更が . の前まで及び、miro.com・canon.jp の . が 。 になった。
        // 長音の - の前だけにとどめる (. の前は今までどおり半角の . のまま)。
        foreach (var typed in new[] { "miro.com", "jira.com", "node.com", "deno.com", "canon.jp", "siri.com" })
        {
            var result = Committed(Detection.BuiltInWordChecker.Shared, typed);
            Assert.True(!result.Contains('。'), typed + ": . が 。 にならない: " + result);
        }
        // - の前は直ったまま
        ExpectCommitted(("siri-zu", "しりーず"));
    }

    private static string TempDir(string tag)
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-p46-" + tag + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    [Test]
    public static void P46_26_BrokenCannotBeKept_OriginalIsNotOverwritten()
    {
        // 壊れたファイルを .broken に残せないとき (.broken の場所がフォルダーなど) は、次の保存で元のファイルを上書きしない
        // (LanguageMemory・.oversize と同じ。残せないまま上書きすると、元の中身がどこにも残らない)。
        var directory = TempDir("keep-fail");
        try
        {
            const string corrupt = "{\"corrupt-but-recoverable\":";
            var conversionsPath = Path.Combine(directory, "conversions.json");
            var translationsPath = Path.Combine(directory, "translations.json");
            foreach (var path in new[] { conversionsPath, translationsPath })
            {
                File.WriteAllText(path, corrupt);
                Directory.CreateDirectory(path + ".broken");
            }
            new ConversionHistory(conversionsPath).Remember("くらいど", "クラウド");
            new TranslationHistory(translationsPath).Remember("すし", "sushi");
            Assert.Equal(corrupt, File.ReadAllText(conversionsPath), "conversions.json は上書きされない");
            Assert.Equal(corrupt, File.ReadAllText(translationsPath), "translations.json は上書きされない");
        }
        finally { SafeFile.ResetBlockedForTests(); Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void P46_26_UnreadableFile_IsNotOverwritten()
    {
        // 壊れているのではなく、読めなかっただけのファイル (権限・所有者違い。sudo で作られた学習データなど) は、.broken にも残せない。
        // 新しい 1 件のファイルで置き換えると、読めなかった中身が消えるので、置き換えない。
        if (OperatingSystem.IsWindows() || Environment.UserName == "root") return; // 権限で読めなくできる環境だけ
        var directory = TempDir("unreadable");
        try
        {
            const string original = "{\"ひみつ\":{\"Text\":\"秘密\"}}";
            var conversionsPath = Path.Combine(directory, "conversions.json");
            var translationsPath = Path.Combine(directory, "translations.json");
            foreach (var path in new[] { conversionsPath, translationsPath })
            {
                File.WriteAllText(path, original);
                File.SetUnixFileMode(path, UnixFileMode.None);
            }
            try
            {
                new ConversionHistory(conversionsPath).Remember("くらいど", "クラウド");
                new TranslationHistory(translationsPath).Remember("すし", "sushi");
            }
            finally
            {
                foreach (var path in new[] { conversionsPath, translationsPath }) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            foreach (var path in new[] { conversionsPath, translationsPath })
            {
                Assert.Equal(original, File.ReadAllText(path), Path.GetFileName(path) + " は上書きされない");
                Assert.True(!File.Exists(path + ".broken"), "読めなかっただけなので .broken は作らない");
            }
        }
        finally { SafeFile.ResetBlockedForTests(); Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void P46_26_BrokenIsSymlink_TargetIsNotTouched()
    {
        // .broken がリンクのとき、コピーがリンク先まで上書きして権限を 0600 にしていた。一時ファイルに書いて置き換えるので、リンクそのものが置き換わる。
        if (OperatingSystem.IsWindows()) return;
        var directory = TempDir("symlink");
        try
        {
            const string corrupt = "{\"corrupt\":";
            const UnixFileMode loose = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
            var path = Path.Combine(directory, "conversions.json");
            var victim = Path.Combine(directory, "victim.txt");
            File.WriteAllText(victim, "VICTIM-ORIGINAL");
            File.SetUnixFileMode(victim, loose);
            File.CreateSymbolicLink(path + ".broken", victim);
            File.WriteAllText(path, corrupt);
            _ = new ConversionHistory(path);
            Assert.Equal("VICTIM-ORIGINAL", File.ReadAllText(victim), "リンク先は書き換えない");
            Assert.True(File.GetUnixFileMode(victim) == loose, "リンク先の権限も変えない");
            Assert.Equal(corrupt, File.ReadAllText(path + ".broken"), ".broken に壊れた中身が残る");
            Assert.True(new FileInfo(path + ".broken").LinkTarget is null, ".broken はリンクではなく普通のファイルになる");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void P46_26_EmptyFile_DoesNotReplaceBroken()
    {
        // 中身のない (0 バイト・空白だけの) ファイルには残すものがない。前に残した .broken を空で潰さない。
        var directory = TempDir("empty");
        try
        {
            var path = Path.Combine(directory, "conversions.json");
            File.WriteAllText(path + ".broken", "FIRST-CORRUPT");
            foreach (var empty in new[] { "", "   \n" })
            {
                File.WriteAllText(path, empty);
                _ = new ConversionHistory(path);
                Assert.Equal("FIRST-CORRUPT", File.ReadAllText(path + ".broken"), "中身が " + empty.Length + " 文字のファイルで .broken を潰さない");
            }
        }
        finally { SafeFile.ResetBlockedForTests(); Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void P46_26_ExistingLooseBroken_IsRestrictedAndUpdated()
    {
        // 前の版が 0644 で作った .broken があっても、残し直したあとは本人だけが読み書きできる (0600) になる。
        if (OperatingSystem.IsWindows()) return;
        var directory = TempDir("loose");
        try
        {
            const string corrupt = "{\"corrupt\":";
            var path = Path.Combine(directory, "conversions.json");
            File.WriteAllText(path + ".broken", "OLD");
            File.SetUnixFileMode(path + ".broken", UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            File.WriteAllText(path, corrupt);
            _ = new ConversionHistory(path);
            Assert.Equal(corrupt, File.ReadAllText(path + ".broken"), ".broken が新しい中身になる");
            Assert.True(File.GetUnixFileMode(path + ".broken") == (UnixFileMode.UserRead | UnixFileMode.UserWrite), ".broken は本人だけが読み書きできる");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void P46_26_CorruptAgain_ReplacesBroken_KnownLimit()
    {
        // 既知の限界 (KNOWN_LIMITS): 壊れたまま、もう一度壊れると、.broken は新しい方で置き換わる (最初に壊れたコピーは残らない)。
        var directory = TempDir("again");
        try
        {
            var path = Path.Combine(directory, "conversions.json");
            File.WriteAllText(path, "{\"first\":");
            _ = new ConversionHistory(path);
            Assert.Equal("{\"first\":", File.ReadAllText(path + ".broken"), "1 回目の壊れた中身");
            File.WriteAllText(path, "{\"second\":");
            _ = new ConversionHistory(path);
            Assert.Equal("{\"second\":", File.ReadAllText(path + ".broken"), "2 回目の壊れた中身で置き換わる");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void P46_26_WarnLog_DoesNotContainTypedReading()
    {
        // 壊れた学習データを読めなかったときの警告に、打った読み (JSON のキー) を入れない。JsonException.Message は
        // "Path: $.打った読み.Used" のようにキーを含むので、「ログに入力した文字を残す」が OFF でも漏れていた。
        var directory = TempDir("log");
        try
        {
            var secret = "ひみつ" + Guid.NewGuid().ToString("N");
            var conversionsPath = Path.Combine(directory, "conversions.json");
            var translationsPath = Path.Combine(directory, "translations.json");
            File.WriteAllText(conversionsPath, "{\"" + secret + "\":{\"Text\":\"秘密\",\"Used\":\"not-a-date\"}}");
            File.WriteAllText(translationsPath, "{\"" + secret + "\":{\"sushi\":\"not-a-number\"}}");
            _ = new ConversionHistory(conversionsPath);
            _ = new TranslationHistory(translationsPath);
            var logged = string.Join("\n", Diagnostics.Log.Snapshot().Select(e => e.Message));
            Assert.True(logged.Contains("変換の学習データを読めませんでした") && logged.Contains("英訳の学習データを読めませんでした"), "警告は出る");
            Assert.True(!logged.Contains(secret), "打った読みをログに残さない");
        }
        finally { SafeFile.ResetBlockedForTests(); Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void P46_29_Data_BoundaryCases()
    {
        // P46-15 の査読 (RA-6): data を英語にする守りの境界。data を含む英語の複合語は英字のまま、data を含む日本語の語は今までどおり。
        // 同梱の英単語表と、実機の NSSpellChecker に近い模擬の両方で確かめる。
        foreach (var checker in new Detection.IWordChecker[] { Detection.BuiltInWordChecker.Shared, new NsLikeChecker() })
        {
            ExpectCommitted(checker,
                ("mydata", "mydata"), ("bigdata", "bigdata"), ("database", "database"), ("dataset", "dataset"), ("datacenter", "datacenter"),
                ("kyouhadatagaooi", "きょうはだたがおおい"), ("madatabun", "まだたぶん"), ("hidatabi", "ひだたび"), ("kodatawarinoaru", "こだたわりのある"),
                ("yamadatarouwoyobu", "やまだたろうをよぶ"), ("sorehadatadato", "それはだただと"), ("watashihadatawomiru", "わたしはだたをみる"),
                ("hedatari", "へだたり"), ("hedataru", "へだたる"));
        }
    }

    [Test]
    public static void P46_29_Data_KnownLimits()
    {
        // 既知の限界 (KNOWN_LIMITS): metadata は英語の複合語だが直らない (めただた。守りを外すと めたdata になるが、それも誤り)。
        ExpectCommitted(("metadata", "めただた"));
        ExpectCommitted(new NsLikeChecker(), ("metadata", "めただた"));
    }

    private const string ClearOriginal = "{\"ひみつ\":{\"Text\":\"秘密\"}}";

    [Test]
    public static void P46_30_ClearAll_UnreadableFile_IsDeletedAndSavingResumes()
    {
        // P46-26 は読めなかったファイルを守るため保存を止めるが、そのせいで「学習データをすべて消去」が効かなくなっていた
        // (メモリを空にしても書けず、打った読みがファイルに残るのに、例外が出ないので成功と表示された)。
        // 消去は利用者が確認して選んだ操作なので、止めていたファイルは消して、保存を再開する。
        if (OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess)
        {
            Console.WriteLine("  P46_30_ClearAll_UnreadableFile: スキップ (権限で読めなくできる環境だけ)");
            return;
        }
        var directory = TempDir("clear-unreadable");
        try
        {
            var conversionsPath = Path.Combine(directory, "conversions.json");
            var translationsPath = Path.Combine(directory, "translations.json");
            foreach (var path in new[] { conversionsPath, translationsPath })
            {
                File.WriteAllText(path, ClearOriginal);
                File.SetUnixFileMode(path, UnixFileMode.None);
            }
            var history = new ConversionHistory(conversionsPath);
            var translations = new TranslationHistory(translationsPath);
            Assert.True(SafeFile.IsBlocked(conversionsPath) && SafeFile.IsBlocked(translationsPath), "読めなかったので保存を止めている");
            Assert.True(LearningData.ClearAll(history, null, null, translations, null, [conversionsPath, translationsPath]), "消せたら成功を返す");
            foreach (var path in new[] { conversionsPath, translationsPath })
            {
                Assert.True(!File.ReadAllText(path).Contains("ひみつ"), Path.GetFileName(path) + ": 打った読みが残らない");
                Assert.True(!SafeFile.IsBlocked(path), Path.GetFileName(path) + ": 止めた状態が解ける");
            }
            history.Remember("くらいど", "クラウド");
            Assert.Equal("クラウド", new ConversionHistory(conversionsPath).Get("くらいど"), "消去のあとは保存が再開する (ファイルから読み直して確かめる)");
        }
        finally { SafeFile.ResetBlockedForTests(); Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void P46_30_ClearAll_LockedAtStartup_IsDeletedAndSavingResumes()
    {
        // 起動したとき他のプログラムが開いていて読めなかった (Windows の共有違反と同じ IOException。FileShare.None は Mac / Linux でも排他で開く) ファイルも同じ。
        var directory = TempDir("clear-locked");
        try
        {
            var path = Path.Combine(directory, "conversions.json");
            File.WriteAllText(path, ClearOriginal);
            ConversionHistory history;
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                history = new ConversionHistory(path);
                Assert.True(SafeFile.IsBlocked(path), "読めなかったので保存を止めている");
            }
            Assert.True(LearningData.ClearAll(history, null, null, null, null, [path]), "消せたら成功を返す");
            Assert.True(!File.ReadAllText(path).Contains("ひみつ"), "打った読みが残らない");
            Assert.True(!SafeFile.IsBlocked(path), "止めた状態が解ける");
        }
        finally { SafeFile.ResetBlockedForTests(); Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void P46_30_ClearAll_CannotDeleteBlockedFile_ReturnsFalse()
    {
        // 止めていたファイルを消せなかった (フォルダーに書けない) ときは、成功と答えずに失敗を返す (Mac は「消去できなかったものがあります」と出す)。
        if (OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess)
        {
            Console.WriteLine("  P46_30_ClearAll_CannotDeleteBlockedFile: スキップ (権限で消せなくできる環境だけ)");
            return;
        }
        var directory = TempDir("clear-nodelete");
        try
        {
            var path = Path.Combine(directory, "conversions.json");
            File.WriteAllText(path, ClearOriginal);
            File.SetUnixFileMode(path, UnixFileMode.None);
            var history = new ConversionHistory(path);
            Assert.True(SafeFile.IsBlocked(path), "読めなかったので保存を止めている");
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            Assert.True(!LearningData.ClearAll(history, null, null, null, null, [path]), "消せなかったので失敗を返す");
            Assert.True(SafeFile.IsBlocked(path), "消せなかったので止めたまま");
        }
        finally
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            SafeFile.ResetBlockedForTests();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public static void P46_30_ClearLeftovers_RemovesInterruptedTempFiles()
    {
        // 保存の途中で止まると 名前.<GUID>.tmp (.broken を書く途中なら 名前.broken.<GUID>.tmp) が残る。打った内容が入っているので、全消去で消す。
        var directory = TempDir("clear-tmp");
        try
        {
            var path = Path.Combine(directory, "conversions.json");
            var other = Path.Combine(directory, "userdict.txt");
            var leftovers = new[] { path + "." + Guid.NewGuid().ToString("N") + ".tmp", path + ".broken." + Guid.NewGuid().ToString("N") + ".tmp" };
            var kept = new[] { other + "." + Guid.NewGuid().ToString("N") + ".tmp", Path.Combine(directory, "notes.txt") };
            foreach (var file in leftovers.Concat(kept)) File.WriteAllText(file, "ひみつ");
            Assert.True(LearningData.ClearLeftovers([path], []), "消せる");
            foreach (var file in leftovers) Assert.True(!File.Exists(file), Path.GetFileName(file) + ": 消える");
            foreach (var file in kept) Assert.True(File.Exists(file), Path.GetFileName(file) + ": 学習データのものではないので消さない");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void P46_31_WarnLog_LanguageMemoryAndUserModel_DoNotContainTypedWords()
    {
        // P46-26 (RC-5) は変換と英訳の学習データの警告から打った読みを外したが、languages.json (打った英語/日本語の語がキー) と
        // model.json (打った綴りの先頭がキー) の警告は JsonException.Message のままで、"Path: $.打った語" がログに出た。
        var directory = TempDir("log2");
        try
        {
            var secret = "ひみつ" + Guid.NewGuid().ToString("N");
            var languagesPath = Path.Combine(directory, "languages.json");
            var modelPath = Path.Combine(directory, "model.json");
            File.WriteAllText(languagesPath, "{\"" + secret + "\":\"not-an-object\"}");
            File.WriteAllText(modelPath, "{\"version\":1,\"prefixes\":{\"" + secret + "\":\"not-an-object\"}}");
            _ = new LanguageMemory(languagesPath);
            _ = new Meltype.Learning.UserModel(modelPath);
            var logged = string.Join("\n", Diagnostics.Log.Snapshot().Select(e => e.Message));
            Assert.True(logged.Contains("英語 / 日本語の学習データを読めませんでした") && logged.Contains("model.json を読み込めなかった"), "警告は出る");
            Assert.True(!logged.Contains(secret), "打った語をログに残さない");
        }
        finally { SafeFile.ResetBlockedForTests(); Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void P46_31_Describe_ShowsOnlyKindAndPosition()
    {
        // 位置が無い JsonException でも「行 、位置 」と空欄にならない。ほかの例外は種類と HResult (原因の切り分けに使う数字) だけで、メッセージ (パスが入る) は出さない。
        var noPosition = SafeFile.Describe(new System.Text.Json.JsonException("Path: $.ひみつ"));
        Assert.True(!noPosition.Contains("ひみつ") && !noPosition.Contains("行 、") && noPosition.Contains('?'), "位置が無いときは ?: " + noPosition);
        var io = SafeFile.Describe(new IOException("Access to the path '/Users/someone/secret.json' is denied."));
        Assert.True(io.StartsWith("IOException (0x") && !io.Contains("someone") && !io.Contains('/'), "種類と HResult だけ: " + io);
    }
}
