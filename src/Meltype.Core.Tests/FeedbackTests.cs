// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Config;
using Meltype.Detection;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>協力者のテストで報告された問題 (2026-09) と、判定の強さ (Aggressive / Balanced / Conservative / Manual)。</summary>
internal static class FeedbackTests
{
    private static string Showing(string typed, string? before = null, DetectionLevel level = DetectionLevel.Balanced)
    {
        var k = new CompositionTests.Keyboard { Level = level };
        k.Host.PrecedingText = before;
        k.Type(typed);
        return k.Showing ?? "";
    }

    /// <summary>Windows のスペルチェッカーを使って打つ (使えない環境では null)。</summary>
    private static string? TypeWithSpellChecker(string typed, DetectionLevel level = DetectionLevel.Balanced)
    {
        if (TestSupport.WordChecker is not { IsAvailable: true } checker) return null;
        CompositionTests.Detector.SpellChecker = checker;
        try
        {
            var k = new CompositionTests.Keyboard { Level = level };
            k.Type(typed);
            return k.Host.Document;
        }
        finally
        {
            CompositionTests.Detector.SpellChecker = null;
        }
    }

    [Test]
    public static void SmallKana_WithXAndL()
    {
        Assert.Equal("ぁ", Showing("xa"));
        Assert.Equal("ぁ", Showing("la"));
        Assert.Equal("まぁ", Showing("mala"), "mala は英語ではなく まぁ");
    }

    [Test]
    public static void Issue303_ArtistNames_StayEnglish()
    {
        // 本家 #303 (PR #315): 大文字の語 + 日本語 (K|はぃふぁ、She|えらん、Gran|で) に区切られていた名前と、記号を混ぜて書く名前。
        // 名前は dictionaries/propernouns.txt の「音楽」、記号入りの名前は CompositionDetector.IsStylizedName。
        foreach (var (typed, expected) in new[]
        {
            ("Wiz Khalifa", "Wiz Khalifa"), ("Ariana Grande", "Ariana Grande"), ("Ed Sheeran", "Ed Sheeran"),
            ("Marshmello", "Marshmello"), ("beabadoobee", "beabadoobee"), ("The Chainsmokers", "The Chainsmokers"),
            ("Iann Dior", "Iann Dior"), ("dior", "dior"), ("999dobby", "999dobby"),
            ("B.o.B", "B.o.B"), ("U.S.A.", "U.S.A."), ("T.K.O.", "T.K.O."), ("R.I.P.", "R.I.P."),
            ("Chri$tian Gate$", "Chri$tian Gate$"), ("Ke$ha", "Ke$ha"), ("A$AP", "A$AP"), ("Ke$haga", "Ke$haga"),
            // 大文字の語 + 助詞は今までどおり区切る
            ("Anisiyouka", "Aにしようか"), ("Tokyonisumu", "Tokyoにすむ"),
        })
        {
            var k = new CompositionTests.Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, typed);
        }
    }

    [Test]
    public static void Issue303_StylizedNameRule_LeavesJapaneseTypingAlone()
    {
        // 記号入りの名前の規則は「入力全体が 英字 1 文字ずつを . でつないだ略語」か「大文字で始まり、語の中に $ がある名前」のときだけ。
        // 日本語としてよく打つ . や $ (数字・単位・ドメイン・文の終わり・語末の $) と、前後に日本語が続く形は今までの判定のまま。
        // 期待値は変更前の Core の答え (スペルチェッカーは同梱の英単語表)。
        CompositionTests.Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try
        {
            foreach (var (typed, expected) in new[]
            {
                ("10.5", "10.5"), ("No.5", "No.5"), ("A.I.", "A.I."), ("3.14", "3.14"), ("$100", "＄100"), ("1$", "1＄"),
                ("ke$ha", "け＄は"), ("foo$bar", "ふぉお＄bar"), ("kyou$", "きょう＄"), ("Kyou$", "きょう＄"), ("Ke$", "け＄"), ("Hello.wakarimashita", "Hello。わかりました"), ("example.com", "example.com"),
                ("foo.bar", "ふぉお。bar"), ("A.I.nohanashi", "A.I.のはなし"), ("U.S.A.no", "う.S.A.の"),
                ("a.b.c", "あ。b。c"), ("x.y.z", "x。y。z"), ("Mr.Tanaka", "Mr。Tanaka"), ("Meltype.dll", "Meltype.dll"),
                // 辞書に入れなかった綴り (ariana = アリアナ、iann = イアン) と、辞書の語の頭になる日本語の語は、今までどおり
                ("ariana", "ありあな"), ("iann", "いあん"), ("kyouhaarianawo", "きょうはありあなを"), ("aria", "ありあ"), ("ariawase", "ありあわせ"),
                ("chai", "ちゃい"), ("chain", "ちゃいん"), ("chairo", "ちゃいろ"), ("kyouhaiannwo", "きょうはいあんを"),
            })
            {
                var k = new CompositionTests.Keyboard();
                k.Type(typed + "\n");
                Assert.Equal(expected, k.Host.Document, typed);
            }
            // 手動 (Manual) は Shift で打った大文字始まりの語だけを英語にする設定なので、この規則は使わない (今までどおり)
            foreach (var (typed, expected) in new[] { ("Ke$ha", "け＄は"), ("B.o.B", "B.お。B") })
            {
                var k = new CompositionTests.Keyboard { Level = DetectionLevel.Manual };
                k.Type(typed + "\n");
                Assert.Equal(expected, k.Host.Document, "手動: " + typed);
            }
        }
        finally
        {
            CompositionTests.Detector.SpellChecker = null;
        }
    }

    [Test]
    public static void Issue369_Huihui_StaysEnglish()
    {
        // Issue #369: huihui (AI のモデルを出している huihui-ai) が ふいふい (→ 回回) になっていた。ふいふい は日本語の語ではない
        foreach (var (typed, expected) in new[] { ("huihui", "huihui"), ("huihuino", "huihuiの"), ("fuifui", "ふいふい") })
        {
            var k = new CompositionTests.Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, typed);
        }
    }

    /// <summary>Q2 で足した AI のモデル名・ツール名 (dictionaries/propernouns.txt の「AI モデル・ツール」)。小文字で打つ形。</summary>
    private static readonly string[] AiNames =
    [
        "gemma", "paligemma", "medgemma", "llama", "codellama", "tinyllama", "llava", "llamafile",
        "mistral", "mixtral", "pixtral", "codestral", "devstral", "qwen", "phi", "grok",
        "roberta", "distilbert", "starcoder", "vicuna", "alpaca", "orca", "zephyr", "codex", "hunyuan", "ernie", "florence", "jamba",
        "lora", "qlora", "gguf", "onnx", "veo",
        "cline", "crewai", "comfyui", "openrouter", "openwebui", "cerebras", "civitai", "unsloth",
    ];

    private static string Enter(string typed)
    {
        var k = new CompositionTests.Keyboard();
        k.Type(typed + "\n");
        return k.Host.Document;
    }

    [Test]
    public static void Q2_AiModelNames_StayEnglish()
    {
        // AI のモデル名・ツール名は、ローマ字として読めたり、英単語表に無かったりして崩れていた
        // (gemma4 → げっま4、llama → っぁま、codellama → こでっlあま、qwen → qうぇん、mixtral → mixtらl)。
        // 辞書だけで決まるように、スペルチェッカー無しと、同梱の英単語表 (BuiltInWordChecker。--repro と同じ) の両方で確かめる。
        foreach (var checker in new Detection.IWordChecker?[] { null, Detection.BuiltInWordChecker.Shared })
        {
            CompositionTests.Detector.SpellChecker = checker;
            try
            {
                var label = checker is null ? "スペルチェッカー無し" : "英単語表";
                foreach (var name in AiNames)
                {
                    Assert.Equal(name, Enter(name), $"{label}: {name}");
                    var capital = char.ToUpperInvariant(name[0]) + name[1..];
                    Assert.Equal(capital, Enter(capital), $"{label}: {capital}");
                    Assert.Equal(name + "4", Enter(name + "4"), $"{label}: {name}4");
                    Assert.Equal(name + "を", Enter(name + "wo"), $"{label}: {name}wo");
                    Assert.Equal(name + "で", Enter(name + "de"), $"{label}: {name}de");
                }
                // 実機で報告された形 (gemma4 → げっま4) と、バージョン・助詞・記号がつく形
                foreach (var (typed, expected) in new[]
                {
                    ("gemma4", "gemma4"), ("gemma3", "gemma3"), ("gemma-4", "gemma-4"), ("gemma4wo", "gemma4を"), ("gemma4ha", "gemma4は"),
                    ("llama3", "llama3"), ("llama3.1", "llama3.1"), ("llama.cpp", "llama.cpp"), ("codellama", "codellama"),
                    ("qwen2.5", "qwen2.5"), ("qwen3", "qwen3"), ("mistralno", "mistralの"), ("mixtral8x7b", "mixtral8x7b"), ("phi4", "phi4"),
                    ("lorano", "loraの"), ("pip install qwen", "pip install qwen"), ("llama gemma qwen", "llama gemma qwen"),
                })
                {
                    Assert.Equal(expected, Enter(typed), $"{label}: {typed}");
                }
            }
            finally
            {
                CompositionTests.Detector.SpellChecker = null;
            }
        }
    }

    [Test]
    public static void Q2_AiModelNames_ShowEnglishWhileTyping()
    {
        // 打っている途中の表示も、語が打ち終わった時点で英字 (入力中に げっま のまま見えない)
        foreach (var name in new[] { "gemma", "gemma4", "llama", "llama3", "codellama", "qwen", "mixtral", "comfyui" })
        {
            Assert.Equal(name, Showing(name), name);
        }
    }

    [Test]
    public static void Q2_AiModelNames_CollidingWithJapanese_AreNotInTheDictionary()
    {
        // 日本語の語と同じ綴り (kimi = きみ、sora = そら、haiku = はいく、yi = い) や、打ち間違いの直し (olmo = omo の o の隣の l、molmo = momo) で
        // 日本語になる綴りは辞書に入れない (入れると、日本語を打ったのに英字で確定する)。大文字で始めれば辞書が無くても英字。
        CompositionTests.Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try
        {
            foreach (var (typed, expected) in new[]
            {
                ("kimi", "きみ"), ("sora", "そら"), ("haiku", "はいく"), ("yi", "い"), ("suno", "すの"), ("pika", "ぴか"), ("kiro", "きろ"),
                ("olmo", "おも"), ("molmo", "もも"),
                ("Kimi", "Kimi"), ("Sora", "Sora"), ("Suno", "Suno"), ("Olmo", "Olmo"), ("OLMo", "OLMo"), ("Molmo", "Molmo"),
            })
            {
                Assert.Equal(expected, Enter(typed), typed);
            }
        }
        finally
        {
            CompositionTests.Detector.SpellChecker = null;
        }
    }

    [Test]
    public static void Q2_JapaneseWordsStartingLikeAiNames_AreUnchanged()
    {
        // 辞書の語の頭 4 文字以上は、打っている途中だけ英字に見える (CompositionDetector の growing && HasPrefix)。
        // 日本語の語の末尾が、辞書の語の頭と同じ綴りになる語 (〜みに = mini、〜ねも = nemo、あうと = auto、あいで = aide) は
        // 表示が崩れるので、その頭を持つ語 (MiniMax・Ministral・Nemotron・AutoGPT・AutoGen・Aider・Manus) は辞書に入れなかった。
        // 入れたときに表示も確定も変わらないことを、今の答えで固定する。
        CompositionTests.Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try
        {
            foreach (var (typed, expected) in new[]
            {
                ("chinamini", "ちなみに"), ("tanoshimini", "たのしみに"), ("oyasumini", "おやすみに"), ("okanemo", "おかねも"), ("kanemo", "かねも"),
                ("chekkuauto", "ちぇっくあうと"), ("shattoauto", "しゃっとあうと"), ("aidea", "あいであ"), ("mini", "みに"), ("auto", "あうと"),
                ("umanushi", "うまぬし"), ("gemu", "げむ"), ("geta", "げた"), ("jamu", "じゃむ"), ("kodomo", "こども"), ("imagawa", "いまがわ"),
                // 辞書に入れなかった AI の名前は、今までの答えのまま (既知の限界)
                ("minimax", "みにmax"), ("nemotron", "ねもちろん"), ("aider", "あいで"), ("autogen", "あうとげん"), ("manus", "まぬs"),
                // codegemma (code + gemma) は、辞書の語 gemma の前に こで がつくので、gemma だけ英字になる (今は直せない)
                ("codegemma", "こでgemma"),
            })
            {
                Assert.Equal(expected, Enter(typed), typed);
            }
            foreach (var (typed, expected) in new[] { ("chinamini", "ちなみに"), ("okanemo", "おかねも"), ("chekkuauto", "ちぇっくあうと"), ("mini", "みに"), ("aide", "あいで") })
            {
                Assert.Equal(expected, Showing(typed), "表示: " + typed);
            }
        }
        finally
        {
            CompositionTests.Detector.SpellChecker = null;
        }
    }

    [Test]
    public static void Issue375_DoubledLetterBeforeParticle_IsAcceptedTradeOff()
    {
        // #375 の修正 (語の最後の文字と助詞の頭が同じでも区切る) の代償として、同梱の単語表が知らない英語の語の
        // 「二重字 + 助詞の頭」(slotto など) は、英単語ではなく 語 + 助詞 と読む。ここで現在の挙動を固定する
        // (本家の変更と同じ。実機の NSSpellChecker は slot を知っているので、実機とは答えが違うことがある)
        var k = new CompositionTests.Keyboard();
        k.Type("slotto\n");
        Assert.Equal("slotと", k.Host.Document, "slotto");
        // 日本語の語は、二重字の綴りでも変わらない
        foreach (var (typed, expected) in new[] { ("kitto", "きっと"), ("zuttoiku", "ずっといく"), ("matteite", "まっていて") })
        {
            var j = new CompositionTests.Keyboard();
            j.Type(typed + "\n");
            Assert.Equal(expected, j.Host.Document, typed);
        }
    }

    [Test]
    public static void ShiftedSingleConsonant_StaysUppercase()
    {
        foreach (var c in "WRTYPSDGHKLZXVBNM")
        {
            Assert.Equal(c.ToString(), Showing(c.ToString()), $"Shift+{c}");
        }
        Assert.Equal("かW", Showing("kaW"));
    }

    /// <summary>短い英単語だけを知っているスペルチェッカー (Windows のスペルチェッカーの代わり)。</summary>
    private sealed class FewWordsChecker(params string[] words) : Detection.IWordChecker
    {
        public bool IsAvailable => true;
        public bool IsWord(string lower) => words.Contains(lower);
    }

    [Test]
    public static void Laughter_IsNotTakenAsWordFoundMidRomaji()
    {
        // Windows のスペルチェッカーは taw (ビー玉) を知っている。kiyagat|taw の taw で、笑いの w を英字にしていた (きやがっtaw)
        var saved = CompositionTests.Detector.SpellChecker;
        CompositionTests.Detector.SpellChecker = new FewWordsChecker("taw", "new", "show");
        try
        {
            foreach (var (typed, expected) in new[] { ("kiyagattaw", "きやがったw"), ("kitaw!", "きたw！"), ("new", "new"), ("show", "show") })
            {
                var k = new CompositionTests.Keyboard();
                k.Type(typed + "\n");
                Assert.Equal(expected, k.Host.Document, typed);
            }
        }
        finally
        {
            CompositionTests.Detector.SpellChecker = saved;
        }
    }

    [Test]
    public static void ShiftedSymbols_StartComposition()
    {
        Assert.Equal("！", Showing("!"));
        Assert.Equal("？", Showing("?"));
        Assert.Equal(":", Showing(":"));
        Assert.Equal("～", Showing("~"));
        Assert.Equal("!", Showing("!", before: "Hello"), "英文の後は半角");
    }

    [Test]
    public static void TripleSlash_IsEllipsis()
    {
        // 報告 (Discord のリスト #4): /// を … にできるようにする。URL (file:///) はそのまま。
        Assert.Equal("…", Showing("///"));
        Assert.Equal("それで…", Showing("sorede///"));
        Assert.Equal("file:///", Showing("file:///"));
    }

    [Test]
    public static void ShortParticles_AfterEnglishWord_AreJapanese()
    {
        foreach (var (typed, expected) in new[] { ("no", "の"), ("to", "と"), ("ga", "が") })
        {
            Assert.Equal(expected, Showing(typed, before: "GitHub"), $"GitHub + {typed}");
            Assert.Equal(expected, Showing(typed, before: "今日は GitHub "), $"今日は GitHub + {typed}");
        }
        Assert.Equal("no", Showing("no", before: "GitHub", level: DetectionLevel.Aggressive), "積極的なら英語");
    }

    [Test]
    public static void ShortWords_InEnglishSentence_AreEnglish()
    {
        Assert.Equal("to", Showing("to", before: "I want "));
        Assert.Equal("a", Showing("a", before: "this is "));
        Assert.Equal("is", Showing("is", before: "this "), "子音で終わる語は片側が英語なら英語");
    }

    [Test]
    public static void Levels_ChangeHowEagerlyEnglishIsShown()
    {
        Assert.Equal("sushi", Showing("sushi", before: "I like "), "英文の後");
        Assert.Equal("すし", Showing("sushi", before: "I ", level: DetectionLevel.Conservative), "慎重: 1 語だけの文脈では英語にしない");
        Assert.Equal("google", Showing("google", level: DetectionLevel.Conservative), "慎重でもローマ字として読めない英単語は英語");
        Assert.Equal("あまぞn", Showing("amazon", level: DetectionLevel.Conservative), "慎重: ローマ字として読める固有名詞は日本語のまま");
    }

    [Test]
    public static void Manual_OnlySuggests_TabAccepts()
    {
        var k = new CompositionTests.Keyboard { Level = DetectionLevel.Manual };
        k.Type("google");
        Assert.Equal("ごおgぇ", k.Showing, "手動: 自動では英字にしない");
        Assert.True(k.Host.View!.Hint.Contains("Tab → google"), $"提案を表示: {k.Host.View.Hint}");
        k.Press(VirtualKeys.Tab);
        Assert.Equal("google", k.Showing, "Tab で提案どおり英字");
        k.Type("\n");
        Assert.Equal("google", k.Host.Document);

        Assert.Equal("Google", Showing("Google", level: DetectionLevel.Manual), "Shift で打った大文字始まりは手動でも英語");
    }

    [Test]
    public static void EnglishSentences_WithSpellChecker()
    {
        foreach (var sentence in new[] { "I want to go to the park", "make sure you have time", "the meeting is at nine", "my name is taro", "this is a pen" })
        {
            if (TypeWithSpellChecker(sentence + "\n") is not { } result) return;
            Assert.Equal(sentence, result, "英文");
        }
    }

    [Test]
    public static void JapaneseSentences_WithSpellChecker()
    {
        foreach (var (typed, expected) in new[]
        {
            ("kore ha pen desu", "これはぺんです"),
            ("GitHub no repo", "GitHub のれぽ"),
            ("Google to Apple", "Google とApple"),
            ("watashi ha sushi ga suki", "わたしはすしがすき"),
            ("mala", "まぁ"),
        })
        {
            if (TypeWithSpellChecker(typed + "\n") is not { } result) return;
            Assert.Equal(expected, result, "日本語の文");
        }
    }
}

internal static class KanaInputTests
{
    private static CompositionTests.Keyboard Kana(DetectionLevel level = DetectionLevel.Balanced) => new() { Kana = true, Level = level };

    [Test]
    public static void KanaKeys_ProduceKana()
    {
        var k = Kana();
        k.TypeKanaKeys("tu");
        Assert.Equal("かな", k.Showing, "T = か, U = な");
        k.TypeKanaKeys("t@");
        Assert.Equal("かなが", k.Showing, "濁点は直前のかなに付く");
        k.Press(VirtualKeys.Back);
        Assert.Equal("かな", k.Showing, "BackSpace で が を 1 文字消す");
    }

    [Test]
    public static void KanaKeys_ShiftGivesSmallKanaAndPunctuation()
    {
        var k = Kana();
        k.TypeKeys((0x45, true), (0x5A, true), (0xBC, true), (0xBE, true), (0x33, false), (0x33, true));
        Assert.Equal("ぃっ、。あぁ", k.Showing);
        k.TypeKeys((0xBC, false));
        Assert.Equal("ぃっ、。あぁね", k.Showing, "Shift なしの , は ね");
    }

    [Test]
    public static void KanaInput_EnglishWordsAreShownAsEnglish()
    {
        var k = Kana();
        k.TypeKanaKeys("google");
        Assert.Equal("google", k.Showing, "打ったキーが英単語で、かなとしては日本語にならない");
        k.Type("\n");
        Assert.Equal("google", k.Host.Document);

        var japanese = Kana();
        japanese.TypeKanaKeys("byiaf");
        Assert.Equal("こんにちは", japanese.Showing);
    }

    [Test]
    public static void KanaInput_SpaceAroundEnglish()
    {
        // かな入力でも、確定した英単語の前後に半角スペースが入る
        var k = Kana();
        k.SpaceAroundEnglish = true;
        k.TypeKanaKeys("google");
        k.Type("\n");
        k.TypeKanaKeys("byiaf");
        k.Type("\n");
        Assert.Equal("google こんにちは", k.Host.Document);
    }

    [Test]
    public static void KanaInput_FollowsLevels()
    {
        var manual = Kana(DetectionLevel.Manual);
        manual.TypeKanaKeys("google");
        Assert.Equal("きららきりい", manual.Showing, "手動: 自動では英字にしない");
        Assert.True(manual.Host.View!.Hint.Contains("Tab → google"), manual.Host.View.Hint);

        var shift = Kana(DetectionLevel.Manual);
        shift.TypeKanaKeys("Google");
        Assert.Equal("Google", shift.Showing, "Shift で打った大文字始まりは英語");
    }
}

internal static class MisspellingTests
{
    [Test]
    public static void Misspelling_IsSuggestedAndFixedWithTab()
    {
        var k = new CompositionTests.Keyboard();
        k.Type("buresureddo");
        Assert.Equal("ぶれすれっど", k.Showing);
        Assert.Equal("もしかして: ブレスレット　<Tab>で修正", k.Host.View!.Suggestion, "変換ボックスの「もしかして」の行");
        Assert.True(!k.Host.View.Hint.Contains("もしかして"), "案内の行には出さない");
        k.Press(VirtualKeys.Tab);
        Assert.Equal("ぶれすれっと", k.Showing, "Tab で正しい読みに直す");
        Assert.True(!k.Host.View!.Hint.Contains("もしかして"), "直した後は出ない");
        k.Type("\n");
        Assert.Equal("ぶれすれっと", k.Host.Document);
    }

    [Test]
    public static void Misspelling_ListedPairsAndSpellingVariants()
    {
        foreach (var (typed, right) in new[]
        {
            ("shumire-shon", "シミュレーション"),
            ("komyunike-shon", null),
            ("kominyuke-shon", "コミュニケーション"),
            ("bure-suretto", "ブレスレット"),
            ("figiasuke-to", "フィギュア"),
            ("kyouhaiitenki", null),
            ("buresurettowokau", null),
        })
        {
            var k = new CompositionTests.Keyboard();
            k.Type(typed);
            var suggestion = k.Host.View!.Suggestion ?? "";
            if (right is null) Assert.True(suggestion.Length == 0, $"{typed}: 誤りではない ({suggestion})");
            else Assert.True(suggestion.Contains($"もしかして: {right}"), $"{typed}: {suggestion}");
        }
    }

    [Test]
    public static void Misspelling_TabWhileConverting_Reconverts()
    {
        var k = new CompositionTests.Keyboard();
        k.Type("buresureddo ");
        Assert.True(k.Host.View!.Converting, "Space で変換中");
        Assert.True(k.Host.View.Suggestion?.Contains("もしかして: ブレスレット") == true, k.Host.View.Suggestion ?? "");
        k.Press(VirtualKeys.Tab);
        Assert.True(k.Host.View!.Converting, "直して変換し直す");
        Assert.Equal("ぶれすれっと", k.Showing);
    }
}

internal static class CodeProfileTests
{
    [Test]
    public static void LineTracker_FollowsTypingAndResets()
    {
        var line = new LineTracker();
        Assert.True(line.Text is null, "最初は分からない");
        line.NewLine();
        line.Append("x = 1 ");
        Assert.Equal(LineKind.Code, LineContext.Classify(line.Text!));
        line.Append("// ");
        Assert.Equal(LineKind.Comment, LineContext.Classify(line.Text!), "// の後はコメント");
        line.Backspace();
        line.Backspace();
        line.Backspace();
        Assert.Equal("x = 1 ", line.Text);
        line.NewLine();
        Assert.Equal("x = 1 \n", line.Text, "改行で新しい行 (前の行も持っておく)");
        Assert.Equal(LineKind.Code, LineContext.ClassifyText(line.Text!));
        line.Invalidate();
        Assert.True(line.Text is null, "キャレットが動いたら分からない");
        line.SetFromText("first line\r\n    print(\"こん");
        Assert.Equal(LineKind.String, LineContext.ClassifyText(line.Text!), "UI Automation で読んだ文字列の、今の行で調べる");
    }

    [Test]
    public static void LineContext_MultiLineStringsAndComments()
    {
        // Python の """ の中の行 (本文) で日本語が打てなかった
        Assert.Equal(LineKind.String, LineContext.ClassifyText("def f():\n    \"\"\"\n    "));
        Assert.Equal(LineKind.String, LineContext.ClassifyText("x = '''\nほん"));
        Assert.Equal(LineKind.Comment, LineContext.ClassifyText("/*\n  説明"));
        Assert.Equal(LineKind.Comment, LineContext.ClassifyText("<!--\n"));
        // 閉じた後はコード
        Assert.Equal(LineKind.Code, LineContext.ClassifyText("\"\"\"\ndoc\n\"\"\"\nx = "));
        Assert.Equal(LineKind.Code, LineContext.ClassifyText("/* a */\nint x = "));
        // コメント・1 行の文字列の中の """ /* は数えない
        Assert.Equal(LineKind.Code, LineContext.ClassifyText("# \"\"\" in comment\nx = "));
        Assert.Equal(LineKind.Code, LineContext.ClassifyText("s = \"/*\"\nx = "));
    }

    [Test]
    public static void Settings_CodeAppsAreCode()
    {
        var settings = new Settings();
        Assert.Equal(AppProfile.Code, settings.ProfileFor("Code.exe"));
        Assert.Equal(AppProfile.Code, settings.ProfileFor("windowsterminal.exe"), "大文字小文字は区別しない");
        Assert.Equal(AppProfile.General, settings.ProfileFor("chrome.exe"));
        Assert.Equal(AppProfile.Code, settings.Clone().ProfileFor("Code.exe"), "複製しても種類を保つ");

        // v3 の設定ファイル (種類が無い) を読み込むと、コードエディター・ターミナルが「コード」になる。ユーザーが OFF にしたものは OFF のまま。
        var old = new Settings { SettingsVersion = 3, AppRules = [new AppRule { Process = "Code.exe", Enabled = false }, new AppRule { Process = "chrome.exe" }] };
        old.Migrate();
        Assert.Equal(AppProfile.Code, old.ProfileFor("Code.exe"));
        Assert.True(!old.IsAppEnabled("Code.exe"), "OFF のまま");
        Assert.Equal(AppProfile.Code, old.ProfileFor("pwsh.exe"), "足りないコードアプリを追加");
        Assert.Equal(AppProfile.General, old.ProfileFor("chrome.exe"));
    }

    [Test]
    public static void Settings_CustomAppKinds()
    {
        // 報告: アプリ別設定の種類を、一般とコードだけでなく自分で作れるように。
        var settings = new Settings
        {
            DetectionLevel = DetectionLevel.Balanced,
            AppKinds = [new AppKind { Name = "チャット", Base = AppProfile.General, DetectionLevel = DetectionLevel.Aggressive, LiveConversion = false, StartInEnglish = true }],
            AppRules = [new AppRule { Process = "Discord.exe", Kind = "チャット" }, new AppRule { Process = "Code.exe", Profile = AppProfile.Code }],
        };
        Assert.Equal(DetectionLevel.Aggressive, settings.ForApp("discord.exe").DetectionLevel, "独自の種類の判定の強さ");
        Assert.True(!settings.ForApp("Discord.exe").LiveConversion, "独自の種類のライブ変換");
        Assert.Equal(DetectionLevel.Balanced, settings.ForApp("chrome.exe").DetectionLevel, "ほかのアプリは全体の設定");
        Assert.True(settings.KindFor("Discord.exe")?.StartInEnglish == true, "最初は英数");
        Assert.Equal(AppProfile.Code, settings.ProfileFor("Code.exe"));

        var path = Path.Combine(Path.GetTempPath(), $"meltype-kinds-{Guid.NewGuid():N}.json");
        try
        {
            settings.Save(path);
            var loaded = Settings.Load(path);
            Assert.Equal("チャット", loaded.KindFor("Discord.exe")?.Name, "保存して読み込んでも種類を保つ");
            Assert.Equal(DetectionLevel.Aggressive, loaded.Clone().ForApp("Discord.exe").DetectionLevel, "複製しても保つ");
        }
        finally
        {
            File.Delete(path);
        }
    }
}

internal static class LanguageLearningTests
{
    [Test]
    public static void CommonJapanese_NeedsTwoTimesToBecomeEnglish()
    {
        // 一度 kyouha を英字で確定しただけで、ずっと kyouha朝から… になっていた。
        var typos = RomajiTypoCorrector.Load(CompositionTests.Detector.Romaji);
        Assert.True(typos.IsCommonJapanese("kyouha") && typos.IsCommonJapanese("sushi"), "よく使う日本語の読み");
        Assert.True(!typos.IsCommonJapanese("api") && !typos.IsCommonJapanese("github") && !typos.IsCommonJapanese("tao"), "日本語の語にならない");
        var memory = new LanguageMemory(null) { IsCommonJapanese = typos.IsCommonJapanese };
        memory.Remember("kyouha", english: true);
        Assert.True(memory.Get("kyouha") is null, "1 回では英語にしない");
        memory.Remember("kyouha", english: true);
        Assert.Equal(true, memory.Get("kyouha") ?? false, "2 回で英語");
        memory.Remember("api", english: true);
        Assert.Equal(true, memory.Get("api") ?? false, "ふつうの語は 1 回で英語");
    }

    [Test]
    public static void F10_TeachesEnglish_ThenUsedInContext()
    {
        var memory = new LanguageMemory(null);
        CompositionTests.Detector.Memory = memory;
        try
        {
            var k = new CompositionTests.Keyboard(languages: memory);
            k.Type("api");
            Assert.Equal("あぴ", k.Showing, "最初は日本語");
            k.Press(VirtualKeys.F10);
            k.Type("\n");
            Assert.Equal("api", k.Host.Document);
            Assert.Equal(true, memory.Get("api"), "F10 で英字にして確定したので覚える");

            var next = new CompositionTests.Keyboard(languages: memory);
            next.Type("apinoerror\n");
            Assert.Equal("apiのerror", next.Host.Document, "次からは文の中でも英字");
        }
        finally
        {
            CompositionTests.Detector.Memory = null;
        }
    }

    [Test]
    public static void ShortLearnedWord_DoesNotSplitJapanese()
    {
        // 一度 go を英字で確定したら、日本語 (nihongo) が にほんgo になっていた。
        var memory = new LanguageMemory(null);
        memory.Remember("go", english: true, explicitChoice: true);
        CompositionTests.Detector.Memory = memory;
        try
        {
            var k = new CompositionTests.Keyboard(languages: memory);
            k.Type("nihongo\n");
            Assert.Equal("にほんご", k.Host.Document, "日本語のすぐ後ろでは、覚えた短い英単語を使わない");
            k = new CompositionTests.Keyboard(languages: memory);
            k.Type("go\n");
            Assert.Equal("go", k.Host.Document, "単独なら覚えたとおり英字");
        }
        finally
        {
            CompositionTests.Detector.Memory = null;
        }
    }



    [Test]
    public static void EnglishWordEndingInNg_BeforeParticle()
    {
        // kyouhameetinggaarimasu が きょうはめえちんっがあります になっていた (meeting の g が が とつながって っが)。
        // また、表示では きょうはmeetingです なのに、確定したら打ち間違いとして g を直されて めえちんがです になっていた。
        // meeting を英単語と知るのに、スペルチェッカー (Windows) か同梱の英単語の一覧 (Mac・Linux) を使う。
        CompositionTests.Detector.SpellChecker = TestSupport.WordChecker is { IsAvailable: true } checker ? checker : Detection.BuiltInWordChecker.Shared;
        try
        {
            foreach (var (typed, expected) in new[]
            {
                ("meetingga", "meetingが"), ("shoppinggasuki", "shoppingがすき"), ("sanngatsu", "さんがつ"),
                ("kyouhameetinggaarimasu", "きょうはmeetingがあります"), ("kyouhameetingdesu", "きょうはmeetingです"), ("onegaishimsu", "おねがいします"),
            })
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
    public static void SymbolCandidates_ShowHalfOrFullWidth()
    {
        // 変換の候補で、記号が半角か全角か分からなかった (@ と ＠)。両方あるときは右に「半角」「全角」と出す。
        var k = new CompositionTests.Keyboard();
        k.Type("@ ");
        var view = k.Host.View!;
        Assert.True(view.Converting, "変換中");
        var notes = view.Notes ?? [];
        Assert.Equal("半角", notes.ElementAtOrDefault(view.Candidates.ToList().IndexOf("@")), string.Join(" ", view.Candidates));
        Assert.Equal("全角", notes.ElementAtOrDefault(view.Candidates.ToList().IndexOf("＠")), string.Join(" ", view.Candidates));
    }

    [Test]
    public static void UnitsAfterNumbers_StayLetters()
    {
        // 単位 (mm、min) が打ちにくく、日本語になることがあった (10mmで → 10っまで、5min → 5みん)。
        foreach (var (typed, expected) in new[]
        {
            ("10mmde", "10mmで"), ("kyouha10mmdesu", "きょうは10mmです"), ("5min", "5min"), ("3mol", "3mol"), ("2mmol", "2mmol"),
            ("100mlnomizu", "100mlのみず"), ("3nin", "3にん"), ("1man", "1まん"), ("10mina", "10みな"), ("5ko", "5こ"),
        })
        {
            var k = new CompositionTests.Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, typed);
        }
    }


    [Test]
    public static void UnitsAfterNumbers_DoubledConsonant()
    {
        // 50ccgentuki が 50っc原付 になっていた (cc が っ + c になる)。数字 (半角) の直後の cc・pp・ppm・ppb・cal は英字のまま
        foreach (var (typed, expected) in new[]
        {
            ("50ccgentuki", "50ccげんつき"), ("50cc", "50cc"), ("50ccno", "50ccの"), ("50ccdesu", "50ccです"), ("50ccwo", "50ccを"),
            ("50ccha", "50ccは"), ("kyouha50ccnobaiku", "きょうは50ccのばいく"), ("50CCno", "50CCの"), ("5ppm", "5ppm"), ("5ppb", "5ppb"),
            ("5ppno", "5ppの"), ("5calde", "5calで"), ("100mm", "100mm"), ("3kg", "3kg"), ("2ml", "2ml"), ("5km", "5km"), ("10gb", "10gb"),
            // 数字の直後でも、日本語のローマ字として自然な列は今までどおり
            ("5ppu", "5っぷ"), ("5ppo", "5っぽ"), ("50ccaga", "50っかが"), ("3ma", "3ま"), ("2ka", "2か"), ("5ki", "5き"), ("4tt", "4っt"), ("5ss", "5っs"),
            // 数字の直後でなければ今までどおり (っ + 子音)
            ("cc", "っc"),
        })
        {
            var k = new CompositionTests.Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, typed);
        }
    }

    [Test]
    public static void UnitsAfterNumbers_DoubledConsonant_EditingKeys()
    {
        // Backspace で戻す: 50ccg → Backspace → 50cc (続けて no を打てる)
        var k = new CompositionTests.Keyboard();
        k.Type("50ccg");
        Assert.Equal("50ccg", k.Showing, "打ちかけの g");
        k.Press(VirtualKeys.Back);
        k.Type("no\n");
        Assert.Equal("50ccの", k.Host.Document, "Backspace のあと続けて打てる");

        k = new CompositionTests.Keyboard();
        k.Type("50ccno");
        k.Press(VirtualKeys.Back);
        k.Type("\n");
        Assert.Equal("50cc", k.Host.Document, "の を消すと 50cc");
        k = new CompositionTests.Keyboard();
        k.Type("50ccno");
        k.Press(VirtualKeys.Back);
        k.Press(VirtualKeys.Back);
        k.Type("c\n");
        Assert.Equal("50cc", k.Host.Document, "の と c を消して c を打ち直しても 50cc");

        // F10 / F6 / F7 (半角英字・ひらがな・カタカナ)
        k = new CompositionTests.Keyboard();
        k.Type("50ccno");
        k.Press(VirtualKeys.F10);
        Assert.Equal("50ccno", k.Showing, "F10 は打った英字");
        k.Type("\n");
        Assert.Equal("50ccno", k.Host.Document);
        k = new CompositionTests.Keyboard();
        k.Type("50ccno");
        k.Press(VirtualKeys.F6);
        Assert.Equal("50ccの", k.Showing, "F6 はひらがな (cc は英字のまま)");
        k = new CompositionTests.Keyboard();
        k.Type("50ccno");
        k.Press(VirtualKeys.F7);
        Assert.Equal("50ccノ", k.Showing, "F7 はカタカナ");

        // Space で変換 (表示に 50cc がそのまま残る)
        k = new CompositionTests.Keyboard();
        k.Type("50ccgentuki ");
        Assert.True(k.Host.View!.Converting, "Space で変換に入る");
        Assert.True(k.Host.View!.Text.StartsWith("50cc", StringComparison.Ordinal), "変換中の表示も 50cc: " + k.Host.View!.Text);
    }

    [Test]
    public static void UnitsAfterNumbers_FullWidthDigits_Unchanged()
    {
        // 全角数字の直後は対象外 (今までどおり)。単位の規則は半角数字の直後だけ
        var text = new Meltype.Composition.CompositionText(CompositionTests.Detector);
        foreach (var c in "５０cc") text.Append(c);
        Assert.Equal("５０っc", text.Display(final: true), "全角数字の直後は今までどおり");
    }

    [Test]
    public static void TesterReports_20261003()
    {
        // テスターの報告 (2026-10-03): これあれか、開きの記号の全角、Ah!、swingin'、a / u / r
        CompositionTests.Detector.SpellChecker = TestSupport.WordChecker is { IsAvailable: true } checker ? checker : Detection.BuiltInWordChecker.Shared;
        try
        {
            foreach (var (typed, expected) in new[]
            {
                ("koreareka", "これあれか"), ("koreade", "koreaで"),
                ("\"uchiagebanashihaGo Through!\"", "\"うちあげばなしはGo Through!\""), ("(Ooh Let's Think wow...) ", "(Ooh Let's Think wow...) "),
                ("(chuui)", "(ちゅうい)"), ("\"kyouhaGo!\"tteitta", "\"きょうはGo!\"っていった"),
                ("1. Ah! Ah!", "1. Ah! Ah!"), ("ahoka", "あほか"),
                ("Swingin'", "Swingin'"), ("rockin' and rollin'", "rockin' and rollin'"),
                ("A fool a fool a, for u", "A fool a fool a, for u"), ("how r u", "how r u"), ("kyouhau", "きょうはう"),
                ("atarashiiPCwokatta", "あたらしいPCをかった"), ("iPhonewokatta", "iPhoneをかった"),
                ("toshiteOCRwoshi,amerikagonaruEnglishwohappyni", "としてOCRをし、あめりかごなるEnglishをhappyに"), ("Tokyonisumu", "Tokyoにすむ"),
                ("motometeSNSde", "もとめてSNSで"), ("shilyowhugaltsucowhu", "しょうがっこう"), ("dochiramouserga", "どちらもuserが"), ("macOSnoupdate", "macOSのupdate"),
                ("tabetemitaidesupart1026beta", "たべてみたいですpart1026beta"), ("kaibunsyorta2026kotosimo", "かいぶんしょrta2026ことしも"), ("PS5wokaitai", "PS5をかいたい"),
                ("cyiisai", "ちぃいさい"), ("yiu", "いう"), ("wu", "う"), ("ceito", "せいと"),
                ("translatebot[Thinking is thinking]", "translatebot「Thinking is thinking」"), ("fuwafuwanacornyorifuwafuwanachocolatenohougayum", "ふわふわなcornよりふわふわなchocolateのほうがyum"),
                ("hey yo say!", "hey yo say!"), ("korosuzobot", "ころすぞbot"),
                ("appuruulottitukereba", "あっぷるうぉっちつければ"), ("JapanesetoEnglishwohodohodonimazetahougagoodkamoshirenai", "JapaneseとEnglishをほどほどにまぜたほうがgoodかもしれない"), ("JavaScriptwokaku", "JavaScriptをかく"), ("iPaddeiisonnnadekakunakute", "iPadでいいそんなでかくなくて"), ("atohahelppe-jitoka", "あとはhelpぺーじとか"), ("HHireta", "HHいれた"), ("grokga", "grokが"), ("cwdde", "cwdで"), ("pwdde", "pwdで"), ("cwdga", "cwdが"), ("grokniyoruto", "grokによると"), ("guri-nnshanottara51kmijoukukan", "ぐりーんしゃのったら51kmいじょうくかん"), ("shitaraavgadete", "したらavがでて"), ("jimotogeoguessershitetara", "じもとgeoguesserしてたら"), ("jiketsurtashite", "じけつrtaして"),
                // Issue #375: 知らない英字の語の最後の文字と助詞の頭が同じ (cwd + de) でも区切る (語の頭から読めないので っ の綴りではない)
                // 知らない語 (BuiltIn / NSSpellChecker のどちらも英単語と答えない語) でも同じ: gpg+ga・jwt+to・tdd+de
                ("gpgga", "gpgが"), ("jwtto", "jwtと"), ("tddde", "tddで"),
                // summary.json (2026-10-05): 音の途中から始まる英単語 (kara|na|l の anal、da|me|x の amex) で小書き文字が崩れていた
                ("yakaranala", "やからなぁ"), ("damexe", "だめぇ"), ("hotelya", "hotelや"),
                // 笑いの w (文末の w が消えていた、ww が っw になっていた)。英単語の最後の w はそのまま
                ("kiyagattaw", "きやがったw"), ("daneww", "だねww"), ("toottawwwww", "とおったwwwww"), ("wwww", "wwww"), ("kitaww!", "きたww！"), ("new", "new"), ("aww", "aww"),
                // 読めない略語の最後の c + は・や (vrc|ya が vr|ちゃ になっていた)。読める語 (まち) と大文字の略語の後ろ (EDちゃう) は分けない
                ("vrcyaranai", "vrcやらない"), ("vchairu", "vcはいる"), ("pcha", "pcは"), ("machi", "まち"), ("ochaire", "おちゃいれ"), ("EDchau", "EDちゃう"),
                ("everyonenohatsugen", "everyoneのはつげん"),
                // 英語の歌詞: 行の始めの I'll の後ろ (I'll ご になっていた)、riverside, (river しで、 になっていた)
                ("I'll go to see you again tomorrow", "I'll go to see you again tomorrow"), ("by the riverside, I'm sitting", "by the riverside, I'm sitting"),
                ("shirigaru teenage girl", "しりがるteenage girl"), ("bakudannnihanarenai oh no!", "ばくだんにはなれないoh no!"), ("supeaman woah", "すぺあまんwoah"),
                // 英文の中の短い語 + 記号 (let's go! の go が ご になっていた)。日本語の後ろの記号は今までどおり全角
                ("let's go!", "let's go!"), ("I said no?", "I said no?"), ("sorena!", "それな！"), ("nande?", "なんで？"),
                // 記号の前・確定時の笑いの w (きたw！ の w が確定で消えていた)
                ("kitaw!", "きたw！"), ("hontow?w", "ほんとw？w"),
                // 略語 + 日本語 + 記号は今までどおり (BE|かな？ が BEkana？ になっていた)
                ("tougouhandakaraBEkana?", "とうごうはんだからBEかな？"), ("fubusangaXshisuginadakenanda!!", "ふぶさんがXしすぎなだけなんだ！！"), ("tsubemyunorevancedtsukatteru", "つべみゅのrevancedつかってる"),
                // テスターの報告 (2026-10-05): 英語のユーザー名が打てない。@ の後ろ (メンション)・_ の入った語は英字のまま
                ("@kuraido", "@kuraido"), ("@una08142009 arigatou", "@una08142009 ありがとう"), ("upah_setu", "upah_setu"), ("cafely_latte", "cafely_latte"),
                ("taro@gmail.com", "たろ@gmail.com"), ("@akisamesan", "@akisamesan"),
                // Issue #12: ローマ字として読めてしまう英単語 (feature → ふぇあつれ)。日本語の中でも英字
                ("feature", "feature"), ("future", "future"), ("nature", "nature"), ("remote", "remote"), ("online", "online"),
                ("atarashiifeaturewotsuika", "あたらしいfeatureをついか"), ("kyouharemotedesu", "きょうはremoteです"),
            })
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
    public static void ShortWord_ChosenFromCandidates_IsLearnedOnSecondTime()
    {
        // 変換の候補から go を英字で選んだだけで覚えると、日本語の中まで英字になりやすい (にほんgo)。2 文字の語は 2 回で覚える。
        var memory = new LanguageMemory(null) { IsReadableRomaji = _ => true };
        memory.Remember("go", english: true);
        Assert.Equal(null, memory.Get("go"), "1 回目はまだ覚えない");
        Assert.True(!memory.Entries().Single().Active, "一覧では「2 回目を待っている」");
        memory.Remember("go", english: true);
        Assert.Equal(true, memory.Get("go"), "2 回目で英語として覚える");
        memory.Remember("to", english: true, explicitChoice: true);
        Assert.Equal(true, memory.Get("to"), "F10 ではっきり直したら 1 回で覚える");
        memory.Remember("api", english: true);
        Assert.Equal(true, memory.Get("api"), "3 文字以上は今までどおり 1 回で覚える");
        memory.Remove(["go"]);
        Assert.Equal(null, memory.Get("go"), "一覧から消したら忘れる");
    }

    [Test]
    public static void ShiftSpace_ConvertsEnglishWordAsRomaji()
    {
        // 英字と判定された語も変換できるように: Shift+Space でローマ字として読んで変換する。
        var memory = new LanguageMemory(null);
        memory.Remember("go", english: true, explicitChoice: true);
        CompositionTests.Detector.Memory = memory;
        try
        {
            var k = new CompositionTests.Keyboard(languages: memory);
            k.Type("go");
            k.TypeKeys((VirtualKeys.Space, true));
            k.Type("\n");
            Assert.Equal("ご", k.Host.Document, "Shift+Space で日本語の候補が先頭");
            Assert.Equal(false, memory.Get("go"), "日本語で確定したので、次から日本語");
        }
        finally
        {
            CompositionTests.Detector.Memory = null;
        }
    }

    [Test]
    public static void F6_TeachesJapanese()
    {
        var memory = new LanguageMemory(null);
        CompositionTests.Detector.Memory = memory;
        try
        {
            var k = new CompositionTests.Keyboard(languages: memory);
            k.Type("google");
            k.Press(VirtualKeys.F6);
            k.Type("\n");
            Assert.Equal("ごおgぇ", k.Host.Document);
            Assert.Equal(false, memory.Get("google"), "F6 でかなにして確定したので覚える");
            var next = new CompositionTests.Keyboard(languages: memory);
            next.Type("google");
            Assert.Equal("ごおgぇ", next.Showing);
        }
        finally
        {
            CompositionTests.Detector.Memory = null;
        }
    }
}

internal static class RawCandidateTests
{
    [Test]
    public static void Conversion_OffersTypedLetters_AndLearns()
    {
        var memory = new LanguageMemory(null);
        CompositionTests.Detector.Memory = memory;
        try
        {
            var k = new CompositionTests.Keyboard(languages: memory);
            k.Type("api ");
            var candidates = k.Host.View!.Candidates;
            Assert.True(candidates.Contains("api") && candidates.Contains("ａｐｉ"), $"候補に打ったままの英字と全角の英字: {string.Join(" ", candidates)}");
            // Space を連打して英字まで送る
            for (var i = 0; i < 10 && k.Host.View!.Candidates[k.Host.View.SelectedIndex] != "api"; i++) k.Press(VirtualKeys.Space);
            k.Type("\n");
            Assert.Equal("api", k.Host.Document);
            Assert.Equal(true, memory.Get("api"), "英字を選んで確定したので覚える");
        }
        finally
        {
            CompositionTests.Detector.Memory = null;
        }
    }

    [Test]
    public static void EnglishWordEndingInN_BeforeParticle()
    {
        foreach (var (typed, expected) in new[] { ("pythonnobug", "pythonのbug"), ("kotlinnihenkou", "kotlinにへんこう"), ("kannji", "かんじ"), ("konnnichiha", "こんにちは") })
        {
            var k = new CompositionTests.Keyboard();
            k.Type(typed);
            Assert.Equal(expected, k.Showing, typed);
        }
    }
}

internal static class RomajiTypoTests
{
    [Test]
    public static void RomajiTypos_AreFixedOnCommit()
    {
        foreach (var (typed, expected) in new[]
        {
            ("onegaishimsu", "おねがいします"),   // 抜け
            ("arigatpu", "ありがとう"),           // 隣のキー
            ("arigtou", "ありがとう"),
            ("sumimasne", "すみません"),          // 入れ替わり
            ("shitmeasu", "してます"),
            ("gozaimsu", "ございます"),
            ("yorosikuy", "よろしく"),            // 余計な 1 文字
            ("kinouhaamegafuttemshita", "きのうはあめがふってました"),
        })
        {
            var k = new CompositionTests.Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, typed);
        }
    }

    [Test]
    public static void RomajiTypos_LeaveEnglishAndAbbreviations()
    {
        foreach (var (typed, expected) in new[]
        {
            ("oknotasuku", "okのたすく"),
            ("googlede", "googleで"),
            ("kyouhamtgdesu", "きょうはmtgです"),
            ("sdakega", "sだけが"),
            ("htmlnokaisetu", "htmlのかいせつ"),
        })
        {
            var k = new CompositionTests.Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, typed);
        }
    }

    [Test]
    public static void RomajiTypos_CanBeTurnedOff()
    {
        var k = new CompositionTests.Keyboard();
        k.CorrectTypos = false;
        k.Type("onegaishimsu\n");
        Assert.Equal("おねがいしmす", k.Host.Document);
    }

    [Test]
    public static void RomajiTypos_LoneKanaThenStrayConsonant_KeepsTheConsonant()
    {
        // Q1: 子音 + 母音 + z / q で Enter すると、末尾の z / q が消えていた (baz → ば、foo_bar_baz → foo_bar_ba)。
        // 原因は「語末に子音が 1 つ残った。隣のキーなら打ち間違いとして消す」(RomajiTypoCorrector.Fix) が、前が 1 音 (ば・か・さ) だけでも働くこと。
        // z・q は a の隣のキー (s・d・k・r なども母音の隣)。前が 1 音だけでは打ち間違いの根拠が無い (略語・変数名・ファイル名かもしれない) ので消さない。
        foreach (var (typed, expected) in new[]
        {
            ("baz", "ばz"), ("kaz", "かz"), ("saz", "さz"), ("taz", "たz"), ("haz", "はz"), ("baq", "ばq"), ("kaq", "かq"),
            ("bas", "ばs"), ("kas", "かs"), ("aq", "あq"), ("foo_bar_baz", "foo_bar_baz"), ("foo_bar_baq", "foo_bar_baq"), ("kanz", "かんz"),
        })
        {
            var k = new CompositionTests.Keyboard();
            k.Type(typed);
            var showing = k.Showing;
            k.Type("\n");
            Assert.Equal(expected, k.Host.Document, typed);
            // 打っている間に見えていたものと、Enter で入るものが同じ
            Assert.Equal(showing, k.Host.Document, typed + " の表示");
        }
    }

    [Test]
    public static void RomajiTypos_LoneKanaThenStrayConsonant_DoesNotTouchEnglishOrOtherFixes()
    {
        // Q1 の規則 (前が 1 音だけなら消さない) が、英単語・略語・ほかの打ち間違いの直しを変えない
        CompositionTests.Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try
        {
            foreach (var (typed, expected) in new[]
            {
                ("jazz", "jazz"), ("quiz", "quiz"), ("pizza", "pizza"), ("buzz", "buzz"), ("fizz", "fizz"), ("qr", "qr"), ("xz", "xz"), ("zzz", "っっz"),
                ("qa", "qあ"), ("kazu", "かず"), ("bax", "ばx"), ("bak", "ばk"), ("bab", "ばb"),
                // 前が 2 音以上の打ち間違いは今までどおり直す (余計な 1 文字・隣のキー・z の打ち間違い)
                ("yorosikuy", "よろしく"), ("desuh", "です"), ("desuy", "です"), ("arigatpu", "ありがとう"), ("onegaishimsu", "おねがいします"),
                ("kanb", "かん"), ("sanh", "さん"), ("bakz", "ばか"), ("hazkimeru", "はじめる"), ("mazjnoke", "まずのけ"), ("kozlsite", "こぞして"),
            })
            {
                var k = new CompositionTests.Keyboard();
                k.Type(typed + "\n");
                Assert.Equal(expected, k.Host.Document, typed);
            }
            // 打ち間違いを直さない設定なら、もともと消えない (原因が打ち間違いの直しにあることを固定する)
            var off = new CompositionTests.Keyboard();
            off.CorrectTypos = false;
            off.Type("baz\n");
            Assert.Equal("ばz", off.Host.Document);
        }
        finally
        {
            CompositionTests.Detector.SpellChecker = null;
        }
    }

    [Test]
    public static void RomajiTypos_Q1_AlsoHoldsBeforeSymbols_AndTheKnownLimitsArePinned()
    {
        // Q1 の補足 (査読 R5): Space・記号の直前の確定も同じ FixTypos を通る。baz? や def baz(): でも z を消さない。
        // 既知の限界: 前が 2 音以上で、最後が 1 文字の付属語 (ば・は・た) で終わるものは、z・q が今も消える
        // (foobaz → ふぉおば、kyouhaz → きょうは)。直すと tabetaz → たべた のような本物の打ち間違いの補正も失われるので、直していない。
        // 将来この限界を塞ぐ・広げるときに赤になるよう、現在の答えを固定する
        CompositionTests.Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try
        {
            foreach (var (typed, expected) in new[]
            {
                ("baz?", "ばz？"), ("def baz():", "def ばz():"),
                ("foobaz", "ふぉおば"), ("kyouhaz", "きょうは"), ("tabetaz", "たべた"),
            })
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
    public static void Q3_ReadableEnglishWords_StayEnglishInsideJapanese()
    {
        // 本家 #411: 英語を打つつもりの change が ちゃんげ になる (これは change してほしい → これはちゃんげしてほしい)。
        // change・chance・before・season などは、ローマ字として最後まで読めて日本語の語にならず、英語の中でよく使う語なので、
        // dictionaries/english-readable.txt に入れて、日本語の文の中でも英字にする。BuiltIn の英単語表で確かめる (NSSpellChecker でも同じ答えになることは別に比べた)。
        CompositionTests.Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try
        {
            foreach (var (typed, expected) in new[]
            {
                ("chance", "chance"), ("korehachancewokakunin", "これはchanceをかくにん"),
                ("soregachancedayo", "それがchanceだよ"), ("kyouhachancesuru", "きょうはchanceする"),
                ("korehabeforeshitehoshii", "これはbeforeしてほしい"), ("soregaregiondayo", "それがregionだよ"), ("korehahopedayo", "これはhopeだよ"),
                ("seasonwoshiraberu", "seasonをしらべる"), ("situationwokakunin", "situationをかくにん"),
                ("positionwokimeru", "positionをきめる"), ("korehamaintainshitehoshii", "これはmaintainしてほしい"), ("databasewokesu", "databaseをけす"),
                ("vitaminwotoru", "vitaminをとる"),
            })
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
    public static void Q3_ReadableEnglishWords_LeaveJapaneseAndTheKnownLimitsAlone()
    {
        // english-readable.txt に語を足したとき、日本語の語の中・前後に偶然その綴りが現れても日本語のままであること。
        // 下の入力は、足す語を選ぶときに readings.txt のローマ字 (単独・助詞つき・語の組み合わせ) と辞書の説明文のローマ字で悪化した形 (ride・shade・nine・gone・note・mouse など)。
        // 助詞と同じ綴りで始まる・終わる語 (note・mouse・nine・side・code・line・white) は、日本語の語と区別できないので入れていない。今の答えを固定する (限界)。
        CompositionTests.Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try
        {
            foreach (var (typed, expected) in new[]
            {
                ("hitorideni", "ひとりでに"), ("kagaishade", "かがいしゃで"), ("ainude", "あいぬで"), ("worea", "をれあ"), ("attoiumanine", "あっというまにね"),
                ("aigone", "あいごね"), ("anpine", "あんぴね"), ("ayamachine", "あやまちね"), ("isouroute", "いそうろうて"), ("atsumarine", "あつまりね"), ("sazone", "さぞね"),
                ("AutoIMEnotesuto", "AutoIMEのてすと"), ("dochiramouserga", "どちらもuserが"), ("Meltype.cwokesu", "Meltype.cをけす"),
                ("imagenzai", "いまげんざい"), ("devinte-ji", "でゔぃんてーじ"),
                // 査読 R9: retain・gotten を外した (〜れたいんです・〜ってんです)
                ("wasuretaindesu", "わすれたいんです"), ("wakaretaindesu", "わかれたいんです"), ("ogottendesu", "おごってんです"), ("nigottendayo", "にごってんだよ"),
                // 査読 R7/R8 で見つかった型: 訓令式 (tu/si)・れ+あそん・すれば・れ+ふせ。tube・basin・reason・ensure・refuse・devin を外したので日本語のまま
                ("betubetu", "べつべつ"), ("motubeki", "もつべき"), ("nakerebasinpai", "なければしんぱい"), ("koreasonde", "これあそんで"), ("ouensureba", "おうえんすれば"), ("korefusegu", "これふせぐ"), ("oreasondekuru", "おれあそんでくる"),
                // ちゃん付けの名前+げ…: change を入れないので日本語のまま (change 自体は ちゃんげ のまま = 本家 #411 は直さない判断)
                ("yuuchangenki", "ゆうちゃんげんき"), ("hanakochangeki", "はなこちゃんげき"), ("korehachangeshitehoshii", "これはちゃんげしてほしい"),
                ("tomatowotabeta", "とまとをたべた"), ("korehamakeshitehoshii", "これはまけしてほしい"), ("nikkoutetsuduki", "にっこうてつづき"),
                // 限界 (入れていない語): 今までの答えのまま
                ("korehacodewokaku", "これはこでをかく"), ("korehasidegaii", "これはしでがいい"), ("korehawhitedayo", "これはうぃてだよ"), ("korehanotewotoru", "これはのてをとる"),
                ("korehaimagewokesu", "これはいまげをけす"), ("korehaideawodasu", "これはいであをだす"),
            })
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
    public static void Q3_EnglishReadable_Dictionary_EntriesAreWellFormed()
    {
        // 足す語の決まり: 4 文字以上の小文字の英字で、ローマ字として読める (読めない語は英語と判定されるので、ここに入れる意味が無い)
        var words = Detection.DictionarySource.Load("english-readable.txt", null).ToList();
        Assert.True(words.Count >= 100, "Q3 で足した語が読み込まれている: " + words.Count);
        Assert.True(words.Contains("chance") && words.Contains("feature") && !words.Contains("change"), "chance (Q3) と feature (#12) が入り、change は入らない");
        foreach (var word in words)
        {
            Assert.True(word.Length >= 4 && word.All(char.IsAsciiLetterLower), "4 文字以上の小文字の英字: " + word);
            Assert.True(CompositionTests.Detector.Romaji.AnalyzeFragment(word).IsValid, "ローマ字として読める語 (features の s のような最後の子音は残ってよい): " + word);
        }
        Assert.Equal(words.Count, words.Distinct().Count(), "重複が無い");
    }

    [Test]
    public static void Issue303_StylizedName_KnownLimits_AreWordByWord_AndShowTheNameWhileTyping()
    {
        // P34 の補足 (査読 R6): 記号入りの名前の規則は「空白で区切った 1 語が名前の形のとき」だけ働く。
        // 名前の直後に記号が付く形 (Ke$ha, / (B.o.B) / U.S.A.,) は効かず、変更前の崩れた答えのまま (本家 PR #315 の正規表現も入力全体に当てる形で、同じ制限)。
        // 広げるときに赤になるよう、現在の答えを固定する。空白で区切った文の中では名前になる
        CompositionTests.Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try
        {
            foreach (var (typed, expected) in new[]
            {
                ("kyouha B.o.B\n", "今日はB.o.B"), ("kyouha Ke$ha dayo\n", "今日はKe$ha だよ"),
                ("Ke$ha,\n", "け＄は、"), ("(B.o.B)\n", "(B.お。B)"), ("U.S.A.,\n", "う.S.A。、"),
            })
            {
                var k = new CompositionTests.Keyboard();
                k.Type(typed);
                Assert.Equal(expected, k.Host.Document, typed);
            }
            // 1 文字ずつ打つ途中の表示も、名前が出来上がった時点で英字になる
            foreach (var (typed, expected) in new[] { ("B.o.B", "B.o.B"), ("Ke$ha", "Ke$ha"), ("U.S.A.", "U.S.A.") })
            {
                var k = new CompositionTests.Keyboard();
                k.Type(typed);
                Assert.Equal(expected, k.Showing, typed + " の入力中の表示");
            }
        }
        finally
        {
            CompositionTests.Detector.SpellChecker = null;
        }
    }

    [Test]
    public static void ShortHeadThenCapital_IsName()
    {
        // 本家 #266 (本家 1640142 の例): McAfeeto が McA|feeto で切れて McAふぇえと になっていた。Mc・Le の後ろの大文字で終わる頭 (McA・LeB) は略語ではなく名前の途中
        foreach (var (typed, expected) in new[]
        {
            ("McAfeeto", "McAfeeと"), ("McAfeewo", "McAfeeを"), ("McAfeede", "McAfeeで"), ("LeBronde", "LeBronで"),
            // 英単語 + 大文字 1 文字・大文字が続く略語は今までどおり区切る
            ("PlanBdeiku", "PlanBでいく"), ("TypeAnohou", "TypeAのほう"), ("OrpCde", "OrpCで"), ("oRPCde", "oRPCで"), ("ORPCde", "ORPCで"),
            ("OCRwoshi", "OCRをし"), ("DeNAde", "DeNAで"), ("OpenAIno", "OpenAIの"), ("MrXde", "MrXで"),
            ("McAfee", "McAfee"), ("McKinseyde", "McKinseyで"), ("McDonaldsde", "McDonaldsで"),
        })
        {
            var k = new CompositionTests.Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, typed);
        }

        var typing = new CompositionTests.Keyboard();
        typing.Type("McAfeeto");
        Assert.Equal("McAfeeと", typing.Showing);
    }

    [Test]
    public static void CamelCaseName_IsNotCutIntoParticle()
    {
        // 大文字つなぎの名前 (TanakaTaiga・TanakaKanade・TanakaMakoto・UserGuide) の語尾を、助詞 (が・で・と) として削らない。
        // 本家 #268 (DeVitode → DeVitoで) のために、大文字つなぎの名前 + 助詞で区切る処理を試したが、DeVito|de と Kana|de (名前) は
        // 機械的に区別できず、実在の名前 (Taiga・Kanade・Mitsuha・Makoto) を削ってしまうので入れていない。
        // 名前を壊すより、区切られずに英字のままのほうが利用者が直しやすい。ここは、この方針 (名前と英語を削らない) を守るテスト。
        foreach (var (typed, expected) in new[]
        {
            // 語尾が が・で・は・と に似る日本人の名前 (姓 + 名を Shift でつなげて打つ)
            ("TanakaTaiga", "TanakaTaiga"), ("TanakaRyoga", "TanakaRyoga"), ("TanakaDaiga", "TanakaDaiga"), ("TanakaKanade", "TanakaKanade"),
            ("TanakaMitsuha", "TanakaMitsuha"), ("TanakaKotoha", "TanakaKotoha"), ("TanakaKaede", "TanakaKaede"), ("SatoChiharu", "SatoChiharu"),
            ("TanakaMakoto", "TanakaMakoto"), ("SatoHayato", "SatoHayato"), ("ItoKento", "ItoKento"), ("WatanabeKaito", "WatanabeKaito"),
            ("KobayashiHiroto", "KobayashiHiroto"), ("KatoMikoto", "KatoMikoto"), ("SatoShino", "SatoShino"),
            ("TanakaMakoto desu", "TanakaMakoto です"), ("Please use TanakaMakoto today", "Please use TanakaMakoto today"),
            ("TanakaTaiga to", "TanakaTaiga と"), ("TanakaKanade wo", "TanakaKanade を"),
            // 語尾が助詞に似る英語の大文字つなぎ語
            ("SuperRossini", "SuperRossini"), ("SunGizmo", "SunGizmo"), ("GoPinto", "GoPinto"),
            ("UserGuide", "UserGuide"), ("StyleGuide", "StyleGuide"), ("JsonDecode", "JsonDecode"), ("TokyoKyoto", "TokyoKyoto"), ("FromToronto", "FromToronto"),
            ("GrandPiano", "GrandPiano"), ("SuperDomino", "SuperDomino"), ("BigTomato", "BigTomato"), ("TokyoYamaha", "TokyoYamaha"),
            ("IsaacNewtonno", "IsaacNewtonno"), ("AlexanderHamiltonno", "AlexanderHamiltonno"),
            // 既存の区切り (知っている語 + 助詞) は変えない
            ("BigBangtoha", "BigBangとは"), ("BigBangtoiudouga", "BigBangというどうが"), ("GeorgeWashingtonga", "GeorgeWashingtonが"),
        })
        {
            var k = new CompositionTests.Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, typed);
        }
    }
}
