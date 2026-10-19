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

        /// <summary>クリップボードの文字 (テスト用)。</summary>
        public string? ClipboardText { get; set; }

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
        public ParenthesisWidth Parentheses { get; set; } = ParenthesisWidth.HalfWidth;
        public BraceWidth Braces { get; set; } = BraceWidth.FullWidth;
        public bool Prediction { get; set; } = true;

        /// <summary>変換中の Shift+Enter で確定してキーをアプリに渡すか (CompositionOptions.ShiftEnterNewline)。既定は ON。</summary>
        public bool ShiftEnterNewline { get; set; } = true;

        /// <summary>ATOK スタイルのキー操作か (CompositionOptions.TokMode)。既定は OFF (今までの動作)。</summary>
        public bool TokMode { get; set; }

        /// <summary>入力中の Tab で変換を始めるか (CompositionOptions.TabConversion)。既定は OFF (今までの動作)。</summary>
        public bool TabConversion { get; set; }

        /// <summary>コード系アプリか (CompositionOptions.CodeApp)。既定は OFF (一般アプリ)。</summary>
        public bool CodeApp { get; set; }

        /// <summary>変換後も続けて入力できるか (CompositionOptions.ContinueAfterConversion)。既定は OFF (今までの動作)。</summary>
        public bool Continue { get; set; }

        /// <summary>変換直後の数字を入力として打つか (CompositionOptions.TypeDigitsAfterConversion)。既定は OFF (今までの動作)。</summary>
        public bool TypeDigits { get; set; }

        public bool SuggestEnabled { get; set; } = true;

        /// <summary>計算式を変換したときに結果を候補に出すか (CompositionOptions.CalculatorCandidates)。既定は OFF。</summary>
        public bool CalculatorCandidates { get; set; }

        /// <summary>子音の略入力 (ast → 明日) を候補に出すか (CompositionOptions.ConsonantAbbreviations)。既定は OFF。</summary>
        public bool ConsonantAbbreviations { get; set; }

        /// <summary>英語 / 日本語の学習を前の文脈ごとに分けるか (CompositionOptions.ContextualLearning)。既定は OFF。</summary>
        public bool ContextualLearning { get; set; }

        /// <summary>かな + w を笑いにするか (CompositionOptions.LaughterAfterKana)。既定は OFF。</summary>
        public bool LaughterAfterKana { get; set; }

        /// <summary>クリップボードの文字を候補に出すか (CompositionOptions.ClipboardCandidates)。既定は OFF。</summary>
        public bool ClipboardCandidates { get; set; }

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
            Func<string, IReadOnlyList<string>>? predictions = null, DictionarySuggestions? suggestions = null, int suggestThreshold = 3,
            Func<DateTime>? now = null)
        {
            Direct = direct;
            Controller = new CompositionController(Gate, Detector, converter ?? Converter, Host, new CompositionOptions
            {
                LiveConversion = () => live,
                ContinueAfterConversion = () => Continue,
                ShiftEnterNewline = () => ShiftEnterNewline,
                TokMode = () => TokMode,
                TabConversion = () => TabConversion,
                CodeApp = () => CodeApp,
                TypeDigitsAfterConversion = () => TypeDigits,
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
                CalculatorCandidates = () => CalculatorCandidates,
                ConsonantAbbreviations = () => ConsonantAbbreviations,
                ContextualLearning = () => ContextualLearning,
                LaughterAfterKana = () => LaughterAfterKana,
                ClipboardCandidates = () => ClipboardCandidates,
                RomajiTypos = Typos,
                CorrectTypos = () => CorrectTypos,
                SpaceAroundEnglish = () => SpaceAroundEnglish,
                Punctuation = () => Punctuation,
                FullWidthSymbols = () => FullWidthSymbols,
                Parentheses = () => Parentheses,
                Braces = () => Braces,
                Prediction = () => Prediction,
                Suggestions = suggestions,
                DictionarySuggest = () => SuggestEnabled,
                DictionarySuggestThreshold = () => suggestThreshold,
                Now = now ?? (() => DateTime.Now),
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
    public static void Brackets_WidthFollowsSettings()
    {
        // 既定: ( ) は半角、{ } は全角
        var normal = new Keyboard();
        normal.Type("kyouha(ha)\n");
        Assert.Equal("きょうは(は)", string.Join("|", normal.Host.Output));
        var braces = new Keyboard();
        braces.Type("kyouha{ha}\n");
        Assert.Equal("きょうは｛は｝", string.Join("|", braces.Host.Output));

        // 設定: ( ) を全角に
        var full = new Keyboard { Parentheses = ParenthesisWidth.FullWidth };
        full.Type("kyouha(ha)\n");
        Assert.Equal("きょうは（は）", string.Join("|", full.Host.Output));

        // 設定: { } を半角に
        var half = new Keyboard { Braces = BraceWidth.HalfWidth };
        half.Type("kyouha{ha}\n");
        Assert.Equal("きょうは{は}", string.Join("|", half.Host.Output));
    }

    [Test]
    public static void Backtick_BeforeEnglish_StaysHalfWidth()
    {
        // 報告 (本家 #243): 日本語の文の中の Markdown のインラインコード `code` で、バッククォートが全角の ｀ になる。
        // 後ろが英語の ( " ' と同じく、後ろが英語なら開きも閉じも半角にする。
        foreach (var (typed, expected) in new[]
        {
            ("kono`config`wo\n", "この`config`を"),
            ("`git`dekakuninn\n", "`git`でかくにん"),
            ("kono`git status`wo\n", "この`git status`を"),
        })
        {
            var k = new Keyboard();
            k.Type(typed);
            Assert.Equal(expected, string.Join("", k.Host.Output), typed);
        }
        // Markdown のコードフェンス (```python) は 3 つとも半角
        var fence = new Keyboard();
        fence.Type("```python\n");
        Assert.Equal("```python", string.Join("", fence.Host.Output), "```python");
        // 後ろが日本語のときは、これまでどおり全角のまま (日本語の文の記号)
        var ja = new Keyboard();
        ja.Type("`konnnichiha`\n");
        Assert.Equal("｀こんにちは｀", string.Join("", ja.Host.Output), "日本語に挟まれたバッククォートは半角にしない");
    }

    [Test]
    public static void ZArrows_HJKL()
    {
        // 提案 (本家 #234): Apple の日本語入力と同じ z + h/j/k/l で ← ↓ ↑ →。変換ボックスの先頭だけ。
        foreach (var (typed, expected) in new[] { ("zh", "←"), ("zj", "↓"), ("zk", "↑"), ("zl", "→"), ("zhzl", "←→"), ("zhzjzkzl", "←↓↑→") })
        {
            var k = new Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, string.Join("", k.Host.Output), typed);
        }
        // 矢印の後に Space・記号を挟めば、そのまま続けて日本語を打てる
        var after = new Keyboard();
        after.Type("zh!\n");
        Assert.Equal("←！", string.Join("", after.Host.Output), "zh!");
        // 既存の z + 記号は変わらない
        var sym = new Keyboard();
        sym.Type("z/\n");
        Assert.Equal("・", string.Join("", sym.Host.Output), "z/");
    }

    /// <summary>macOS のスペルチェッカーのように、zh を英単語と答える単語チェッカー (ほかは同梱の単語リスト)。</summary>
    private sealed class ZhIsWordChecker : Detection.IWordChecker
    {
        public bool IsAvailable => true;
        public bool IsWord(string lower) => lower is "zh" or "ZH" || Detection.BuiltInWordChecker.Shared.IsWord(lower);
    }

    [Test]
    public static void ZArrows_WorkEvenIfSpellCheckerKnowsZh()
    {
        // Mac 実機の報告: macOS のスペルチェッカーが zh を英単語と答えるため、zh だけ矢印にならなかった (zj・zk・zl は矢印になる)。
        Detector.SpellChecker = new ZhIsWordChecker();
        try
        {
            foreach (var (typed, expected) in new[] { ("zh", "←"), ("zj", "↓"), ("zhzl", "←→"), ("zhzhzh", "←←←"), ("zhang", "zhang"), ("zlib", "zlib") })
            {
                var k = new Keyboard();
                k.Type(typed + "\n");
                Assert.Equal(expected, string.Join("", k.Host.Output), typed);
            }
            // 入力中の表示も矢印 (Enter の前)
            var typing = new Keyboard();
            typing.Type("zh");
            Assert.Equal("←", typing.Showing, "入力中の表示");
        }
        finally
        {
            Detector.SpellChecker = null;
        }
    }

    [Test]
    public static void ZArrows_DoNotBreakEnglishOrTypoCorrection()
    {
        // 変換ボックスの先頭だけが矢印。文頭の英語 (zhang・zlib・zkSync) は元の綴りに戻る。
        // 文の途中・記号の後ろ・語の途中の zl・zk・zh、z の打ち間違い (zu → zj, zi → zk, zo → zl) は、これまでどおり。
        foreach (var (typed, expected) in new[]
        {
            ("zhang", "zhang"), ("Zhang", "Zhang"), ("zlib", "zlib"), ("Zlatan", "Zlatan"), ("zkSync", "zkSync"), ("Zhou", "Zhou"), ("ZK", "ZK"),
            ("kyouhazlibwotsukau", "きょうはzlibをつかう"), ("kyouhazhang", "きょうはzhang"),
            ("(zlib)", "(zlib)"), ("\"zkp\"", "”zkp”"), ("foo.zlib", "ふぉお。zlib"),
            ("buzzkill", "buzzkill"), ("bzl", "bzl"), ("quizlet", "quizlet"), ("wizkid", "wizkid"),
            ("mizjwonomu", "みずをのむ"), ("mazjnoke", "まずのけ"), ("hazkimeru", "はじめる"), ("kozlsite", "こぞして"),
        })
        {
            // 実際と同じく、辞書にない英単語 (buzzkill) はスペルチェッカーで見る
            Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
            try
            {
                var k = new Keyboard();
                k.Type(typed + "\n");
                Assert.Equal(expected, string.Join("", k.Host.Output), typed);
            }
            finally
            {
                Detector.SpellChecker = null;
            }
        }
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
            ("e-mail", "e-mail"), ("co-op", "co-op"), ("e-maildeokuru", "e-mailでおくる"), ("x-ray", "x-ray"), ("lo-fi", "lo-fi"), ("Lo-fi", "Lo-fi"), ("sci-fi", "sci-fi"), ("lo-fiwokiku", "lo-fiをきく"), ("r-18", "r-18"), ("sub-6", "sub-6"), ("GPT-6.7", "GPT-6.7"),
            ("e-to", "えーと"), ("su-pa-", "すーぱー"), ("o-bun", "おーぶん"),
        })
        {
            var k = new Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, typed);
        }
    }

    [Test]
    public static void Hyphen_AtStartOrAfterDigit_StaysHyphen()
    {
        // 箇条書きの - kyou wa が ーきょうわ、10-ji が 10ーじ、03-1234-5678desu が 03ー1234ー5678です になっていた
        foreach (var (typed, expected) in new[]
        {
            ("- kyou wa", "- 今日わ"), ("10-ji", "10-じ"), ("10-ji ni kaigi desu", "10-じにかいぎです"),
            ("03-1234-5678desu", "03-1234-5678です"), ("2026-10-12", "2026-10-12"), ("03--5", "03--5"),
            // 長音・英語の区切りは今までどおり
            ("ra-men", "らーめん"), ("su-pa-", "すーぱー"), ("e-mail", "e-mail"), ("ko-hi-2hai", "こーひー2はい"),
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
    public static void LoneCapitalX_IsNotSmallKanaPrefix()
    {
        // Q9: SNS の X。語の頭の大文字の X 1 文字は、次の n と合わせて xn = ん にしない (Xnotoukouha が んおとうこうは になっていた)。
        // 小文字の x (xtu = っ・xn = ん) と、英字の後ろの X は今までどおり。
        foreach (var (typed, expected) in new[]
        {
            ("Xnotoukouha", "Xのとうこうは"), ("Xno", "Xの"), ("Xni", "Xに"), ("Xnitoukousita", "Xにとうこうした"),
            ("kyouhaXnitoukousita", "きょうはXにとうこうした"), ("Xnoakaunto", "Xのあかうんと"), ("Xgawakaru", "Xがわかる"),
            ("xtu", "っ"), ("xnotoukou", "んおとうこう"), ("Xbox", "Xbox"), ("Xboxnogemu", "Xboxのげむ"),
            // X の後ろだけは 2 音以上の助詞でも区切る。ほかの大文字 1 文字 (名前の頭) には広げない。や は助詞にしない
            ("Xnado", "Xなど"), ("Xkara", "Xから"), ("Xnadodehappyousuru", "Xなどではっぴょうする"), ("Xshika", "Xしか"), ("Xsika", "Xしか"),
            ("Xgurai", "Xぐらい"), ("Xkurai", "Xくらい"), ("Xnara", "Xなら"), ("Xnante", "Xなんて"), ("Xyori", "Xより"), ("Xdake", "Xだけ"), ("Xbakari", "Xばかり"), ("Xmadeni", "Xまでに"), ("Xmadedesu", "Xまでです"), ("Xyatte", "Xyatte"),
            ("Ishikawa", "Ishikawa"), ("Imadegawa", "Imadegawa"), ("Ashikaga", "Ashikaga"), ("Bkara", "Bkara"), ("Okara", "Okara"), ("Ayaka", "Ayaka"), ("Xya", "Xya"),
        })
        {
            var k = new Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, typed);
        }
    }

    [Test]
    public static void Main_StaysEnglish_ButMindAndMinecraftStayJapanese()
    {
        // Q11: git のブランチ名 main (ローマ字でも まいん と読める)。mainnima-ji が まいんいまーじ になっていた。
        // ただし まいんど (マインド)・まいんくらふと (マインクラフト) のように、続く 1 音とで日本語の語の頭になるときは今までどおり日本語。
        foreach (var (typed, expected) in new[]
        {
            ("mainnima-jisitehosii", "mainにまーじしてほしい"), ("mainni", "mainに"), ("maindema-ji", "mainでまーじ"), ("mainwomiru", "mainをみる"),
            ("mainnokoudo", "mainのこうど"), ("main", "main"), ("mainbranch", "mainbranch"), ("domainni", "domainに"),
            ("maindo", "まいんど"), ("maindosetto", "まいんどせっと"), ("rimaindo", "りまいんど"), ("mainkurafuto", "まいんくらふと"),
            ("mainichi", "まいにち"), ("mainasu", "まいなす"), ("maina-", "まいなー"), ("mainen", "まいねん"),
            // 日本語のすぐ後ろの まいん は日本語 (うまいんです・あまいんだよ・くまいんだ)。まいんち (毎日)、remaindo (リマインド) も日本語
            ("umaindesu", "うまいんです"), ("amaindayo", "あまいんだよ"), ("semainjanai", "せまいんじゃない"), ("kumainda", "くまいんだ"),
            ("mainchi", "まいんち"), ("remaindo", "れまいんど"), ("korehadomainde", "これはどまいんで"),
            // 同じ一覧の、n で終わる語 + n で始まる助詞 (lesson + ni)
            ("lessonnituite", "lessonについて"),
        })
        {
            var k = new Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, typed);
        }
    }

    [Test]
    public static void Main_AfterJapaneseLine_AndLearnedMain()
    {
        // Q11 の実機確認: 前の行が日本語のとき、行の先頭の mainnima-ji が まいんに (→ mine に) になっていた。
        // main を英字で覚えている (F10 など) と、maindo が mainど になっていた (覚えた英字がマインドの守りより先に効く)。
        foreach (var learned in new[] { false, true })
        {
            var memory = new LanguageMemory(null);
            if (learned) memory.Remember("main", english: true, explicitChoice: true);
            Detector.Memory = memory;
            try
            {
                foreach (var (typed, expected) in new[]
                {
                    ("mainnima-jisitehosii", "mainにまーじしてほしい"), ("maindo", "まいんど"), ("mainkurafuto", "まいんくらふと"),
                })
                {
                    foreach (var before in new[] { "", "kyouhaXnitoukousita\n" })
                    {
                        var k = new Keyboard(languages: memory);
                        k.Type(before + typed + "\n");
                        Assert.Equal(before.Length > 0 ? "きょうはXにとうこうした" + expected : expected, k.Host.Document, $"learned={learned} before={before.Length > 0} {typed}");
                    }
                }
                // 同じ入力の中の日本語の後ろ: 覚えていなければ今までどおりかな (うまいんです を守る)。覚えていれば、記号の後ろと同じく覚えた英字
                foreach (var (typed, plain, withLearned) in new[]
                {
                    ("kyouhamainnimerge", "きょうはまいんにmerge", "きょうはmainにmerge"),
                    ("umaindesu", "うまいんです", "うmainです"),
                    ("`main`ni", "｀まいん｀に", "`main`に"),
                })
                {
                    var k = new Keyboard(languages: memory);
                    k.Type(typed + "\n");
                    Assert.Equal(learned ? withLearned : plain, k.Host.Document, $"learned={learned} {typed}");
                }
            }
            finally
            {
                Detector.Memory = null;
            }
        }
    }

    [Test]
    public static void LearnWhileTyping_F6_LearnsTheEnglishWordNotTheWholeInput()
    {
        // 入力中の F6 は、打った英字まるごと (kyouhamergedesu) ではなく、英語だった区間の語 (merge) を日本語として覚える
        var memory = new LanguageMemory(null);
        Detector.Memory = memory;
        try
        {
            var k = new Keyboard(languages: memory);
            k.Type("kyouhamergedesu");
            Assert.Equal("きょうはmergeです", k.Showing, "前提: 自動で merge が英語の区間");
            k.Press(VirtualKeys.F6);
            k.Press(VirtualKeys.Return);
            Assert.True(memory.Get("merge") == false, "merge を日本語として覚える");
            Assert.True(memory.Get("kyouhamergedesu") is null, "打った英字まるごとは覚えない");
            var next = new Keyboard(languages: memory);
            next.Type("kyouhamergedesu");
            Assert.True(!(next.Showing ?? "").Contains("merge", StringComparison.Ordinal), "次は merge の区間が日本語: " + next.Showing);
        }
        finally
        {
            Detector.Memory = null;
        }
    }

    [Test]
    public static void LearnWhileTyping_F6_WithPunctuation_StillLearnsTheEnglishWord()
    {
        // 入力に「、」が混ざっていても、英語だった区間の語 (merge) は覚える (語として覚えられるかは Remember が確かめる。R46-4)
        var memory = new LanguageMemory(null);
        Detector.Memory = memory;
        try
        {
            var k = new Keyboard(languages: memory);
            k.Type("kyouha,mergedesu");
            Assert.True((k.Showing ?? "").Contains("merge", StringComparison.Ordinal), "前提: 自動で merge が英語の区間: " + k.Showing);
            k.Press(VirtualKeys.F6);
            k.Press(VirtualKeys.Return);
            Assert.True(memory.Get("merge") == false, "merge を日本語として覚える");
            Assert.True(memory.Entries().All(e => e.Word == "merge"), "ほかの語は覚えない: " + string.Join(",", memory.Entries().Select(e => e.Word)));
        }
        finally
        {
            Detector.Memory = null;
        }
    }

    [Test]
    public static void LearnWhileTyping_F10_MixedLearnsNothing_SingleJapaneseLearns()
    {
        var memory = new LanguageMemory(null);
        Detector.Memory = memory;
        try
        {
            var mixed = new Keyboard(languages: memory);
            mixed.Type("kyouhamergedesu");
            Assert.Equal("きょうはmergeです", mixed.Showing, "前提: 英語と日本語が混ざる");
            mixed.Press(VirtualKeys.F10);
            mixed.Press(VirtualKeys.Return);
            Assert.Equal(0, memory.Count, "混ざった入力は、どの語を英語にしたいか決められないので覚えない: " + string.Join(",", memory.Entries().Select(e => e.Word)));

            var single = new Keyboard(languages: memory);
            single.Type("nihongo");
            Assert.Equal("にほんご", single.Showing, "前提: 日本語 1 区間");
            single.Press(VirtualKeys.F10);
            single.Press(VirtualKeys.Return);
            var learned = memory.Entries().FirstOrDefault(e => e.Word == "nihongo");
            Assert.True(learned is not null && learned.English, "nihongo を英語として覚える (2 回ルールで有効になるのは 2 回目から)");
        }
        finally
        {
            Detector.Memory = null;
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
    public static void AutoCorrect_NotAfterAnExplicitChoice_F10_F6_Tab()
    {
        // 利用者が F10 / F6 / Tab ではっきり決めた確定は、後ろの語を見て自動で確定し直さない。
        // 対照: 決めていない確定は、後ろの語で書き戻される (どの対照も、実際に消して入れ直す: bs: が出る)
        var plainEnglish = new Keyboard();
        plainEnglish.Host.PrecedingText = "I love ";
        plainEnglish.Type("sushi ");
        Assert.Equal("sushi ", plainEnglish.Host.Document, "前提: 前が英語なので英語で確定");
        plainEnglish.Host.PrecedingText = null;
        plainEnglish.Type("gasuki\n");
        Assert.Equal("すしがすき", plainEnglish.Host.Document, "対照: 後ろに日本語が続いたので書き戻す");
        Assert.True(plainEnglish.Host.Events.Any(e => e.StartsWith("bs:")), "対照: 実際に消して入れ直す: " + string.Join("|", plainEnglish.Host.Events));

        var plainKana = new Keyboard();
        plainKana.Type("i ");
        plainKana.Type("want ");
        Assert.Equal("I want ", plainKana.Host.Document, "対照: 後ろに英語が続いたので書き戻す");
        Assert.True(plainKana.Host.Events.Any(e => e.StartsWith("bs:")), "対照: 実際に消して入れ直す: " + string.Join("|", plainKana.Host.Events));

        // F10 で英字にして確定した sushi は、後ろの日本語で「すし」に書き戻さない
        var f10 = new Keyboard();
        f10.Host.PrecedingText = "I love ";
        f10.Type("sushi");
        f10.Press(VirtualKeys.F10);
        f10.Type(" ");
        Assert.Equal("sushi ", f10.Host.Document, "前提: F10 で英字のまま確定");
        f10.Host.PrecedingText = null;
        f10.Type("gasuki\n");
        Assert.True(!f10.Host.Events.Any(e => e.StartsWith("bs:")), "F10 で決めた確定は書き戻さない: " + string.Join("|", f10.Host.Events));
        Assert.True(f10.Host.Document.StartsWith("sushi ", StringComparison.Ordinal), "sushi のまま: " + f10.Host.Document);

        // F6 でかなにして確定した「い」は、後ろの英語で I に書き戻さない
        var plainEnter = new Keyboard();
        plainEnter.Type("i\n");
        Assert.Equal("い", plainEnter.Host.Document, "前提: 単独の i は Enter でかなのまま確定");
        plainEnter.Type("want ");
        Assert.Equal("Iwant ", plainEnter.Host.Document, "対照: F6 を押さなければ、後ろに英語が続いたので I に書き戻す: " + string.Join("|", plainEnter.Host.Events));
        Assert.True(plainEnter.Host.Events.Any(e => e.StartsWith("bs:")), "対照: 実際に消して入れ直す");
        var f6 = new Keyboard();
        f6.Type("i");
        f6.Press(VirtualKeys.F6);
        f6.Press(VirtualKeys.Return);
        Assert.Equal("い", f6.Host.Document, "前提: F6 でかなのまま確定");
        f6.Type("want ");
        Assert.True(!f6.Host.Events.Any(e => e.StartsWith("bs:")), "F6 で決めた確定は書き戻さない: " + string.Join("|", f6.Host.Events));
        Assert.True(f6.Host.Document.StartsWith("い", StringComparison.Ordinal), "い のまま: " + f6.Host.Document);

        // 判定の強さが手動のとき、Tab で受けた提案 (LevelOverride) の確定も、書き戻さない
        var tab = new Keyboard { Level = Meltype.Config.DetectionLevel.Manual };
        tab.Host.PrecedingText = "I love ";
        tab.Type("sushi");
        Assert.Equal("すし", tab.Showing, "前提: 手動の強さでは日本語のまま (英字は Tab の提案)");
        tab.Press(VirtualKeys.Tab);
        Assert.Equal("sushi", tab.Showing, "前提: Tab で提案どおり英字に");
        tab.Type(" ");
        Assert.Equal("sushi ", tab.Host.Document, "前提: Tab で英字のまま確定");
        tab.Host.PrecedingText = null;
        tab.Type("gasuki\n");
        Assert.True(!tab.Host.Events.Any(e => e.StartsWith("bs:")), "Tab で決めた確定は書き戻さない: " + string.Join("|", tab.Host.Events));
        Assert.True(tab.Host.Document.StartsWith("sushi ", StringComparison.Ordinal), "sushi のまま: " + tab.Host.Document);
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
    public static void AutoCorrect_EnglishPronounAfterEnglishFunctionWord_IsNotRewrittenToJapanese()
    {
        // 報告 (本家 #265): Are you susi? で、英語の you を「よう」に確定し直していた。
        // 英語の機能語 (Are・Thank・How are) のすぐ後ろで単独で確定した you / are / we / me / she は、後ろに日本語が続いても日本語に直さない。
        foreach (var (typed, expected) in new[]
        {
            ("Are you susi?\n", "Are you すし？"),
            ("Are you ningen?\n", "Are you にんげん？"),
            ("Do you ningen\n", "Do you にんげん"),
            ("Thank you ningen\n", "Thank you にんげん"),
            ("How are you ningen\n", "How are you にんげん"),
        })
        {
            var k = new Keyboard();
            k.Type(typed);
            Assert.Equal(expected, k.Host.Document, typed);
        }
        // 直前が名詞・記号のとき (iPhone 用・PC 用の「よう」、Hello, you) と、文頭の you は、これまでどおり日本語に確定し直す
        foreach (var (typed, expected) in new[]
        {
            ("you ningen\n", "ようにんげん"),
            ("iPhone you nokesu\n", "iPhone ようのけす"),
            ("PC you nokeibu\n", "PC ようのけいぶ"),
            ("Windows you nogamen\n", "Windows ようのがめん"),
            ("Hello, you ningen\n", "Hello, ようにんげん"),
        })
        {
            var k = new Keyboard();
            k.Type(typed);
            Assert.Equal(expected, k.Host.Document, typed);
        }
    }

    [Test]
    public static void AutoCorrect_ShortEnglishWordInsideEnglishSentence_IsNotRewrittenToJapanese()
    {
        // 本家 #317: 英文の途中の go・so・do・to (ローマ字では ご・そ・ど・と) は、後ろに日本語 (すし) が続いても確定し直さない。
        // 英語の機能語 (want to・is・I・will …) のすぐ後ろのときだけ。
        foreach (var (typed, expected) in new[]
        {
            ("I want to go susi\n", "I want to go すし"),
            ("It is so susi\n", "It is so すし"),
            ("I have to do ningen?\n", "I have to do にんげん？"),
            ("I have to be ningen\n", "I have to be にんげん"),
            ("Thank you so susi?\n", "Thank you so すし？"),
            ("I will go nihon\n", "I will go にほん"),
            ("We can see susi\n", "We can see すし"),
            ("Please take susi\n", "Please take すし"),
            ("Today I go susi\n", "Today I go すし"),
            ("Please come here susi\n", "Please come here すし"),
            ("Maybe I'll go susi\n", "Maybe I'll go すし"),
            // to・no・he の前が、文の途中にしか来ない語 (not・please・also) のときは英語のまま
            ("I promise not to susi\n", "I promise not to すし"),
            ("We also to susi\n", "We also to すし"),
            // 縮約形 (n't 以外: 're 've 'm 'd と let's it's that's) と、その後ろの no・to (査読 R11)
            ("Let's go susi\n", "Let's go すし"), ("We're here susi\n", "We're here すし"), ("I'd see susi\n", "I'd see すし"),
            ("We've seen susi\n", "We've seen すし"), ("It's so susi\n", "It's so すし"), ("That's no susi\n", "That's no すし"), ("I'm to susi\n", "I'm to すし"),
            ("You can't see susi\n", "You can't see すし"),
        })
        {
            var k = new Keyboard();
            k.Type(typed);
            Assert.Equal(expected, k.Host.Document, typed);
            Assert.True(!k.Host.Events.Any(e => e.StartsWith("bs:") || e.StartsWith("replace")), typed + " 消さない: " + string.Join(" ", k.Host.Events));
        }
    }

    [Test]
    public static void AutoCorrect_ShortEnglishWordAfterNounOrAtStart_StillRewrittenToJapanese()
    {
        // 上の規則を、名詞のあとの助詞 (New York の・Google Chrome と)・文頭・日本語由来の語には広げない。今までどおり日本語に確定し直す。
        foreach (var (typed, expected) in new[]
        {
            ("New York no tenki\n", "New York のてんき"),
            ("Google Chrome to ringo wo taberu\n", "Google Chrome とりんごをたべる"),
            ("Visual Studio Code no settei\n", "Visual Studio Code のせってい"),
            ("dark mode no settei\n", "dark mode のせってい"),
            ("pull request to tsukau\n", "pull request とつかう"),
            ("Hello World so tsukau\n", "Hello World そつかう"),
            // 査読 R10: 英語の題名・名詞の末尾 (Go・Be・Live・where・if・for・and・click・look) の後ろの no・to・he も助詞のつもり
            ("Let It Go no uta\n", "Let It Go のうた"),
            ("Let It Be no kashi\n", "Let It Be のかし"),
            ("YouTube Live no haishin\n", "YouTube Live のはいしん"),
            ("SQL where no baai\n", "SQL where のばあい"),
            ("Excel if no kinou\n", "Excel if のきのう"),
            ("Python and Go no hikaku\n", "Python and Go のひかく"),
            ("React for no naka\n", "React for のなか"),
            ("Python click no hou ga\n", "Python click のほうが"),
            // (L'Arc・Moody's のような ' を含む名前は縮約形ではない。テスト用キーボードは ' を ’ にするので --repro で確認した)
            // 代名詞の後ろの to・no・he (I love you no koto)
            ("I love you no koto\n", "I love you のこと"),
            // he も助詞 (へ) のつもりのことが多い。直前が記号・日本語に直接つながるときは読まず従来どおり (査読 R11)
            ("Let It Go he ningen\n", "Let It Go へにんげん"), ("Hello world,also to susi\n", "Hello world,also とすし"),
            ("go susi\n", "ごすし"),
            ("to ringo\n", "とりんご"),
            ("I want sushi gasuki\n", "I want すしがすき"),
            ("I like ramen daisuki\n", "I like らめんだいすき"),
            ("I like sake daisuki\n", "I like さけだいすき"),
            // 代名詞の後ろの no・to・he は助詞のつもり (歌詞のように I love you のこと と打つ)。so・do は英語の so・do なので上の規則で英語のまま
            ("I love you no koto\n", "I love you のこと"),
            ("I miss you to issho ni\n", "I miss you といっしょに"),
            ("Thank you no kokoro\n", "Thank you のこころ"),
            // 1 文字の i は、英語の文の中でも今までどおり (小文字の i のまま残しても I にはならないので、直す利点が無い)
            ("I think i ninaru\n", "I think いになる"),
        })
        {
            var k = new Keyboard();
            k.Type(typed);
            Assert.Equal(expected, k.Host.Document, typed);
        }
    }

    [Test]
    public static void JapaneseOriginWords_AreReadFromTheEnglishDictionarySection()
    {
        // 「日本語由来」の段落 (sushi・ramen・anime …) だけが対象。技術用語や go・so のような短い語は含めない。
        foreach (var word in new[] { "sushi", "ramen", "anime", "sake", "kimono", "gundam" }) Assert.True(CompositionDetector.IsJapaneseOriginWord(word), word);
        foreach (var word in new[] { "go", "so", "do", "to", "you", "python", "github", "want" }) Assert.True(!CompositionDetector.IsJapaneseOriginWord(word), word);
    }

    [Test]
    public static void AutoCorrect_ShortEnglishWordInsideEnglishSentence_NeedsKnownCaretText()
    {
        // キャレットの前の文字が分からない (自分の確定の記録を信じる時間を過ぎ、入力欄からも読めない) ときは、今までどおり。
        var k = new Keyboard();
        k.Host.PrecedingText = "I want to ";
        k.Type("go ");
        Assert.Equal("go ", k.Host.Document, "前が英語なので英語で確定");
        k.Host.PrecedingText = null;
        k.Type("susi\n");
        Assert.Equal("ごすし", k.Host.Document, "自分の記録 (go ) だけでは、その前の語 (to) が分からないので機能語かどうか見られない: 従来どおり日本語に直す");
    }

    [Test]
    public static void AutoCorrect_ShortEnglishWordInsideEnglishSentence_AtTheStartOfALine()
    {
        // 実機のトレース (2026-10-11): 2 行目以降の文は、直前の文字が「改行」で、語の前を空白か文頭のときだけ読む規則では読めず、
        // We're here susi の here が へれ に直った (1 行目だけを打つテスト用キーボードでは出なかった)。行頭 (改行・タブの直後) も文頭と同じに読む。
        // 入力欄は ’ (U+2019) にすることがあるので、We’re も試す。自分の確定の記録を信じる 1.5 秒を過ぎた後 (ゆっくり打った場合) の、入力欄から読む経路を試す。
        foreach (var (before, first, second, typed, expected) in new[]
        {
            ("Hi there\nWe're ", "here ", "here ", "susi\n", "here すし"),
            ("Hi there\nWe\u2019re ", "here ", "here ", "susi\n", "here すし"),
            ("Hi there\r\nI want to ", "go ", "go ", "susi\n", "go すし"),
            // 代名詞 (P27): 行頭の Are の後ろの you
            ("Hi there\nAre ", "you ", "you ", "susi\n", "you すし"),
        })
        {
            var k = new Keyboard();
            k.Host.PrecedingText = before;
            k.Type(first);
            k.Host.PrecedingText = before + second;
            Thread.Sleep(1700); // OwnCommitTrustMs
            k.Type(typed);
            Assert.Equal(expected, k.Host.Document, before.Replace("\n", "\\n") + first + typed.Trim());
        }
        // 直前が日本語・記号に直接つながる語 (これはplease) は、今までどおり読まない
        var jp = new Keyboard();
        jp.Host.PrecedingText = "これはwe ";
        jp.Type("go ");
        jp.Host.PrecedingText = "これはwe go ";
        jp.Type("susi\n");
        Assert.Equal("ごすし", jp.Host.Document, "日本語に直接つながる we は機能語と見ない: 従来どおり");
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
        var cases = new Dictionary<string, string> { ["[kagi]"] = "「かぎ」", ["-"] = "ー", ["/"] = "/", ["z/"] = "・", ["#"] = "#", ["("] = "(", [")"] = ")", ["]"] = "」", ["@"] = "@", [",,,"] = "...", ["\\"] = "\\", [","] = "、",
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
    public static void DotSuffix_FileExtensionsStayEnglish()
    {
        // 本家 #260・#254 (本家 PR #267 のテストを移植): 英語の語.拡張子 の拡張子がひらがなになる (MeltypeTip.dっl、Meltype.coんふぃg、Meltype.ご。mod)
        var cases = new Dictionary<string, string>
        {
            ["MeltypeTip.dll"] = "MeltypeTip.dll", ["Meltype.aab"] = "Meltype.aab", ["Meltype.config"] = "Meltype.config", ["Meltype.index"] = "Meltype.index",
            ["Meltype.ini"] = "Meltype.ini", ["Meltype.go.mod"] = "Meltype.go.mod", ["Meltype.abi.json"] = "Meltype.abi.json", ["Meltype.so.1"] = "Meltype.so.1",
            ["Meltype.e57"] = "Meltype.e57", ["Meltype.cargo/config.toml"] = "Meltype.cargo/config.toml", ["Meltype.db-journal"] = "Meltype.db-journal",
            // 表示で英字にした拡張子を、確定でローマ字の打ち間違いとして直さない (amr → あめ、bas → ば)
            ["Meltype.amr"] = "Meltype.amr", ["Meltype.bas"] = "Meltype.bas", ["Meltype.ann"] = "Meltype.ann",
            // 長い名前の途中の助詞・ドメインの頭では分けない
            ["Meltype.gitignore"] = "Meltype.gitignore", ["Meltype.gcno"] = "Meltype.gcno", ["Meltype.automount"] = "Meltype.automount",
            // 拡張子・ドメインの後ろの助詞からは日本語
            ["MeltypeTip.dllwokesu"] = "MeltypeTip.dllをけす", ["setup.exewojikkou"] = "setup.exeをじっこう", ["github.comnipush"] = "github.comにpush",
            ["tetr.iode"] = "tetr.ioで", ["google.comdekensaku"] = "google.comでけんさく",
        };
        foreach (var (typed, expected) in cases)
        {
            var k = new Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, $"「{typed}」");
        }
        // 最後までローマ字として読める語は日本語のまま (ドメインの de を英字にして deす にしていた)
        var desu = new Keyboard();
        desu.Type("Meltype.desu\n");
        Assert.True(desu.Host.Document.EndsWith("です", StringComparison.Ordinal), desu.Host.Document);
    }

    [Test]
    public static void DotSuffix_ParticlesAfterSuffix_AndPlainWordsAndUnchanged()
    {
        // Mac 版の追加: 本家 PR #267 のままだと、今まで日本語にできていた入力が悪化した (査読で発見)
        var cases = new Dictionary<string, string>
        {
            // 数字入りの拡張子・co.jp・TV・2 文字のドメインの後ろの助詞は、日本語にする (mp4wokesu を英字のまま残さない)
            ["Meltype.mp4wokesu"] = "Meltype.mp4をけす", ["Meltype.7zwojikkou"] = "Meltype.7zをじっこう", ["yahoo.co.jpde"] = "yahoo.co.jpで",
            ["amazon.co.jpdekaimono"] = "amazon.co.jpでかいもの", ["Wakatte.TVde"] = "Wakatte.TVで", ["mail.google.comwo"] = "mail.google.comを",
            ["Meltype.pdfnipush"] = "Meltype.pdfにpush", ["Meltype.prprojwokesu"] = "Meltype.prprojをけす", ["Meltype.json.jsonwo"] = "Meltype.json.jsonを",
            // よく使う拡張子の後ろの助詞だけでも日本語にする
            ["readme.mdwo"] = "readme.mdを", ["test.jsonha"] = "test.jsonは", ["index.htmlno"] = "index.htmlの",
            // 普通の英単語は、途中で分けない (vi|で|お にしない)
            ["Meltype.video"] = "Meltype.video", ["cloud.storage"] = "cloud.storage", ["Test.organ"] = "Test.organ", ["git.promote"] = "git.promote",
            // 一覧に無い拡張子・英単語・短い名前・1 文字の拡張子の後ろの助詞も日本語にする (査読で見つかった悪化)
            ["Meltype.plistwokesu"] = "Meltype.plistをけす", ["Meltype.outwo"] = "Meltype.outを", ["Meltype.dbwo"] = "Meltype.dbを",
            ["Meltype.libnipush"] = "Meltype.libにpush", ["Meltype.systemwo"] = "Meltype.systemを", ["user.idga"] = "user.idが",
            ["Meltype.storyboardwo"] = "Meltype.storyboardを", ["Meltype.cwokesu"] = "Meltype.cをけす", ["Meltype.swokesu"] = "Meltype.sをけす",
            ["Meltype.jpnikki"] = "Meltype.jpにっき", ["mail.google.commadekesu"] = "mail.google.comまでけす", ["Meltype.dwgmade"] = "Meltype.dwgまで",
            // 英単語の途中で切らない (gol|de|nga、pho|to|shop)
            ["user.goldenga"] = "user.goldenが", ["Meltype.photoshop"] = "Meltype.photoshop", ["Meltype.smoke"] = "Meltype.smoke",
            // 数字・略語・三点リーダーの後ろの日本語は、今までどおり (No.5kaime、A.I.ha、Hmm...souka)
            ["No.5kaime"] = "No.5かいめ", ["No.1.2bai"] = "No.1.2ばい", ["A.I.nohanashi"] = "A.I.のはなし", ["Hmm...souka"] = "Hmm...そうか",
            // 一覧にあっても日本語としてよく打つ綴り・日本語の打ち間違いは、今までどおり (OK.ii yo、Hello.wakarimashta)
            ["OK.ii yo"] = "OK.いいよ", ["Hello.wakarimashta"] = "Hello。わかりました",
            // 拡張子・ドメインの後ろの、助詞以外の日本語・大文字の略語 (README.md|だよ、mp4|を|MP3に)
            ["README.mddayo"] = "README.mdだよ", ["github.comjanai"] = "github.comじゃない", ["Meltype.plistdayo"] = "Meltype.plistだよ",
            ["video.mp4woMP3ni"] = "video.mp4をMP3に", ["github.commade"] = "github.commade",
            // 「っ」で始まる日本語 (OK.って)
            ["OK.tte"] = "OK.って",
            // 変わらないもの: バージョン番号・URL
            ["1.5"] = "1.5", ["v2.0"] = "v2.0", ["https://github.com/foo"] = "https://github.com/foo",
        };
        foreach (var (typed, expected) in cases)
        {
            var k = new Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, $"「{typed}」");
        }
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
    public static void Zero_AfterSpaceConversion_IsTypedNotLost()
    {
        // denwa wa 03-1234-5678desu が でんわﾜ-1234-5678です (03 が消える) になっていた: 変換中の 0 は候補の番号ではない
        foreach (var (typed, expected) in new[]
        {
            ("denwa wa 03-1234-5678desu", "でんわわ03-1234-5678です"), ("kyou wa 090", "今日わ090"), ("kawa 0", "川0"),
        })
        {
            var k = new Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, typed);
        }
    }

    [Test]
    public static void Digits_RightAfterConversion_AreTyped_WhenSettingIsOn()
    {
        // 区切りのつもりの Space の後の数字 (kyou wa 12) が、候補の選択になって消えていた
        var k = new Keyboard { TypeDigits = true };
        k.Type("kyou wa 12\n");
        Assert.Equal("今日わ12", k.Host.Document, "変換した直後の数字は入力");
        k = new Keyboard(moreCandidates: reading => reading == "かわ" ? ManyKawa : []) { TypeDigits = true };
        k.Type("kawa ");
        k.Press(VirtualKeys.Down);
        k.Press(0x32);
        Assert.Equal("皮", string.Concat(k.Host.Output), "候補を動かしたあとは、数字で候補を選んで確定する (2 番は 皮)");
    }

    [Test]
    public static void Digits_WhenSettingIsOn_AfterEachCandidateMove_SelectCandidate()
    {
        // 候補を動かす操作 (Space・↓・↑・PageDown・← →) のどれでも、動かしたあとは数字で選べる。動かす前の数字は入力
        foreach (var move in new[] { VirtualKeys.Space, VirtualKeys.Down, VirtualKeys.Up, VirtualKeys.Next, VirtualKeys.Left, VirtualKeys.Right })
        {
            var k = new Keyboard(moreCandidates: reading => reading == "かわ" ? ManyKawa : []) { TypeDigits = true };
            k.Type("kawa ");
            k.Press(move);
            k.Press(0x31);
            Assert.True(k.Showing is null && k.Host.Output.Count == 1, $"{move:X} の後の 1 は候補の選択で確定する");
        }
        // 候補を動かす前に打った数字は入力。そのあとの数字も続けて入力 (変換が確定して新しい入力になる)
        var typed = new Keyboard(moreCandidates: reading => reading == "かわ" ? ManyKawa : []) { TypeDigits = true };
        typed.Type("kawa 12\n");
        Assert.Equal("川12", typed.Host.Document, "動かす前の 1 2 は入力");
    }

    [Test]
    public static void Digits_RightAfterConversion_SelectCandidate_WhenSettingIsOff()
    {
        var k = new Keyboard(moreCandidates: reading => reading == "かわ" ? ManyKawa : []);
        k.Type("kawa ");
        k.Press(0x32);
        Assert.Equal("皮", string.Concat(k.Host.Output), "OFF なら今までどおり、変換した直後から数字で選ぶ");
    }

    [Test]
    public static void Zero_AfterSpaceConversion_WithContinueAfterConversion_IsTyped()
    {
        // 「変換後も続けて入力」が ON でも、変換中の 0 は候補の番号ではなく入力 (固定した文節のあとに続く)
        var k = new Keyboard { Continue = true };
        k.Type("kyou wa 0");
        k.Type("\n");
        Assert.Equal("今日わ0", k.Host.Document, "続けて入力 ON でも 0 は消えない");
    }

    [Test]
    public static void Label_BeforeColon_StaysEnglish()
    {
        // Tel: が てl: になっていた (Tel だけなら英語)。: ; が続いても、ローマ字として読めない大文字始まりの語は英語のまま
        foreach (var (typed, expected) in new[]
        {
            ("Tel:", "Tel:"), ("Fax:", "Fax:"), ("Tex:", "Tex:"), ("Tel: 03-1234-5678 desu", "Tel: 03-1234-5678 です"),
            // 最後まで読める語・句読点は今までどおり日本語
            ("Kyou:", "きょう:"), ("Tel,", "てl、"),
        })
        {
            var k = new Keyboard();
            k.Type(typed + "\n");
            Assert.Equal(expected, k.Host.Document, typed);
        }
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

    /// <summary>しゃーなし・しゃあなし を 西なし にする変換エンジン (Mozc の苦手な語の代わり)。</summary>
    private sealed class NishinashiConverter : IKanjiConverter
    {
        public string? Convert(string hiragana) => hiragana.Replace("しゃーなし", "西なし").Replace("しゃあなし", "西なし");
        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) => [new(hiragana, Convert(hiragana)!)];
    }

    [Test]
    public static void Shanashi_IsNotConvertedToPlaceName()
    {
        // #368: sha-nashi (しゃーなし) を、変換エンジンが 西なし にしていた。同梱の語句でかなのまま最初の候補にする
        foreach (var (typed, expected) in new[] { ("sha-nashi", "しゃーなし"), ("shaanashi", "しゃあなし") })
        {
            var k = new Keyboard(converter: new NishinashiConverter(), userDictionary: new UserDictionary(null));
            k.Type(typed + " ");
            Assert.Equal(expected, k.Host.View!.Candidates[0], string.Join(" ", k.Host.View!.Candidates));
            k.Press(VirtualKeys.Return);
            Assert.Equal(expected, k.Host.Document);
        }
        // 文の中でも西なしにしない
        var sentence = new Keyboard(converter: new NishinashiConverter(), userDictionary: new UserDictionary(null));
        sentence.Type("sorehasha-nashidayo\n");
        Assert.True(!sentence.Host.Document.Contains("西"), sentence.Host.Document);
    }

    [Test]
    public static void Ki_OffersOldKanji()
    {
        // #371: き の候補に 気 の旧字体 氣 も出す
        var k = new Keyboard();
        k.Type("ki ");
        Assert.True(k.Host.View!.Candidates.Contains("氣"), string.Join(" ", k.Host.View!.Candidates));
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
        Assert.True(dictionary.Add("", "記") is not null, "空の読みは登録できない");

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

    [Test]
    public static void SpaceAroundEnglish_WhenTypingInTheCompositionBox()
    {
        // #248: 英数を使わず、変換ボックスの中で英語と日本語を判定して打っても、設定「英単語の前後に半角スペース」が効く
        Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try
        {
            foreach (var (typed, expected) in new[]
            {
                ("kyouhaGooglenipushshita\n", "きょうは Google に push した"),
                ("pingwoutsu\n", "ping をうつ"),
                ("meetingga\n", "meeting が"),
                ("kyouhashoppingniiku\n", "きょうは shopping にいく"),
                // 別々に確定しても、前に確定した英単語との間に入れる
                ("GitHub\nni\n", "GitHub に"),
            })
            {
                var k = new Keyboard { SpaceAroundEnglish = true };
                k.Type(typed);
                Assert.Equal(expected, k.Host.Document, typed);
            }
        }
        finally
        {
            Detector.SpellChecker = null;
        }
    }

    /// <summary>ping・pong を英単語と答えるスペルチェッカー (実機の NSSpellChecker は ping を英単語と答える。同梱の一覧 BuiltIn は答えない)。</summary>
    private sealed class PingIsWordChecker : Detection.IWordChecker
    {
        public bool IsAvailable => true;
        public bool IsWord(string lower) => lower is "ping" or "pong" || Detection.BuiltInWordChecker.Shared.IsWord(lower);
    }

    [Test]
    public static void Ping_StaysEnglish_WhateverTheSpellCheckerSays()
    {
        // #320: ping の判定が、スペルチェッカーが知っているか (実機) 知らないか (同梱の一覧) で入力中の表示と確定結果がずれていた。
        // 英語の一覧 (english.txt) に ping・pong を足して、どちらでも ping のまま
        foreach (Detection.IWordChecker checker in new Detection.IWordChecker[] { Detection.BuiltInWordChecker.Shared, new PingIsWordChecker() })
        {
            Detector.SpellChecker = checker;
            try
            {
                foreach (var (typed, expected) in new[] { ("ping", "ping"), ("pingwoutsu", "pingをうつ"), ("pong", "pong"), ("pongga", "pongが") })
                {
                    var k = new Keyboard();
                    k.Type(typed);
                    var showing = k.Showing;
                    k.Type("\n");
                    Assert.Equal(expected, k.Host.Document, $"{typed} ({checker.GetType().Name}) の確定");
                    if (typed == "pingwoutsu") Assert.Equal(expected, showing, $"{typed} ({checker.GetType().Name}) の入力中の表示");
                }
            }
            finally
            {
                Detector.SpellChecker = null;
            }
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
