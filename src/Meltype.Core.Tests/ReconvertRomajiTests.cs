// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>
/// ローマ字のまま入った文を、選んで再変換 (Mac: Shift+Space) して日本語にする (本家 #332)。
/// 選んだ文字は MeltypeSession.Reconvert に渡る。ローマ字なら読みに戻さず、Meltype の判定で語ごとに英語 / 日本語を決める。
/// </summary>
internal static class ReconvertRomajiTests
{
    private static MeltypeSession Create(CompositionOptions? options = null, IKanjiConverter? converter = null) =>
        new(CompositionTests.Detector, converter ?? new CompositionTests.FakeConverter(), options ?? new CompositionOptions(), () => new Settings());

    private static SessionResult Press(MeltypeSession session, int vk) => session.HandleKey(vk, null, false, false, false, false);

    /// <summary>覚えさせた文節を記録する変換エンジン。</summary>
    private sealed class RecordingConverter : IKanjiConverter, ILearningConverter
    {
        private readonly CompositionTests.FakeConverter _inner = new();
        public List<string> Learned { get; } = [];
        public string? Convert(string hiragana) => _inner.Convert(hiragana);
        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null) => _inner.ConvertClauses(hiragana, context);

        public void Learn(string? context, IReadOnlyList<ConversionClause> clauses)
        {
            lock (Learned) Learned.Add(string.Join("|", clauses.Select(c => $"{c.Reading}={c.Text}")));
        }
    }

    private static readonly (string Selected, string Expected)[] RomajiSentences =
    [
        ("kyouha iitenki desu ne", "きょうはいいてんきですね"),
        ("asita no meeting de review shimasu", "あしたの meeting で review します"),
        ("PR no review onegai shimasu", "PR の review おねがいします"),
    ];

    [Test]
    public static void Reconvert_RomajiSentence_ShowsJapaneseCandidate_EnterReplaces()
    {
        // #332: ローマ字のまま入った文を選んで Shift+Space → 日本語の所だけ日本語にした文を候補に出し、Enter で置き換える
        CompositionTests.Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try
        {
            foreach (var (selected, expected) in RomajiSentences)
            {
                var session = Create();
                var result = session.Reconvert(selected);
                Assert.True(result.Consumed, selected + ": 再変換を始めたらキーは使う");
                Assert.True(result.View is { Converting: true }, selected + ": 候補を出す");
                Assert.Equal(expected, result.View?.Text, selected);
                Assert.True(result.View!.Candidates.Contains(selected), "選んだままの文も候補に");
                // 変換エンジンの追加候補 (読み = 日本語にした文) は引かない: 候補は 日本語にした文 → 選んだままの文 の 2 つだけ
                var withMore = Create(new CompositionOptions { MoreCandidates = reading => ["追加候補1", "追加候補2"] }).Reconvert(selected).View!;
                Assert.Equal(expected + " | " + selected, string.Join(" | ", withMore.Candidates), selected);
                Assert.Equal(0, result.Commits.Count, "Enter まで置き換えない");
                Assert.True(session.IsComposing, "変換ボックスが開いている");
                var enter = Press(session, VirtualKeys.Return);
                Assert.Equal(expected, enter.Commits.Single().Text, selected);
                Assert.True(enter.View is null && !session.IsComposing, "確定したら閉じる");
            }
        }
        finally
        {
            CompositionTests.Detector.SpellChecker = null;
        }
    }

    [Test]
    public static void Reconvert_RomajiSentence_SpaceShowsOriginal_AndEscCancels()
    {
        CompositionTests.Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try
        {
            const string selected = "asita no meeting de review shimasu";
            var session = Create();
            session.Reconvert(selected);
            // Space で 2 番目の候補 (選んだままの文) に進み、Enter で確定すると同じ文字に戻る
            var space = Press(session, VirtualKeys.Space);
            Assert.True(space.Consumed, "再変換のあとの Space は候補の操作に使う");
            Assert.Equal(selected, space.View?.Text);
            Assert.Equal(selected, Press(session, VirtualKeys.Return).Commits.Single().Text);

            // Esc は 1 回で、選んでいた文字を書き戻してやめる (日本語にした文は出ない)
            session = Create();
            session.Reconvert(selected);
            var esc = Press(session, VirtualKeys.Escape);
            Assert.True(esc.Consumed, "Esc は取り消しに使う");
            Assert.Equal(selected, esc.Commits.Single().Text);
            Assert.True(esc.View is null && !session.IsComposing, "変換ボックスは閉じる");
            Assert.True(Press(session, VirtualKeys.Escape).Commits.Count == 0, "書き戻しは 1 回だけ");

            // BackSpace も同じ
            session = Create();
            session.Reconvert(selected);
            var back = Press(session, VirtualKeys.Back);
            Assert.Equal(selected, back.Commits.Single().Text);
            Assert.True(back.View is null && !session.IsComposing, "変換ボックスは閉じる");
        }
        finally
        {
            CompositionTests.Detector.SpellChecker = null;
        }
    }

    [Test]
    public static void Reconvert_RomajiSentence_IsNotLearned()
    {
        // 日本語にした文 (読みではない) を、変換エンジンにも履歴にも覚えさせない。
        // 日本語だけの文 (読みに英字が入らない) は、そのまま確定すると変換エンジンに「読み = 日本語にした文」として渡されてしまう。Space で選び直すと履歴にも入る
        CompositionTests.Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try
        {
            foreach (var presses in new[] { new int[0], [VirtualKeys.Space], [VirtualKeys.Space, VirtualKeys.Space] })
            {
                var converter = new RecordingConverter();
                var history = new ConversionHistory(null);
                var session = Create(new CompositionOptions { History = history }, converter);
                Assert.True(session.Reconvert("kyouha iitenki desu ne").Consumed, "再変換が始まっている");
                foreach (var vk in presses) Press(session, vk);
                Assert.True(Press(session, VirtualKeys.Return).Commits.Count == 1, "確定した");
                Thread.Sleep(100);
                Assert.Equal(0, converter.Learned.Count, $"Space {presses.Length} 回: " + string.Join(" / ", converter.Learned));
                Assert.Equal(0, history.Count, $"Space {presses.Length} 回: 履歴にも入れない");
            }
        }
        finally
        {
            CompositionTests.Detector.SpellChecker = null;
        }
    }

    [Test]
    public static void Reconvert_RomajiSentence_ShiftArrowsDoNotResize()
    {
        CompositionTests.Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try
        {
            var session = Create();
            var first = session.Reconvert("kyouha iitenki desu ne").View!;
            var shiftRight = session.HandleKey(VirtualKeys.Right, null, true, false, false, false);
            Assert.True(shiftRight.Consumed, "区切りは変えられないが、キーはアプリに渡さない");
            Assert.Equal(first.Text, shiftRight.View?.Text);
            // Shift+← は文節の読みを縮めて変換し直す (読みは日本語にした文なので、ばらばらになる)。ローマ字の再変換では区切りを変えない
            var shiftLeft = session.HandleKey(VirtualKeys.Left, null, true, false, false, false);
            Assert.True(shiftLeft.Consumed, "区切りは変えられないが、キーはアプリに渡さない");
            Assert.Equal(first.Text, shiftLeft.View?.Text);
            Assert.Equal(first.Candidates, shiftLeft.View?.Candidates);
            Assert.Equal("きょうはいいてんきですね", Press(session, VirtualKeys.Return).Commits.Single().Text);
        }
        finally
        {
            CompositionTests.Detector.SpellChecker = null;
        }
    }

    [Test]
    public static void Reconvert_RomajiSentence_SpaceAroundEnglishDoesNotDoubleSpaces()
    {
        CompositionTests.Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try
        {
            var session = Create(new CompositionOptions { SpaceAroundEnglish = () => true });
            session.Reconvert("asita no meeting de review shimasu");
            Assert.Equal("あしたの meeting で review します", Press(session, VirtualKeys.Return).Commits.Single().Text);
        }
        finally
        {
            CompositionTests.Detector.SpellChecker = null;
        }
    }

    /// <summary>ローマ字の再変換を試して、キーを使わなかった (今までどおり、アプリに渡す) か。</summary>
    private static bool PassesThrough(string selected, Func<MeltypeSession>? create = null)
    {
        var session = (create ?? (() => Create()))();
        var result = session.Reconvert(selected);
        return !result.Consumed && result.View is null && !session.IsComposing
            && !session.HandleKey(VirtualKeys.Space, null, false, false, false, false).Consumed;
    }

    [Test]
    public static void Reconvert_EnglishCodeUrlAndPaths_AreLeftAlone()
    {
        // 日本語だと言える所が無い選択 (英文・URL・パス・メール・コマンド・コード・製品名) は、今までどおり何もしない (英文を勝手に日本語にしない)。
        // to・no・so・do・name のような英語とも日本語とも読める語が入っていても、それだけでは日本語にしない。
        foreach (var checker in new Detection.IWordChecker?[] { null, Detection.BuiltInWordChecker.Shared })
        {
            CompositionTests.Detector.SpellChecker = checker;
            try
            {
                foreach (var selected in new[]
                {
                    "this is a pen", "I want to go home", "See you tomorrow", "Thank you so much", "He said no to me", "Do you want to go", "Please let me know", "Go to the next page", "no no no", "to go", "me too",
                    "https://github.com/yksr-melt/Meltype/pull/332", "http://localhost:8080/search?q=hello+world", "/Users/ryo/Downloads/report.pdf", "~/Documents/notes.txt", "C:\\Users\\ryo\\memo.txt", "src/main.rs",
                    "ryo@example.com", "git commit -m fix", "git push origin main", "npm install -g typescript", "ls -la /tmp", "export PATH=$HOME/bin:$PATH", "const x = 42;", "SELECT * FROM users WHERE id = 1",
                    "README.md", ".gitignore", "v1.2.3", "a3f9c1d", "azooKey", "iPhone", "GitHub", "TanakaMakoto", "OK", "WIFI", "123", "3.14", "a+b", "...", "!!!", "   ",
                    // 英単語の端が 1 音だけかなに読める (Take|shi、by|F|ri|day): かなが 2 つ以上読めるものだけを日本語の証拠にする
                    "I met Takeshi yesterday", "Tell Takeshi about the plan", "I will send the report by Friday.", "abc",
                    // 英文に日本語のローマ字のような語が 1 つだけ紛れている (Ryo): 日本語の区間の英字が文の 1/4 に満たないので、日本語にしない
                    // (限界: 辞書に無い英単語 docs・unencumbered は、実際の打ち間違いの直し (RomajiTypos。このテストは持たない) で日本語に見えてしまう)
                    "Hello, my name is Ryo.",
                    // 数字の入った語 (k8s + ha) は、証拠にしない (限界: k8sは にもしない)
                    "k8sha", "k8skara",
                    // 大文字で始まる語 + 助詞に見える終わり (固有名詞・人名): 助詞の証拠にしない
                    "Clyde", "Typha", "Manto", "Christmastide", "Tanaka Makoto", "Takahashi Kanade",
                })
                {
                    Assert.True(PassesThrough(selected), $"{selected}: 今までどおり ({(checker is null ? "辞書なし" : "同梱の英単語表")})");
                }
            }
            finally
            {
                CompositionTests.Detector.SpellChecker = null;
            }
        }
    }

    [Test]
    public static void Reconvert_RomajiMixedWithCode_KeepsCodeTokensAsTyped()
    {
        // 日本語の所があれば変換するが、URL・パス・ファイル名・コマンドの引数は英字のまま残す
        CompositionTests.Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try
        {
            foreach (var (selected, expected) in new[]
            {
                ("README.md wo yonde kudasai", "README.md をよんでください"),
                ("config.yaml no settei wo kaete", "config.yaml のせっていをかえて"),
                ("https://github.com/yksr-melt/Meltype/pull/332 wo mite kudasai", "https://github.com/yksr-melt/Meltype/pull/332 をみてください"),
                ("git commit -m kyouno shuusei", "git commit -m きょうのしゅうせい"),
                ("~/Documents/memo.txt wo hiraite", "~/Documents/memo.txt をひらいて"),
                ("foo@example.com ni mail shimasu", "foo@example.com に mail します"),
                ("GitHub ni push shimashita", "GitHub に push しました"),
                ("iPhone no settei wo kaeta", "iPhone のせっていをかえた"),
                ("Mac no settei ga wakaranai", "Mac のせっていがわからない"),
                // pull request no … の no: 前までが英語でも、日本語の助詞のまま
                ("pull request no kakunin wo onegai shimasu", "pull request のかくにんをおねがいします"),
                ("Visual Studio Code no settei", "Visual Studio Code のせってい"),
                ("New York no tenki", "New York のてんき"),
                // 前の語が英語なら、そのことも手がかりにする (mode が もで、you が よう にならない)
                ("dark mode no settei", "dark mode のせってい"),
                ("I love you no koto", "I love you のこと"),
                // 直前が感動詞 1 語 (oh) なら、続く no は英語のまま (爆弾にはなれない oh no!)
                ("bakudannnihanarenai oh no!", "ばくだんにはなれない oh no!"),
                // 読み切れない英字だけの区間 (my の m + y) は、かなにせず英語として扱う
                ("my name taro desu", "my name たろです"),
            })
            {
                var result = Create().Reconvert(selected);
                Assert.True(result.Consumed, selected + ": 日本語の所があるので再変換する");
                Assert.Equal(expected, result.View?.Text, selected);
            }
        }
        finally
        {
            CompositionTests.Detector.SpellChecker = null;
        }
    }

    [Test]
    public static void Reconvert_RomajiWithoutSpaces_AddsNoSpacesInsideAWord()
    {
        // 空白なしで打った語の中 (kyouhagoogledekensaku) は、打ったときと同じく空白を足さない。空白で区切って打った所だけ、英語の前後に空白を残す
        CompositionTests.Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try
        {
            Assert.Equal("今日はgoogleで検索", Create().Reconvert("kyouhagoogledekensaku").View?.Text);
            Assert.Equal("今日は google で検索", Create().Reconvert("kyouha google dekensaku").View?.Text);
            // 英語の語 + 助詞だけの語 (sdk|to・xml|de): 助詞が英語の語の後ろに続くので、日本語と言える
            Assert.Equal("sdkと", Create().Reconvert("sdkto").View?.Text);
            Assert.Equal("xmlで", Create().Reconvert("xmlde").View?.Text);
            // 英語の語が長くても、助詞が同じ語の中に続けば日本語 (日本語の割合の下限は、空白で区切った英文に紛れた 1 語を除くためのもの)
            Assert.Equal("authorizationheaderが", Create().Reconvert("authorizationheaderga").View?.Text);
            Assert.Equal("configurationmanagerで", Create().Reconvert("configurationmanagerde").View?.Text);
            // 前後の空白は残す
            Assert.Equal("  今日は  ", Create().Reconvert("  kyouha  ").View?.Text);
        }
        finally
        {
            CompositionTests.Detector.SpellChecker = null;
        }
    }

    [Test]
    public static void Reconvert_RomajiNeedsEvidence_ShortAmbiguousWordsAreNotEnough()
    {
        CompositionTests.Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try
        {
            // 3 文字以上の、英単語でも英語とも読める語でもないかな読みがある → 日本語。to・no・de だけ・短い 2 文字だけでは日本語にしない
            Assert.True(Create().Reconvert("kyou").Consumed, "kyou は日本語");
            Assert.True(Create().Reconvert("desu").Consumed, "desu は日本語");
            Assert.True(PassesThrough("ii ne"), "短い語 (ii ne) だけでは証拠が足りないので今までどおり (限界)");
            Assert.True(PassesThrough("to no"), "to no だけでは今までどおり");
            Assert.True(PassesThrough("ni wo ga"), "助詞だけでは今までどおり");
            // 英語の語のあとの助詞は、同じ語の中なら日本語と言える
            Assert.True(Create().Reconvert("sslto").Consumed, "sslto は日本語");
            // 空白で離れた助詞は、証拠にならない
            Assert.True(PassesThrough("ssl to"), "ssl to は空白で離れているので今までどおり");
        }
        finally
        {
            CompositionTests.Detector.SpellChecker = null;
        }
    }

    /// <summary>決まった語だけを英単語と答える偽のスペルチェッカー (NSSpellChecker が naka・mako・kana を英単語と答えるのに似せる)。</summary>
    private sealed class FewWordsChecker(params string[] words) : Detection.IWordChecker
    {
        public bool IsAvailable => true;
        public bool IsWord(string lower) => words.Contains(lower) || Detection.BuiltInWordChecker.Shared.IsWord(lower);
    }

    [Test]
    public static void Reconvert_CapitalizedName_CutInTheMiddle_IsNotEvidence()
    {
        // 辞書が naka・mako・kana を英単語と答えると、人名 Nakamura・Makoto・Kanade が Naka|mura・Mako|to・Kana|de に切れる。
        // 大文字で始まる語の後ろに続く部分は、日本語の証拠にしない (名前だけの選択を日本語にしない)
        CompositionTests.Detector.SpellChecker = new FewWordsChecker("naka", "mako", "kana");
        try
        {
            foreach (var selected in new[] { "Nakamura Hiroto", "Tanaka Makoto", "Takahashi Kanade", "Nakamura", "Makoto", "Kanade" })
            {
                Assert.True(PassesThrough(selected), selected + ": 名前だけなら今までどおり");
            }
            // 小文字の語 + 助詞は、同じように切れても日本語 (sdk|to)
            CompositionTests.Detector.SpellChecker = new FewWordsChecker("sdk");
            Assert.Equal("sdkと", Create().Reconvert("sdkto").View?.Text);
        }
        finally
        {
            CompositionTests.Detector.SpellChecker = null;
        }
    }

    [Test]
    public static void Reconvert_RomajiTypos_AreFixedLikeTyping()
    {
        // 打ち間違い (onegaishimsu) は、打ったときの Enter と同じく直す。端末に入れた文は打ち間違いを直せていないので、直す価値が高い
        CompositionTests.Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try
        {
            var options = new CompositionOptions { RomajiTypos = RomajiTypoCorrector.Load(CompositionTests.Detector.Romaji) };
            Assert.Equal("おねがいします", Create(options).Reconvert("onegaishimsu").View?.Text);
            Assert.Equal("review をおねがいします", Create(options).Reconvert("review wo onegaishimsu").View?.Text);
            Assert.Equal("おねがいします", Create().Reconvert("onegaishimasu").View?.Text);
            // 設定で打ち間違いの直しが OFF なら直さない
            var off = new CompositionOptions { RomajiTypos = RomajiTypoCorrector.Load(CompositionTests.Detector.Romaji), CorrectTypos = () => false };
            Assert.True(Create(off).Reconvert("onegaishimsu").View?.Text != "おねがいします", "OFF なら直さない");
        }
        finally
        {
            CompositionTests.Detector.SpellChecker = null;
        }
    }

    [Test]
    public static void Reconvert_AsciiTextWithoutJapanese_StillUsesHistoryReading()
    {
        // 日本語だと言える所が無い ASCII の文字でも、変換履歴に読みがあれば今までどおり読みに戻す (確定した GitHub → ぎっとはぶ)
        CompositionTests.Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try
        {
            var history = new ConversionHistory(null);
            history.Remember("ぎっとはぶ", "GitHub");
            var session = Create(new CompositionOptions { History = history });
            var result = session.Reconvert("GitHub");
            Assert.True(result.Consumed && result.View is { Converting: true }, "履歴に読みがあるので読みに戻して変換する");
            // 今までの再変換は、1 回目の Esc で読みに戻る (元の文字はまだ書き戻さない)。ローマ字の再変換は 1 回で書き戻す
            var esc = Press(session, VirtualKeys.Escape);
            Assert.True(esc.Commits.Count == 0 && session.IsComposing, "今までの再変換: 1 回目の Esc は読みに戻るだけ");
            // 履歴に無ければ、読めない文字は何もしない
            Assert.True(PassesThrough("GitHub"), "履歴が無ければ今までどおり");
        }
        finally
        {
            CompositionTests.Detector.SpellChecker = null;
        }
    }

    [Test]
    public static void Reconvert_Romaji_DoesNotRecommitThePreviousWord()
    {
        // 直前に英語で確定した、英語とも日本語とも読める語 (I want sushi の sushi) を、選んだ文字の再変換の確定で日本語に確定し直さない
        // (確定し直しは手前の文字を消すので、選んだ文字ではなく sushi が消える)
        CompositionTests.Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try
        {
            var session = Create();
            foreach (var c in "I want sushi ") session.HandleKey(c == ' ' ? VirtualKeys.Space : char.ToUpperInvariant(c), c == ' ' ? null : c, char.IsAsciiLetterUpper(c), false, false, false);
            Assert.True(session.Reconvert("asita no meeting de review shimasu").Consumed, "再変換が始まっている");
            var enter = Press(session, VirtualKeys.Return);
            Assert.Equal("あしたの meeting で review します", string.Concat(enter.Commits.Select(c => c.Text)));
            Assert.True(enter.Commits.All(c => c.DeleteBefore == 0), "手前の文字を消さない: " + string.Join(" ", enter.Commits.Select(c => $"{c.DeleteBefore}:{c.Text}")));
        }
        finally
        {
            CompositionTests.Detector.SpellChecker = null;
        }
    }

    [Test]
    public static void Reconvert_EnglishWithCapitalizedName_IsLeftAlone()
    {
        // 英文の中の人名 (Thanks, Kenji! / Hi Tanaka!) は、句読点が付くと「語として確定」して日本語に読めるが、それだけでは日本語だと言えない。
        // 大文字で始まる語と、敬称だけの語 (san・kun・chan・sama) は、日本語の証拠にしない (査読 R18b。以前は名前の直後の ! . , ? で候補が出ていた)。
        foreach (var checker in new Detection.IWordChecker?[] { null, Detection.BuiltInWordChecker.Shared })
        {
            CompositionTests.Detector.SpellChecker = checker;
            try
            {
                foreach (var selected in new[]
                {
                    "Thanks, Kenji!", "Hi Tanaka!", "Dear Kenji,", "Hello, Yuki.", "Hi Tanaka! Can you send me the report?",
                    "Good morning, Yoshida!", "Sorry, Ryo.", "Welcome, Rina!", "Kenji, can you send it?", "Kenji! Can you send it?",
                    // 敬称が付いても、英文の中なら同じ (Takeshi san は Take|shi + san に切れ、san だけが証拠に見える)
                    "Thanks, Takeshi san!", "Hi Takeshi san", "Hi Masato san,", "Hi Masato kun", "Hi Masato chan", "Hi Masato sama", "Thanks, Tanaka-san!", "Dear Tanaka-san,",
                    // 文頭 (と . ! ? の後) の語は小文字にして扱うが、その語自体は日本語の証拠にしない: 名前が文頭の英文・2 文目が名前の英文は、今までどおり
                    "Tanaka-san, can you send it?", "Kenji, thanks for the report.", "Thanks for the report. Kenji, can you send it?", "I met him yesterday. Thanks, Kenji!", "Hello. Tanaka! Can you hear me?",
                })
                {
                    Assert.True(PassesThrough(selected), $"{selected}: 今までどおり ({(checker is null ? "辞書なし" : "同梱の英単語表")})");
                }
            }
            finally
            {
                CompositionTests.Detector.SpellChecker = null;
            }
        }
    }

    [Test]
    public static void Reconvert_EnglishStartingWithAmbiguousWord_IsNotPushedOverTheShare()
    {
        // 文頭の語 (Make・Run・You) は小文字にすると日本語に読める。その字数を日本語の割合に数えると、辞書に無い語が 1 つある英文 (zipapp) が割合の下限を越えて候補になる。
        // 小文字に直した語の字数は割合の分子に数えない (分母には数える) ので、今までどおり候補なし。
        CompositionTests.Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try
        {
            foreach (var selected in new[] { "Run the zipapp command line interface.", "You MUST handle these via tmux send-keys:", "Make a link for an identifier, given name-to-URL mappings." })
            {
                Assert.True(PassesThrough(selected), selected + ": 今までどおり (同梱の英単語表)");
            }
        }
        finally
        {
            CompositionTests.Detector.SpellChecker = null;
        }
    }

    [Test]
    public static void Reconvert_CapitalizedWords_KnownLimitsAndWhatStillWorks()
    {
        CompositionTests.Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try
        {
            // 文頭の大文字のローマ字文は、小文字の語に証拠があれば今までどおり日本語にする
            // (Kyou・Ohayou のような大文字の語は、語ごとの判定で英語になる = 元から証拠ではなかった。文頭だけ例外にする理由が無い: 名前が文頭の英文 Kenji, can you send it? が出てしまう)
            foreach (var selected in new[] { "Kyou wa tenki ga ii", "Ohayou gozaimasu", "Watashi wa gakusei desu", "Tanaka-san ni onegai shimasu", "Kyou Tanaka-san ni aimasu" })
            {
                Assert.True(Create().Reconvert(selected).Consumed, selected + ": 小文字の語に証拠がある");
            }
            // 限界 (現在の答えを固定する。広げるときは別の項目として、英文の誤爆 Thanks, Kenji! との釣り合いを測り直す):
            // 日本語だと言える語が大文字で始まる語 (名前・敬称つきの名前・挨拶) だけの選択は、候補を出さず今までの再変換に任せる
            foreach (var selected in new[] { "Arigatou", "Sumimasen", "Tanaka-san", "Tanaka-san ni", "Sato-sensei ni", "Mika-chan", "Tanaka san", "Kenji!", "Konnichiwa" })
            {
                Assert.True(PassesThrough(selected), selected + ": 大文字で始まる語だけでは日本語にしない (限界)");
            }
            // 敬称だけの語が証拠にならないのは、単独のときだけ。小文字の名前や他の語に証拠があれば日本語にする
            Assert.True(Create().Reconvert("tanaka san ni onegai shimasu").Consumed, "小文字の語に証拠がある");
            Assert.True(Create().Reconvert("kyou san").Consumed, "kyou が証拠");
        }
        finally
        {
            CompositionTests.Detector.SpellChecker = null;
        }
    }

    [Test]
    public static void Reconvert_SentenceInitialCapital_IsReadAsJapanese()
    {
        // アプリは英数で打つと文頭を自動で大文字にする (Asita no meeting …)。先頭の語だけ英語のまま残らず、小文字で打ったときと同じに日本語にする。
        // 先頭の 1 文字だけが大文字の語を、判定と変換のために小文字に直して扱う。この語自体は日本語の証拠に数えない (他の語に証拠があるときだけ)。
        CompositionTests.Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try
        {
            foreach (var (selected, expected) in new[]
            {
                ("Asita no meeting de review shimasu", "あしたの meeting で review します"),
                ("Kyou wa genki desu", "きょうわげんきです"),
                ("Kenji ni onegai shimasu", "けんじにおねがいします"),
                ("Ohayou gozaimasu", "おはようございます"),
                ("Watashi wa gakusei desu", "わたしわがくせいです"),
                // 先頭の語に句読点が付いていても、ハイフンでつながっていても同じ (Tanaka-san の ー は、小文字で打った tanaka-san ni … と同じ従来の答え)
                ("Hai, wakarimashita", "はい、わかりました"),
                ("Dorobou, matte kudasai", "どろぼう、まってください"),
                ("Doukyuu-sei to asobimasu", "どうきゅうーせいとあそびます"),
                ("Kyou, asita mo ganbarimasu", "きょう、あしたもがんばります"),
                ("Tanaka-san ni onegai shimasu", "たなかーさんにおねがいします"),
                // . ! ? のあとの語も文頭として扱う
                ("Kyou wa genki desu. Asita mo ganbarimasu.", "きょうわげんきです。あしたもがんばります。"),
                ("Kyou wa ame desu ka? Asita wa hare desu.", "きょうわあめですか？あしたわはれです。"),
            })
            {
                var result = Create().Reconvert(selected);
                Assert.True(result.Consumed, selected + ": 候補を出す");
                Assert.Equal(expected, result.View?.Text, selected);
                Assert.True(result.View!.Candidates.Contains(selected), selected + ": 選んだままの文も候補に");
            }
            // 先頭の語が英語の語なら、大文字のまま英語として残す (大文字を小文字にして返さない)
            Assert.Equal("Hello no meeting でしょう", Create().Reconvert("Hello no meeting deshou").View?.Text);
            // 全部大文字の語 (略語・地名) は、そのまま扱う (小文字に直さない = AI が あI にならない)
            Assert.Equal("PR の review おねがいします", Create().Reconvert("PR no review onegai shimasu").View?.Text);
            Assert.Equal("AI をつかってきょうのかいぎをまとめる", Create().Reconvert("AI wo tsukatte kyou no kaigi wo matomeru").View?.Text);
            Assert.Equal("TOKYO にいきます", Create().Reconvert("TOKYO ni ikimasu").View?.Text);
        }
        finally
        {
            CompositionTests.Detector.SpellChecker = null;
        }
    }

    private static MeltypeSession CreateWithOsReading(CompositionOptions? options = null)
    {
        var session = Create(options);
        // OS の読み (CFStringTokenizer) は、ASCII の英文でもでたらめなかなを返す (実測: this is a pen → てひせぃさぺん、Thanks, Kenji! → てはんくすけんじ)
        session.ReadingProvider = text => text.All(c => c < 0x80) ? "でたらめ" : text is "今日" ? "きょう" : text is "今日 明日" ? "きょうあした" : null;
        return session;
    }

    [Test]
    public static void Reconvert_EnglishSelection_DoesNotFallBackToTheOsReading()
    {
        // ローマ字の再変換が false を返した英文は、従来の再変換 (OS の読み) に落ちて、でたらめなかなの候補が出ていた (実機: Thanks, Kenji! → 手ハンクス感じ)。
        // ASCII の複数語の選択では OS の読みを使わない = キーはアプリに渡す (Consumed=false)。履歴・意味辞書の読みは残す。
        foreach (var checker in new Detection.IWordChecker?[] { null, Detection.BuiltInWordChecker.Shared })
        {
            CompositionTests.Detector.SpellChecker = checker;
            try
            {
                foreach (var selected in new[]
                {
                    "Thanks, Kenji!", "this is a pen", "Hi Tanaka! Can you send me the report?", "Kenji, can you send it?", "I love you", "Hello world",
                    "Please send me the report by Friday.", "https://example.com/a b",
                })
                {
                    var session = CreateWithOsReading();
                    var result = session.Reconvert(selected);
                    Assert.True(!result.Consumed && !session.IsComposing, $"{selected}: 英文は候補を出さない ({(checker is null ? "辞書なし" : "同梱の英単語表")})");
                    Assert.True(!session.HandleKey(VirtualKeys.Space, null, false, false, false, false).Consumed, selected + ": 関所も閉じていない");
                }
            }
            finally
            {
                CompositionTests.Detector.SpellChecker = null;
            }
        }
    }

    [Test]
    public static void Reconvert_SingleAsciiWord_StillUsesTheOsReading()
    {
        // 空白の無い 1 語の選択 (名前 Takeshi → たけし → 武史) は、従来どおり OS の読みを使う (複数語だけ使わない)。前後の空白は語の数に数えない
        CompositionTests.Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try
        {
            foreach (var selected in new[] { "Takeshi", "Kenji", " Takeshi ", "proto" })
            {
                var session = CreateWithOsReading();
                session.ReadingProvider = text => text.Trim() is "Takeshi" or "Kenji" or "proto" ? "たけし" : null;
                var result = session.Reconvert(selected);
                Assert.True(result.Consumed && result.View is { Converting: true }, $"{selected}: 1 語は OS の読みで再変換する");
            }
            // 複数語は、同じ OS の読みを返す提供元でも使わない
            var many = CreateWithOsReading();
            many.ReadingProvider = text => "たけし";
            Assert.True(!many.Reconvert("Takeshi san").Consumed && !many.IsComposing, "複数語は OS の読みを使わない");
        }
        finally
        {
            CompositionTests.Detector.SpellChecker = null;
        }
    }

    [Test]
    public static void Reconvert_AsciiSelection_StillUsesHistoryMeaningAndKanaReadings()
    {
        // OS の読みを使わなくなっても、履歴・意味辞書の読み、かな・漢字の選択の読み (OS を含む) は今までどおり
        CompositionTests.Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try
        {
            var history = new ConversionHistory(null);
            history.Remember("ぎっとはぶ", "GitHub");
            var meanings = MeaningDictionary.Parse("Hello World\tはろーわーるど\tあいさつ。\n");
            var options = new CompositionOptions { History = history, Meanings = meanings };
            foreach (var selected in new[] { "GitHub", "Hello World" })
            {
                var session = CreateWithOsReading(options);
                var result = session.Reconvert(selected);
                Assert.True(result.Consumed && result.View is { Converting: true }, $"{selected}: 履歴・意味辞書の読みで再変換する");
            }
            Assert.Equal("今日", CreateWithOsReading().Reconvert("今日").View?.Text);
            Assert.True(CreateWithOsReading().Reconvert("今日 明日").Consumed, "漢字の複数語は OS の読みを使う (使わないのは ASCII のローマ字の複数語だけ)");
            Assert.True(CreateWithOsReading().Reconvert("きょう").Consumed, "かなはそのまま読み");
        }
        finally
        {
            CompositionTests.Detector.SpellChecker = null;
        }
    }

    [Test]
    public static void Reconvert_RomajiNeedsEvidence_ShareAndLengthBoundaries()
    {
        // 変異テストで見つかった穴: 日本語の割合の下限 (0.25) と、証拠の語の最低文字数 (3) を踏む入力が無く、0.20 や 4 に変えても赤にならなかった
        CompositionTests.Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try
        {
            // かいぎ (kaigi) 5 字 / 英字全体: 18 字 (0.28) → 日本語、20 字 (ちょうど 0.25) → 日本語、21 字 (0.238)・22 字 (0.227) → 英文に紛れた 1 語なので何もしない
            Assert.Equal("look at the かいぎ room", Create().Reconvert("look at the kaigi room").View?.Text);
            Assert.Equal("look at the かいぎ room ok", Create().Reconvert("look at the kaigi room ok").View?.Text);
            Assert.True(PassesThrough("look at the kaigi room too"), "21 字中 5 字 (0.238) は下限に満たない");
            Assert.True(PassesThrough("look at the kaigi schedule"), "22 字中 5 字 (0.227) は下限に満たない");
            // 証拠の語は 3 文字から (to・no・de の 2 文字は足りない)。3 文字の hai・kau は日本語
            Assert.Equal("はい", Create().Reconvert("hai").View?.Text);
            Assert.Equal("Amazon でかう", Create().Reconvert("Amazon de kau").View?.Text);
            Assert.True(PassesThrough("ni wo"), "2 文字の語だけでは証拠にならない");
        }
        finally
        {
            CompositionTests.Detector.SpellChecker = null;
        }
    }

    [Test]
    public static void Reconvert_Romaji_IgnoredWhileComposingOrDirect()
    {
        var session = Create();
        Type(session, "kyou");
        Assert.True(!session.Reconvert("kyouha iitenki desu ne").Consumed, "入力中は再変換しない");
        session.CommitPending();
        session.Direct = true;
        Assert.True(!session.Reconvert("kyouha iitenki desu ne").Consumed, "英数 (直接入力) では再変換しない");
    }

    [Test]
    public static void IsRomajiText_OnlyAsciiWithLetters()
    {
        Assert.True(CompositionController.IsRomajiText("kyouha iitenki desu ne"), "ローマ字の文");
        Assert.True(CompositionController.IsRomajiText("PR no review"), "大文字を含む");
        Assert.True(CompositionController.IsRomajiText("a"), "1 文字");
        Assert.True(!CompositionController.IsRomajiText("123"), "英字を含まない");
        Assert.True(!CompositionController.IsRomajiText("   "), "空白だけ");
        Assert.True(!CompositionController.IsRomajiText("きょうは"), "かな");
        Assert.True(!CompositionController.IsRomajiText("kyouhaです"), "かなが混ざる");
        Assert.True(!CompositionController.IsRomajiText("line1\nline2"), "改行を含む (複数行は今までの再変換に任せる)");
        Assert.True(!CompositionController.IsRomajiText("a\tb"), "タブを含む");
    }

    private static void Type(MeltypeSession session, string text)
    {
        foreach (var c in text) session.HandleKey(char.ToUpperInvariant(c), c, false, false, false, false);
    }
}
