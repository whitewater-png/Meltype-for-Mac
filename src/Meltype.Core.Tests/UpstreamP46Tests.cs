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

    /// <summary>
    /// 判定器 checker を差し込んで打ち、確定した文字が suffix で終わるかだけを確かめる。
    /// 確定の前半が、確かめたい規則と別の規則の答えで、そこまで期待値に入れると別の規則を直したときに赤になるときに使う。
    /// </summary>
    private static void ExpectCommittedEndsWith(Detection.IWordChecker checker, params (string Typed, string Suffix)[] cases)
    {
        foreach (var (typed, suffix) in cases)
        {
            var got = Committed(checker, typed);
            Assert.True(got.EndsWith(suffix, StringComparison.Ordinal), $"{typed} 期待値: …{suffix} で終わる / 実際: {got}");
        }
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

    /// <summary>打ち終わった時点の表示 (変換ボックスの文字) と、続けて Enter で確定した結果を返す。終わったら元の判定器に戻す。</summary>
    private static (string Shown, string Committed) ShownAndCommitted(Detection.IWordChecker checker, string typed)
    {
        var previous = CompositionTests.Detector.SpellChecker;
        CompositionTests.Detector.SpellChecker = checker;
        try
        {
            var k = new CompositionTests.Keyboard();
            k.Type(typed);
            var shown = k.Host.View?.Text ?? "";
            k.Type("\n");
            return (shown, k.Host.Document);
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

        /// <summary>変換エンジンに渡された読み (呼ばれたかだけでなく、何を渡したかを見る)。</summary>
        public readonly List<string> Received = [];

        private static string? Map(string hiragana) => hiragana switch { "な～～～" => "名～～～", "らーめん" => "ラーメン", _ => null };

        public string? Convert(string hiragana)
        {
            Calls++;
            Received.Add(hiragana);
            return Map(hiragana);
        }

        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null)
        {
            Calls++;
            Received.Add(hiragana);
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
    /// 実機の NSSpellChecker が単語と答える語のうち、確かめた 13 語 (shat・doit・ment・gat・gatt・mahout・techno・empowerment・entitlement・ament・underemployment・underachievement・emolument。後ろの 6 語は P46-47・P46-48・P46-50・P46-51・P46-61 で足した) だけを足した模擬。
    /// それ以外は同梱の英単語表のまま。実機はほかにも表にない語 (datas・taanal など) を単語と答えるので、無作為の 577 入力では 5.5% ほど食い違う (7 語のときに測った値。語を足したあとは測り直していない)。
    /// 見つけられるのは、この 13 語に関わる、語の境目の読み方が実機でだけ変わる退行に限られる。
    /// </summary>
    private sealed class NsLikeChecker : Detection.IWordChecker
    {
        public bool IsAvailable => true;

        public bool IsWord(string lower) =>
            lower is "shat" or "doit" or "ment" or "gat" or "gatt" or "mahout" or "techno" or "empowerment" or "entitlement" or "ament" or "underemployment" or "underachievement" or "emolument" || Detection.BuiltInWordChecker.Shared.IsWord(lower);
    }

    /// <summary>
    /// 論理の確認用 (実機のまねではない): cadent (ca + dent) を単語と答える判定器。IsPartOfLongerEnglishWord の「先頭の単位まで伸ばす」ところ (ループの j >= 0) を試すためだけに使う。
    /// 開発機で NSSpellChecker を直接呼んだ答え (tools/nsd) も同梱の英単語表も cadent を単語と答えないので、この形は今の判定器では起きない
    /// (利用者が cadent のような語を英字の語として学習した場合は別。推測。IME の中は実機未確認)。
    /// </summary>
    private sealed class HeadWordChecker : Detection.IWordChecker
    {
        public bool IsAvailable => true;

        public bool IsWord(string lower) => lower is "cadent" || Detection.BuiltInWordChecker.Shared.IsWord(lower);
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
        // 辞書の追加 (P46-1〜15) を、実機の NSSpellChecker の答えを一部の語だけまねた判定器 (NsLikeChecker。まねた語はその定義の注にある) でも確かめる。
        // 同梱の英単語表だけでは、実機でだけ起きる差 (語の境目の読み方の違い) が見えない。
        ExpectCommitted(new NsLikeChecker(),
            ("infobesity", "infobesity"), ("infobesitywo", "infobesityを"), ("kyouhainfobesity", "きょうはinfobesity"),
            ("vibecoding", "vibecoding"), ("oem3", "oem3"), ("Xcode", "Xcode"), ("DeepSeek", "DeepSeek"), ("Mozc", "Mozc"),
            ("guesstimation", "guesstimation"), ("greedflationwo", "greedflationを"), ("robonomicswo", "robonomicsを"), ("netizenrywo", "netizenryを"),
            ("japanese", "japanese"), ("japanesewo", "japaneseを"), ("data", "data"), ("datawo", "dataを"), ("kyouhadatawo", "きょうはだたを"),
            ("adtech", "adtech"), ("adtechwo", "adtechを"), ("kyouhaadtech", "きょうはadtech"), ("kyouhaadtechwo", "きょうはadtechを"));
    }

    [Test]
    public static void P46_23_Adtech_NoAfterAdtech_IsJapaneseWithNsLikeChecker()
    {
        // 実機の NSSpellChecker は techno を単語と答えるので、adtech + の (adtechno…) の no が英語側に取り込まれ、adtechnoyouna になっていた
        // (P46-7 で adtech を足した影響。P46-23 の時点では既知の限界として固定していた)。main の T1 (d36ef48: 見つけた英語の区間を後ろの長い語で
        // 捨てる条件に AND を足す) で、見つけた区間 (adtech) が同梱の一覧の語で、長い語 (techno…) が一覧に無く、後ろの部分が助詞 1 つ (no) のときは
        // 区間を捨てなくなり、直った。
        ExpectCommitted(new NsLikeChecker(), ("adtechnoyouna", "adtechのような"), ("adtechno", "adtechの"));
        ExpectCommitted(("adtechnoyouna", "adtechのような"));
    }

    [Test]
    public static void P46_41_Adtech_FollowedByEnglishLookingWord_KnownLimit()
    {
        // 既知の限界 (KNOWN_LIMITS K88): adtech の直後に、英語の語に読める続きが来ると、続きまで英字になる (P46-7 の前は adtechまで・adtechはなみ・adtechはは)。
        // 無作為の読み 1,218 形のうち 3 形 (NS・BI とも)。単独の adtech や、日本語の後ろ・助詞つきの形は直っているので、辞書には入れたままにしている。
        // 直ったら、この期待値を adtechまで・adtechはなみ・adtechはは に変える。
        foreach (var checker in new Detection.IWordChecker[] { Detection.BuiltInWordChecker.Shared, new NsLikeChecker() })
            ExpectCommitted(checker, ("adtechmade", "adtechmade"), ("adtechhanami", "adtechhanami"), ("adtechhaha", "adtechhaha"));
    }

    [Test]
    public static void P46_47_FragmentOfALongerEnglishWord_IsNotReadAsJapaneseTsu()
    {
        // 5 回目の統合の確認 (探索員 4 人): 統合 (P46 + main) でだけ、日本語 + 英単語 + する (souunemploymentsuru) が、そううnemployめんつる のように t と s が つ にまとまっていた。
        // 英単語で終わるかを後ろの単位からさかのぼって見るとき、P46-20 は「かな + つ が読みの頭になる断片」(ment = めんつ) を日本語として飛ばす。
        // main の規則は、伸ばす音の u から始まる語 (unemployment。本家 7a20aff) と、同梱の一覧にない母音 1 つの単位から始まる語 (empowerment。H1) を飛ばす。
        // 単独の main では ment が (実機の NSSpellChecker が単語と答えるので) 救い、単独の P46 では長い語が救っていたのが、両方が入ると何も見られなくなる。
        // 断片が知っている英単語で、さらに前へ伸ばした並びも知っている英単語なら、断片は長い語の終わりなので、読みの頭として飛ばさない。
        // 変異の確認 (5 回目の査読 RM-01、6 回目の RN-01、7 回目の RP1-01・02、8 回目の RQ1-01・02、9 回目の RR1-02、10 回目の RS1-01・02、11 回目の RT1-01・02、12 回目の RU1-01・03、13 回目の RW1-01・02、14 回目の RX1-01・02、15 回目の RY1-01・02、16 回目の RZ1-01・02、17 回目の SA1-01・02、18 回目の SB1-01・02。P46-47 で答えが変わる入力と、語頭から始まる語・記号をはさむ形の 2,848 入力を、BI と NS で現在のコードと比べた。
        // 7 回目の査読員は、長い語 (BI 3,360・NS 500) と別の作り方の入力 (BI は大文字・数字や記号をはさむ形・短い断片・非常に長い列・深い語の 4,292、NS は大文字と深い語の 668) の、合わせて BI 7,652・NS 1,168 入力で、
        // 8 回目の査読員は、長い語 39 × 頭 4 × 尾 2 = 312 入力と、大文字の置き方 76 × 頭 3 × 尾 2 = 456 入力で、BI と NS で比べた。
        // 8 回目の対応では、6 語の大文字の置き方 82 通り × 頭 3 × 尾 2 = 492 入力を、この模擬の判定器で比べた。
        // 9 回目の査読員は、略語 (AI・PC・IT・OK) の直後に頭が大文字の長い語を置いた 14 語 × 前 6 × 尾 2 = 168 入力を、BI と NS で比べた。
        // 10 回目の査読員は、3 文字の略語 (API・USB・NHK・SNS。対照に PC) の直後に頭が大文字の語を置いた 168 入力と、略語の直後に頭が小文字の語を置いた 168 入力
        // (どちらも 14 語 × 前 6 × 尾 2)、読みの頭になる断片 (ment・pant・shat・doit・dent・gatt と、頭が大文字のもの) の前に日本語や大文字を含む英字を置いた
        // 12 × 前 6 × 尾 3 = 216 入力を、BI と NS で比べた。
        // 11 回目の査読員は、3 文字の略語 (API・USB・NHK・SNS・CPU) の直後に頭が小文字の語を置いた 前 3 × 略語 5 × 語 10 × 尾 2 = 300 入力と、
        // 同じ略語の直後に読みの頭になる断片を置いた 前 3 × 略語 5 × 断片 8 = 120 入力を、BI と NS で比べた。
        // 12 回目の査読員は、伸ばす音の う で終わる前置き 11 (kyou・sou・kinou など) と対照 2 (kyouha・ima) の後ろに断片と語 11 を置いた 143 入力、
        // 前置き 8 × u で始まる英単語 15 × 尾 3 = 360 入力、7 文字以上の大文字だけの語 8 (WARNING・YOUTUBE など) × 形 10 = 80 入力を、BI と NS で比べた。
        // 13 回目の査読員は、12 回目の 143 入力と、伸ばす音の う で終わる前置き 8 (kyou・sou・yuu・kuu など) × 英単語 14 (commit・chat など) × 尾 2 (suru・shitai)
        // = 224 入力を、BI と NS で比べた。
        // 14 回目の査読員は、u の段の伸ばす音で終わる前置き 12 (yuu・kuu・juu など) と o の段の対照 2 (sou・kyou) × u で始まる語の頭を含む後ろ 6 (nitsuite・nsetsu など)
        // = 84 入力と、前置き 2 (kyouha・ima) × 7 文字以上の大文字だけの並び 11 (辞書に無い HTTPSAPI・GITHUBAPI など 8 と、辞書の語 WARNING・UNDERSTANDING・INTERNATIONAL)
        // × 後ろ 4 (amentsuru など)・3 (Amentsuru など) = 88・66 入力を、BI と NS で比べた。
        // 15 回目の査読員は、前置き 2 (kyouha・ima) × 10 文字以上の大文字だけの並び 10 (辞書に無い HTTPSAPIJP・GITHUBAPIJP など 8 と、辞書の語 INTERNATIONAL・UNDERSTANDING)
        // × 後ろ 3 (amentsuru・pantsuwokau・dentsu) = 60 入力を、BI と NS で比べた。
        // 16 回目の査読員は、大文字だけの並び 32 (7 文字以上の 16 と、QWER・AWSJ など 2〜6 文字の 16) × 8 形 (kyouha・ima・入力の頭の後の頭が大文字・小文字の語と、守りの 2 形)
        // = 256 入力を、BI と NS で比べた。
        // 17 回目の査読員は、語 6 (moment・document など) × 大文字だけの並び 5 × 形 6 (位置 3 × 語の頭が小文字・大文字) = 180 入力を、BI と NS で比べた):
        // - 長い語の下限 (ループの letters.Length >= 4) を 6 にすると 208 入力で答えが変わる → ament の例で赤になる。
        // - ループの j >= 0 を j > 0 にすると、現在の判定器 (同梱の表・NS) では 2,848 入力のどれも変わらないが、等価ではない。先頭の単位から始まる語が、全体も「かな + つ」の読みの頭に見えるとき
        //   (外側のループの先頭の単位でも ReadsAsJapaneseWithTsu が真になって受け入れられない) だけ変わる。今の判定器にはそのような語が無いので、cadent を単語と答える
        //   試験用の判定器 (HeadWordChecker) の例を下に足した (論理の確認。今の判定器では起きない形)。
        // - ループの「英字だけの単位」の条件を小文字だけ (IsAsciiLetterLower) にすると、大文字で始まる語 (Entitlement) を前へ伸ばせず、断片 ment が日本語の読みの頭になる → 大文字の例で赤になる。
        // - 前へ伸ばす深さに上限 (6・7 単位) を入れると、長い語の頭まで 8 単位さかのぼる語 (underemployment) を伸ばせない → その例で赤になる。
        // - ループの「英字だけの単位」の条件を、小文字だけか頭だけ大文字の単位にする (pO のような単位で止まる)、または大文字で始まる単位まで伸ばしたらそこで止める (Po で止まる) と、
        //   語の途中に大文字をはさむ語 (empOwerment・emPowerment) を前へ伸ばせず、断片 ment が日本語の読みの頭になる → 語の途中の大文字の例で赤になる。
        // - 前へ伸ばす長さに文字数の上限 (15 文字) を入れると、16 文字の語 (underachievement) を伸ばせない → その例で赤になる。
        // - 「大文字の略語の途中 (AI の I) から始まる語は見ない」規則 (EndsWithEnglishWordFromUnit。本家 #129、P45-10) を、前へ伸ばすループにも当てると、
        //   略語の直後の、頭が大文字の語 (PCUnderachievement の Underachievement) を前へ伸ばせない → 略語の例で赤になる (9 回目の査読員の 168 入力では、NS で 56 入力の答えが …めんつる・…めんつ に変わる。BI は 0)。
        //   9 回目の査読員の変異のうち、前へ伸ばす長さの上限を 16 文字にする変異 (NS 168・384 入力) と、大文字を 2 字以上含む単位で止める変異 (NS 280 入力) は、
        //   査読員の入力で答えが変わらず、テストで捕まえていない。
        // - 同じ規則を 3 文字以上の略語 (API) の直後だけに当てる、または略語の直後の、頭が小文字の語 (PCament の ament) を見ない、の 2 つの写し方は、上の略語の例では
        //   赤にならないが、NS で答えが変わる (10 回目の査読員の 168 入力ずつで、NS 44・24 入力が …めんつる・…めんつ に変わる。BI は 0) → 3 文字の略語の例と、頭が小文字の語の例で赤になる。
        // - 2 文字目以降に大文字を含む並び (PCpant) を、判定器に聞かずに長い語とみなす変異 (救いすぎ) は、救えない側の例では赤にならないが、同梱の表でも答えが変わる
        //   (10 回目の査読員の 216 入力で BI 33・NS 96 入力。kyouhaPCpantsuwokau が、取り込み前と同じ きょうはPCpantすをかう に戻る) → 断片の守りの例で赤になる。
        // - 3 文字以上の略語の直後の、頭が小文字の語 (APIament の ament) だけを見ない変異 (11 回目の査読員。上の 2 つの写し方を組み合わせたもの) は、上の略語の例では
        //   赤にならないが、NS で答えが変わる (査読員の 420 入力で NS 60 入力。BI は 0。kyouhaAPIamentsuru が きょうはAPIあめんつる になる) → 略語の長さごとの例で赤になる。
        // - 3 文字以上の略語の中まで伸ばしたら、判定器に聞かずに長い語とみなす変異 (救いすぎ。11 回目の査読員) は、PC の守りの例では赤にならないが、同梱の表でも答えが変わる
        //   (査読員の 420 入力で BI 45・NS 117 入力。kyouhaAPIpantsuwokau が きょうはAPIpantすをかう になる) → 略語の長さごとの守りの例で赤になる。
        // - この 2 つと、10 回目の「3 文字以上の略語の直後だけに当てる」変異で、略語の文字数を 4・5・6 文字にした変異も、略語の長さごとの例で赤になる。
        //   7 文字以上にした写し方は、略語に限らず大文字だけの語 (WARNING など) の直後で答えが変わる (12 回目の査読員の 80 入力。abbr7 は BI 0・NS 24、lowabbr7 は BI 0・NS 16、abbr7ov は BI 24・NS 24 入力。例: kyouhaWARNINGpantsuwokau が …pantすをかう に、kyouhaWARNINGamentsuru が …あめんつる に (NS))
        //   → 大文字だけの語 (7 文字の WARNING と 13 文字の UNDERSTANDING) の例で赤になる。文字数を 8〜13 にした写し方 (abbr8〜13・lowabbr8〜13・abbr8ov〜13ov) も、13 文字の UNDERSTANDING の例で赤になる。14 文字以上にした写し方 (abbr14・lowabbr14・abbr14ov) は、14 文字以上の大文字だけの語の例が無いので、テストで捕まえていない
        //   7 文字以上にした写し方 (lowabbr7・abbr7) を、直前の大文字の並びが辞書の語でない (略語の) ときだけに当てる変異 (capsnw7・capsnw7u。14 回目の査読員) は、
        //   7 文字以上の例が辞書の語 (WARNING・UNDERSTANDING) だけだと赤にならないが、答えが変わる (14 回目の査読員の入力で、capsnw7 は NS 16/88・BI 0、capsnw7u は NS 16/66・BI 0 入力。変わるのは辞書に無い 8 つの並びの amentsuru・Amentsuru の形だけで、kyouhaHTTPSAPIamentsuru が きょうはHTTPSAPIあめんつる に) → 辞書に無い大文字だけの並びの例で赤になる。
        //   この写し方の文字数 K を変えたもの (直前の K 文字以上が大文字で辞書の語でないとき。capsnwK・capsnwKu) は、辞書に無い並びの例が 8 文字 (HTTPSAPI) と 9 文字 (GITHUBAPI)
        //   だけだと、K = 10〜13 のもの (15 回目の査読員の capsnw10・capsnw10u・capsnw13) が赤にならないが、答えが変わる (15 回目の査読員の 60 入力で、capsnw10 は NS 16/60・BI 0 入力。変わるのは辞書に無い 8 つの並び × 前置き 2 の amentsuru の形だけで、kyouhaHTTPSAPIJPamentsuru が きょうはHTTPSAPIJPあめんつる に)
        //   → 辞書に無い並びを 7〜13 文字で 1 つずつ置いた例で、K = 2〜13 の写し方 (capsnw2〜13・capsnw2u〜13u) がどれも赤になる (K = 8〜13 の 1 本目の赤は、K 文字以上の辞書に無い並びのうち並びの最初の例。10〜13 は、足した HTTPSAPIJP・NASDAQJPAPI・HTTPSAPIJSON・GITHUBAPIJSON の例)。直前の大文字の並びがちょうど K 文字のときだけに当てる写し方 (capsnwKx・capsnwKux) も、
        //   K = 7〜13 のどれも、ちょうど K 文字の例 (JSONAPI・HTTPSAPI・GITHUBAPI・HTTPSAPIJP・NASDAQJPAPI・HTTPSAPIJSON・GITHUBAPIJSON) で赤になる。K を 14 以上にした写し方 (capsnw14・capsnw14u) は、14 文字以上の辞書に無い大文字だけの並びの例が無いので、テストで捕まえていない。
        //   この写し方を、並びが入力の頭から始まる (前置きの無い) ときだけに当てたもの (capsnw7s・lowabbr3s) と、ちょうど 3〜5 文字の並びだけに当てたもの
        //   (capsnw3x〜5x・capsnw3ux〜5ux。どれも 16 回目の査読員) は、前置きの無い頭が小文字の語の例が 2 文字の AIamentsuru だけで、3〜5 文字の並びの例が辞書の語
        //   (API・HTTP・HTTPS) だけだと赤にならないが、答えが変わる (16 回目の査読員の 256 入力で、capsnw7s は NS 26・lowabbr3s は NS 58・capsnw4x は NS 8 入力 (BI はどれも 0)。例: HTTPSAPIJPamentsu が HTTPSAPIJPあめんつ に、kyouhaQWERamentsuru が きょうはQWERあめんつる に) → 下の略語の並びの位置 3 × 語の頭 2 の形と、辞書に無い 3〜5 文字の並びの例で、16 回目の査読員の 12 本 (capsnw7s・lowabbr3s・capsnw2x〜6x・capsnw2ux〜6ux) がどれも赤になる (1 本目の赤は、capsnw7s が HTTPSAPIamentsu、lowabbr3s が APIamentsu、capsnw3x〜5x が kyouhaJPNamentsuru・kyouhaQWERamentsuru・kyouhaJETROamentsuru)。
        //   選んだ次元 (並びの長さ・辞書の語か・位置・語の頭が大文字か小文字か) の組ごとに、ちょうど K 文字の並びだけに当てる写し方を作って流した (格子の族): 150 本のうち、置いた組と長さ (辞書に無い並び 2〜13 文字・辞書の語の並び 3〜13 文字) の 138 本はどれも、その組の形とその長さの並びの例で赤になる。緑は、14 文字以上に当てるもの (6 本) と、2 文字の辞書の語の並びだけに当てるもの (6 本。IsKnownEnglishWord は 3 文字未満の語を辞書の語としないので、この写し方は答えを変えない)。
        //   語の後ろ (su・suru・wokau) で条件を付けた写し方は答えを変えない (開発機の NSSpellChecker と同梱の表で 423 入力の答えを変えない (判断のときの最後の単位 (n か t) で条件を付けた対照は NS で 276 入力を変える))。t と s を つ にまとめる判断は後ろを打つ前に行われ、後ろは差し込み先から見えない。
        //   語の頭が大文字の子音のときだけに当てる写し方 (sa1_consU。17 回目の査読員) は、大文字の並びの直後の頭が大文字の語の例が A (Ament) と U (PCUnderachievement)
        //   だけだと赤にならないが、答えが変わる (17 回目の査読員の 180 入力で、開発機の NSSpellChecker で 30・同梱の表で 0 入力。例: kyouhaHTTPSDocumentsuru が きょうはHTTPSDおくめんつる に (変異なしは きょうはHTTPSDocumentする)) → 下の略語の並びの頭が大文字の子音の形 ({abbr}Cement) で赤になる。
        //   語の頭の字の種類ごとに写し方を作って流した: 頭が大文字の子音 (sa1_consU・3 文字以上の並びの後だけの h_UC3)・A (h_UA)・A 以外の大文字の母音 (h_Uv。並びが 3 文字以上のときだけの h_Uv3・kyouha の後のほかだけの h_UvIH も)・小文字の a 1 つの単位 (h_la) だけに当てるものは赤になる (1 本目の赤は kyouhaPCCementsuru・kyouhaAPICementsuru・kyouhaPCAmentsuru・kyouhaPCUnderachievementsuru・kyouhaJPNElementsuru・imaPCElementsu・AIamentsuru)。頭が小文字の子音 (sa1_cons)・頭の単位が 2 字以上 (sa1_multi) だけに当てるものは緑で、答えを変えない (下の略語の並びの注)。a 以外の小文字の母音 1 つの単位だけに当てるもの (sa1_lvow) は、P46-60 では緑で答えを変えないと書いたが、同梱の一覧に無い語では答えが変わる (18 回目の査読員の 48 入力 (語は emolument・Emolument・inducement・Inducement の 4 つ、並び 4 × 位置 3) のうち、開発機の NSSpellChecker で 12 入力 (頭が小文字の emolument の 12 入力のすべて。ほかの 3 語の 36 入力は 0。P46-61 の注は母数を 24 と書いていた。19 回目の査読 SC1-02)。例: kyouhaPCemolumentsuru が きょうはPCえもlumenつる に (変異なしは きょうはPCemolumentする)。同梱の一覧に無い、頭が e・i・u・o の語だけに当てる写し方 (sb1_lvNB) も、emolument の 12 入力のすべて (査読員はこの写し方を emolument の 12 入力だけで測った)) → 下の略語の並びの emolument の形で赤になる (1 本目の赤は、どちらも kyouhaPCemolumentsuru)。頭が A 以外の大文字の母音の語に、並びの長さや位置で条件を付けた写し方 (18 回目の査読員の sb1_Uv4 (並び 4 文字以上)・sb1_UvH2 (入力の頭の 2 文字)・sb1_UvI3 (ima の後の 3 文字以上)・sb1_UvK4 (kyouha の後の 4 文字以上)) は、P46-60 の Element の 3 例では赤にならないが、答えが変わる (査読員の Element・Underachievement の 24 入力で、開発機の NSSpellChecker で 10・2・6・4 入力。例: kyouhaHTTPSElementsuru が きょうはHTTPSEぇめんつる に) → 下の Element の例の 1 つ目の組 (英字のまま残る 7 つの並び × 位置 3) で赤になる (1 本目の赤は kyouhaHTTPElementsuru・PCElementsu・imaJPNElementsu・kyouhaHTTPElementsuru)。直前の並びが 3 文字の辞書の語のときだけ・7 文字以上のときだけに当てる写し方 (19 回目の査読員の sc1_UvD3・sc1_Uv7) は、Element の例が 1 つ目の組だけだと赤にならないが、答えが変わる (査読員の 48 入力 (Element × API・NASDAQ と 7 文字以上の 14 の並び × 位置 3) で、開発機の NSSpellChecker で 3・42 入力。例: kyouhaAPIElementsuru が きょうはAPIEぇめんつる に (変異なしは きょうはあPIElementする)、kyouhaWARNINGElementsuru が きょうはWARNINGEぇめんつる に (変異なしは きょうはWARNINGEぇmentする)) → 下の Element の例の 2 つ目・3 つ目の組で赤になる (1 本目の赤は kyouhaAPIElementsuru・kyouhaWARNINGElementsuru)。直前の並びが 6 文字の辞書に無い語のときだけに当てる写し方 (sc1_Uv6。19 回目の査読員) は、開発機の NSSpellChecker では答えを変えない (NASDAQ を語と答えるので条件が真にならない。査読員の 48 入力で 0) が、この模擬の判定器は NASDAQ を語と答えないので、このテストでは赤になる (1 本目の赤は kyouhaNASDAQElementsuru)。頭が I・O の語だけに当てる写し方 (sb1_UIO。18 回目の査読員) は緑 (I・O の例は置いていない)。同梱の一覧に無い、頭が小文字の u・o の語だけに当てる写し方 (sc1_luo。19 回目の査読員) も緑 (その語の例は置いていない。開発機の NSSpellChecker の答えを変える入力は見つかっていない (20 回目の査読員の 36 入力 (underpayment・overpayment・oddment × 並び PC・JPN・HTTPS・HTTPSAPI × 位置 3) と、21 回目の査読員の 54 入力 (ointment・oddment・unemployment・overpayment・underpayment・outplacement × 並び PC・JPN・HTTP × 位置 3) で 0。どの入力も変異なしで語の後ろの suru・su まで英字のままで、差し込みの道を通る入力かは決め手が無い。21 回目の査読 SE1-03)。直前の並びが 1 文字のときだけに当てる写し方 (sd1_Uv1。20 回目の査読員) は、大文字の並びの直後の、頭が大文字の語の例が、2〜13 文字の 23 の並びの後ろだけだと赤にならないが、開発機の NSSpellChecker の答えを変える (1 文字の並び X・Q・A・I・O × 位置 3 の Element の 15 入力で 15。例: kyouhaXElementsuru が きょうはXEぇめんつる に (変異なしは きょうはXElementする)) → 下の Element の例の 4 つ目の組 (1 文字の並び X・A × 位置 3) で赤になる (1 本目の赤は kyouhaXElementsuru)。それを並びの字が子音のとき・母音のときに分けた写し方 (im20_Uv1C・im20_Uv1V。20 回目の実装者) も、4 つ目の組で赤になる (1 本目の赤は kyouhaXElementsuru・kyouhaAElementsuru)。並びが 1 文字のときだけ、頭が A の語・頭が大文字の子音の語に当てる写し方 (im20_UA1・im20_UC1。20 回目の実装者) は、下の並びのループの後ろの 1 文字の並びの例 (X・A の後ろの Ament・Cement) で赤になる (1 本目の赤は kyouhaXAmentsuru・kyouhaXCementsuru)。並びが X・A 以外の 1 文字のときだけに当てる写し方 (21 回目の査読員の se1_Uv1o・se1_UA1o・se1_UC1o) は、1 文字の並びの例が X・A だけだと赤にならないが、開発機の NSSpellChecker の答えを変える (1 文字の並び X・Q・A・I・O × 語の形 18 の 90 入力で、それぞれ 9。例: kyouhaQElementsuru が きょうはQEぇめんつる に (変異なしは きょうはQElementする)) → 下の 1 文字の並び 26 字の例で赤になる (1 本目の赤は kyouhaBElementsuru・kyouhaBAmentsuru・kyouhaBCementsuru)。並びの字ごとに分けた写し方 (21 回目の実装者の im21_Uv1_A〜Z・im21_UA1_A〜Z・im21_UC1_A〜Z の 78 本。並びが 1 文字で、その字のときだけ、頭が A 以外の大文字の母音・A・大文字の子音の語に当てる) は、im21_UA1_L のほかの 77 本が、開発機の NSSpellChecker の答えを変え (1 文字の並び 26 字 × 形 18 の 468 入力で、それぞれ 3)、どれも赤になる (1 本目の赤は、どれもその字の kyouha の後ろの形。X・A の字のものは上の X・A の例で、ほかの字のものは下の 26 字の例で赤になる)。im21_UA1_L は緑で、答えを変えない (並び L の後ろの Ament は、変異なしでも、P46 だけの木と P46-47 の前の統合でも崩れない。開発機の NSSpellChecker は lament を語と答える)。大文字の子音までさかのぼったら判定器に聞かずに長い語とみなす救いすぎ (h_overC) は、今の守りの例で赤になる (1 本目の赤は kyouhaHTTPpantsuwokau)。格子の族の語の頭に大文字の子音を足したもの: 75 本のうち、辞書に無い並び 2〜13 文字・辞書の語の並び 3〜13 文字の 69 本はどれも、その位置の Cement の形とその長さの並びの例で赤になる。緑は、14 文字以上に当てるもの (3 本) と、2 文字の辞書の語の並びだけに当てるもの (3 本。答えを変えない)。
        // - 伸ばす音の う から始まる語を、長い語の一部なら飛ばさない変異と、断片が長い語の一部なら外側の最後の判定を飛ばしてすぐ英単語とみなす変異 (どちらも 11 回目の査読員) は、
        //   査読員の入力 (前者は 56 入力、後者は 420 入力。BI・NS) で答えが変わらず、テストで捕まえていない (後者は、断片が同梱の一覧にない母音 1 つの単位から始まるときだけ
        //   差が出うる)。
        // - 伸ばす音の う の直後の断片 (きょう|pant・そう|dent) を、読みの頭として飛ばさない変異 (救いすぎ。12 回目の査読員) は、上の守りの例では赤にならないが、
        //   同梱の表でも答えが変わる (査読員の 143 入力で BI 33・NS 88 入力。変わった答えは、どれも取り込み前・main と同じ。kyoupantsuwokau が、取り込み前・main と同じ きょうpantすをかう に戻る) → 伸ばす音の う の直後の守りの例で赤になる。
        //   同じ変異を、前の音が u の段の伸ばす音 (ゆう・くう・つう) の直後だけに当てる写し方 (13 回目の査読員) は、o の段 (きょう・そう・きのう) の守りの例では
        //   赤にならないが、同梱の表でも答えが変わる (査読員の 143 入力で BI 9・NS 24 入力。変わった答えは、どれも取り込み前・main と同じ。yuupantsu が ゆうpantす に戻る) → u の段の守りの例で赤になる。o の段の直後だけに当てる写し方は、o の段の守りの例で赤になる。
        //   断片の直前が い 1 つの単位 (きれい・せんせい の い) なら読みの頭として飛ばさない変異 (13 回目の査読員) は、P46-20 の例 (waishatsu) で赤になる。
        // - 伸ばす音の う から始まる語を見ない規則 (本家 7a20aff) を、伸ばす音の う の直後の単位から始まる語 (きょう|commit) にも当てる変異 (救えない。13 回目の査読員) は、
        //   査読員の 224 入力で BI 88・NS 88 入力の答えが崩れる (kyoucommitsuru が きょうこっみつる に。崩れる前の答えは 5 つの木で同じ)。12 回目の 143 入力でも、BI で 22 入力 (kyoudocumentsuru が きょうどくめんつる に) が崩れる → 伸ばす音の う の直後の英単語の例で赤になる。u の段・o の段の伸ばす音の直後だけに当てる写し方も、それぞれの例で赤になる。
        // - 外側のループの略語の規則 (本家 #129、P45-10) を頭が小文字の語にも当てる変異と、長い語かを見るときに入力途中の t を外して渡す変異 (どちらも 12 回目の査読員) は、
        //   上の例で赤になる。伸ばす音の う から始まる語を見ない規則 (本家 7a20aff) を、つ が続かないときにも当てる変異 (12 回目の査読員) は、査読員の 143 + 360 入力
        //   (BI・NS) で答えが変わらず、テストで捕まえていない。
        // - 伸ばす音の う から始まる語を見ない規則 (本家 7a20aff) を前へ伸ばすループにも当てる変異 (10 回目の査読員) は、長音の u の例で赤になる (足した例は無い)。
        // - 伸ばす音の う を見分ける規則 (IsLongVowelU) を、前の音が o の段のときだけにする変異 (lvo。u の段 (ゆう・くう・じゅう) の分岐を外す。14 回目の査読員) は、
        //   14 回目の査読員の 84 入力で BI 72・NS 72 入力の答えが変わる (u の段の入力すべて。o の段の対照は変わらない。変わった答えは、どれも取り込み前・P46 だけの木と同じで、yuunitsuite が ゆunitsuite に戻る) → u の段の伸ばす音の直後に u で始まる語の頭が来る例 (yuunitsuite) で赤になる。
        // - 数字で終わる略語 (MP3) の直後の、頭が大文字の語を見ない変異 (10 回目の査読員) は、査読員の 168 入力 (BI・NS) で答えが変わらず、テストで捕まえていない。
        // - 前へ伸ばす深さを単位で数えて、上限を 8・10 単位にした変異は、8 回目の査読員の長い語の 312 入力 (BI・NS) で答えが変わらなかった (8 単位は、上の 492 入力でも同じ)。
        //   9 単位以上さかのぼらないと届かない語は見つけていないので、この 2 つはテストで捕まえていない。
        // - 最初の letters.Length < 4 を外す (同じ単位の後ろの本判定が 4 文字以上を要るので、判定器を呼ぶ回数が増えるだけ)・ループの「英字だけの単位」の条件を外す
        //   (IsKnownEnglishWord は小文字にしてから a-z だけの並びかを見るので、数字・記号をはさんだ並びは以降ずっと知らない語になり、元のコードがそこでループを終えるのと同じ。
        //   大文字は元の条件 (IsAsciiLetter) も通す。小文字にすると a-z になる文字 (U+212A ケルビン記号) だけは例外だが、打鍵では普通出てこない。6 回目の RN が試して差なし) は、
        //   読みでも測っても等価。
        ExpectCommitted(new NsLikeChecker(),
            // 長音の u から始まる語 (同梱の一覧にある unemployment)。o・u の段の後ろ (このう) でも同じ
            ("souunemploymentsuru", "そうunemploymentする"), ("kyouunemploymentsurudake", "きょうunemploymentするだけ"), ("konounemploymentsuru", "このunemploymentする"),
            // 同梱の一覧にない、母音で始まる語。前が o・u の段でなくても (きょうは・いま・これを) 同じ
            ("kyouhaempowermentsuru", "きょうはempowermentする"), ("imaentitlementsuru", "いまentitlementする"), ("korewoempowermentsu", "これをempowermentす"),
            // 前の 1 単位 (a) と断片 (ment) で、短い語 (ament。5 文字) になる形。ループの中の長い語の下限を 6 以上にすると、日本語の いまあめんつる に戻る (5 回目の査読 RM-01)
            ("imaamentsuru", "いまamentする"), ("kyouhaamentsuru", "きょうはamentする"),
            // 守り: 前に英単語が続かない断片は P46-20 のとおり日本語 (めんつ・しゃつ)
            ("mentsu", "めんつ"), ("kyouhamentsuwo", "きょうはめんつを"), ("shatsu", "しゃつ"), ("kyouhashatsuwokau", "きょうはしゃつをかう"),
            // 守り: 語尾が su で始まらなければ t と s は つ にならない。語全体が先頭にあるときも今までどおり
            ("kyouhaempowermentsareta", "きょうはempowermentされた"), ("empowermentsuru", "empowermentする"), ("sounitsuite", "そうについて"));
        // 先頭の単位から始まる語 (ループの j >= 0 の境目。6 回目の査読 RN-01)。cadent は ca + dent で、全体も断片 (dent) も知っている語、全体は「かな + つ」の読みの頭にも見える。
        // 先頭の単位まで伸ばさないと、全体が日本語に読まれる (かでんつる)。対照は先頭が日本語の形。
        ExpectCommitted(new HeadWordChecker(), ("cadentsuru", "cadentする"), ("kyouhacadentsuru", "きょうはcadentする"));
        // 大文字で始まる語 (7 回目の査読 RP1-01)。条件を小文字だけにすると Entitlement の E で止まり、きょうはEntitleめんつる になる。
        // 長い語の頭まで 8 単位さかのぼる語 (RP1-02)。深さを 6・7 単位で止めると崩れる (この模擬の判定器では いまうんでれmployめんつる、開発機の NSSpellChecker では いまうんでremployめんつる)。
        ExpectCommitted(new NsLikeChecker(), ("kyouhaEntitlementsuru", "きょうはEntitlementする"), ("imaEntitlementsu", "いまEntitlementす"),
            ("imaunderemploymentsuru", "いまunderemploymentする"));
        // 16 文字の語 (8 回目の査読 RQ1-02)。前へ伸ばす長さを 15 文字までにすると崩れる (この模擬の判定器では いまうんでらちえゔぇめんつる、開発機の NSSpellChecker では いまunderachieveめんつる)。
        ExpectCommitted(new NsLikeChecker(), ("imaunderachievementsuru", "いまunderachievementする"), ("kyouhaunderachievementsu", "きょうはunderachievementす"));
        // 語の途中に大文字をはさむ語 (RQ1-01)。確定の前半 (きょうはえmpOwer… の え) は別の規則の答えで、取り込み前の木から同じなので、語の終わり (断片 ment + する) だけを見る。
        // pO で止める・Po で止めると、どちらも …めんつる になる。今のコードの答えは、開発機の NSSpellChecker で流しても、この模擬の判定器と同じ。
        ExpectCommittedEndsWith(new NsLikeChecker(), ("kyouhaempOwermentsuru", "mentする"), ("imaemPowermentsuru", "mentする"));
        // 大文字の略語の直後に、頭が大文字の長い語が来る形 (9 回目の査読 RR1-02)。略語の途中から始まる語を見ない規則を前へ伸ばすループにも当てると、
        // Underachievement の U・Ament の A で止まって …めんつる になる。今のコードの答えは、開発機の NSSpellChecker で流しても、この模擬の判定器と同じ
        // (NS では、取り込み前と main も同じ答えで、P46 だけの木と P46-47 の前の統合は …めんつる・…めんつ に崩れる。同梱の表では、どの木でも …めんつる・…めんつ)。
        ExpectCommitted(new NsLikeChecker(), ("kyouhaPCUnderachievementsuru", "きょうはPCUnderachievementする"), ("kyouhaPCAmentsuru", "きょうはPCAmentする"),
            ("PCAmentsu", "PCAmentす"));
        // 3 文字の略語の直後に頭が大文字の語、略語の直後に頭が小文字の語 (10 回目の査読 RS1-01)。略語の規則を 3 文字以上の略語の直後だけに当てる・頭が小文字の語を見ない、の
        // どちらの写し方でも …めんつる・…めんつ になる。今のコードの答えは、開発機の NSSpellChecker で流しても、この模擬の判定器と同じ (NS では取り込み前と main も同じ答え。
        // 3 文字の略語の例は P46 だけの木と P46-47 の前の統合で、頭が小文字の語の例は P46-47 の前の統合だけで崩れる。同梱の表では、どの木でも崩れる)。
        ExpectCommitted(new NsLikeChecker(), ("kyouhaAPIAmentsuru", "きょうはAPIAmentする"), ("imaUSBAmentsu", "いまUSBAmentす"),
            ("AIamentsuru", "AIamentする"), ("kyouhaPCamentsu", "きょうはPCamentす"));
        // 守り (10 回目の査読 RS1-02): 大文字を含む英字の直後でも、読みの頭になる断片 (ぱんつ・でんつ) は P46-20 の規則のとおり日本語。2 文字目以降に大文字を含む並びを
        // 判定器に聞かずに長い語とみなすと、取り込み前・main と同じ きょうはPCpantすをかう・PCdentす に戻る (同梱の表でも NS でも)。
        foreach (var checker in new Detection.IWordChecker[] { Detection.BuiltInWordChecker.Shared, new NsLikeChecker() })
            ExpectCommitted(checker, ("kyouhaPCpantsuwokau", "きょうはPCぱんつをかう"), ("PCdentsu", "PCでんつ"));
        // 守り (12 回目の査読 RU1-01): 伸ばす音の う の直後 (きょう・そう・きのう) でも、読みの頭になる断片は P46-20 の規則のとおり日本語。伸ばす音の う の直後の断片を
        // 読みの頭として飛ばさないと、取り込み前・main と同じ きょうpantすをかう・そうでんtす・きのうpantす に戻る (同梱の表でも NS でも。NS では きょうmentすゆ・きょうshatす も)。
        foreach (var checker in new Detection.IWordChecker[] { Detection.BuiltInWordChecker.Shared, new NsLikeChecker() })
            ExpectCommitted(checker, ("kyoupantsuwokau", "きょうぱんつをかう"), ("soudentsu", "そうでんつ"), ("kinoupantsu", "きのうぱんつ"));
        ExpectCommitted(new NsLikeChecker(), ("kyoumentsuyu", "きょうめんつゆ"), ("kyoushatsu", "きょうしゃつ"));
        // 守り (13 回目の査読 RW1-01): 伸ばす音の う は、前の音が o の段 (きょう) でも u の段 (ゆう・くう) でもよい (IsLongVowelU)。u の段の後ろでも、読みの頭になる断片は
        // P46-20 の規則のとおり日本語。u の段の後ろだけで断片を読みの頭として飛ばさないと、取り込み前・main と同じ ゆうpantす・くうでんtす に戻る (同梱の表でも NS でも。NS では ゆうshatす も)。
        foreach (var checker in new Detection.IWordChecker[] { Detection.BuiltInWordChecker.Shared, new NsLikeChecker() })
            ExpectCommitted(checker, ("yuupantsu", "ゆうぱんつ"), ("kuudentsu", "くうでんつ"));
        ExpectCommitted(new NsLikeChecker(), ("yuushatsu", "ゆうしゃつ"));
        // 守り (13 回目の査読 RW1-02): 伸ばす音の う の直後に英単語が来る形 (きょう|commit|する・ゆう|chat|する) は英単語のまま。伸ばす音の う から始まる語を見ない規則
        // (本家 7a20aff) を、う の直後の単位から始まる語にも当てると、きょうこっみつる・ゆうちゃつる に崩れる (同梱の表でも NS でも)。崩れる前の答えは取り込み前から同じなので、P46 の改善を守る例ではなく、P46-55 で足した守りの例の反対側 (英字に残す側) を見る例。
        foreach (var checker in new Detection.IWordChecker[] { Detection.BuiltInWordChecker.Shared, new NsLikeChecker() })
            ExpectCommitted(checker, ("kyoucommitsuru", "きょうcommitする"), ("yuuchatsuru", "ゆうchatする"));
        // 守り (14 回目の査読 RX1-01): 伸ばす音の う から始まる語を見ない規則 (本家 7a20aff。sounitsuite が そうについて) は、前の音が u の段の伸ばす音 (ゆう・じゅう) でも
        // 同じく日本語。この u の段の分岐 (IsLongVowelU の 'u') は本家 7a20aff の IsLongVowelU にすでにあり、Mac には main の 58ec05d (本家 7a20aff を t + s の判定に絞った移植) で
        // 入ったもので、P46 で生じた穴ではない (7a20aff のテストの 5 例のうち、伸ばす音の う から始まる語の例は o の段の sounitsuite・hontounitsukareta・kaijounitsukuto だけで
        // (ほかの 2 例は英単語のままの kyouhacommitsuru・editsuru)、上の u の段の守りの例は、この分岐を外しても答えが変わらない。この書き方は 15 回目の査読 RY1-02 で直した)。
        // u の段の分岐を外すと、取り込み前・P46 だけの木と同じ ゆunitsuite・じゅunitsuite に戻る (同梱の表でも NS でも。58ec05d が入った main・P46-47 の前の統合・統合は、この例の答え)。
        foreach (var checker in new Detection.IWordChecker[] { Detection.BuiltInWordChecker.Shared, new NsLikeChecker() })
            ExpectCommitted(checker, ("yuunitsuite", "ゆうについて"), ("juunitsuite", "じゅうについて"));
        // 略語の長さ (2〜6 文字) ごとに、略語の直後に頭が大文字の語・頭が小文字の語 (NS) と、読みの頭になる断片 (同梱の表と NS) を置く (11 回目の査読 RT1-01・02)。
        // 略語の規則や長い語とみなす規則を「n 文字以上の略語」に写す変異は、n を変えるたびに別の変異になるので、長さごとに例を置く。今のコードの答えは、開発機の
        // NSSpellChecker で流しても、この模擬の判定器と同じ (NS の語の例は取り込み前と main も同じ答えで、頭が大文字の語は P46 だけの木と P46-47 の前の統合で、
        // 頭が小文字の語は P46-47 の前の統合だけで崩れる。守りの例は、取り込み前と main が …pantすをかう・…pantす。同梱の表では、語の例はどの木でも崩れる)。
        // 略語の最後の字と断片で知っている語になる形 (API + dent = Ident。開発機の NSSpellChecker は ident を単語と答える) は、PCAment (PC + Ament) と同じ形で
        // 見分けられないので、長い語の一部として英字に残る (NS では取り込み前・main と同じ APIdentす。P46 だけの木と P46-47 の前の統合では APIでんつ)。例には入れていない。
        // 大文字だけの語は略語に限らないので、7 文字の WARNING と 13 文字の UNDERSTANDING も同じ形で置く (12 回目の査読 RU1-03)。開発機の NSSpellChecker と同梱の表で 5 つの木に流した答えも、上の略語の例と同じ形 (語の例は NS で今のコードの答えのとおり、守りの例は取り込み前と main が …pantすをかう・…pantす)
        // 7 文字以上の、辞書に無い大文字だけの並びも、7〜13 文字で 1 つずつ同じ形で置く (8 文字の HTTPSAPI と 9 文字の GITHUBAPI は 14 回目の査読 RX1-02、7 文字の JSONAPI・
        // 10 文字の HTTPSAPIJP・11 文字の NASDAQJPAPI・12 文字の HTTPSAPIJSON・13 文字の GITHUBAPIJSON は 15 回目の査読 RY1-01)。7 文字以上の例が辞書の語 (WARNING・UNDERSTANDING)
        // だけだと、大文字だけの語の規則を、直前の大文字の並びが辞書の語でない (略語の) ときだけに当てる写し方が赤にならない (NS では kyouhaHTTPSAPIamentsuru が きょうはHTTPSAPIあめんつる に、kyouhaHTTPSAPIAmentsuru が きょうはHTTPSAPIAめんつる に崩れる。同梱の表では、この 4 つの形はどの木でも崩れているので答えが変わらない)。
        // 辞書に無い並びの例が 8・9 文字だけだと、その写し方を 10 文字以上の並びだけに当てたもの (capsnw10 など) も赤にならない (NS では、足した 10〜13 文字の並びの頭が小文字の 2 つの形が崩れる (kyouhaHTTPSAPIJPamentsuru が きょうはHTTPSAPIJPあめんつる に)。同梱の表では、この形はどの木でも崩れているので答えが変わらない)。
        // 形と並びは、選んだ次元 (下の位置・語の頭・並びの長さ・辞書の語か) をそろえて置く (16 回目の査読 RZ1-01・02、17 回目の SA1-01、18 回目の SB1-01・02)。差し込み先
        // (IsPartOfLongerEnglishWord のループ) で写し方が条件にできるものは、ほかにもある (前置きの中身・並びの字・語の頭の字と単位の長さ・断片と語・判定器のほかの述語など)。
        // P46-59 の注の「条件にできる次元」は選んだ次元で、語の頭は大文字か小文字かだけ、語の形の頭は A・a (ament) だけで、語の頭が大文字の子音の形が無かった (17 回目の査読 SA1-01):
        // - 位置 3 (kyouha の後・ima の後・入力の頭) × 語の頭 2 (大文字・小文字) の 6 形をどれも置く。入力の頭の頭が小文字の語 ({abbr}amentsu) と ima の後の頭が大文字の語
        //   (ima{abbr}Amentsu) は 16 回目に足した (それまで前置きの無い頭が小文字の語の例は 2 文字の AIamentsuru だけだった。RZ1-01)。守りの断片も 3 つの位置に置く
        //   (ima{abbr}pantsu を足した)。
        // - 並びは、辞書に無い並びを 2〜13 文字で 1 つずつ (3・4・5 文字の JPN・QWER・JETRO は 16 回目に足した。それまで 3〜5 文字の例は辞書の語の API・HTTP・HTTPS
        //   だけだった。RZ1-02)、大文字だけの辞書の語を 3〜13 文字で 1 つずつ (6・8〜12 文字の SYSTEM・PASSWORD・DIRECTORY・TECHNOLOGY・INFORMATION・RELATIONSHIP は
        //   16 回目に足した) 置く。2 文字の並びは、どの判定器でも辞書の語にならない (IsKnownEnglishWord は 3 文字未満の語を辞書の語としない) ので、2 文字の辞書の語の並びは置けない。
        //   辞書の語か否かは、IsKnownEnglishWord (同梱の英語の辞書 english.txt と固有名詞を見て、4 文字以上なら判定器も見る。覚えた語も見るが、このテストには無い) の答えで、テストの判定器は NsLikeChecker (同梱の表 english-words.txt と足した語)。3〜5 文字の API・HTTP・HTTPS は english.txt の語で、同梱の表には無い (P46-60 までの注は「NsLikeChecker (同梱の表) の答え」と書いていた。18 回目に実装者が見つけた)。開発機の NSSpellChecker は NASDAQ を辞書の語と答える (NS では 6 文字の辞書に無い並びの例は無い)。
        // - 語の後ろ (su・suru・wokau) は次元に入れていない (上の変異の注のとおり、差し込み先から見えない)。
        // - 語の頭の字の種類で、外側のループ (EndsWithEnglishWordFromUnit) が救うかが違う。頭が大文字の語は、大文字の並びの直後の大文字から始まる語を見ない規則 (AI|Init。
        //   本家 #129、P45-10) で外側のループが見ないので、母音 (Ament・Underachievement・Element) でも子音 (Cement) でも、この差し込み先だけが救う。英字の直後の、頭が小文字の
        //   母音 1 つの単位の語は、外側のループが同梱の一覧にある語だけを見る (CompositionText.cs の fromLoneVowel と IsBundledEnglishWord) ので、同梱の一覧に無い語 (ament・
        //   emolument) は差し込み先だけが救い、同梱の一覧にある語 (element・amusement) は外側のループが語の頭から見て救う。分かれ目は、頭が a か否かではなく同梱の一覧にあるか
        //   (P46-60 の注は a か否かで分け、a 以外の小文字の母音 1 つの単位だけに当てる写し方 (sa1_lvow) は答えを変えないと書いたが、確かめた a 以外の語は同梱の一覧にある
        //   element だけだった。18 回目の査読 SB1-02)。頭が小文字の子音の語 (document・cement) と、頭の単位が 2 字以上の語 (do・ce) も、外側のループが語の頭から見て救う
        //   (これだけに当てる写し方は答えを変えない: sa1_cons・sa1_multi は、開発機の NSSpellChecker と同梱の表で、頭の種類ごとの語 23 × 並び 4 × 位置 3 = 276 入力の答えを
        //   変えない (17 回目の査読員の 180 入力でも 0))。語の形に、頭が大文字の子音の 3 形 (kyouha{abbr}Cementsuru・ima{abbr}Cementsu・{abbr}Cementsu) を 17 回目に足した
        //   (語は Cement、断片は ment。答えは開発機の NSSpellChecker で流した統合の木の答えで、Cえment の え は別の規則の答え。その規則を直すと、この 3 形の期待値 (69 入力) も
        //   変わる (この形が見るのは、断片 ment が英字のままか。18 回目の査読 SB1-03)。5 つの木では今の頭が大文字の語 ({abbr}Amentsu) と同じ木で崩れる)。同梱の一覧に無い、
        //   頭が a 以外の小文字の母音 1 つの単位の語の 3 形 (kyouha{abbr}emolumentsuru・ima{abbr}emolumentsu・{abbr}emolumentsu) を 18 回目に足した (emolument は、開発機の
        //   NSSpellChecker が語と答え、同梱の一覧に無いので、模擬の判定器に足した。答えは開発機の NSSpellChecker で流した統合の木の答えで、23 の並びとも同じ形。5 つの木では、
        //   P46-47 の前の統合だけが …えもlumenつ(る) に崩れ、ほかの 4 つは英字のまま。同梱の表では、どの木でも …えもぅめんつ(る) に崩れる)。
        //   頭が A 以外の大文字の母音の語 (Element・Underachievement) は、統合の木でも並びによって答えが違う (Element は、2〜6 文字の 9 つの並びのうち PC・JPN・HTTP・
        //   QWER・HTTPS・JETRO・SYSTEM の後ろでは英字のまま、API・NASDAQ の後ろでは英字のまま残るが並びの頭が崩れ、7 文字以上の 14 の並びの後ろでは …Eぇment… になり、
        //   1 文字の並び X・A の後ろでも英字のまま残る (A は あ になる)。
        //   並びの頭の崩れも Eぇ も、取り込み前の木から同じ答え) ので形には入れず、並びのループの後ろに、Element の例を 23 の並びのすべて × 位置 3 に、答えの形で 3 つの組に
        //   分けて置き、1 文字の並び X・A × 位置 3 を 4 つ目の組にした (17 回目に位置 3 つに 1 つずつ足し、18 回目に英字のまま残る 7 つの並びに、19 回目に 23 の並びの
        //   すべてにし、20 回目に 1 文字の並びを足した。3 例では、並びの長さや位置で条件を付けた写し方 (18 回目の査読員の sb1_Uv4・sb1_UvH2・sb1_UvI3・sb1_UvK4) が、
        //   7 つの並びでは、直前の並びの長さで条件を付けた写し方 (19 回目の査読員の sc1_UvD3・sc1_Uv7) が、23 の並びでは、直前の並びが 1 文字のときだけに当てる写し方
        //   (20 回目の査読員の sd1_Uv1) が赤にならなかった。18 回目の査読 SB1-01・19 回目の査読 SC1-01・20 回目の査読 SD1-01)。頭が U の語の例は kyouhaPCUnderachievementsuru の 1 つで、I・O の例は置いていない。
        // ここに置いた例 (並び 23 × 語の形 12 と守りの形 3 (2 つの判定器)、1 文字の並び X・A × 語の形 6 (Ament・Cement の 3 つの位置)、Element の並び 25 (1 文字の X・A と、2〜13 文字の 23) × 形 3、1 文字の並び A〜Z の 26 字 × 語の形 18 (下の 9 入力を除く。pantsu の形は 2 つの判定器)) の答えをどれも変えない写し方は、ほかのテストの例の答えも変えなければ
        // 捕まえない。見つかっているもの (これで全部とは言えない): 14 文字以上の並びだけに当てるもの (14 文字以上の例を置いていない。20 回目の査読員の sd1_Uv14)、
        // kyouha・ima 以外の前置きだけ (20 回目の査読員の sd1_UvP)、
        // 断片 (letters の初め) が ment・pant 以外のときだけ、語が ament・Ament・Cement・Element・emolument・Underachievement 以外のときだけ (同梱の一覧に無い、頭が大文字の E の語だけに当てる
        // 20 回目の査読員の sd1_EnB も、これに入る)、頭が I・O の語だけに当てるもの (18 回目の査読員の sb1_UIO)、同梱の一覧に無い、頭が小文字の u・o の語だけに当てるもの
        // (19 回目の査読員の sc1_luo。その語の例は置いていない)、頭が U の語のうち、kyouha の後の 2 文字の並びの後ろのものでない語だけに当てるもの (20 回目の査読員の
        // sd1_U3・sd1_UnoK2。頭が U の語の例は kyouhaPCUnderachievementsuru の 1 つだけ)、並びの中身 (字) で条件を付けて、例の並びでない 2 文字以上の並びの後ろだけに当てるもの (21 回目の実装者の im21_Uv2x・im21_UA2x・im21_UC2x (並びが 2 文字で
        // PC でないとき)、im21_Uv3x・im21_UC3x・im21_l3x (並びが 3 文字以上で、上の 23 の並びでないとき)。どれも緑で、開発機の NSSpellChecker の答えを変える (例でない並び
        // (2 文字 10・3〜7 文字 9) × 語の形 18 の 342 入力で、それぞれ 30・30・30・27・27・54。例: kyouhaABElementsuru が きょうはABEぇめんつる に (変異なしは
        // きょうはあBElementする))。同じ形の im21_UA3x・im21_l2x は、ほかの例 (imaUSBAmentsu・AIamentsuru) で赤。1 文字の並びは 26 字すべてに例を置いた (21 回目) ので、
        // この形は 2 文字以上の並びのもの)。例を置いていない形 (1 文字の並びの後ろの、頭が小文字の語のうち、
        // この模擬の判定器と開発機の NSSpellChecker で答えが違う 9 入力 (並び H の後ろの ament と、並び D・M の後ろの emolument の、位置 3。下の 26 字の例の注)。
        // 7 文字以上の並びの後ろの Element は 19 回目に、1 文字の並び X・A の後ろの Ament・Cement・Element は 20 回目に、1 文字の並び 26 字の後ろの語の形 18 は 21 回目に置いた)
        // だけに当てる写し方も、同じ理由で捕まえない (並びが 1 文字のときだけ頭が小文字の語に当てる写し方 (im20_la1・im20_lv1・im20_lc1。20 回目の実装者と、21 回目の査読員の
        // se1_l1、21 回目の実装者の im21_l1_A〜Z の 26 本) は緑だが、開発機の NSSpellChecker で、1 文字の並び 26 字 × 語の形 18 の 468 入力のどれの答えも変えない)。P46-60 までは「見つかっている捕まえていない写し方」を並べたが、一覧に無い写し方が回ごとに
        // 見つかった (18 回目の査読 SB1-01)。
        // P46-59 で書いた「ament・pant 以外の断片」は、語 (ament) と断片 (ment・pant) を分けていなかった (この書き方は 17 回目の査読 SA1-02 で直した)。
        // 2 文字の辞書の語の並びだけに当てる写し方は、その条件が真にならない (上のとおり) ので、答えを変えない。P46-58 で書いた「14 文字以上の並びだけに
        // 当てる写し方は捕まえていない」は、捕まえていないのが 14 文字以上だけと読めたが、3〜5 文字の辞書に無い並びの例が無かった (この書き方は 16 回目の査読 RZ1-02 で直した)。
        // 開発機の NSSpellChecker と同梱の表で 5 つの木に流した答えも、上の略語の例と同じ形 (入力の頭の頭が小文字の語は今の頭が小文字の語と、ima の後の頭が大文字の語は
        // 今の頭が大文字の語と、ima の守りは今の守りと同じ木で崩れる)。
        foreach (var abbr in new[] { "PC", "API", "HTTP", "HTTPS", "NASDAQ", "WARNING", "UNDERSTANDING", "HTTPSAPI", "GITHUBAPI", "JSONAPI", "HTTPSAPIJP", "NASDAQJPAPI", "HTTPSAPIJSON", "GITHUBAPIJSON", "JPN", "QWER", "JETRO", "SYSTEM", "PASSWORD", "DIRECTORY", "TECHNOLOGY", "INFORMATION", "RELATIONSHIP" })
        {
            ExpectCommitted(new NsLikeChecker(), ($"kyouha{abbr}Amentsuru", $"きょうは{abbr}Amentする"), ($"kyouha{abbr}amentsuru", $"きょうは{abbr}amentする"),
                ($"ima{abbr}amentsu", $"いま{abbr}amentす"), ($"{abbr}Amentsu", $"{abbr}Amentす"), ($"{abbr}amentsu", $"{abbr}amentす"), ($"ima{abbr}Amentsu", $"いま{abbr}Amentす"),
                ($"kyouha{abbr}Cementsuru", $"きょうは{abbr}Cえmentする"), ($"ima{abbr}Cementsu", $"いま{abbr}Cえmentす"), ($"{abbr}Cementsu", $"{abbr}Cえmentす"),
                ($"kyouha{abbr}emolumentsuru", $"きょうは{abbr}emolumentする"), ($"ima{abbr}emolumentsu", $"いま{abbr}emolumentす"), ($"{abbr}emolumentsu", $"{abbr}emolumentす"));
            foreach (var checker in new Detection.IWordChecker[] { Detection.BuiltInWordChecker.Shared, new NsLikeChecker() })
                ExpectCommitted(checker, ($"kyouha{abbr}pantsuwokau", $"きょうは{abbr}ぱんつをかう"), ($"ima{abbr}pantsu", $"いま{abbr}ぱんつ"), ($"{abbr}pantsu", $"{abbr}ぱんつ"));
        }
        // 1 文字の並び (子音の字の X と、母音の字の A) の後ろの、頭が大文字の語 (Ament・Cement) も、上のループと同じ答えの形 (20 回目に足した。20 回目の査読 SD1-01。
        // NS では取り込み前と main も同じ答えで、P46 だけの木と P46-47 の前の統合は …Aめんつ(る)・…Cえめんつ(る) に崩れる)。並びが 1 文字のときだけ、頭が A の語・
        // 頭が大文字の子音の語に当てる写し方 (im20_UA1・im20_UC1。20 回目の実装者) は、開発機の NSSpellChecker の答えを変える (1 文字の並び X・Q・A・I・O × 位置 3 の
        // 15 入力で、それぞれ 15) が、この例を足す前の UpstreamP46Tests では赤にならなかった。頭が小文字の語 (ament・emolument・pantsu) は、1 文字の並びの後ろでは
        // 語のあとの suru・su・wokau まで英字のまま (きょうはXamentsuru) で、5 つの木のどれも同じ答え (P46 で変わらない) なので置いていない
        // (21 回目の注: この言い方は、20 回目に測った 1 文字の並び X・Q・A・I・O の後ろでは正しかったが、26 字では正しくない。21 回目に 26 字で測ると、並び C・D・F・G・H・M・N・R・S・T
        // の後ろの 36 入力は P46 で答えが変わり (下の 26 字の例の注)、並び L の後ろの ament は語のあとが英字のまま残らない (5 つの木で同じ答え) ので、21 回目に、どの形も
        // 下の 26 字の例に置いた。21 回目の実装者 IM21-01) (並びが 1 文字のときだけ、頭が
        // 小文字の a・a 以外の母音・子音の語に当てる写し方 (im20_la1・im20_lv1・im20_lc1。20 回目の実装者) は、開発機の NSSpellChecker で、1 文字の並びの 90 入力
        // (並び 5 × 形 18) のどれの答えも変えない。21 回目に 26 字の 468 入力で測っても、どれの答えも変えない)。
        foreach (var abbr in new[] { "X", "A" })
            ExpectCommitted(new NsLikeChecker(), ($"kyouha{abbr}Amentsuru", $"きょうは{abbr}Amentする"), ($"ima{abbr}Amentsu", $"いま{abbr}Amentす"), ($"{abbr}Amentsu", $"{abbr}Amentす"),
                ($"kyouha{abbr}Cementsuru", $"きょうは{abbr}Cえmentする"), ($"ima{abbr}Cementsu", $"いま{abbr}Cえmentす"), ($"{abbr}Cementsu", $"{abbr}Cえmentす"));
        // 頭が A 以外の大文字の母音の語 (Element) の例を、23 の並びのすべて × 位置 3 に、答えの形で 3 つの組に分けて置き、1 文字の並び X・A × 位置 3 を 4 つ目の組にする
        // (17 回目に位置 3 つに 1 つずつ足し、18 回目に
        // 英字のまま残る 7 つの並びに、19 回目に 23 の並びのすべてにし、20 回目に 1 文字の並びの 4 つ目の組を足した。上の略語の並びの注のとおり、形には入れていない)。
        // 今のコードの答えは、開発機の NSSpellChecker で流しても、この模擬の判定器と同じ (75 入力。NS では 4 つの組のどれも取り込み前と main も同じ答えで、P46 だけの木と P46-47 の前の統合は …Eぇめんつ(る) に崩れる
        // (並びは崩れない)。同梱の表では、どの木でも …Eぇめんつ(る) に崩れる)。
        // 1 つ目の組: Element が英字のまま残る 7 つの並び。
        foreach (var abbr in new[] { "PC", "JPN", "HTTP", "QWER", "HTTPS", "JETRO", "SYSTEM" })
            ExpectCommitted(new NsLikeChecker(), ($"kyouha{abbr}Elementsuru", $"きょうは{abbr}Elementする"), ($"ima{abbr}Elementsu", $"いま{abbr}Elementsu"), ($"{abbr}Elementsu", $"{abbr}Elementsu"));
        // 2 つ目の組: 2〜6 文字の残りの 2 つ (3 文字の辞書の語の API と、6 文字の NASDAQ)。Element は英字のまま残るが、並びの頭が崩れる (API は 3 つの位置のどれも あPI、
        // NASDAQ は kyouha・ima の後で んASDAQ。入力の頭では NASDAQElementsu のまま)。並びの頭の崩れは、取り込み前と main も同じ答え (P46 で入ったものではない) で、
        // その答えが変わると、この 6 入力の期待値も変わる。開発機の NSSpellChecker は NASDAQ を語と答え、この模擬の判定器は語と答えない (答えは同じ)。
        ExpectCommitted(new NsLikeChecker(), ("kyouhaAPIElementsuru", "きょうはあPIElementする"), ("imaAPIElementsu", "いまあPIElementsu"), ("APIElementsu", "あPIElementsu"),
            ("kyouhaNASDAQElementsuru", "きょうはんASDAQElementする"), ("imaNASDAQElementsu", "いまんASDAQElementsu"), ("NASDAQElementsu", "NASDAQElementsu"));
        // 3 つ目の組: 7 文字以上の 14 の並び。Element の ment は英字のまま残るが、頭が Eぇ に崩れる。Eぇ は取り込み前の木から同じ答えで (上の注の「別の規則」)、
        // その規則を直すと、この 42 入力の期待値も変わる。
        foreach (var abbr in new[] { "WARNING", "UNDERSTANDING", "HTTPSAPI", "GITHUBAPI", "JSONAPI", "HTTPSAPIJP", "NASDAQJPAPI", "HTTPSAPIJSON", "GITHUBAPIJSON",
                     "PASSWORD", "DIRECTORY", "TECHNOLOGY", "INFORMATION", "RELATIONSHIP" })
            ExpectCommitted(new NsLikeChecker(), ($"kyouha{abbr}Elementsuru", $"きょうは{abbr}Eぇmentする"), ($"ima{abbr}Elementsu", $"いま{abbr}Eぇmentす"), ($"{abbr}Elementsu", $"{abbr}Eぇmentす"));
        // 4 つ目の組: 1 文字の並び (子音の字の X と、母音の字の A)。Element は英字のまま残る。X は英字のまま、A は 3 つの位置のどれも あ になる (取り込み前と main も
        // 同じ答え。P46 で入ったものではない。その答えが変わると、この 3 入力の期待値も変わる)。20 回目の査読 SD1-01。
        ExpectCommitted(new NsLikeChecker(), ("kyouhaXElementsuru", "きょうはXElementする"), ("imaXElementsu", "いまXElementsu"), ("XElementsu", "XElementsu"),
            ("kyouhaAElementsuru", "きょうはあElementする"), ("imaAElementsu", "いまあElementsu"), ("AElementsu", "あElementsu"));
        // 1 文字の並び A〜Z の 26 字すべての後ろの、語の形 18 (頭が大文字の語 Ament・Cement・Element と、頭が小文字の語 ament・emolument・pantsu × 位置 3)
        // (21 回目に足した。21 回目の査読 SE1-01。上の X・A の例と Element の 4 つ目の組と同じ 18 入力も、ここにもう一度置いている)。20 回目の 1 文字の並びの例は X・A だけで、
        // 並びが X・A 以外の 1 文字のときだけに当てる写し方 (21 回目の査読員の se1_Uv1o・se1_UA1o・se1_UC1o) は、全テストを緑のまま通り、開発機の NSSpellChecker の答えを変えた
        // (1 文字の並び X・Q・A・I・O × 語の形 18 の 90 入力で、それぞれ 9。例: kyouhaQElementsuru が きょうはQEぇめんつる に)。
        // 期待値は、開発機の NSSpellChecker で流した統合の木の答えで、この模擬の判定器でも同じ答え (26 字 × 形 18 の 468 入力のうち、下の 9 入力を除く 459 入力。
        // pantsu の 78 入力は同梱の表でも同じ答えなので、2 つの判定器で確かめる)。
        // - 頭が大文字の語 (234 入力) は、どの並びの後ろでも、NS で取り込み前と main も同じ答え。P46 だけの木と P46-47 の前の統合は、どちらも同じ 231 入力で崩れる
        //   (207 入力は語の ment が めんつ に、並び C・D・F・G・N・S・T の後ろの Ament の 21 入力は …Ameんつ… に、並び R の後ろの Ament の 3 入力は …RAmenつ… に。
        //   崩れないのは、並び L の後ろの Ament の 3 入力。開発機の NSSpellChecker は lament を語と答える)。答えの形は並びの字で違う (どれも取り込み前と main も同じ答え):
        //   Ament は、並び C・D・F・G・N・S・T の後ろでは …Ameんt… になる (同梱の表でも、この 7 字の後ろでだけ …Ameん… に分かれる。どの規則によるかは確かめていない)。
        //   Cement は、並び I の後ろでは ICement のまま、ほかの 25 字の後ろでは …Cえment… になる。Element は、並び A・E・I・O・U・N の後ろでは並びの字が あ・え・い・お・う・ん
        //   になり (Element は英字のまま)、ほかの 20 字の後ろでは並びの字も英字のまま。
        // - 頭が小文字の語 (234 入力) は、並び C・D・F・G・H・M・N・R・S・T の後ろの 36 入力で、NS の答えが取り込み前・main と違う (P46 で変わった。P46 だけの木と
        //   P46-47 の前の統合も、統合と同じ答え。P46 のどの項目によるかは確かめていない)。例: kyouhaCamentsuru が、取り込み前・main の きょうはCameんtする から
        //   きょうはCameんつる に、kyouhaRamentsuru が きょうはRamentする から きょうはRamenつる に、kyouhaSpantsuwokau が きょうはSpantすをかう から きょうはSpanつをかう に
        //   (Spantsu の 3 入力は同梱の表でも同じ)。どちらの答えも正しいわけではない (語として通らない英字の並び) が、答えが変わると、この入力の期待値も変わる。
        //   ほかの 198 入力は、5 つの木で同じ答え (並びが 1 文字のときだけ頭が小文字の語に当てる写し方は、468 入力のどれの答えも変えない。上の注)。
        // - 置いていない 9 入力: 並び H の後ろの ament (開発機の NSSpellChecker は hame を語と答えて …Hameんつ… に、この模擬の判定器は答えず Hament… のまま) と、
        //   並び D・M の後ろの emolument (開発機の NSSpellChecker は lumen を語と答えて …emolumenつ… に、この模擬の判定器は …Demoぅめんつ… に) の、位置 3。
        //   9 入力とも、NS の答えは取り込み前・main と違う (上の 36 入力に入る)。
        foreach (var c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ")
        {
            var a = c.ToString();
            var ament = "CDFGNST".Contains(c) ? "Ameんt" : "Ament";
            var cement = c == 'I' ? "ICement" : $"{a}Cえment";
            var elem = c switch { 'A' => "あ", 'E' => "え", 'I' => "い", 'O' => "お", 'U' => "う", 'N' => "ん", _ => a };
            ExpectCommitted(new NsLikeChecker(), ($"kyouha{a}Amentsuru", $"きょうは{a}{ament}する"), ($"ima{a}Amentsu", $"いま{a}{ament}す"), ($"{a}Amentsu", $"{a}{ament}す"),
                ($"kyouha{a}Cementsuru", $"きょうは{cement}する"), ($"ima{a}Cementsu", $"いま{cement}す"), ($"{a}Cementsu", $"{cement}す"),
                ($"kyouha{a}Elementsuru", $"きょうは{elem}Elementする"), ($"ima{a}Elementsu", $"いま{elem}Elementsu"), ($"{a}Elementsu", $"{elem}Elementsu"));
            var (ak, ai, ah) = c switch
            {
                'L' => ("きょうはLamentする", "いまLameんtす", "Lameんtす"),
                'R' => ("きょうはRamenつる", "いまRamenつ", "Ramenつ"),
                _ when "CDFGNST".Contains(c) => ($"きょうは{a}ameんつる", $"いま{a}ameんつ", $"{a}ameんつ"),
                _ => ($"きょうは{a}amentsuru", $"いま{a}amentsu", $"{a}amentsu"),
            };
            if (c != 'H')
                ExpectCommitted(new NsLikeChecker(), ($"kyouha{a}amentsuru", ak), ($"ima{a}amentsu", ai), ($"{a}amentsu", ah));
            if (c is not ('D' or 'M'))
                ExpectCommitted(new NsLikeChecker(), ($"kyouha{a}emolumentsuru", $"きょうは{a}emolumentsuru"), ($"ima{a}emolumentsu", $"いま{a}emolumentsu"),
                    ($"{a}emolumentsu", $"{a}emolumentsu"));
            var (pk, pi, ph) = c == 'S' ? ("きょうはSpanつをかう", "いまSpanつ", "Spanつ") : ($"きょうは{a}pantsuwokau", $"いま{a}pantsu", $"{a}pantsu");
            foreach (var checker in new Detection.IWordChecker[] { Detection.BuiltInWordChecker.Shared, new NsLikeChecker() })
                ExpectCommitted(checker, ($"kyouha{a}pantsuwokau", pk), ($"ima{a}pantsu", pi), ($"{a}pantsu", ph));
        }
    }

    [Test]
    public static void P46_24_LiveConversion_StillConvertsReadingsWithDigitsOrLetters()
    {
        // P46-19 の見落とし: 記号 (～ ! ? .) を数えないつもりが、かな以外 (数字・英字) も数えず、12がつ・100えん・iPhone15をかう のライブ変換が止まっていた。
        // 文字として読めるもの (かな・ー・数字・英字) は数え、記号だけを数えない。
        // 数字: 数字を含む読みが変換エンジンに渡る。
        foreach (var (typed, digits) in new[] { ("12gatsu", "12"), ("100en", "100"), ("2024nen", "2024"), ("iPhone15wokau", "15") })
        {
            var converter = new CountingLiveConverter();
            var k = new CompositionTests.Keyboard(live: true, converter: converter);
            k.Type(typed + "\n");
            Assert.True(converter.Received.Any(r => r.Contains(digits)), typed + ": 数字を含む読みを変換エンジンに渡す: " + string.Join(" / ", converter.Received));
        }
        // 英字: かなが 3 文字以下で、後ろに読みきれない子音が残る形 (きょう + qxz) は、英字も数えて 4 文字に届く (かなだけ数えると呼ばれない)。
        foreach (var (typed, japanese) in new[] { ("kyouqxz", "きょう"), ("kyouq", "きょう"), ("soukq", "そう") })
        {
            var converter = new CountingLiveConverter();
            var k = new CompositionTests.Keyboard(live: true, converter: converter);
            k.Type(typed + "\n");
            Assert.True(converter.Received.Contains(japanese), typed + ": 英字も数えて、日本語の区間を変換エンジンに渡す: " + string.Join(" / ", converter.Received));
        }
        // 対照 (数え方を変える前でも呼ばれる): 記号が付いても、かなだけで 4 文字以上ある。
        var control = new CountingLiveConverter();
        new CompositionTests.Keyboard(live: true, converter: control).Type("arigatou!\n");
        Assert.True(control.Calls > 0, "arigatou!: かなが 4 文字以上あれば、記号が付いても呼ぶ (対照)");
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
        foreach (var checker in new Detection.IWordChecker[] { Detection.BuiltInWordChecker.Shared, new NsLikeChecker() })
        {
            foreach (var typed in new[] { "miro.com", "jira.com", "node.com", "deno.com", "canon.jp", "siri.com" })
            {
                var result = Committed(checker, typed);
                Assert.True(result.Contains('.') && !result.Contains('。'), typed + ": 半角の . のまま残り、。 にならない: " + result);
            }
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
        if (OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess)
        {
            Console.WriteLine("  P46_26_UnreadableFile_IsNotOverwritten: スキップ (権限で読めなくできる環境だけ)");
            return;
        }
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
        // 既知の限界 (KNOWN_LIMITS K87): metadata は英語の複合語だが直らない (めただた。守りを外すと めたdata になるが、それも誤り)。
        // 直ったら (metadata が英語になったら)、この期待値を metadata に変える。
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

    [Test]
    public static void P46_32_BlockedPath_WarnsOnceNotOnEverySave()
    {
        // 止めたファイルへの保存のたびに警告を出すと、確定を 1000 回するとログ (上限 1000 件) が埋まって、止めた原因の行が押し出される。
        var directory = TempDir("warn-once");
        try
        {
            var path = Path.Combine(directory, "conv-" + Guid.NewGuid().ToString("N") + ".json");
            var name = Path.GetFileName(path);
            SafeFile.Block(path);
            for (var i = 0; i < 20; i++) SafeFile.WriteAllBytes(path, [1, 2, 3]);
            Assert.Equal(1, Diagnostics.Log.Snapshot().Count(e => e.Message.Contains(name)), "警告は 1 回だけ");
            Assert.True(!File.Exists(path), "止めているので書かない");
            SafeFile.Unblock(path);
            SafeFile.WriteAllBytes(path, [1, 2, 3]);
            Assert.True(File.Exists(path), "止めた状態が解けたら書ける");
            SafeFile.Block(path);
            SafeFile.WriteAllBytes(path, [4]);
            Assert.Equal(2, Diagnostics.Log.Snapshot().Count(e => e.Message.Contains(name)), "止め直したら、また 1 回出る");
        }
        finally { SafeFile.ResetBlockedForTests(); Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void P46_33_BlankLookingFiles_DoNotReplaceBroken()
    {
        // 中身がない (0 バイト・空白・NUL・BOM だけの) ファイルは、前に残した .broken を潰さない。強制終了のあとの NUL で埋まったファイルや、
        // メモ帳で「Unicode」保存した空のファイル (UTF-16 の BOM だけ) も同じ。データになりうるバイトが混じっていれば、今までどおり残す。
        var blanks = new (string Name, byte[] Bytes)[]
        {
            ("NUL", new byte[] { 0, 0, 0 }), ("UTF-16LE の BOM", new byte[] { 0xFF, 0xFE }), ("UTF-16BE の BOM", new byte[] { 0xFE, 0xFF }),
            ("VT", new byte[] { 0x0B }), ("FF", new byte[] { 0x0C }), ("UTF-16LE の空白と改行", new byte[] { 0xFF, 0xFE, 0x20, 0x00, 0x0A, 0x00 }),
            ("UTF-8 の BOM と CR・TAB", new byte[] { 0xEF, 0xBB, 0xBF, 0x09, 0x0D }),
        };
        foreach (var (name, bytes) in blanks)
        {
            var directory = TempDir("blank");
            try
            {
                var path = Path.Combine(directory, "conversions.json");
                File.WriteAllText(path + ".broken", "FIRST-CORRUPT");
                File.WriteAllBytes(path, bytes);
                _ = new ConversionHistory(path);
                Assert.Equal("FIRST-CORRUPT", File.ReadAllText(path + ".broken"), name + ": 前の .broken を潰さない");
            }
            finally { SafeFile.ResetBlockedForTests(); Directory.Delete(directory, recursive: true); }
        }
        var withData = TempDir("blank-data");
        try
        {
            var path = Path.Combine(withData, "conversions.json");
            File.WriteAllText(path + ".broken", "FIRST-CORRUPT");
            File.WriteAllBytes(path, new byte[] { 0x00, (byte)'{' });
            _ = new ConversionHistory(path);
            Assert.True(File.ReadAllBytes(path + ".broken").SequenceEqual(new byte[] { 0x00, (byte)'{' }), "{ が混じっていれば .broken に残す");
        }
        finally { SafeFile.ResetBlockedForTests(); Directory.Delete(withData, recursive: true); }
    }

    [Test]
    public static void P46_34_ProtectUnreadable_ClassifiesByException()
    {
        // P46-26 の分け方を例外ごとに固定する: IOException・UnauthorizedAccessException (読めなかっただけ) は .broken を作らず保存を止める。
        // FileNotFound・DirectoryNotFound (読もうとしたら無かった) は何もしない。それ以外 (壊れている) は .broken に残す。
        var cases = new (string Name, Exception Error, bool Blocked, bool Broken)[]
        {
            ("IOException", new IOException(), true, false), ("UnauthorizedAccessException", new UnauthorizedAccessException(), true, false),
            ("FileNotFoundException", new FileNotFoundException(), false, false), ("DirectoryNotFoundException", new DirectoryNotFoundException(), false, false),
            ("JsonException", new System.Text.Json.JsonException("x"), false, true),
        };
        foreach (var (name, error, blocked, broken) in cases)
        {
            var directory = TempDir("classify");
            try
            {
                var path = Path.Combine(directory, "conversions.json");
                File.WriteAllText(path, "{\"corrupt\":");
                SafeFile.ProtectUnreadable(path, error);
                Assert.Equal(blocked, SafeFile.IsBlocked(path), name + ": 保存を止めるか");
                Assert.Equal(broken, File.Exists(path + ".broken"), name + ": .broken に残すか");
            }
            finally { SafeFile.ResetBlockedForTests(); Directory.Delete(directory, recursive: true); }
        }
    }

    [Test]
    public static void P46_36_ClearAll_BlockedFileNotReleased_ReturnsFalse()
    {
        // 止めたファイルの一覧を渡さない呼び方 (Windows 版・テスト) で、保存を止めたままのファイルを消そうとしても、書かれないので中身が残る。成功と答えてはいけない。
        var directory = TempDir("clear-blocked-kept");
        try
        {
            var conversionsPath = Path.Combine(directory, "conversions.json");
            var suggestionsPath = Path.Combine(directory, "suggest.json");
            var translationsPath = Path.Combine(directory, "translations.json");
            var history = new ConversionHistory(conversionsPath);
            var suggestions = new DictionarySuggestions(suggestionsPath);
            var translations = new TranslationHistory(translationsPath);
            var paths = new[] { conversionsPath, suggestionsPath, translationsPath };
            foreach (var path in paths) SafeFile.Block(path);
            Assert.True(!LearningData.ClearAll(history, null, null, null, null), "変換: 保存を止めたままなら失敗を返す");
            Assert.True(!LearningData.ClearAll(null, suggestions, null, null, null), "辞書の提案: 保存を止めたままなら失敗を返す");
            Assert.True(!LearningData.ClearAll(null, null, null, translations, null), "英訳: 保存を止めたままなら失敗を返す");
            foreach (var path in paths) SafeFile.Unblock(path);
            Assert.True(LearningData.ClearAll(history, suggestions, null, translations, null), "止めた状態を解けば成功を返す");
        }
        finally { SafeFile.ResetBlockedForTests(); Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void P46_36_ClearAll_FolderNotWritable_ReturnsFalseAndKeepsContent()
    {
        // 読める普通のファイルでも、フォルダーに書けなければ、メモリは空になってもファイルの中身は残る。それを成功と答えてはいけない
        // (2 回目の査読 RE-1 の一般形、3 回目の RG-11。以前は Clear() が保存の失敗を握るので true を返し、Mac は「消去しました」と出していた)。
        if (OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess)
        {
            Console.WriteLine("  P46_36_ClearAll_FolderNotWritable: スキップ (権限で書けなくできる環境だけ)");
            return;
        }
        var directory = TempDir("clear-readonly");
        try
        {
            var conversionsPath = Path.Combine(directory, "conversions.json");
            var suggestionsPath = Path.Combine(directory, "suggest.json");
            var translationsPath = Path.Combine(directory, "translations.json");
            var history = new ConversionHistory(conversionsPath);
            var suggestions = new DictionarySuggestions(suggestionsPath);
            var translations = new TranslationHistory(translationsPath);
            history.Remember("くらいど", "クラウド");
            translations.Remember("ひみつ", "secret");
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            try
            {
                Assert.True(!LearningData.ClearAll(history, null, null, null, null), "変換: フォルダーに書けないので失敗を返す");
                Assert.True(!LearningData.ClearAll(null, suggestions, null, null, null), "辞書の提案: フォルダーに書けないので失敗を返す");
                Assert.True(!LearningData.ClearAll(null, null, null, translations, null), "英訳: フォルダーに書けないので失敗を返す");
                Assert.Equal("クラウド", new ConversionHistory(conversionsPath).Get("くらいど"), "書けなかったので、ファイルには学習が残っている (これが成功と答えてはいけない理由)");
            }
            finally { File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
            Assert.True(LearningData.ClearAll(history, suggestions, null, translations, null), "書けるようになれば、もう一度の消去で成功する");
            Assert.True(new ConversionHistory(conversionsPath).Get("くらいど") is null, "消えた");
        }
        finally
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            SafeFile.ResetBlockedForTests();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public static void P46_38_ClearAllAt_ReleasesEveryLearningFile()
    {
        // ClearAllAt (公開の ClearAll() の本体) は、学習データの 5 つのファイルのどれが止まっていても、削除して止めた状態を解く。
        // 3 回目の査読 RG-1: この一覧を渡す行を消しても、どのテストも赤にならなかった。ユーザー辞書は、止まっていても触らない。
        var directory = TempDir("clear-at");
        try
        {
            string P(string name) => Path.Combine(directory, name);
            var learning = new[] { P("model.json"), P("conversions.json"), P("suggest.json"), P("translations.json"), P("languages.json") };
            var userDictionary = P("userdict.txt");
            File.WriteAllText(P("conversions.json"), ClearOriginal);
            File.WriteAllText(userDictionary, "ひみつ\tひみつ");
            foreach (var path in learning) SafeFile.Block(path);
            SafeFile.Block(userDictionary);
            Assert.True(LearningData.ClearAllAt(learning[0], learning[1], learning[2], learning[3], learning[4], [P("meltype.log")]), "消せたら成功を返す");
            foreach (var path in learning) Assert.True(!SafeFile.IsBlocked(path), Path.GetFileName(path) + ": 止めた状態が解ける");
            Assert.True(!File.ReadAllText(P("conversions.json")).Contains("ひみつ"), "打った読みが残らない");
            Assert.True(SafeFile.IsBlocked(userDictionary) && File.ReadAllText(userDictionary) == "ひみつ\tひみつ", "ユーザー辞書は、止まっていても触らない");
        }
        finally { SafeFile.ResetBlockedForTests(); Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void P46_38_ReleaseBlocked_DeletesOnlyTheBlockedFiles()
    {
        // 3 回目の査読 RG-2: 「止めているものだけ削除する」行を消しても、どのテストも赤にならなかった。消すべきでないものを消さないための唯一の砦。
        var directory = TempDir("release-only");
        try
        {
            var id = Guid.NewGuid().ToString("N");
            var blocked = Path.Combine(directory, "blocked-" + id + ".json");
            var normal = Path.Combine(directory, "normal-" + id + ".json");
            var outside = Path.Combine(directory, "outside-" + id + ".txt");
            foreach (var path in new[] { blocked, normal, outside }) File.WriteAllText(path, "KEEP-OR-DELETE");
            SafeFile.Block(blocked);
            SafeFile.Block(outside);
            Assert.True(LearningData.ReleaseBlocked([blocked, normal]), "消せたら成功を返す");
            Assert.True(!File.Exists(blocked) && !SafeFile.IsBlocked(blocked), "止めていたファイルは消えて、止めた状態が解ける");
            Assert.Equal("KEEP-OR-DELETE", File.ReadAllText(normal), "止めていないファイルは消さない");
            Assert.True(File.Exists(outside) && SafeFile.IsBlocked(outside), "渡した一覧に無いものは、止めていても触らない");
            var logged = Diagnostics.Log.Snapshot().Select(e => e.Message).Where(m => m.Contains(id)).ToList();
            Assert.True(logged.Count == 1 && logged[0].Contains("blocked-" + id), "削除した 1 つだけ警告を出す: " + string.Join(" / ", logged));
        }
        finally { SafeFile.ResetBlockedForTests(); Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void P46_39_KeepBrokenFailure_WarningShowsHResult()
    {
        // .broken に残せなかったときの警告は、例外の種類しか出さず、P46-31 で足した HResult (共有違反か権限かの切り分け) が出なかった。
        var directory = TempDir("keep-broken-fail");
        try
        {
            var name = "kb-" + Guid.NewGuid().ToString("N") + ".json";
            var path = Path.Combine(directory, name);
            File.WriteAllText(path, "{\"corrupt\":");
            Directory.CreateDirectory(path + ".broken");   // 残し先がフォルダーなので、置き換えられない
            Assert.True(!SafeFile.KeepBroken(path), "残せなければ false");
            var warning = string.Join("\n", Diagnostics.Log.Snapshot().Select(e => e.Message).Where(m => m.Contains(name)));
            Assert.True(warning.Contains("を .broken に残せませんでした") && warning.Contains("(0x"), "種類と HResult が出る: " + warning);
            Assert.True(!warning.Contains(directory), "パスは出さない: " + warning);
        }
        finally { SafeFile.ResetBlockedForTests(); Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void P46_39_LanguageMemory_BlockedPath_WarnsOnce()
    {
        // languages.json は、大きすぎて退避もできなかったときだけ保存を止める。そのあいだの警告も、P46-32 と同じく止めてから 1 回だけにする
        // (保存のたびに出すと、ログの上限 1000 件が埋まって、止めた原因の行が押し出される)。出した警告の数は、ログの版 (書いた行数) から数える。
        var directory = TempDir("language-warn-once");
        try
        {
            var path = Path.Combine(directory, "languages.json");
            var memory = new LanguageMemory(path);
            SafeFile.Block(path);
            int Warnings(Action action)
            {
                var before = Diagnostics.Log.Version;
                action();
                var written = (int)(Diagnostics.Log.Version - before);
                return Diagnostics.Log.Snapshot().TakeLast(written).Count(e => e.Level == Diagnostics.LogLevel.Warn);
            }
            Assert.Equal(1, Warnings(() => { for (var i = 0; i < 20; i++) memory.Remember("word" + (char)('a' + i), english: true); }),
                "20 回の保存で、警告は 1 回だけ (止めた警告。保存できなかったので取り消した、という警告は、止めているあいだは出さない)");
            SafeFile.Unblock(path);
            SafeFile.Block(path);
            Assert.Equal(1, Warnings(() => memory.Remember("again", english: true)), "止め直したら、また 1 回出る");
            Assert.True(!File.Exists(path), "止めているので書かない");
        }
        finally { SafeFile.ResetBlockedForTests(); Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void P46_42_LeadingDot_CommitKeepsTheShownText()
    {
        // 4 回目の査読 RJ-01: 先頭が . の入力で、確定のとき打った英字が消えたり別の字に変わったりした (打っている間の表示は .zoverです なのに、確定すると .zoveです)。
        // 確定の直前の打ち間違いの直し (FixTypos) は、「表示で英字に見えている文字」の表を見て、英字の部分には手を付けない。
        // その表に、先頭の . で始まる英字の区間 (.zover) が入っていなかったので、読めない並び (zover|desu の rd) の r が消えた。
        ExpectCommitted(
            (".zoverdesu", ".zoverです"), (".zoverdetsu", ".zoverでつ"), (".zoverdesu-zu", ".zoverdesu-zu"), (".zovers", ".zovers"), (".zovermasu", ".zovermasu"), (".zoverd", ".zoverd"),
            (".reverdetsu", ".reverでつ"), (".kyoumolinks", ".kyoumolinks"), (".doumolisttsu", ".doumolisっつ"), (".kinoumolasttsu", ".kinoumolasっつ"),
            // 守り: ドットファイルは今までどおり (打ち間違いの直しにも、英字の部分は触られない)
            (".gitignore", ".gitignore"), (".env", ".env"), (".exe", ".exe"), (".dotfilesdesu", ".dotfilesです"),
            // 守り: 表示が日本語になる入力 (最後の e で日本語に変わる) は、打ち間違いが今までどおり直る (r が消える)
            (".zovermade", "。ぞゔぇまで"));
    }

    [Test]
    public static void P46_42_LeadingDot_CommittedEnglishIsAlwaysTyped()
    {
        // 先頭が . の入力 (名前 + ローマ字の続き) を 1,452 通り確定して、次の 2 つを見る。
        // (1) 出力の英字の連続が、必ず打った文字の一部になる (打った英字が消えたり、別の字に化けたりしない)。直す前は 111 通りで崩れた。
        // (2) 表示で英字に見えている入力 (表示が . で始まる) は、確定してもその表示のまま。(1) だけだと、r が消えただけの崩れ
        //     (.zoverdesu → .zoveです。zove は打った文字の一部) を見逃す (5 回目の査読 RL-02。直す前に表示と確定が食い違った 140 通りのうち 42 通り)。
        //     表示が日本語になる入力 (.zovermade → 。ぞゔぇまで。最後の e で表示が日本語に変わる) は、打ち間違いの直しが今までどおり効くので、(2) の対象にしない。
        var stems = new[]
        {
            "zover", "rever", "mover", "hover", "cover", "lover", "kyoumolink", "doumolist", "kinoumolast", "kyoumolike", "doumolink", "sorehalist",
            "git", "env", "bash", "config", "node", "zsh", "vim", "npm", "ver", "verde", "over", "hoge", "foo", "bar", "main", "test", "kyou", "doumo",
            "zoverd", "kyoumolinkd", "doumolistd", "mailt", "backupt", "overt", "moverd", "readme", "dockerfile", "bashrc", "profile", "ignore", "kyoumoreadm", "doumolast",
        };
        var tails = new[]
        {
            "", "desu", "detsu", "tsu", "su", "s", "ttsu", "wo", "no", "ga", "ni", "de", "dayo", "ne", "-zu", "s-zu", "desu-zu", "mo", "kara", "made", "to", "ha", "ka", "ta", "te",
            "deshita", "masu", "dene", "tsuka", "ssu", "sa", "ssa", "taa",
        };
        var broken = new List<string>();
        var differs = new List<string>();
        var count = 0;
        var shownEnglish = 0;
        foreach (var stem in stems)
        {
            foreach (var tail in tails)
            {
                var typed = "." + stem + tail;
                var (shown, committed) = ShownAndCommitted(Detection.BuiltInWordChecker.Shared, typed);
                count++;
                var lower = typed.ToLowerInvariant();
                if (System.Text.RegularExpressions.Regex.Matches(committed, "[A-Za-z]+").Any(m => !lower.Contains(m.Value.ToLowerInvariant()))) broken.Add(typed + " → " + committed);
                if (shown.StartsWith('.'))
                {
                    shownEnglish++;
                    if (committed != shown) differs.Add(typed + " → 表示 " + shown + " / 確定 " + committed);
                }
            }
        }
        Assert.Equal(1452, count, "確かめた入力の数");
        Assert.True(broken.Count == 0, $"{count} 通りのうち {broken.Count} 通りで、打っていない英字が出た: " + string.Join(" / ", broken.Take(6)));
        Assert.True(shownEnglish >= 1200, $"表示が英字の入力が少なすぎる (この性質を見られていない): {shownEnglish}");
        Assert.True(differs.Count == 0, $"表示が英字の {shownEnglish} 通りのうち {differs.Count} 通りで、確定が表示と違った: " + string.Join(" / ", differs.Take(6)));
    }

    [Test]
    public static void P46_43_ClearAllAt_DeletesLogsAndLeftoversButNotTheUserFiles()
    {
        // 3 回目の査読 RG-1 は学習データ 5 ファイルの一覧を固定したが、同じ関数の残り 2 点が穴だった (4 回目 RI-1: 変異 MI・MH が緑のまま)。
        // ダイアログは「ログも消します」「学習データの退避コピーも消します」と約束するので、その配線は ClearAllAt で守る。
        var directory = TempDir("clear-at-wiring");
        try
        {
            string P(string name) => Path.Combine(directory, name);
            var learning = new[] { P("model.json"), P("conversions.json"), P("suggest.json"), P("translations.json"), P("languages.json") };
            var guid = Guid.NewGuid().ToString("N");
            var removed = new[] { P("meltype.log"), P("meltype.log.old"), P("crash.log"), P("conversions.json.broken"), P("suggest.json.oversize"), P("languages.json." + guid + ".tmp") };
            var kept = new[] { P("userdict.txt"), P("userdict.txt.broken"), P("config.json.broken") };
            foreach (var path in removed.Concat(kept)) File.WriteAllText(path, "ひみつ");
            Assert.True(LearningData.ClearAllAt(learning[0], learning[1], learning[2], learning[3], learning[4], [P("meltype.log"), P("crash.log")]), "消せたら成功を返す");
            foreach (var path in removed) Assert.True(!File.Exists(path), Path.GetFileName(path) + ": ログ・退避コピー・一時ファイルは消える");
            foreach (var path in kept) Assert.True(File.Exists(path), Path.GetFileName(path) + ": ユーザー辞書・設定とその退避コピーは消さない");
        }
        finally { SafeFile.ResetBlockedForTests(); Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void P46_43_ClearAllAt_ReturnsFalseWhenALogCannotBeDeleted()
    {
        // 4 回目の査読 RI-1 (変異 MH): ログや退避コピーを消せなかったときも、全消去は失敗を返す (Mac は「消去できなかったものがあります」と出す)。
        // 戻り値の合成を外しても、学習データそのものは消せるので、どのテストも赤にならなかった。root・Windows は権限で消せなくできないので飛ばす。
        if (OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess) return;
        var directory = TempDir("clear-at-log-kept");
        var logs = Path.Combine(directory, "logs");
        var all = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        try
        {
            Directory.CreateDirectory(logs);
            var log = Path.Combine(logs, "meltype.log");
            File.WriteAllText(log, "ひみつ");
            File.SetUnixFileMode(logs, UnixFileMode.UserRead | UnixFileMode.UserExecute);   // 中のファイルを消せない
            string P(string name) => Path.Combine(directory, name);
            Assert.True(!LearningData.ClearAllAt(P("model.json"), P("conversions.json"), P("suggest.json"), P("translations.json"), P("languages.json"), [log]), "ログを消せなかったので失敗を返す");
            Assert.True(File.Exists(log), "消せなかったログは残る");
        }
        finally
        {
            File.SetUnixFileMode(logs, all);
            SafeFile.ResetBlockedForTests();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public static void P46_43_ClearAllAt_FailureWarningSurvivesInTheNewLog()
    {
        // 全消去が失敗したとき、Mac のダイアログは「meltype.log を確認してください」と案内する。ところが全消去はログも消すので、
        // 失敗の警告を書いたあとでログを消すと、理由が消えることがある (3 回目 RH の申し送り・4 回目 RK-08)。ログへの書き出しは 1 秒おきの追記なので、
        // たいていは消去のあとに新しいログへ書かれるが、書き出しが消去の途中に重なると、警告ごと消える。ログを先に消せば、警告は必ず新しいログに残る。
        // 消去の前に積んである記録 (打った文字が入ることがある) は、消すログと一緒に消え、新しいログには出ない。
        // 書き出しが消去の途中に重なる最悪の場合を、書き出しを休まず繰り返す別のスレッドで作る (Windows は開いているログを消せないので、このスレッドは立てない)。
        var directory = TempDir("clear-at-log-order");
        var log = Path.Combine(directory, "meltype.log");
        using var stop = new CancellationTokenSource();
        Task? flusher = null;
        try
        {
            string P(string name) => Path.Combine(directory, name);
            // 消せない「止めたファイル」: 中身のあるフォルダーを conversions.json の名前で置く (File.Delete が例外を投げる)
            Directory.CreateDirectory(P("conversions.json"));
            File.WriteAllText(Path.Combine(P("conversions.json"), "x"), "x");
            SafeFile.Block(P("conversions.json"));
            Diagnostics.Log.SetFileOutput(log);
            if (!OperatingSystem.IsWindows()) flusher = Task.Run(() => { while (!stop.IsCancellationRequested) { Diagnostics.Log.FlushFile(); Thread.Yield(); } });
            for (var i = 0; i < 20; i++)
            {
                Diagnostics.Log.Info("BEFORE-CLEAR-" + i);   // 消去の前の記録
                Assert.True(!LearningData.ClearAllAt(P("model.json"), P("conversions.json"), P("suggest.json"), P("translations.json"), P("languages.json"), [log]), "止めたファイルを消せないので失敗を返す");
                stop.Token.WaitHandle.WaitOne(2);   // 書き出しのスレッドが、消去の途中に積んだ警告を書く時間
                Diagnostics.Log.FlushFile();
                var text = File.Exists(log) ? File.ReadAllText(log) : "";
                Assert.True(text.Contains("conversions.json を消せませんでした"), $"{i} 回目: 失敗の警告が新しいログに残る: {text}");
                Assert.True(!text.Contains("BEFORE-CLEAR-" + i), $"{i} 回目: 消去の前の記録は新しいログに出ない");
            }
        }
        finally
        {
            stop.Cancel();
            flusher?.Wait();
            Diagnostics.Log.SetFileOutput(null);
            SafeFile.ResetBlockedForTests();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public static void P46_43_LanguageMemory_UnblockedSaveFailure_StillWarns()
    {
        // 4 回目の査読 RI-2 (変異 MN): 保存を止めているときは「今回の学習は取り消しました」の警告を出さない (止めた警告が原因を言っている) が、
        // 止めていない失敗 (フォルダーに書けない) では、出さないと学習されない原因がログに残らない。「いつも出さない」に変えても、どのテストも赤にならなかった。
        if (OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess) return;
        var directory = TempDir("language-unblocked-fail");
        var all = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        try
        {
            var memory = new LanguageMemory(Path.Combine(directory, "languages.json"));
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);   // フォルダーに書けない (保存を止めてはいない)
            var before = Diagnostics.Log.Version;
            var undo = memory.RememberUndoable("sample", english: true);
            var written = (int)(Diagnostics.Log.Version - before);
            var cancelled = Diagnostics.Log.Snapshot().TakeLast(written).Count(e => e.Level == Diagnostics.LogLevel.Warn && e.Message.Contains("今回の学習は取り消しました"));
            Assert.True(undo is null, "保存できなかったので覚えない");
            Assert.Equal(1, cancelled, "止めていない失敗は、取り消した、という警告を出す");
        }
        finally { File.SetUnixFileMode(directory, all); Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public static void P46_46_ClearAll_PassesTheThreeLogsTheDialogPromises()
    {
        // 5 回目の査読 RL-04: 公開の ClearAll() は AppPaths の本物の保存場所を消すのでテストから呼べず、ClearAllAt に渡すログの一覧が、どのテストにも固定されていなかった
        // (一覧を meltype.log だけにしても全テストが緑)。一覧を取り出して、ダイアログが約束する 3 つ (meltype.log・crash.log・imk-trace.log) であることを固定する。
        var logs = LearningData.DefaultLogFiles;
        Assert.Equal(3, logs.Length, "ログの数");
        Assert.Equal(3, logs.Distinct().Count(), "3 つは別のファイル");
        Assert.True(logs.Contains(AppPaths.LogFile), "meltype.log");
        Assert.True(logs.Contains(AppPaths.CrashLogFile), "crash.log");
        Assert.True(logs.Contains(AppPaths.TraceLogFile), "imk-trace.log");
    }
}
