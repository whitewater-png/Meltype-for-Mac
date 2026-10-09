// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>Meltype キーボード (変換ボックス)。</summary>
internal static class CompositionTests
{
    internal static readonly CompositionDetector Detector = CompositionDetector.CreateDefault();

    internal sealed class FakeConverter : IKanjiConverter
    {
        public string? Convert(string hiragana) => hiragana switch
        {
            "きょう" => "今日",
            "にほんご" => "日本語",
            "こんにちは" => "今日は",
            "きょうは" => "今日は",
            "でけんさく" => "で検索",
            "たんい" => "単位",
            _ => null,
        };

        /// <summary>直近の ConvertClauses に渡された文脈 (文脈なしの呼び出しは数えない)。</summary>
        public string? LastContext { get; private set; }

        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null)
        {
            if (context is not null) LastContext = context;
            return hiragana switch
            {
                "たんいをとる" => [new("たんいを", "単位を"), new("とる", "取る")],
                "ひるか" => [new("ひる", "昼"), new("か", "化")],
                // 本物の変換エンジンは、前に同じ語があると区切りを変える (「記号等」含め、 + きごうとう → 気強盗)。
                "きごうとう" when context?.Contains("記号等") == true => [new("き", "気"), new("ごうとう", "強盗")],
                "きごうとう" => [new("きごう", "記号"), new("とう", "等")],
                "あつい" => [new("あつい", "熱い")],
                "かわ" => [new("かわ", "川")],
                "すぱいだーまっ" => [new("すぱいだーま", "スパイダーマ"), new("っ", "っ")],
                "はしを" => [new("はしを", "橋を")],
                // 本物の変換エンジン (Mozc) の苦手なもの: え、 → 得、、がちで → 勝ちで
                "え、しらん" => [new("え", "得"), new("、", "、"), new("しらん", "知らん")],
                "えかく" => [new("え", "絵"), new("かく", "描く")],
                "がちでやばい" => [new("がちで", "勝ちで"), new("やばい", "ヤバい")],
                // 英単語の後ろの する の活用: した → 下、したい → 死体
                "した" => [new("した", "下")],
                "したい" => [new("したい", "死体")],
                _ => null,
            };
        }
    }

    internal sealed class FakeHost : ICompositionHost
    {
        public List<string> Output { get; } = [];
        public List<string> Events { get; } = [];
        public CompositionView? View { get; private set; }
        public bool PhysicalShift { get; set; }

        /// <summary>入力欄のキャレットの直前にある (と見なす) 確定済みの文字列。</summary>
        public string? PrecedingText { get; set; }

        /// <summary>入力欄のキャレットの後ろにある (と見なす) 文字列。</summary>
        public string? FollowingText { get; set; }

        public void RequestSurroundingText(Action<string?, string?> callback) => callback(PrecedingText ?? (Document.Length > 0 ? Document : null), FollowingText);

        /// <summary>入力欄の中身 (確定した文字列と、確定し直すときの削除を反映したもの)。</summary>
        public string Document { get; private set; } = "";

        public void DeleteBackward(int count)
        {
            Events.Add($"bs:{count}");
            Document = Document[..Math.Max(0, Document.Length - count)];
        }

        public void CommitText(string text)
        {
            Output.Add(text);
            Events.Add($"text:{text}");
            Document += text;
        }

        public void Replay(KeyEvent e) => Events.Add($"{(e.IsUp ? "up" : "down")}:{e.Vk:X2}");
        public void Replay(MouseButtonEvent e) => Events.Add($"mouse:{e.Message:X}");

        public char? CharFromKey(KeyEvent e, bool shift)
        {
            if (VirtualKeys.IsLetter(e.Vk)) return shift || PhysicalShift ? (char)e.Vk : char.ToLowerInvariant((char)e.Vk);
            var shifted = shift || PhysicalShift;
            foreach (var (c, key) in JisKeys)
            {
                if (key.Vk == e.Vk && key.Shift == shifted) return c;
            }
            return null;
        }

        /// <summary>JIS 配列の数字・記号のキー (文字 → 仮想キー, Shift)。</summary>
        public static readonly Dictionary<char, (int Vk, bool Shift)> JisKeys = new()
        {
            ['0'] = (0x30, false), ['1'] = (0x31, false), ['2'] = (0x32, false), ['3'] = (0x33, false), ['4'] = (0x34, false),
            ['5'] = (0x35, false), ['6'] = (0x36, false), ['7'] = (0x37, false), ['8'] = (0x38, false), ['9'] = (0x39, false),
            ['!'] = (0x31, true), ['"'] = (0x32, true), ['#'] = (0x33, true), ['$'] = (0x34, true), ['%'] = (0x35, true),
            ['&'] = (0x36, true), ['\''] = (0x37, true), ['('] = (0x38, true), [')'] = (0x39, true),
            ['-'] = (0xBD, false), ['='] = (0xBD, true), ['^'] = (0xDE, false), ['~'] = (0xDE, true), ['\\'] = (0xDC, false), ['|'] = (0xDC, true),
            ['@'] = (0xC0, false), ['`'] = (0xC0, true), ['['] = (0xDB, false), ['{'] = (0xDB, true),
            [';'] = (0xBB, false), ['+'] = (0xBB, true), [':'] = (0xBA, false), ['*'] = (0xBA, true), [']'] = (0xDD, false), ['}'] = (0xDD, true),
            [','] = (0xBC, false), ['<'] = (0xBC, true), ['.'] = (0xBE, false), ['>'] = (0xBE, true), ['/'] = (0xBF, false), ['?'] = (0xBF, true),
            ['_'] = (0xE2, true),
        };

        public bool IsShiftDown() => PhysicalShift;

        public void Show(CompositionView view) => View = view;
        public void Hide() => View = null;
    }

    internal sealed class Keyboard
    {
        private long _now = 1000;
        private bool _ctrlHeld;
        public CaptureGate Gate { get; } = new(() => { });
        public FakeHost Host { get; } = new();
        public CompositionController Controller { get; }

        private static readonly Detection.ScoreEngine DirectEngine = TestSupport.CreateEngine();
        private static readonly CandidateDictionary Candidates = CandidateDictionary.Load(null);
        private static readonly ContextRules Rules = ContextRules.Load(null);
        private static readonly MisspellingDictionary Misspellings = MisspellingDictionary.Load(null);
        private static readonly Lazy<RomajiTypoCorrector> SharedTypos = new(() => RomajiTypoCorrector.Load(Detector.Romaji));
        private static RomajiTypoCorrector Typos => SharedTypos.Value;
        private bool _directEnglishWord;

        /// <summary>自動判定の強さ。</summary>
        public Meltype.Config.DetectionLevel Level { get; set; } = Meltype.Config.DetectionLevel.Balanced;

        private bool _shiftHeld;

        /// <summary>かな入力 (JIS) か。</summary>
        public bool Kana { get; set; }
        public bool CorrectTypos { get; set; } = true;
        public bool SpaceAroundEnglish { get; set; }
        public PunctuationStyle Punctuation { get; set; } = PunctuationStyle.Japanese;
        public bool FullWidthSymbols { get; set; } = true;
        public bool Prediction { get; set; } = true;

        /// <summary>変換中の Shift+Enter で確定してキーをアプリに渡すか (CompositionOptions.ShiftEnterNewline)。既定は ON。</summary>
        public bool ShiftEnterNewline { get; set; } = true;

        /// <summary>変換後も続けて入力できるか (CompositionOptions.ContinueAfterConversion)。既定は OFF (今までの動作)。</summary>
        public bool Continue { get; set; }

        public bool SuggestEnabled { get; set; } = true;

        /// <summary>かな入力で、仮想キーを順に打つ (shift: その打鍵で Shift を押す)。</summary>
        public void TypeKeys(params (int Vk, bool Shift)[] keys)
        {
            foreach (var (vk, shift) in keys)
            {
                if (shift)
                {
                    Host.PhysicalShift = true;
                    Key(VirtualKeys.LShift);
                }
                Press(vk);
                if (shift)
                {
                    Key(VirtualKeys.LShift, up: true);
                    Host.PhysicalShift = false;
                }
            }
        }

        /// <summary>かな入力で、英字キー (と , . / - の記号キー) を打つ。大文字は Shift を押して打つ。</summary>
        public void TypeKanaKeys(string keys) =>
            TypeKeys(keys.Select(c => (c switch { ',' => 0xBC, '.' => 0xBE, '/' => 0xBF, '-' => 0xBD, '@' => 0xC0, '[' => 0xDB, _ => (int)char.ToUpperInvariant(c) }, char.IsAsciiLetterUpper(c))).ToArray());

        /// <summary>英数 (直接入力) 状態か。</summary>
        public bool Direct { get; set; }

        public long Now => _now;

        public FakeConverter Converter { get; } = new();

        public Keyboard(bool live = false, bool direct = false, ConversionHistory? history = null, IKanjiConverter? converter = null,
            Func<string, IReadOnlyList<string>>? moreCandidates = null, UserDictionary? userDictionary = null, LanguageMemory? languages = null,
            TranslationDictionary? translations = null, TranslationHistory? translationHistory = null,
            Func<string, IReadOnlyList<string>>? predictions = null, DictionarySuggestions? suggestions = null, int suggestThreshold = 3)
        {
            Direct = direct;
            Controller = new CompositionController(Gate, Detector, converter ?? Converter, Host, new CompositionOptions
            {
                LiveConversion = () => live,
                ContinueAfterConversion = () => Continue,
                ShiftEnterNewline = () => ShiftEnterNewline,
                DirectMode = () => Direct,
                ClassifyDirect = (letters, final) => DirectEngine.Evaluate(new Detection.DetectionInput(letters, letters.Select(c => (int)char.ToUpperInvariant(c)).ToArray(), final)).Verdict,
                DirectDecided = japanese => { if (japanese) Direct = false; else _directEnglishWord = true; },
                Candidates = Candidates,
                ContextRules = Rules,
                History = history ?? new ConversionHistory(null),
                MoreCandidates = moreCandidates,
                Predictions = predictions,
                UserDictionary = userDictionary,
                Level = () => Level,
                KanaInput = () => Kana,
                Misspellings = Misspellings,
                Languages = languages,
                Translations = translations,
                TranslationHistory = translationHistory,
                RomajiTypos = Typos,
                CorrectTypos = () => CorrectTypos,
                SpaceAroundEnglish = () => SpaceAroundEnglish,
                Punctuation = () => Punctuation,
                FullWidthSymbols = () => FullWidthSymbols,
                Prediction = () => Prediction,
                Suggestions = suggestions,
                DictionarySuggest = () => SuggestEnabled,
                DictionarySuggestThreshold = () => suggestThreshold,
            });
        }

        /// <summary>MeltypeEngine.StartsComposition と同じ条件。</summary>
        private bool Starts(KeyEvent k)
        {
            if (!k.IsDown || _ctrlHeld) return false;
            var letter = VirtualKeys.IsLetter(k.Vk);
            if (Direct) return letter && !_directEnglishWord && Level != Meltype.Config.DetectionLevel.Manual;
            if (Kana && Detection.KanaDetector.IsKanaKey(k.Vk)) return true;
            return letter || k.Vk is >= 0x30 and <= 0x39 or >= 0xBA and <= 0xC0 or >= 0xDB and <= 0xDF or 0xE2;
        }

        /// <summary>フックと同じく、関所が閉じていれば英字キーで変換ボックスを開く。</summary>
        public bool Key(int vk, bool up = false)
        {
            var e = new KeyEvent(vk, 0, false, up, false, _now += 30);
            // 実際のフックと同じく、Ctrl が押されている間は変換ボックスを開かない。
            if (vk == VirtualKeys.LControl) _ctrlHeld = !up;
            if (vk == VirtualKeys.LShift) _shiftHeld = !up;
            if (Direct && !up && !VirtualKeys.IsLetter(vk) && !VirtualKeys.IsModifier(vk)) _directEnglishWord = false;
            var swallowed = Gate.OnKey(e, Starts);
            if (!swallowed) Host.Events.Add($"{(up ? "passed-up" : "passed")}:{vk:X2}");
            Controller.Pump();
            return swallowed;
        }

        public void Press(int vk)
        {
            Key(vk);
            Key(vk, up: true);
        }

        public void Type(string text)
        {
            foreach (var c in text)
            {
                if (char.IsAsciiLetterUpper(c))
                {
                    Host.PhysicalShift = !Gate.IsCaptured;
                    Key(VirtualKeys.LShift);
                    Press(c);
                    Key(VirtualKeys.LShift, up: true);
                    Host.PhysicalShift = false;
                }
                else if (c == ' ') Press(VirtualKeys.Space);
                else if (c == '\n') Press(VirtualKeys.Return);
                else if (c == '\b') Press(VirtualKeys.Back);
                else if (FakeHost.JisKeys.TryGetValue(c, out var key) && key.Shift)
                {
                    Host.PhysicalShift = !Gate.IsCaptured;
                    Key(VirtualKeys.LShift);
                    Press(key.Vk);
                    Key(VirtualKeys.LShift, up: true);
                    Host.PhysicalShift = false;
                }
                else if (FakeHost.JisKeys.TryGetValue(c, out key)) Press(key.Vk);
                else Press(char.ToUpperInvariant(c));
            }
        }

        public string? Showing => Host.View?.Text;
    }

    [Test]
    public static void Romaji_IsShownAsKanaAndCommittedWithEnter()
    {
        var k = new Keyboard();
        k.Type("konnnichiha");
        Assert.Equal("こんにちは", k.Showing, "未確定の間は変換ボックスにかなで表示");
        Assert.Equal(0, k.Host.Output.Count, "Enter まではテキストボックスに何も入らない");
        k.Type("\n");
        Assert.Equal("こんにちは", k.Host.Output.Single());
        Assert.True(k.Showing is null, "確定したら変換ボックスを閉じる");
        Assert.True(!k.Gate.IsCaptured, "確定後はキーを横取りしない");
    }

    [Test]
    public static void Google_IsShownAsEnglish_NotGoogle_Kana()
    {
        var k = new Keyboard();
        k.Type("goog");
        Assert.Equal("goog", k.Showing, "固有名詞 (Google) の先頭と分かった時点で英字");
        k.Type("le");
        Assert.Equal("google", k.Showing, "英単語と分かった時点で英字に切り替わる (ごおｇぇ にならない)");
        k.Type("\n");
        Assert.Equal("google", k.Host.Output.Single());
    }

    [Test]
    public static void OnlyTheEnglishWordBecomesEnglish()
    {
        // 前に日本語があっても、英単語の部分だけが英字になる。
        var cases = new Dictionary<string, string>
        {
            ["kyouhagoogle"] = "きょうはgoogle",
            ["kyouhanikonha"] = "きょうはにこんは",
            ["googlede"] = "googleで",
            ["kyouhagoogledekensaku"] = "きょうはgoogleでけんさく",
            ["githubnipush"] = "githubにpush",
            ["repo"] = "れぽ", // ローマ字としても読める語は日本語のまま (F10 で英字)
            ["koreha"] = "これは",
        };
        foreach (var (typed, expected) in cases)
        {
            var k = new Keyboard();
            k.Type(typed);
            Assert.Equal(expected, k.Showing, $"「{typed}」");
        }
    }

    // 実機で報告: 「やあやあ、私だよ」→「やあやあ、だよ私」、「ところでgoogleって」→「ところでってgoogle」
    [Test]
    public static void ReportedOrderSwaps()
    {
        foreach (var live in new[] { false, true })
        {
            var k = new Keyboard(live);
            k.Type("tokorodegooglette\n");
            Assert.Equal("ところでgoogleって", string.Concat(k.Host.Output), $"live={live}");

            k = new Keyboard(live);
            k.Type("yaayaa,watashidayo\n");
            Assert.Equal("やあやあ、わたしだよ", string.Concat(k.Host.Output), $"live={live}");
        }
    }

    [Test]
    public static void Clauses_SokuonAfterKatakanaIsKatakana()
    {
        // 報告: すぱいだーまっ → スパイダーマっ。カタカナの語の後ろの「っ」の文節はカタカナの「ッ」にする。
        var k = new Keyboard();
        k.Type("supaida-maxtu ");
        Assert.Equal("スパイダーマ|ッ", string.Join("|", k.Host.View!.Clauses!));
    }

    [Test]
    public static void Clauses_InterjectionE_AndGachi()
    {
        // summary.json: 文頭の「え、」が 得、、がちで が 勝ちで になっていた (勝ち の読みは かち)
        foreach (var (typed, expected) in new[] { ("e,shiran ", "え|、|知らん"), ("ekaku ", "絵|描く"), ("gachideyabai ", "ガチで|ヤバい"),
            // 英単語 + する: push|下、commit|死体 になっていた。英単語の無いところ (した = 下) は変換エンジンのまま
            ("pushshita ", "push|した"), ("commitshitai ", "commit|したい"), ("shita ", "下") })
        {
            var k = new Keyboard();
            k.Type(typed);
            Assert.Equal(expected, string.Join("|", k.Host.View!.Clauses!), typed);
        }
    }

    public static void Clauses_SelectWithArrowsAndConvertEach()
    {
        var k = new Keyboard();
        k.Type("tanniwotoru ");
        Assert.Equal("単位を|取る", string.Join("|", k.Host.View!.Clauses!), "Space で文節に区切って変換");
        Assert.Equal(0, k.Host.View.SelectedClause, "最初は先頭の文節を選択");
        k.Press(VirtualKeys.Right);
        Assert.Equal(1, k.Host.View.SelectedClause, "→ で次の文節");
        k.Type(" ");
        Assert.Equal("単位を|とる", string.Join("|", k.Host.View.Clauses!), "Space で選択中の文節だけ次の候補");
        k.Press(VirtualKeys.Left);
        Assert.Equal(0, k.Host.View.SelectedClause, "← で前の文節");
        k.Type("\n");
        Assert.Equal("単位をとる", k.Host.Output.Single());
    }

    [Test]
    public static void Arrows_BeforeSpace_EnterClauseSelection()
    {
        // 報告: 矢印キーで文節を選ぼうとすると確定してしまう → 変換前でも矢印で文節の選択に入る。
        var k = new Keyboard();
        k.Type("tanniwotoru");
        k.Press(VirtualKeys.Left);
        Assert.True(k.Host.View!.Converting, "← で文節の選択に入る (確定しない)");
        Assert.Equal(0, k.Host.Output.Count);
        Assert.Equal(1, k.Host.View.SelectedClause, "← なら最後の文節から");
        k.Press(VirtualKeys.Left);
        Assert.Equal(0, k.Host.View.SelectedClause);
        k.Type(" ");
        k.Type("\n");
        Assert.Equal("たんいを取る", k.Host.Output.Single(), "選んだ文節だけ候補が変わる");
    }

    [Test]
    public static void Candidates_IncludeHomophonesFromDictionary()
    {
        // 報告: とうてん が 当店 しか出ない。
        var k = new Keyboard();
        k.Type("toutenn ");
        Assert.True(k.Host.View!.Candidates.Contains("読点"), string.Join(",", k.Host.View.Candidates));
        k = new Keyboard();
        k.Type("hashiwo ");
        Assert.True(k.Host.View!.Candidates.Contains("箸を") && k.Host.View.Candidates.Contains("端を"), "助詞付きの文節でも同音異義語を出す: " + string.Join(",", k.Host.View.Candidates));
        var extra = CandidateDictionary.Load(null);
        Assert.True(extra.Lookup("おんげー").Contains("音ゲー"), "おんげー → 音ゲー");
        Assert.True(extra.Lookup("りあとも").Contains("リア友"), "りあとも → リア友");
        Assert.True(extra.Lookup("すこんぶ").Contains("すこん部"), "すこんぶ → すこん部");
        Assert.True(extra.Lookup("かちで").Contains("ガチで"), "かちで → ガチで");
        Assert.True(extra.Lookup("いんゆめ").Contains("淫夢"), "いんゆめ → 淫夢");
        Assert.True(extra.Lookup("いん").Contains("淫"), "文節が分かれた いん + ゆめ でも 淫夢 にできる");
    }

    [Test]
    public static void CorpusContextRules_PreferChatFormsWhenCued()
    {
        var rules = ContextRules.Load(null);
        Assert.Equal("ガチで", rules.Choose("かちで", "終わってる")!);
        Assert.Equal("垢", rules.Choose("あか", "Twitterのアカウント")!);
        Assert.Equal("鯖", rules.Choose("さば", "Discordコミュ")!);
        Assert.Equal("めるちゃん", rules.Choose("めるちゃん", "先輩")!);
        Assert.Equal("ガチで", rules.Choose("かちで", "オンゲーしかしてないから知らん")!);
        Assert.Equal("音ゲーしか", rules.Choose("おんげーしか", "ACの曲を漁ろう")!);
        Assert.Equal("淫夢", rules.Choose("いんゆめ", "R18画像")!);
        Assert.Equal("すこん部", rules.Choose("すこんぶ", "鯖のオーナー")!);
        Assert.Equal("音ゲーしか", rules.Choose("おんげーしか", "してないから")!);
        Assert.Equal("淫", rules.Choose("いん", "夢のr－18画像")!);
        Assert.Equal("鯖", rules.Choose("さば", "すこん部のオーナ人")!);
        Assert.Equal("え", rules.Choose("え", "ほんと")!);
        Assert.Equal("うちの", rules.Choose("うちの", "家族は")!);
        Assert.Equal("ねむ", rules.Choose("ねむ", "先輩に聞いて")!);
        Assert.Equal("いま", rules.Choose("いま", "やるなら")!);
        Assert.Equal("垢", rules.Choose("あか", "ログインできなくなって")!);
        Assert.Equal("なに", rules.Choose("なに", "って")!);
        Assert.Equal("めるちゃん", rules.Choose("めるちゃん", "後輩")!);
    }

    [Test]
    public static void QuestionParticle_KaStaysKana()
    {
        var k = new Keyboard();
        k.Type("hiruka ");
        Assert.Equal("昼|か", string.Join("|", k.Host.View!.Clauses!));
    }

    [Test]
    public static void NAndSmallKanaSpellings()
    {
        var cases = new Dictionary<string, string>
        {
            ["kaxnji"] = "かんじ", ["kannji"] = "かんじ", ["kanji"] = "かんじ", // ん: n / xn / nn
            ["who"] = "うぉ", ["ulo"] = "うぉ", ["uxo"] = "うぉ",
            ["xtu"] = "っ", ["ltsu"] = "っ", ["vu"] = "ゔ", ["thi"] = "てぃ",
        };
        foreach (var (typed, expected) in cases)
        {
            var k = new Keyboard();
            k.Type(typed);
            Assert.Equal(expected, k.Showing, $"「{typed}」");
        }
    }

    [Test]
    public static void Context_FromTextBeforeCaret()
    {
        // 直前に確定済みの文字 (入力欄から読む) が英語なら英語、日本語なら日本語。
        var k = new Keyboard();
        k.Host.PrecedingText = "I love ";
        k.Type("sushi");
        Assert.Equal("sushi", k.Showing);

        k = new Keyboard();
        k.Host.PrecedingText = "今日は";
        k.Type("sushi");
        Assert.Equal("すし", k.Showing);

        k = new Keyboard();
        k.Host.PrecedingText = "hello";
        k.Type(",");
        Assert.Equal(",", k.Showing, "英文の続きのカンマは半角");

        k = new Keyboard();
        k.Host.PrecedingText = "今日は";
        k.Type(",");
        Assert.Equal("、", k.Showing, "日本語の続きなら読点");
    }

    [Test]
    public static void Punctuation_AfterEnglish_FollowedByJapanese()
    {
        // 英単語の後の , . も、すぐ後ろに日本語が続くなら日本語の句読点 (Ok,こんな → Ok、こんな)
        foreach (var (typed, expected) in new[]
        {
            ("Ok,konnnakanjidesu.\n", "Ok、こんなかんじです。"),
            ("OK,sorede.\n", "OK、それで。"),
            ("ok.jaa,mataashita.\n", "ok。じゃあ、またあした。"),
            // 全部英語なら英文の句読点のまま
            ("Hello, world.\n", "Hello, world."),
            // 空白を挟んだら英文の句読点のまま (Space で英語として確定している)
            ("Ok, konnnakanjidesu.\n", "Ok, こんなかんじです。"),
        })
        {
            var k = new Keyboard();
            k.Type(typed);
            Assert.Equal(expected, k.Host.Document, typed.TrimEnd());
        }
    }

    [Test]
    public static void Punctuation_Comma()
    {
        // 設定「，．」: 技術文書向け
        var k = new Keyboard { Punctuation = PunctuationStyle.Comma };
        k.Type("kyouha,.\n");
        Assert.Equal("きょうは，．", string.Join("|", k.Host.Output));
    }

    [Test]
    public static void Punctuation_Comma_AfterDigit()
    {
        // 数字の後の読点化も設定に従う (x64，a)
        var k = new Keyboard { Punctuation = PunctuationStyle.Comma };
        k.Type("x64,a\n");
        Assert.Equal("x64，あ", string.Join("|", k.Host.Output));
    }

    [Test]
    public static void Punctuation_CommaJapanese()
    {
        var k = new Keyboard { Punctuation = PunctuationStyle.CommaJapanese };
        k.Type("kyouha,.\n");
        Assert.Equal("きょうは，。", string.Join("|", k.Host.Output));
    }

    [Test]
    public static void HalfWidthExclamation()
    {
        // 設定「記号を全角にする」OFF: ! ? ~ を打ったままの半角で入れる (既定の全角は ConversionKeys 側で確認済み)
        var k = new Keyboard { FullWidthSymbols = false };
        k.Type("kyouha!?~\n");
        Assert.Equal("きょうは!?~", string.Join("|", k.Host.Output));
        var full = new Keyboard();
        full.Type("kyouha!?~\n");
        Assert.Equal("きょうは！？～", string.Join("|", full.Host.Output));
    }

    [Test]
    public static void Comma_AtEnd_DoesNotBreakFollowingInput()
    {
        // 報告: 読点を最後に打つと英数に固定される。
        var k = new Keyboard();
        k.Type("kyouha,\n");
        k.Type("konnnichiha\n");
        Assert.Equal("きょうは、|こんにちは", string.Join("|", k.Host.Output));
    }

    [Test]
    public static void DirectMode_RomajiSwitchesBackToJapanese()
    {
        var k = new Keyboard(direct: true);
        k.Type("konnnichiha");
        Assert.True(!k.Direct, "ローマ字だと分かったら日本語入力に戻る");
        Assert.Equal("こんにちは", k.Showing, "判定中に打った英字も変換ボックスに入る");
        Assert.Equal(0, k.Host.Output.Count);
        AssertSentThenErased(k.Host.Events);
    }

    [Test]
    public static void DirectMode_LongVowelDashSwitchesToJapanese()
    {
        // 英数状態で ro-maji と打つと、- の時点で英語として出してしまっていた (ローマ字 が打てない)。
        var k = new Keyboard(direct: true);
        k.Type("ro-maji");
        Assert.True(!k.Direct, "母音の後の - (長音) で日本語に戻る");
        Assert.Equal("ろーまじ", k.Showing);
        Assert.Equal(2, k.Host.Events.Count(e => e.StartsWith("down:")), "- の前の ro だけ送っていた (- は送らない)");
        AssertSentThenErased(k.Host.Events);

        // 英語の接頭辞 (e-) の後は - の後ろで決める: e-mail・co-op は英語、e-me-ru は日本語
        foreach (var word in new[] { "e-mail ", "co-op ", "re-do " })
        {
            k = new Keyboard(direct: true);
            k.Type(word);
            Assert.True(k.Direct, word + "は英数のまま");
        }
        // 2 文字の英単語 (up) でも、続きがローマ字なら日本語 (upa- → うぱー)。打ち終われば英語のまま。
        k = new Keyboard(direct: true);
        k.Type("upa-");
        Assert.Equal("うぱー", k.Showing ?? "(なし)");
        k = new Keyboard(direct: true);
        k.Type("up ");
        Assert.True(k.Direct, "up + Space は英数のまま");
        k = new Keyboard(direct: true);
        k.Type("e-me-ru");
        Assert.True(!k.Direct, "e-me-ru は日本語に戻る");
        Assert.Equal("えーめーる", k.Showing);
    }

    [Test]
    public static void HyphenatedEnglishWords_StayEnglish()
    {
        // えーmail、こーおp になっていた
        foreach (var (typed, expected) in new[]
        {
            ("e-mail", "e-mail"), ("co-op", "co-op"), ("e-maildeokuru", "e-mailでおくる"), ("x-ray", "x-ray"), ("r-18", "r-18"), ("sub-6", "sub-6"), ("GPT-6.7", "GPT-6.7"),
            ("e-to", "えーと"), ("su-pa-", "すーぱー"), ("o-bun", "おーぶん"),
        })
        {
            var k = new Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, typed);
        }
    }

    [Test]
    public static void Loanwords_OfferLatinSpelling()
    {
        // リナックス を変換しても Linux が出なかった
        var dictionary = CandidateDictionary.Load(null);
        Assert.True(dictionary.Lookup("りなっくす").Contains("Linux"), "リナックス → Linux");
        Assert.True(dictionary.Lookup("りなっくすで").Contains("Linuxで"), "助詞が付いても");
        Assert.True(dictionary.Lookup("じゃばすくりぷと").Contains("JavaScript"), "ジャバスクリプト → JavaScript");
        // JMdict に無い社名 (brands.txt)
        Assert.True(dictionary.Lookup("しゃおみ").Contains("Xiaomi"), "しゃおみ → Xiaomi");
        Assert.True(dictionary.Lookup("でぃすこーど").Contains("Discord"), "ディスコード → Discord");
    }

    [Test]
    public static void Candidates_OtokoNoKo_IncludesMusume()
    {
        // おとこのこ → 男の子 だけでなく 男の娘 も変換候補に出す (既定の候補辞書)
        var dictionary = CandidateDictionary.Load(null);
        var words = dictionary.Lookup("おとこのこ");
        Assert.True(words.Contains("男の子"), "おとこのこ → 男の子");
        Assert.True(words.Contains("男の娘"), "おとこのこ → 男の娘");
        Assert.True(dictionary.Lookup("おとこのこが").Contains("男の娘が"), "助詞が付いても 男の娘が");
    }

    [Test]
    public static void CandidateMeaning_FromTranslations()
    {
        // 候補で止まったら意味を出す (同音異義語の手がかり)
        var translations = TranslationDictionary.Load();
        Assert.Equal("bridge", translations.Meaning("橋"));
        Assert.Equal("chopsticks", translations.Meaning("箸を"), "助詞が付いても");
        Assert.Equal(null, translations.Meaning("はし"), "かなだけの候補には出さない");
        Assert.Equal(null, translations.Meaning("bridge"));
    }

    [Test]
    public static void CandidateMeaning_Japanese()
    {
        // 日本語の意味 (ウィクショナリー)。読みで意味を選ぶ、活用した形・助詞付きでも引ける
        var meanings = MeaningDictionary.Load();
        Assert.True(meanings.Lookup("箸を", "はしを")?.Contains("食器") == true, "箸を → 食器の一種");
        Assert.True(meanings.Lookup("橋", "はし")?.Contains("渡る") == true, "はし と読んだ 橋");
        Assert.True(meanings.Lookup("橋", "きょう")?.StartsWith("中脳") == true, "きょう と読んだ 橋 は脳橋の意味");
        Assert.True(meanings.Lookup("持って", "もって") is not null, "活用した形 (持って → 持つ)");
        Assert.True(meanings.Lookup("美しかった", "うつくしかった") is not null, "い形容詞の活用");
        Assert.Equal(null, meanings.Lookup("はし", "はし"), "かなだけの候補には出さない");
    }

    [Test]
    public static void LaterLongerEnglishWord_WinsOverShorterOne()
    {
        // motteschoolhe が mottes (英単語) + ちょおl + へ になっていた。持って + school + へ。
        var k = new Keyboard();
        k.Type("kameramotteschoolheiku\n");
        Assert.Equal("かめらもってschoolへいく", k.Host.Document);
    }

    [Test]
    public static void SingleCapital_ThenParticle_IsSplit()
    {
        // Anisiyouka が英字のままになっていた (A にしようか)。名前 (Tanaka) は区切らない。
        foreach (var (typed, expected) in new[] { ("Anisiyouka", "Aにしようか"), ("Xgawakaru", "Xがわかる"), ("Tanaka", "Tanaka") })
        {
            var k = new Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, typed);
        }
    }

    [Test]
    public static void CommaAfterDigit_IsTouten_UnlessDigitFollows()
    {
        // x64,arm64 の , は読点 (x64、arm64)。1,000 の , は桁区切りのまま。
        foreach (var (typed, expected) in new[] { ("x64,arm64", "x64、arm64"), ("1,000en", "1,000えん") })
        {
            var k = new Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, typed);
        }
    }

    [Test]
    public static void ShortProperNoun_AfterJapanese_IsJapanese()
    {
        // ある程度は (aruteidoha) の doha を固有名詞 (Doha) として英字にしていた
        var k = new Keyboard();
        k.Type("aruteidoha\n");
        Assert.Equal("あるていどは", k.Host.Document);
        k = new Keyboard();
        k.Type("doha\n");
        Assert.Equal("doha", k.Host.Document, "単独なら固有名詞のまま");
    }

    /// <summary>判定を待たずにアプリへ送った英字 (down) を、ローマ字と分かった時点で同じ数だけ BackSpace で消している。</summary>
    private static void AssertSentThenErased(List<string> events)
    {
        var downs = events.Count(e => e.StartsWith("down:"));
        Assert.True(downs > 0, "判定を待たずに英字を送っている");
        var bs = events.FindIndex(e => e.StartsWith("bs:"));
        Assert.Equal($"bs:{downs}", bs < 0 ? "(なし)" : events[bs], "送った英字を消す");
        Assert.True(events.FindLastIndex(e => e.StartsWith("down:")) < bs, "送った後に消す");
    }

    [Test]
    public static void DirectMode_EnglishIsSentWithoutWaiting()
    {
        // 英数状態で打った英字が、判定 (ローマ字かどうか) が終わるまで出てこなかった。
        // can・game・today のようにローマ字としても読める語は、Space を押すまで丸ごと出なかった。
        foreach (var word in new[] { "can", "game", "today", "hello" })
        {
            var k = new Keyboard(direct: true);
            k.Type(word);
            Assert.Equal(string.Join(",", word.ToCharArray()), Letters(k.Host.Events), $"{word}: 打ったそばから送る (英語と決まった後は素通し)");
            k.Type(" ");
            Assert.True(k.Direct, $"{word}: 英数のまま");
            Assert.True(!k.Host.Events.Any(e => e.StartsWith("bs:")), $"{word}: 英語なら消さない");
        }
    }

    [Test]
    public static void DirectMode_EnglishPassesThroughInOrder()
    {
        var k = new Keyboard(direct: true);
        k.Type("hello world");
        Assert.True(k.Direct, "英語なら英数のまま");
        Assert.True(k.Showing is null, "変換ボックスは出さない");
        Assert.Equal("h,e,l,l,o, ,w,o,r,l,d", Letters(k.Host.Events), "保留した分も素通しした分も、打った順番どおりに届く");
    }

    private static string Letters(List<string> events)
    {
        // 再生した押下 (down) と素通しした押下 (passed) を順に並べる。キーアップは数えない。
        return string.Join(",", events
            .Select(e => e.Split(':'))
            .Where(p => p[0] is "down" or "passed")
            .Select(p => System.Convert.ToInt32(p[1], 16))
            .Select(vk => vk == 0x20 ? " " : char.ToLowerInvariant((char)vk).ToString()));
    }

    [Test]
    public static void DirectMode_IdleReleasesHeldKeys()
    {
        var k = new Keyboard(direct: true);
        k.Type("ka");
        Assert.Equal(2, k.Host.Events.Count(e => e.StartsWith("down:")), "判定中でも打った英字はすぐ送る");
        Assert.True(k.Gate.IsCaptured, "判定中は打鍵を受け取る");
        k.Controller.Tick(k.Now + 1000);
        Assert.Equal(2, k.Host.Events.Count(e => e.StartsWith("down:")), "しばらく打たなければ英語とみなす (送り直さない)");
        Assert.True(!k.Host.Events.Any(e => e.StartsWith("bs:")), "消さない");
        Assert.True(!k.Gate.IsCaptured, "判定をやめたら横取りもやめる");
    }

    [Test]
    public static void ProperNouns_AreEnglishEvenIfRomajiReadable()
    {
        var cases = new Dictionary<string, string>
        {
            ["amazon"] = "amazon", ["adobe"] = "adobe", ["netflix"] = "netflix", ["spotify"] = "spotify",
            ["kyouhaamazondekaimono"] = "きょうはamazonでかいもの",
            ["nihongonobenkyou"] = "にほんごのべんきょう", // 短い名前 (Ben) は文の途中で英語にしない
            ["suzuki"] = "すずき", // 日本語で書くことが多い名前は固有名詞辞書に入れていない
        };
        foreach (var (typed, expected) in cases)
        {
            var k = new Keyboard();
            k.Type(typed);
            Assert.Equal(expected, k.Showing, $"「{typed}」");
        }
    }

    [Test]
    public static void ProperNouns_OfferCanonicalCasing()
    {
        var k = new Keyboard();
        k.Type("iphonede");
        k.Press(VirtualKeys.Left); // 最後の文節 (で)
        k.Press(VirtualKeys.Left); // iphone
        k.Type(" ");
        Assert.Equal("iPhone", k.Host.View!.Clauses![0], "候補に正しい大文字小文字の形がある");
    }

    [Test]
    public static void Ambiguous_UsesBothSides()
    {
        (string? Before, string? After, string Expected)[] cases =
        [
            ("I love ", null, "sushi"),      // 前が英語
            (null, " is great", "sushi"),    // 後ろが英語
            ("I love ", " is great", "sushi"),
            ("今日は", null, "すし"),         // 前が日本語
            (null, "が好き", "すし"),         // 後ろが日本語
            ("I love ", "が好き", "すし"),     // 食い違うときは日本語
            (null, null, "すし"),             // 分からなければ日本語
        ];
        foreach (var (before, after, expected) in cases)
        {
            var k = new Keyboard();
            k.Host.PrecedingText = before;
            k.Host.FollowingText = after;
            k.Type("sushi");
            Assert.Equal(expected, k.Showing, $"前=「{before}」 後ろ=「{after}」");
        }
    }

    [Test]
    public static void Conversion_UsesTextBeforeCaretAsContext()
    {
        var k = new Keyboard();
        k.Host.PrecedingText = "この本は";
        k.Type("atsui ");
        Assert.Equal("この本は", k.Converter.LastContext, "キャレットの前の日本語を変換エンジンに文脈として渡す");
    }

    [Test]
    public static void Conversion_ContextRulesIke()
    {
        var rules = ContextRules.Load(null);
        Assert.Equal("行け", rules.Choose("いけ", "ねむ学校"), "学校いけ → 行け (池にしない)");
        Assert.Equal("行けよ", rules.Choose("いけよ", "早く"), "助詞付きでも 行け");
        Assert.Equal("池", rules.Choose("いけ", "公園の"), "公園なら 池");
        Assert.Equal(null, rules.Choose("いけん", "学校の"), "意見 を 行けん にしない");
        Assert.Equal(null, rules.Choose("いけない", "学校で"), "いけない を 行けない にしない");
    }

    [Test]
    public static void Conversion_ContextRulesPickTheRightWord()
    {
        var k = new Keyboard();
        k.Host.PrecedingText = "今日の気温は";
        k.Type("atsui ");
        Assert.Equal("暑い", k.Host.View!.Clauses![0], "気温 が前にあれば あつい → 暑い");

        k = new Keyboard();
        k.Host.PrecedingText = "財布の";
        k.Type("kawa ");
        Assert.Equal("革", k.Host.View!.Clauses![0], "財布 が前にあれば かわ → 革");

        k = new Keyboard();
        k.Type("kawa ");
        Assert.Equal("川", k.Host.View!.Clauses![0], "手がかりが無ければ変換エンジンの結果");
    }

    [Test]
    public static void Conversion_LearnsUserChoice()
    {
        var history = new ConversionHistory(null);
        var k = new Keyboard(history: history);
        k.Type("hashiwo ");
        var candidates = k.Host.View!.Candidates;
        var index = candidates.ToList().IndexOf("箸を");
        Assert.True(index > 0, string.Join(",", candidates));
        for (var i = 0; i < index; i++) k.Type(" ");
        k.Type("\n");
        Assert.Equal("箸を", k.Host.Output.Single());

        k = new Keyboard(history: history);
        k.Type("hashiwo ");
        Assert.Equal("箸を", k.Host.View!.Clauses![0], "前に選び直した変換が最初の候補になる");
    }

    [Test]
    public static void CapitalI_IsEnglish()
    {
        // 報告: I want の I が「い」になる。
        var k = new Keyboard();
        k.Type("I");
        Assert.Equal("I", k.Showing, "大文字 1 文字でも英語");
        k.Type(" want ");
        Assert.Equal("I want ", k.Host.Document);
    }

    [Test]
    public static void AutoCorrect_JapaneseToEnglishAfterCommit()
    {
        // i を Space で変換して確定した後に、はっきり英語の語 (want) が続いたら「I 」に確定し直す。
        var k = new Keyboard();
        k.Type("i want ");
        Assert.Equal("I want ", k.Host.Document, string.Join("|", k.Host.Events));
    }

    [Test]
    public static void AutoCorrect_EnglishToJapaneseAfterCommit()
    {
        var k = new Keyboard();
        k.Host.PrecedingText = "I love ";
        k.Type("sushi ");
        Assert.Equal("sushi ", k.Host.Document, "前が英語なので英語で確定");
        k.Host.PrecedingText = null;
        k.Type("gasuki\n");
        Assert.Equal("すしがすき", k.Host.Document, "後ろに日本語が続いたので日本語に確定し直す");
    }

    [Test]
    public static void AutoCorrect_NotAfterCaretMoved()
    {
        var k = new Keyboard();
        k.Type("i ");
        k.Type("w");
        k.Controller.ForgetLastCommit(); // Meltype を通らないキー (矢印など) が押された
        k.Type("ant ");
        Assert.True(!k.Host.Events.Any(e => e.StartsWith("bs:")), "キャレットが動いたかもしれないので消さない");
    }

    [Test]
    public static void AutoCorrect_NotWhenCaretIsElsewhere()
    {
        // 「i 」の後にクリックで別の場所へ移った (Meltype はクリックを知らない) 想定。キャレットの前が記録と違うので消さない。
        var k = new Keyboard();
        k.Type("i w\b"); // 「い」が確定し、変換ボックスが空になる
        k.Host.PrecedingText = "ほかの場所";
        Thread.Sleep(1700); // 自分の確定の記録を信じる時間 (OwnCommitTrustMs) を過ぎて、入力欄から読んだ文字を使わせる
        k.Type("want ");
        Assert.True(!k.Host.Events.Any(e => e.StartsWith("bs:")), "別の場所の文字を消さない: " + string.Join("|", k.Host.Events));
    }

    [Test]
    public static void AutoCorrect_StillWorksAfterTrustTimeWhenPrecedingMatches()
    {
        // 1.5 秒たって入力欄から読んだ文字を使うようになっても、記録と一致すれば直す (誤検知しない)。
        var k = new Keyboard();
        k.Type("i w\b");
        k.Host.PrecedingText = "い";
        Thread.Sleep(1700);
        k.Type("want ");
        Assert.True(k.Host.Events.Contains("bs:1"), string.Join("|", k.Host.Events));
    }

    [Test]
    public static void AutoCorrect_TreatsNoBreakSpaceAsSpace()
    {
        // Chrome・Safari は行末の空白を U+00A0 で返すことがある。
        var k = new Keyboard();
        k.Host.PrecedingText = "I love ";
        k.Type("sushi g\b");
        k.Host.PrecedingText = "I love sushi\u00A0";
        Thread.Sleep(1700);
        k.Type("gasuki\n");
        Assert.True(k.Host.Events.Any(e => e.StartsWith("bs:")), string.Join("|", k.Host.Events));
    }

    [Test]
    public static void AutoCorrect_NotWhenUserChoseCandidate()
    {
        var k = new Keyboard();
        k.Type("i  "); // 2 回目の Space で候補を選び直した
        k.Type("want ");
        Assert.True(!k.Host.Events.Any(e => e.StartsWith("bs:")), "自分で選んだ変換は直さない");
    }

    [Test]
    public static void Symbols_StartComposition()
    {
        // 報告: かぎかっこが入力できない。
        var cases = new Dictionary<string, string> { ["[kagi]"] = "「かぎ」", ["-"] = "ー", ["/"] = "/", ["z/"] = "・", ["#"] = "#", ["("] = "(", [")"] = ")", ["]"] = "」", ["@"] = "@", [",,,"] = "...", ["\\"] = "￥", [","] = "、",
            // 報告: Shift で打つ記号が全角で打てない、/ が打てない。英語の中では半角のまま。
            ["$%&"] = "＄％＆", ["kyouha(tenki)"] = "きょうは(てんき)", ["hello@example"] = "hello@example",
            ["tetr.io"] = "tetr.io", ["Wakatte.TV"] = "Wakatte.TV", ["J-core"] = "J-core", ["p-hub"] = "p-hub", ["talk-admin"] = "talk-admin",
            // 報告: ca / cu / co で か く こ
            ["cacuco"] = "かくこ", ["iijane"] = "いいじゃね", ["shoucanshi"] = "しょうかんし", ["vanpaia"] = "ゔぁんぱいあ", ["wyiwye"] = "ゐゑ", ["GPL3.0"] = "GPL3.0", ["3.14desu"] = "3.14です", ["oknotasuku"] = "okのたすく",
        };
        foreach (var (typed, expected) in cases)
        {
            var k = new Keyboard();
            k.Type(typed);
            Assert.Equal(expected, k.Showing, $"「{typed}」");
        }
        var ramen = new Keyboard();
        ramen.Type("ra-men\n");
        Assert.Equal("らーめん", ramen.Host.Document, "日本語の長音の打ち方は変えない");
    }

    [Test]
    public static void BuiltInPhrases_AreSplitOut()
    {
        // 報告: 白馬の王子様 → ハクバノ王子サマ、ばらまいてた愛 → ばらまいて他愛
        var dictionary = new UserDictionary(null);
        Assert.True(dictionary.Split("はくばのおうじさま")?.Any(p => p.Word == "白馬の王子様") == true, "白馬の王子様");
        Assert.True(dictionary.Split("ばらまいてたあい")?.First().Word == "ばらまいてた", "ばらまいてた|あい");
        Assert.Equal(0, dictionary.Count, "同梱の語句はユーザー辞書の一覧に出さない");
    }

    [Test]
    public static void Symbols_HalfWidthCandidate()
    {
        // 報告: Space を続けて押して、記号も半角で出せるように。
        var k = new Keyboard();
        k.Type("( ");
        Assert.True(k.Host.View!.Candidates.Contains("("), "（ の候補に ( がある: " + string.Join(",", k.Host.View.Candidates));
        // # は最初から半角 (Discord のチャンネル名・ハッシュタグ)。Space で全角の ＃ にできる。
        k = new Keyboard();
        k.Type("# ");
        Assert.Equal("#", k.Host.View!.Candidates[0]);
        Assert.True(k.Host.View.Candidates.Contains("＃"), "# の候補に ＃ がある: " + string.Join(",", k.Host.View.Candidates));
    }

    [Test]
    public static void CapitalizedWord_FollowedByJapanese()
    {
        // 報告: 今日はAutoIMEnotesutowosimasu が全部英字になる。大文字で始まる語の後ろの日本語は日本語にする。
        var cases = new Dictionary<string, string>
        {
            ["AutoIMEnotesuto"] = "AutoIMEのてすと", ["Githubdekaku"] = "Githubでかく", ["OKdesu"] = "OKです",
            ["Tokyo"] = "Tokyo", ["Hello"] = "Hello",
            // 報告: I don't → どん't。短縮形は英語。
            ["don't"] = "don't", ["I'm"] = "I'm",
            // 報告: TSユーザー → Tシューざー、issue → いっすえ
            ["TSyu-za-"] = "TSゆーざー", ["issue"] = "issue",
        };
        foreach (var (typed, expected) in cases)
        {
            var k = new Keyboard();
            k.Type(typed);
            Assert.Equal(expected, k.Showing, $"「{typed}」");
        }
    }

    [Test]
    public static void Numbers_StayHalfWidth()
    {
        var k = new Keyboard();
        k.Type("2025");
        Assert.Equal("2025", k.Showing, "数字だけなら半角のまま");
        k.Type(" ");
        // 報告: 1 だけで変換しても ① などが出ない。日本語の中・文の頭では Space で変換して候補を出す (1 番目は半角の数字のまま)。
        Assert.True(k.Host.View is { Converting: true } view && view.Clauses![0] == "2025", "数字だけでも Space で変換する");
        k = new Keyboard();
        k.Host.PrecedingText = "I have ";
        k.Type("2 ");
        Assert.Equal("2 ", k.Host.Output.Single(), "英文の中の数字は Space で確定して空白");

        k = new Keyboard();
        k.Type("3ji");
        Assert.Equal("3じ", k.Showing, "数字の後にかなが続けば日本語");
        Assert.Equal("2025年10月", CompositionController.NormalizeHalfWidth("２０２５年１０月", "2025ねん10がつ"), "変換エンジンが全角にした数字は半角に戻す");
        Assert.Equal("１つ", CompositionController.NormalizeHalfWidth("１つ", "ひとつ"), "読みに半角数字が無ければそのまま");
    }

    [Test]
    public static void Candidates_ExpandWithWindowsCandidates()
    {
        var k = new Keyboard(moreCandidates: reading => reading == "かわ" ? ["川", "皮", "河", "革"] : []);
        k.Type("kawa");
        Assert.True(k.Host.View!.Candidates.Count == 0, "打っている間は候補を取りに行かない");
        // 報告: Space を 1 回押しただけでは候補が一部しか出ず、選び間違えやすい。最初から一覧をすべて出す。
        k.Type(" ");
        Assert.Equal("川", k.Host.View!.Clauses![0]);
        Assert.True(k.Host.View.Candidates.Contains("河") && k.Host.View.Candidates.Contains("革"), string.Join(",", k.Host.View.Candidates));
        k.Type(" ");
        Assert.Equal("皮", k.Host.View!.Clauses![0], "次の候補");
    }

    private static readonly string[] ManyKawa = ["川", "皮", "河", "革", "側", "侍", "可和", "加和", "香和", "佳和", "華", "歌和"];

    [Test]
    public static void Digit_SelectsAndCommits()
    {
        var k = new Keyboard(moreCandidates: reading => reading == "かわ" ? ManyKawa : []);
        k.Type("kawa ");
        k.Press(0x32);
        Assert.Equal("皮", string.Concat(k.Host.Output), "2 を押すと 2 番目の候補で確定する");
        Assert.True(k.Showing is null, "確定したら変換ボックスを閉じる");
    }

    [Test]
    public static void Digit_BeforeConversion_StaysACharacter()
    {
        var k = new Keyboard();
        k.Type("a1");
        Assert.Equal("あ1", k.Showing, "変換前の数字は今までどおり文字として入る");
    }

    [Test]
    public static void Digit_OutOfRange_DoesNothing()
    {
        var k = new Keyboard(moreCandidates: reading => reading == "かわ" ? ["川", "皮"] : []);
        k.Type("kawa ");
        var before = k.Host.View!.Clauses![0];
        var count = k.Host.View.Candidates.Count;
        k.Press(0x30 + count + 1 > 0x39 ? 0x39 : 0x30 + count + 1);
        Assert.True(k.Showing is not null, "候補より大きい番号では確定しない");
        Assert.Equal(before, k.Host.View!.Clauses![0], "候補は動かない");
        Assert.Equal(0, k.Host.Output.Count, "何も出力しない");
    }

    [Test]
    public static void PageDown_MovesNine_AndStopsAtEnds()
    {
        var k = new Keyboard(moreCandidates: reading => reading == "かわ" ? ManyKawa : []);
        k.Type("kawa ");
        var total = k.Host.View!.Candidates.Count;
        Assert.True(total > 10, "テストの前提: 候補が 10 個以上ある");
        k.Press(VirtualKeys.Next);
        Assert.Equal(k.Host.View!.Candidates[9], k.Host.View.Clauses![0], "PageDown で 9 個進む (1 番目から 10 番目)");
        k.Press(VirtualKeys.Next);
        Assert.Equal(k.Host.View!.Candidates[total - 1], k.Host.View.Clauses![0], "末尾で止まる");
        k.Press(VirtualKeys.Prior);
        Assert.Equal(k.Host.View!.Candidates[Math.Max(total - 10, 0)], k.Host.View.Clauses![0], "PageUp で 9 個戻る");
        k.Press(VirtualKeys.Prior);
        k.Press(VirtualKeys.Prior);
        Assert.Equal(k.Host.View!.Candidates[0], k.Host.View.Clauses![0], "先頭で止まる");
    }

    [Test]
    public static void Backspace_ThenRetype_RereadsRomaji()
    {
        // 報告: test の最後の t を消して u → 「てsう」になる。
        var k = new Keyboard();
        k.Type("test\bu");
        Assert.Equal("てす", k.Showing, "読めなかった s も次の文字と合わせて読み直す");
    }

    [Test]
    public static void LetterThenJapanese_IsNotWholeEnglish()
    {
        // 報告: sだけが と打つと sdakega になる。
        var k = new Keyboard();
        k.Type("sdakega");
        Assert.Equal("sだけが", k.Showing);
        Assert.Equal("stackoverflow", new Func<string?>(() => { var s = new Keyboard(); s.Type("stackoverflow"); return s.Showing; })(),
            "続きもローマ字として読めない語は英単語のまま");
    }

    [Test]
    public static void HalfWidth_IsKeptAfterConversion()
    {
        Assert.Equal("sだけが", CompositionController.NormalizeHalfWidth("ｓだけが", "sだけが"));
        Assert.Equal("2025年", CompositionController.NormalizeHalfWidth("２０２５年", "2025ねん"));
        Assert.Equal("ＡＢＣ", CompositionController.NormalizeHalfWidth("ＡＢＣ", "えーびーしー"), "読みに英数字が無ければ全角のまま");
    }

    /// <summary>覚えさせた文節を記録する変換エンジン (Mozc の代わり)。</summary>
    private sealed class LearningConverter : IKanjiConverter, ILearningConverter
    {
        private readonly FakeConverter _inner = new();
        public List<string> Learned { get; } = [];
        public string? Convert(string hiragana) => _inner.Convert(hiragana);
        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) => _inner.ConvertClauses(hiragana, context);

        public void Learn(string? context, IReadOnlyList<ConversionClause> clauses)
        {
            lock (Learned) Learned.Add(string.Join("|", clauses.Select(c => $"{c.Reading}={c.Text}")));
        }
    }

    [Test]
    public static void WiWe_OfferOldKana()
    {
        var k = new Keyboard();
        k.Type("wisuki- ");
        var candidates = k.Host.View!.Candidates;
        Assert.True(candidates.Contains("ゐすきー") && candidates.Contains("ヰスキー"), string.Join(",", candidates));
        k = new Keyboard();
        k.Type("uisuki- ");
        Assert.True(!k.Host.View!.Candidates.Any(c => c.Contains('ゐ') || c.Contains('ヰ')), "ui で打ったら出さない");
        // 日本語のすぐ後ろの we は英単語にしない (こ + we = こうぇ → こゑ)
        foreach (var (typed, old) in new[] { ("kowi ", "こゐ"), ("kowe ", "こゑ") })
        {
            k = new Keyboard();
            k.Type(typed);
            Assert.True(k.Host.View?.Candidates.Contains(old) == true, typed + ": " + string.Join(",", k.Host.View?.Candidates ?? []));
        }
    }

    [Test]
    public static void Translations_AreOfferedAfterJapanese()
    {
        // アイデア: 「ふくざつな」を変換したら complex / complicated も候補に。な が付くなら な形容詞 の訳を先に。
        var dictionary = TranslationDictionary.Parse("川\tn\triver,stream\n複雑\tn\tcomplexity\n複雑\tna\tcomplex,complicated");
        Assert.Equal("complex,complicated,complexity", string.Join(",", dictionary.Lookup("複雑な", "ふくざつな")));
        Assert.Equal("complexity,complex,complicated", string.Join(",", dictionary.Lookup("複雑", "ふくざつ")));
        Assert.Equal(0, dictionary.Lookup("川べり", "かわべり").Count, "語の後ろが助詞などでなければ出さない");

        var history = new TranslationHistory(null);
        IReadOnlyList<string> Convert()
        {
            var k = new Keyboard(translations: dictionary, translationHistory: history);
            k.Type("kawa ");
            return k.Host.View!.Candidates;
        }
        var k = new Keyboard(translations: dictionary, translationHistory: history);
        k.Type("kawa ");
        var view = k.Host.View!;
        var index = view.Candidates.ToList().IndexOf("river");
        Assert.True(index > 0 && view.Candidates[0] == "川", "英訳は日本語の候補の後ろ: " + string.Join(",", view.Candidates));
        Assert.Equal("英訳", view.Notes?[index] ?? "(なし)");
        for (var i = 0; i < index; i++) k.Press(VirtualKeys.Space);
        k.Press(VirtualKeys.Return);
        Assert.Equal("river", k.Host.Output.Single());

        // 学習は弱め: 1 回選んでも 1 番目にはしない。2 回選んだら 2 番目。
        Assert.Equal("川", Convert()[0], "1 回では 1 番目にしない");
        history.Remember("かわ", "river");
        Assert.Equal("川,river", string.Join(",", Convert().Take(2)), "2 回選んだら 2 番目");
    }

    [Test]
    public static void Commit_TeachesTheConverter()
    {
        // Mozc の学習: 確定した文節 (区切りと文字列) を変換エンジンに覚えさせる。
        var converter = new LearningConverter();
        var k = new Keyboard(converter: converter);
        k.Type("tanniwotoru ");
        k.Press(VirtualKeys.Return);
        // 覚えさせるのは裏で行うので、少し待つ。
        for (var i = 0; i < 100 && converter.Learned.Count == 0; i++) Thread.Sleep(10);
        Assert.Equal("たんいを=単位を|とる=取る", converter.Learned.SingleOrDefault() ?? "(なし)");
    }

    [Test]
    public static void UserDictionary_WinsOverEngine()
    {
        var dictionary = new UserDictionary(null);
        Assert.True(dictionary.Add("きごうとう", "記号等") is null, "登録できる");
        Assert.True(dictionary.Add("き", "記") is not null, "1 文字の読みは登録できない");

        var k = new Keyboard(userDictionary: dictionary);
        k.Host.PrecedingText = "文章を「記号等」含め、"; // 変換エンジンが き|ごうとう と区切ってしまう文脈
        k.Type("kigoutoufukume ");
        Assert.Equal("記号等", k.Host.View!.Clauses![0], "登録した読みの部分は、変換エンジンの区切りに関係なく登録した単語");
        Assert.Equal(2, k.Host.View.Clauses.Count, "残り (ふくめ) は別の文節");

        k = new Keyboard(live: true, userDictionary: dictionary);
        k.Type("kigoutou");
        Assert.Equal("記号等", k.Showing, "ライブ変換でも使う");
    }

    [Test]
    public static void UserDictionary_RegisterInvalidatesLiveCache()
    {
        var dictionary = new UserDictionary(null, builtIn: false);
        var k = new Keyboard(live: true, userDictionary: dictionary);
        k.Type("nurupoga");
        Assert.True(k.Showing != "ヌルポ", "登録前は出ない");
        dictionary.Add("ぬるぽが", "ヌルポ");
        k.Type("\bga"); // 同じかなに戻す (キャッシュが残っていれば登録前の表示のまま)
        Assert.Equal("ヌルポ", k.Showing, "登録直後の次の変換に出る");
    }

    [Test]
    public static void UserDictionary_SharedInstance_KeepsBothRegistrations()
    {
        var path = Path.Combine(Path.GetTempPath(), $"meltype-userdict-{Guid.NewGuid():N}.txt");
        try
        {
            var a = UserDictionary.Shared(path);
            var b = UserDictionary.Shared(path);
            Assert.True(ReferenceEquals(a, b), "同じパスは同じインスタンス");
            a.Add("きごうとう", "記号等");
            b.Add("おーとあいえむいー", "Meltype");
            Assert.Equal("Meltype", a.Lookup("おーとあいえむいー").Single());
            var loaded = new UserDictionary(path);
            Assert.Equal("記号等", loaded.Lookup("きごうとう").Single());
            Assert.Equal("Meltype", loaded.Lookup("おーとあいえむいー").Single());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public static void UserDictionary_SavesAndLoads()
    {
        var path = Path.Combine(Path.GetTempPath(), $"meltype-userdict-{Guid.NewGuid():N}.txt");
        try
        {
            var dictionary = new UserDictionary(path);
            dictionary.Add("きごうとう", "記号等");
            dictionary.Add("おーとあいえむいー", "Meltype");
            var loaded = new UserDictionary(path);
            Assert.Equal("記号等", loaded.Lookup("きごうとう").Single());
            Assert.Equal("Meltype", loaded.Lookup("おーとあいえむいー").Single());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public static void Context_DoesNotChangeClauseBoundaries()
    {
        // 報告: 記号等 がどうしても 機強盗 になる (前に「記号等」があると変換エンジンが き|ごうとう と区切る)。
        var k = new Keyboard();
        k.Host.PrecedingText = "文章を「記号等」含め、";
        k.Type("kigoutou ");
        Assert.Equal("記号|等", string.Join("|", k.Host.View!.Clauses!), "文脈で区切りが変わるなら文脈なしの結果を使う");
    }

    [Test]
    public static void ParticleStart_GetsContext()
    {
        // 報告: ○○になってしまう → 「になってしまう」が「担ってしまう」になる。
        var k = new Keyboard();
        k.Type("ninatteshimau ");
        Assert.Equal("これ", k.Converter.LastContext, "前の文脈が無いときは仮の文脈で「に」を助詞として読ませる");

        k = new Keyboard();
        k.Host.PrecedingText = "〇〇";
        k.Type("ninatteshimau ");
        Assert.Equal("〇〇", k.Converter.LastContext, "○ などの記号も日本語の文脈として渡す");

        k = new Keyboard();
        k.Type("hashiru ");
        Assert.True(k.Converter.LastContext is null, "に 以外で始まる読みには仮の文脈を付けない (はしる → は知る を防ぐ)");
    }

    [Test]
    public static void Learning_SkipsSingleKana()
    {
        var history = new ConversionHistory(null);
        var k = new Keyboard(history: history);
        k.Type("ki  \n"); // き を変換して候補を選び直す
        Assert.Equal(0, history.Count, "1 文字の読みは覚えない (き → 記 が きごうとう まで巻き込むため)");
    }

    [Test]
    public static void Clauses_ShiftArrowResizes()
    {
        var k = new Keyboard();
        k.Type("tanniwotoru ");
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Left);
        Assert.Equal("単位|をとる", string.Join("|", k.Host.View!.Clauses!), "Shift+← で文節を 1 文字縮め、残りは次の文節へ");
        k.Press(VirtualKeys.Right);
        Assert.Equal("たんいを|とる", string.Join("|", k.Host.View.Clauses!), "Shift+→ で 1 文字伸ばす");
        k.Key(VirtualKeys.LShift, up: true);
        k.Type("\n");
        Assert.Equal("たんいをとる", k.Host.Output.Single());
    }

    [Test]
    public static void AmbiguousWord_FollowsEnglishContext()
    {
        var k = new Keyboard();
        k.Type("hello ");
        k.Type("sushi");
        Assert.Equal("sushi", k.Showing, "英語の後なら sushi は英語");
        k.Type(" ");
        Assert.Equal("hello |sushi ", string.Join("|", k.Host.Output), "英語なので Space は空白");
    }

    [Test]
    public static void AmbiguousWord_FollowsJapaneseContext()
    {
        var k = new Keyboard();
        k.Type("kyouha\n");
        k.Type("sushi");
        Assert.Equal("すし", k.Showing, "日本語の後なら sushi は日本語");

        k = new Keyboard();
        k.Type("hello ");
        k.Type("sushiga");
        Assert.Equal("すしが", k.Showing, "英語の後でも、後ろに日本語が続けば日本語");
    }

    [Test]
    public static void Space_AfterEnglishWord_InsertsSpace()
    {
        var k = new Keyboard(live: true);
        k.Type("kyouhagoogle ");
        Assert.Equal("今日はgoogle ", k.Host.Output.Single(), "英単語で終わっていれば、変換ではなく確定して空白");
        // 報告: ライブ変換を OFF にしても漢字になる。OFF なら見えているかなのまま確定する。
        k = new Keyboard();
        k.Type("kyouhagoogle ");
        Assert.Equal("きょうはgoogle ", k.Host.Output.Single(), "ライブ変換 OFF なら かなのまま");
    }

    [Test]
    public static void JapaneseSentences_StayJapanese()
    {
        foreach (var sentence in new[]
        {
            "watashihagakuseidesu", "ashitahaamedesu", "kyouhaiitenkidesune", "sumimasenkakuninshimasu",
            "kanojohasushigasuki", "nihongonobenkyou", "arigatougozaimasu", "kaishaniikimasu", "itsumoarigatou",
            "tomodachitoasobu", "shiryouwookurimasu", "mondaihaarimasen",
        })
        {
            var k = new Keyboard();
            k.Type(sentence + "\n"); // Enter で確定 (末尾の n も ん になる)
            Assert.True(k.Host.Output.Single().All(c => !char.IsAsciiLetter(c)), $"「{sentence}」に英字が混ざった: {k.Host.Output.Single()}");
        }
    }

    [Test]
    public static void LiveConversion_ConvertsWhileTyping()
    {
        var k = new Keyboard(live: true);
        k.Type("kyou");
        Assert.Equal("きょう", k.Showing, "短いうちはかなのまま (的外れな漢字を出さない)");
        k.Type("ha");
        Assert.Equal("今日は", k.Showing, "4 文字以上になったら Space を押さなくても漢字で表示");
        k.Type("google");
        Assert.Equal("今日はgoogle", k.Showing, "英語の部分は変換しない");
        k.Type("dekensaku\n");
        Assert.Equal("今日はgoogleで検索", k.Host.Output.Single());
    }

    [Test]
    public static void LiveConversion_SpaceShowsAlternatives()
    {
        var k = new Keyboard(live: true);
        k.Type("kyou ");
        Assert.True(k.Host.View!.Converting, "Space で候補一覧");
        k.Type(" ");
        Assert.Equal("きょう", k.Showing, "次の候補はかなのまま");
        k.Type("\n");
        Assert.Equal("きょう", k.Host.Output.Single());
    }

    [Test]
    public static void Backspace_DeletesOneKanaAtATime()
    {
        var k = new Keyboard();
        k.Type("kyou\b");
        Assert.Equal("きょ", k.Showing, "ローマ字 1 文字ではなく、かな 1 音ずつ消える");
        k.Type("\b");
        Assert.True(k.Showing is null, "きょ も 1 回で消える");

        k.Type("kitte\b");
        Assert.Equal("きっ", k.Showing);

        k = new Keyboard();
        k.Type("ky\b");
        Assert.Equal("k", k.Showing, "まだ音になっていない子音は 1 文字ずつ");

        k = new Keyboard();
        k.Type("google\b");
        Assert.Equal("googl", k.Showing, "英字は 1 文字ずつ");
    }

    [Test]
    public static void EnglishWords_AreShownAsEnglish()
    {
        foreach (var word in new[] { "github", "hello", "typescript", "npm", "localhost", "zoom", "git", "test", "iphone", "stackoverflow" })
        {
            var k = new Keyboard();
            k.Type(word);
            Assert.Equal(word, k.Showing, $"「{word}」");
        }
    }

    [Test]
    public static void JapaneseWords_AreShownAsKana()
    {
        var expected = new Dictionary<string, string>
        {
            ["watashi"] = "わたし", ["arigatou"] = "ありがとう", ["kana"] = "かな", ["sushi"] = "すし", ["tesuto"] = "てすと",
            ["make"] = "まけ", ["ra-men"] = "らーめn", ["nihon"] = "にほn", ["konnichiwq"] = "こんいちwq", ["tanni"] = "たんい",
        };
        foreach (var (typed, kana) in expected)
        {
            var k = new Keyboard();
            k.Type(typed);
            Assert.Equal(kana, k.Showing, $"「{typed}」");
        }
    }

    [Test]
    public static void FinalN_BecomesN_OnCommit()
    {
        var k = new Keyboard();
        k.Type("nihon\n");
        Assert.Equal("にほん", k.Host.Output.Single());
    }

    [Test]
    public static void Capitalized_IsEnglish()
    {
        var k = new Keyboard();
        k.Type("Tokyo");
        Assert.Equal("Tokyo", k.Showing);
    }

    [Test]
    public static void Space_ConvertsThenCyclesCandidates()
    {
        var k = new Keyboard();
        k.Type("kyou ");
        Assert.Equal("今日", k.Showing);
        Assert.True(k.Host.View!.Converting, "変換中");
        k.Type(" ");
        Assert.Equal("きょう", k.Showing, "もう一度 Space で次の候補");
        k.Type(" ");
        Assert.Equal("キョウ", k.Showing);
        k.Type("\n");
        Assert.Equal("キョウ", k.Host.Output.Single());
    }

    [Test]
    public static void DownArrow_WhileConvertingMovesToNextCandidate()
    {
        var k = new Keyboard();
        k.Type("kyou ");
        Assert.Equal("今日", k.Showing);
        k.Press(VirtualKeys.Down);
        Assert.Equal("きょう", k.Showing, "Down は Space と同じく次候補へ進む");
    }

    [Test]
    public static void TypingAfterConversion_CommitsAndStartsNew()
    {
        var k = new Keyboard();
        k.Type("nihongo wo");
        Assert.Equal("日本語", k.Host.Output.Single(), "変換中に次の文字を打つと確定");
        Assert.Equal("を", k.Showing);
    }

    [Test]
    public static void Space_OnEnglish_CommitsWithSpace()
    {
        var k = new Keyboard();
        k.Type("hello world\n");
        Assert.Equal("hello |world", string.Join("|", k.Host.Output));
    }

    [Test]
    public static void Backspace_EditsAndEscapeCancels()
    {
        var k = new Keyboard();
        k.Type("kak\b");
        Assert.Equal("か", k.Showing);
        k.Type("\b\b");
        Assert.True(k.Showing is null && !k.Gate.IsCaptured, "空になったら閉じる");
        Assert.Equal(0, k.Host.Output.Count);

        k.Type("abc");
        k.Press(VirtualKeys.Escape);
        Assert.True(k.Showing is null, "Esc で取り消し");
        Assert.Equal(0, k.Host.Output.Count);
    }

    [Test]
    public static void FunctionKeys_ChangeDisplay()
    {
        var k = new Keyboard();
        k.Type("tesuto");
        k.Press(VirtualKeys.F7);
        Assert.Equal("テスト", k.Showing);
        k.Press(VirtualKeys.F10);
        Assert.Equal("tesuto", k.Showing);
        k.Press(VirtualKeys.OemAuto); // 半角/全角 で日本語⇔英字
        Assert.Equal("てすと", k.Showing);
    }

    [Test]
    public static void HalfWidthKatakana_F8()
    {
        var k = new Keyboard();
        k.Type("kyouha");
        k.Press(VirtualKeys.F8);
        Assert.Equal("ｷｮｳﾊ", k.Showing);
        k.Press(VirtualKeys.F8);
        Assert.Equal("ｷｮｳﾊ", k.Showing); // 何度押しても同じ (トグルではない)
        k.Press(VirtualKeys.Return);
        Assert.Equal("text:ｷｮｳﾊ", string.Join("|", k.Host.Events.Take(1)), "Enter で半角カナのまま確定する");
    }

    [Test]
    public static void HalfWidthKatakana_Dakuten()
    {
        var k = new Keyboard();
        k.Type("gakkou");
        k.Press(VirtualKeys.F8);
        Assert.Equal("ｶﾞｯｺｳ", k.Showing);
    }

    [Test]
    public static void ToHalfWidthKatakana_Table()
    {
        Assert.Equal("ﾊﾟﾝ､｡｢ｰ｣", CompositionText.ToHalfWidthKatakana("ぱん、。「ー」"));
        Assert.Equal("ｳﾞ･ｬ漢a", CompositionText.ToHalfWidthKatakana("ゔ・ゃ漢a"));
    }

    [Test]
    public static void OtherKeys_CommitFirstThenPassThroughInOrder()
    {
        var k = new Keyboard();
        k.Type("kana");
        k.Press(VirtualKeys.Tab);
        Assert.Equal("text:かな|down:09|passed-up:09", string.Join("|", k.Host.Events), "確定後の キーアップ は関所を通らず直接届く");
    }

    [Test]
    public static void CtrlShortcut_CommitsFirst()
    {
        var k = new Keyboard();
        k.Type("kana");
        k.Key(VirtualKeys.LControl);
        k.Press('C');
        k.Key(VirtualKeys.LControl, up: true);
        Assert.Equal("text:かな|down:A2|passed:43|passed-up:43|passed-up:A2", string.Join("|", k.Host.Events), "確定 → Ctrl を送る → 以降は直接アプリへ");
        Assert.True(!k.Gate.IsCaptured, "ショートカットの後は横取りをやめる");
    }

    [Test]
    public static void AfterShortcut_NextWordStillComposes()
    {
        var k = new Keyboard();
        k.Type("kana");
        k.Key(VirtualKeys.LControl);
        k.Press('S');
        k.Key(VirtualKeys.LControl, up: true);
        k.Type("kyou");
        Assert.Equal("きょう", k.Showing, "ショートカットの後も普通に変換ボックスが使える");
    }

    [Test]
    public static void ShiftArrow_KeepsShift()
    {
        var k = new Keyboard();
        k.Type("hello"); // 英字だけのときの矢印はキャレット移動 (日本語なら文節の選択になる)
        k.Key(VirtualKeys.LShift);
        k.Press(0x25); // ←
        k.Key(VirtualKeys.LShift, up: true);
        Assert.Equal("text:hello|down:A0|down:25|passed-up:25|passed-up:A0", string.Join("|", k.Host.Events), "範囲選択のための Shift はアプリに届く");
    }

    [Test]
    public static void MouseClick_CommitsBeforeClicking()
    {
        var k = new Keyboard();
        k.Type("kana");
        Assert.True(k.Gate.OnMouseButton(new MouseButtonEvent(0x201, 10, 20, 0)), "変換中のクリックは一旦止める");
        k.Controller.Pump();
        Assert.Equal("text:かな|mouse:201", string.Join("|", k.Host.Events), "確定してからクリックを再生");
        Assert.True(!k.Gate.OnMouseButton(new MouseButtonEvent(0x202, 10, 20, 0)), "確定後のクリックは素通し");
    }

    [Test]
    public static void KeysBeforeCapture_AreNotSwallowed()
    {
        var k = new Keyboard();
        Assert.True(!k.Key(VirtualKeys.Space), "変換ボックスが無いときの Space は素通し");
        Assert.True(!k.Key(VirtualKeys.Tab), "Tab など文字を生まないキーは素通し");
        Assert.True(k.Key(0x31), "数字は変換ボックスに入る (数字だけなら半角のまま、Space で空白)");
    }

    [Test]
    public static void KeyUpOfKeyPressedBeforeCapture_IsReplayed()
    {
        // Shift を押したまま大文字で打ち始めた場合、Shift の押下は関所の前にアプリへ届いている。
        var k = new Keyboard();
        k.Key('T');
        k.Key(VirtualKeys.LShift, up: true);
        Assert.True(k.Host.Events.Contains("up:A0"), "離したことを伝えないと Shift が押しっぱなしになる");
    }

    [Test]
    public static void EnglishVerb_PlusSuru()
    {
        // Twitter の報告: 「commitしてpushして」が こっみつぃてぷっして になる
        // (英単語の最後の t + s が つ になる / 末尾の pushsite は site も英単語なので全部英字になる)
        foreach (var (typed, expected) in new[]
        {
            ("commitsitepushsite", "commitしてpushして"),
            ("commitsuru", "commitする"),
            ("commitsitekara", "commitしてから"),
            ("pushsite", "pushして"),
            ("gitpushsite", "gitpushして"),
            ("website", "website"),
            ("websitewomiru", "websiteをみる"),
            ("tetsudou", "てつどう"),
        })
        {
            // 実際と同じく、辞書にない英単語 (website) はスペルチェッカーで見る
            Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
            try
            {
                var k = new Keyboard();
                k.Type(typed + "\n");
                Assert.Equal(expected, k.Host.Document, typed);
            }
            finally
            {
                Detector.SpellChecker = null;
            }
        }
    }

    [Test]
    public static void SpaceAroundEnglish_AddsHalfWidthSpaces()
    {
        // Twitter の要望: 半角英語の前後に半角スペース (設定、最初は OFF)
        foreach (var (text, before, after, expected) in new (string, string?, string?, string)[]
        {
            ("今日はGitHubにpushした", null, null, "今日は GitHub に push した"),
            ("iPhone15を買った", null, null, "iPhone15 を買った"),
            ("3時に行く", null, null, "3時に行く"),                  // 数字だけには入れない
            ("GitHubで、pushした。", null, null, "GitHub で、push した。"), // 記号の隣には入れない
            ("C++の本", null, null, "C++ の本"),
            ("Hello.今日は", null, null, "Hello.今日は"),            // 語の端の記号の後ろには入れない
            ("に", "GitHub", null, " に"),                           // 前に確定した英単語に続ける
            ("GitHub", "今日は", "に", " GitHub "),                  // キャレットの前後が日本語
            ("I want to go", null, null, "I want to go"),          // 英文はそのまま
        })
        {
            Assert.Equal(expected, CompositionController.AddSpacesAroundEnglish(text, before, after), text);
        }
    }

    internal static readonly Func<string, IReadOnlyList<string>> OsewaPredictions =
        r => r.StartsWith("おせ", StringComparison.Ordinal) ? ["お世話になります"] : [];

    [Test]
    public static void Prediction_TabEnterCommits()
    {
        var k = new Keyboard(predictions: OsewaPredictions);
        k.Type("osewa");
        Assert.Equal("お世話になります", k.Host.View!.Predictions![0], "打っている間に予測が出る");
        Assert.Equal(-1, k.Host.View.SelectedPrediction, "Tab で入るまでは選ばれていない");
        k.Press(VirtualKeys.Tab);
        Assert.Equal(0, k.Host.View!.SelectedPrediction, "Tab で最初の予測に入る");
        k.Press(VirtualKeys.Return);
        Assert.Equal("お世話になります", k.Host.Document, "Enter で予測が確定する");
        Assert.True(k.Host.View is null, "確定したら変換ボックスは閉じる");
    }

    [Test]
    public static void ShiftEnter_CommitsAndPassesEnter()
    {
        var k = new Keyboard();
        k.Type("aiueo");
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Return);
        k.Key(VirtualKeys.LShift, up: true);
        Assert.Equal("あいうえお", k.Host.Document, "確定する");
        Assert.True(k.Host.Events.Contains("down:0D"), "Enter はアプリに渡す: " + string.Join("|", k.Host.Events));
    }

    [Test]
    public static void Enter_Alone_DoesNotPassEnter()
    {
        var k = new Keyboard();
        k.Type("aiueo");
        k.Press(VirtualKeys.Return);
        Assert.Equal("あいうえお", k.Host.Document);
        Assert.True(!k.Host.Events.Contains("down:0D"), "Enter 単体は確定のみ: " + string.Join("|", k.Host.Events));
    }

    [Test]
    public static void ShiftEnter_WhileConverting_CommitsAndPassesEnter()
    {
        var k = new Keyboard();
        k.Type("aiueo ");
        Assert.True(k.Host.View is { Converting: true }, "Space で変換中");
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Return);
        k.Key(VirtualKeys.LShift, up: true);
        Assert.True(k.Host.Document.Length > 0, "確定する");
        Assert.True(k.Host.Events.Contains("down:0D"), string.Join("|", k.Host.Events));
    }

    [Test]
    public static void ShiftEnter_WhilePredicting_CommitsPredictionAndPassesEnter()
    {
        var k = new Keyboard(predictions: OsewaPredictions);
        k.Type("osewa");
        k.Press(VirtualKeys.Tab);
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Return);
        k.Key(VirtualKeys.LShift, up: true);
        Assert.Equal("お世話になります", k.Host.Document, "予測が確定する");
        Assert.True(k.Host.Events.Contains("down:0D"), string.Join("|", k.Host.Events));
    }

    [Test]
    public static void Prediction_NotForEnglish()
    {
        var k = new Keyboard(predictions: r => ["google.com"]);
        k.Type("google");
        Assert.True(k.Host.View?.Predictions is null or { Count: 0 }, "英語と判定した語には予測を出さない");
    }

    [Test]
    public static void Prediction_TypingLeaves()
    {
        var k = new Keyboard(predictions: OsewaPredictions);
        k.Type("osewa");
        k.Press(VirtualKeys.Tab);
        k.Type("n");
        Assert.Equal(-1, k.Host.View!.SelectedPrediction, "文字を打つと予測から抜ける");
        Assert.Equal("おせわn", k.Showing, "打った文字は入力として続く");
        Assert.Equal("", k.Host.Document, "予測は確定していない");
    }

    [Test]
    public static void Prediction_FromUserDictionary()
    {
        var dictionary = new UserDictionary(null);
        dictionary.Add("きごうとう", "記号等");
        var k = new Keyboard(userDictionary: dictionary);
        k.Type("kigou");
        Assert.True(k.Host.View!.Predictions!.Contains("記号等"), "ユーザー辞書の前方一致が予測に出る");
    }

    [Test]
    public static void Prediction_FromHistoryAndOrder()
    {
        var history = new ConversionHistory(null);
        history.Remember("おせわになっております", "お世話になっております");
        history.Remember("おせちりょうり", "おせち料理");
        var dictionary = new UserDictionary(null);
        dictionary.Add("おせんべい", "お煎餅");
        var k = new Keyboard(history: history, userDictionary: dictionary, predictions: r => ["お世話になります", "お煎餅"]);
        k.Type("ose");
        var predictions = k.Host.View!.Predictions!;
        Assert.Equal("お煎餅", predictions[0], "ユーザー辞書が最優先");
        Assert.Equal("お世話になります", predictions[1], "次が変換エンジン");
        Assert.True(predictions.Contains("お世話になっております") && predictions.Contains("おせち料理"), "変換履歴からも出る");
        Assert.Equal(1, predictions.Count(p => p == "お煎餅"), "重複は除く");
        Assert.True(!predictions.Contains("おせ"), "読みそのままは出さない");
    }

    [Test]
    public static void Prediction_CommitRemembersAndSpaceConverts()
    {
        var history = new ConversionHistory(null);
        var k = new Keyboard(history: history, predictions: OsewaPredictions);
        k.Type("osewa");
        k.Press(VirtualKeys.Tab);
        k.Press(VirtualKeys.Return);
        Assert.Equal("お世話になります", history.Get("おせわ"), "確定した予測は履歴に覚える (使うほど出やすくなる)");

        k = new Keyboard(predictions: OsewaPredictions);
        k.Type("osewa");
        k.Press(VirtualKeys.Tab);
        k.Press(VirtualKeys.Space);
        Assert.True(k.Host.View!.Converting, "予測に入っていても Space は変換");
        Assert.Equal("", k.Host.Document, "予測は確定しない");
    }

    [Test]
    public static void Prediction_OffAndMinLength()
    {
        var k = new Keyboard(predictions: OsewaPredictions) { Prediction = false };
        k.Type("osewa");
        Assert.True(k.Host.View!.Predictions!.Count == 0, "設定で OFF にすると出ない");
        k.Press(VirtualKeys.Tab);
        Assert.True(!k.Host.Document.Contains("お世話"), "OFF のとき Tab で予測を確定しない (今までどおり読みを確定して Tab を通す)");

        k = new Keyboard(predictions: r => ["x"]);
        k.Type("o");
        Assert.True(k.Host.View!.Predictions!.Count == 0, "読みが 1 文字なら出さない");
    }

    [Test]
    public static void Prediction_EscapeReturnsToTyping()
    {
        var k = new Keyboard(predictions: OsewaPredictions);
        k.Type("osewa");
        k.Press(VirtualKeys.Tab);
        k.Press(VirtualKeys.Escape);
        Assert.Equal(-1, k.Host.View!.SelectedPrediction, "Esc で予測から入力に戻る");
        Assert.Equal("おせわ", k.Showing, "入力は消えない");
    }
}
