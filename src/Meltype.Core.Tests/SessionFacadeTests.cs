// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>Mac 版・Linux 版から使う入力の本体 (MeltypeSession) のテスト。OS がキーを 1 つずつ渡し、使ったかをその場で返す。</summary>
internal static class SessionFacadeTests
{
    private static MeltypeSession Create() => new(CompositionTests.Detector, new CompositionTests.FakeConverter(), new CompositionOptions(), () => new Settings());

    /// <summary>文字を 1 つずつ打つ (英字は大文字なら Shift 付き)。</summary>
    private static List<SessionResult> Type(MeltypeSession session, string text, string? before = null)
    {
        var results = new List<SessionResult>();
        foreach (var c in text)
        {
            var vk = c switch
            {
                ' ' => VirtualKeys.Space,
                '\n' => VirtualKeys.Return,
                '\b' => VirtualKeys.Back,
                ',' => VirtualKeys.OemComma,
                '.' => VirtualKeys.OemPeriod,
                '-' => VirtualKeys.OemMinus,
                _ when char.IsAsciiLetter(c) => char.ToUpperInvariant(c),
                _ => c,
            };
            char? ch = c is ' ' or '\n' or '\b' ? null : c;
            results.Add(session.HandleKey(vk, ch, char.IsAsciiLetterUpper(c), false, false, false, before));
        }
        return results;
    }

    [Test]
    public static void Romaji_ComposesAndEnterCommits()
    {
        var session = Create();
        var results = Type(session, "kyouha");
        Assert.True(results.All(r => r.Consumed), "打った英字はアプリに渡さない");
        Assert.Equal("きょうは", results[^1].View?.Text);
        var enter = Type(session, "\n")[0];
        Assert.True(enter.Consumed, "Enter は確定に使う");
        Assert.Equal("きょうは", enter.Commits.Single().Text);
        Assert.True(enter.View is null, "確定したら変換ボックスを閉じる");
    }

    [Test]
    public static void CtrlK_IsKatakana()
    {
        var session = Create();
        Type(session, "kyouha");
        var result = session.HandleKey(0x4B, 'k', false, true, false, false);
        Assert.True(result.Consumed, "変換ボックスが出ているときの Ctrl+K は表示モードの切替に使う");
        Assert.Equal("キョウハ", result.View?.Text);
        var enter = Type(session, "\n")[0];
        Assert.Equal("キョウハ", enter.Commits.Single().Text);

        var empty = session.HandleKey(0x4B, 'k', false, true, false, false);
        Assert.True(!empty.Consumed, "変換ボックスが空のときの Ctrl+K はアプリに渡す");
    }

    [Test]
    public static void CtrlSemicolon_IsHalfWidthKatakana()
    {
        var session = Create();
        Type(session, "kyouha");
        var result = session.HandleKey(VirtualKeys.Oem1, null, false, true, false, false);
        Assert.Equal("ｷｮｳﾊ", result.View?.Text);
    }

    [Test]
    public static void EnglishWord_SpaceCommitsWithSpace()
    {
        var session = Create();
        var results = Type(session, "google ");
        Assert.Equal("google ", results[^1].Commits.Single().Text);
    }

    [Test]
    public static void KeysOutsideComposition_GoToTheApp()
    {
        var session = Create();
        Assert.True(!session.HandleKey(VirtualKeys.Left, null, false, false, false, false).Consumed, "変換ボックスが空なら矢印はアプリへ");
        Assert.True(!session.HandleKey('C', 'c', false, false, false, true).Consumed, "Command + C はアプリの操作");
        Assert.True(!Type(session, " ")[0].Consumed, "空白はアプリへ");
        session.Direct = true;
        Assert.True(!Type(session, "a")[0].Consumed, "英数 (直接入力) ならすべてアプリへ");
    }

    [Test]
    public static void ShortcutWhileComposing_CommitsThenPassesTheKey()
    {
        var session = Create();
        Type(session, "abc");
        var result = session.HandleKey('S', 's', false, false, false, true);
        Assert.True(!result.Consumed, "Command + S はアプリへ");
        Assert.True(result.Commits.Count == 1, "その前に変換ボックスの内容を確定する");
    }

    [Test]
    public static void ArrowWhileComposing_SelectsClauses()
    {
        var session = Create();
        Type(session, "kyouha");
        var result = session.HandleKey(VirtualKeys.Right, null, false, false, false, false);
        Assert.True(result.Consumed && result.View is { Converting: true }, "変換前の矢印は文節の選択に使う");
    }

    [Test]
    public static void Candidates_CanBeSelectedByIndex()
    {
        var session = Create();
        Type(session, "api ");
        var view = session.SelectCandidate(2).View!;
        Assert.Equal(2, view.SelectedIndex);
        var commit = Type(session, "\n")[0];
        Assert.Equal(view.Candidates[2], commit.Commits.Single().Text);
    }

    [Test]
    public static void Json_IsEscaped()
    {
        var result = new SessionResult(true, [new TextEdit(2, "a\"b\\c\n")], new CompositionView("x", ["y"], 0, true, "h", ["x"], 0));
        const string expected = """{"consumed":true,"commits":[{"deleteBefore":2,"text":"a\"b\\c\n"}],"view":{"text":"x","converting":true,"selectedIndex":0,"selectedClause":0,"hint":"h","candidates":["y"],"clauses":["x"],"suggestion":null,"meaning":null,"predictions":[],"selectedPrediction":-1}}""";
        Assert.Equal(expected, result.ToJson());
    }

    [Test]
    public static void Json_NumbersIgnoreCulture()
    {
        // 負号が U+2212 になるカルチャ (sv-SE など) でも、-1 は "-1" と書く (Swift の JSONDecoder が読めるように)
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            foreach (var name in new[] { "sv-SE", "nb-NO", "fi-FI", "fa-IR", "ar-SA" })
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(name);
                var result = new SessionResult(true, [new TextEdit(1, "x")], new CompositionView("x", ["y"], 0, true, "h", ["x"], -1));
                var json = result.ToJson();
                Assert.True(json.Contains("\"selectedClause\":-1") && json.Contains("\"selectedPrediction\":-1") && json.Contains("\"deleteBefore\":1"), $"{name}: 数は ASCII の - と数字で書く ({json})");
                using var document = System.Text.Json.JsonDocument.Parse(json);
                Assert.Equal(-1, document.RootElement.GetProperty("view").GetProperty("selectedClause").GetInt32(), $"{name}: JSON として読める");
            }
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = saved;
        }
    }

    [Test]
    public static void Json_IncludesOriginalOnlyWhenPresent()
    {
        var result = new SessionResult(true, [new TextEdit(1, "I ", "胃 "), new TextEdit(0, "x")], null);
        Assert.Equal("""{"consumed":true,"commits":[{"deleteBefore":1,"text":"I ","original":"胃 "},{"deleteBefore":0,"text":"x"}],"view":null}""", result.ToJson());
    }

    [Test]
    public static void AutoCorrect_JsonCarriesOriginal()
    {
        var session = Create();
        var results = Type(session, "i want ");
        var edit = results.SelectMany(r => r.Commits).Single(c => c.DeleteBefore > 0);
        Assert.True(edit.Original is { Length: > 0 }, "確定し直しの edit に消す文字列が入る");
        Assert.True(results.Any(r => r.ToJson().Contains("\"original\":")), "JSON に original が出る");
    }

    [Test]
    public static void ForgetLastCommit_StopsAutoCorrect()
    {
        var session = Create();
        Type(session, "i w");
        session.ForgetLastCommit(); // クリックでキャレットが動いた
        var results = Type(session, "ant ");
        Assert.True(!results.SelectMany(r => r.Commits).Any(c => c.DeleteBefore > 0), "クリックの後は確定し直さない");
    }

    [Test]
    public static void FunctionKeyChar_IsNotAddedToComposition()
    {
        // macOS は右矢印の characters に U+F703 (機能キーの文字) を入れてくる。英数字だけの変換ボックスで文字として足してはいけない。
        var session = Create();
        Type(session, "2");
        var result = session.HandleKey(VirtualKeys.Right, (char)0xF703, false, false, false, false);
        Assert.True(!result.Consumed, "英数字だけなら確定してキーはアプリへ");
        Assert.Equal("2", result.Commits.Single().Text);
        Assert.True(result.View is null, "変換ボックスは閉じる");
        Assert.True(!result.ToJson().Contains('\uF703'), "機能キーの文字が混ざらない");
    }

    [Test]
    public static void FunctionKeyChar_KanaEntersClauseSelection()
    {
        var session = Create();
        Type(session, "ka");
        var result = session.HandleKey(VirtualKeys.Right, (char)0xF703, false, false, false, false);
        Assert.True(result.Consumed && result.View is { Converting: true }, "かなは文節選択に入る");
        Assert.Equal("か", result.View!.Text);
    }

    [Test]
    public static void PassedThroughKey_StopsAutoCorrect()
    {
        // 「い」を確定した直後 (1.5 秒以内) に矢印を挟むと、続く want で確定し直さない。
        var session = Create();
        Type(session, "i w\b");
        var arrow = session.HandleKey(VirtualKeys.Right, null, false, false, false, false);
        Assert.True(!arrow.Consumed, "変換ボックスが無いので矢印はアプリへ");
        var results = Type(session, "want ");
        Assert.True(!results.SelectMany(r => r.Commits).Any(c => c.DeleteBefore > 0), "アプリに渡したキーの後は確定し直さない");
    }

    [Test]
    public static void FunctionKeyChars_AreDropped_BoundaryChecked()
    {
        foreach (var code in new[] { 0xF728, 0xF729, 0xF8FF })
        {
            var session = Create();
            Type(session, "2");
            var result = session.HandleKey(VirtualKeys.Right, (char)code, false, false, false, false);
            Assert.True(!result.ToJson().Contains((char)code), $"U+{code:X4} は変換ボックスに足さない");
        }
        // 範囲のすぐ外 (U+F6FF) は文字として通る。
        var outside = Create();
        Type(outside, "2");
        var passed = outside.HandleKey(VirtualKeys.Right, (char)0xF6FF, false, false, false, false);
        Assert.True(passed.ToJson().Contains('\uF6FF'), "U+F6FF は文字として通る");
    }

    [Test]
    public static void ShiftEnter_CommitsAndPassesKeyToApp()
    {
        var session = Create();
        Type(session, "aiueo");
        var result = session.HandleKey(VirtualKeys.Return, '\r', true, false, false, false);
        Assert.True(!result.Consumed, "Shift+Enter は改行をアプリに渡す");
        Assert.True(result.Commits.Any(c => c.Text.Length > 0), "確定したテキストがある");
    }

    [Test]
    public static void ShiftEnter_SettingOff_CommitsAndIsConsumed()
    {
        var session = new MeltypeSession(CompositionTests.Detector, new CompositionTests.FakeConverter(), new CompositionOptions { ShiftEnterNewline = () => false }, () => new Settings());
        Type(session, "aiueo");
        var result = session.HandleKey(VirtualKeys.Return, '\r', true, false, false, false);
        Assert.True(result.Consumed, "OFF のとき Shift+Enter は送り直さない");
        Assert.True(result.Commits.Any(c => c.Text.Length > 0), "確定したテキストがある");
    }

    [Test]
    public static void Enter_Alone_IsConsumed()
    {
        var session = Create();
        Type(session, "aiueo");
        var result = session.HandleKey(VirtualKeys.Return, '\r', false, false, false, false);
        Assert.True(result.Consumed && result.Commits.Any(c => c.Text.Length > 0), "Enter 単体は確定のみ");
    }

    [Test]
    public static void AddUserWord_RegistersOrReturnsReason()
    {
        var dictionary = new UserDictionary(null, builtIn: false);
        var session = new MeltypeSession(CompositionTests.Detector, new CompositionTests.FakeConverter(), new CompositionOptions { UserDictionary = dictionary }, () => new Settings());
        Assert.True(session.AddUserWord("ゆきしろ", "雪代") is null, "登録できたら null");
        Assert.Equal("雪代", dictionary.Lookup("ゆきしろ").Single());
        Assert.True(session.AddUserWord("あ", "亜") is not null, "1 文字の読みは理由を返す");
        Assert.True(Create().AddUserWord("ゆきしろ", "雪代") is null, "辞書が無ければ何もしない");
    }

    [Test]
    public static void Reconvert_HiraganaStartsConversion()
    {
        var session = Create();
        var result = session.Reconvert("きょうは");
        Assert.True(result.Consumed, "再変換を始めたらキーは使う");
        Assert.True(result.View is { Converting: true }, "読みに戻したらそのまま変換中になる");
        Assert.Equal("今日は", result.View?.Text);
        Assert.True(session.IsComposing, "変換ボックスが開いている");
    }

    [Test]
    public static void Reconvert_Katakana_IsReadAsHiragana()
    {
        var result = Create().Reconvert("キョウ");
        Assert.Equal("今日", result.View?.Text);
    }

    [Test]
    public static void Reconvert_ThenSpace_CyclesCandidates()
    {
        var session = Create();
        var first = session.Reconvert("きょうは").View!;
        // 関所が閉じていれば Space はアプリに素通りせず、次の候補に進む
        var space = session.HandleKey(VirtualKeys.Space, null, false, false, false, false);
        Assert.True(space.Consumed, "再変換のあとの Space は変換中の操作に使う");
        Assert.True(space.View!.SelectedIndex > first.SelectedIndex, "Space で次の候補へ進む");
        var enter = session.HandleKey(VirtualKeys.Return, null, false, false, false, false);
        Assert.True(enter.Consumed && enter.Commits.Count == 1, "Enter で選んだ候補を確定する");
        Assert.Equal(space.View.Candidates[space.View.SelectedIndex], enter.Commits.Single().Text);
        Assert.True(enter.View is null, "確定したら変換ボックスを閉じる");
    }

    [Test]
    public static void Reconvert_EscapeRestoresOriginalText()
    {
        var session = Create();
        session.Reconvert("きょうは");
        // 1 回目の Esc は変換を取り消して読みに戻る (まだ変換ボックスは開いている)
        var first = session.HandleKey(VirtualKeys.Escape, null, false, false, false, false);
        Assert.True(first.Consumed && first.Commits.Count == 0, "変換を戻しただけでは元の文字は書き戻さない");
        // 2 回目の Esc でボックスを空にして取り消す: 元の文字を書き戻す
        var second = session.HandleKey(VirtualKeys.Escape, null, false, false, false, false);
        Assert.True(second.Consumed, "Esc は変換ボックスの取り消しに使う");
        Assert.Equal("きょうは", second.Commits.Single().Text);
        Assert.True(second.View is null && !session.IsComposing, "変換ボックスは閉じる");
        // 書き戻しは 1 回だけ
        Type(session, "a");
        var again = session.HandleKey(VirtualKeys.Escape, null, false, false, false, false);
        Assert.True(again.Commits.Count == 0, "通常の入力の Esc では何も書き戻さない");
    }

    [Test]
    public static void Reconvert_EscapeRestoresKanjiOriginal_NotTheReading()
    {
        var history = new ConversionHistory(null);
        history.Remember("きょうは", "京は");
        var options = new CompositionOptions { History = history };
        var session = new MeltypeSession(CompositionTests.Detector, new CompositionTests.FakeConverter(), options, () => new Settings());
        session.Reconvert("京は");
        session.HandleKey(VirtualKeys.Escape, null, false, false, false, false);
        var cancel = session.HandleKey(VirtualKeys.Escape, null, false, false, false, false);
        Assert.Equal("京は", cancel.Commits.Single().Text);
    }

    [Test]
    public static void Reconvert_CommitThenEscape_DoesNotRestore()
    {
        var session = Create();
        session.Reconvert("きょうは");
        session.HandleKey(VirtualKeys.Return, null, false, false, false, false);
        Type(session, "a");
        var esc = session.HandleKey(VirtualKeys.Escape, null, false, false, false, false);
        Assert.True(esc.Commits.Count == 0, "確定したあとの Esc で古い元文字を出さない");
    }

    [Test]
    public static void Escape_WithoutReconvert_DoesNotCommit()
    {
        var session = Create();
        Type(session, "kyou");
        var esc = session.HandleKey(VirtualKeys.Escape, null, false, false, false, false);
        Assert.True(esc.Commits.Count == 0 && !session.IsComposing, "通常の入力の Esc は今までどおり消すだけ");
    }

    [Test]
    public static void Reconvert_UsesHistoryThenMeanings()
    {
        var history = new ConversionHistory(null);
        history.Remember("きょうは", "京は");
        var meanings = MeaningDictionary.Parse("橋\tはし\t川にかける通路。\n");
        var options = new CompositionOptions { History = history, Meanings = meanings };
        var session = new MeltypeSession(CompositionTests.Detector, new CompositionTests.FakeConverter(), options, () => new Settings());
        var byHistory = session.Reconvert("京は");
        Assert.True(byHistory.Consumed, "確定した誤変換は履歴から読みに戻せる");
        Assert.True(byHistory.View is { Converting: true }, "読みに戻して変換中になる (履歴で覚えた 京は が先頭)");
        session.CommitPending();
        Assert.True(session.Reconvert("橋").Consumed, "意味辞書の読みでも戻せる");
    }

    [Test]
    public static void Reconvert_UnknownText_PassesTheKeyThrough()
    {
        var session = Create();
        var result = session.Reconvert("漢字");
        Assert.True(!result.Consumed, "読みに戻せなければキーはアプリに渡す");
        Assert.True(!session.IsComposing, "変換ボックスは開かない");
        Assert.True(!session.HandleKey(VirtualKeys.Space, null, false, false, false, false).Consumed, "関所も閉じていない");
        session.ReadingProvider = text => text == "今日" ? "きょう" : null;
        Assert.Equal("今日", session.Reconvert("今日").View?.Text);
    }

    [Test]
    public static void Reconvert_IgnoredWhileComposingOrDirect()
    {
        var session = Create();
        Type(session, "kyou");
        Assert.True(!session.Reconvert("きょう").Consumed, "入力中は再変換しない");
        session.CommitPending();
        session.Direct = true;
        Assert.True(!session.Reconvert("きょう").Consumed, "英数 (直接入力) では再変換しない");
    }

    [Test]
    public static void ConversionHistory_ReadingOf()
    {
        var history = new ConversionHistory(null);
        history.Remember("かんじ", "漢字");
        Assert.Equal("かんじ", history.ReadingOf("漢字"));
        Assert.True(history.ReadingOf("感じ") is null, "覚えていない語は null");
    }

    [Test]
    public static void MeaningDictionary_ReadingOf()
    {
        var meanings = MeaningDictionary.Parse("橋\tはし\t川にかける通路。\n一\t\t数の最初。\n");
        Assert.Equal("はし", meanings.ReadingOf("橋"));
        Assert.True(meanings.ReadingOf("一") is null, "読みが空の行は飛ばす");
        Assert.True(meanings.ReadingOf("山") is null, "無い語は null");
    }

    private static MeltypeSession CreateWith(Settings settings) =>
        new(CompositionTests.Detector, new CompositionTests.FakeConverter(), new CompositionOptions(), () => settings);

    [Test]
    public static void SetApp_CodeStartsInDirect()
    {
        var settings = new Settings { AppRules = [new AppRule { Process = "com.example.Editor", Profile = AppProfile.Code }] };
        var session = CreateWith(settings);
        session.SetApp("com.example.Editor");
        Assert.Equal(AppProfile.Code, session.AppProfile);
        Assert.True(session.Direct, "「コード」のアプリは英数から始める");
        Assert.True(!Type(session, "a")[0].Consumed, "英数なのでキーはアプリへ");
        session.SetApp("com.apple.TextEdit");
        Assert.Equal(AppProfile.General, session.AppProfile);
    }

    [Test]
    public static void SetApp_MacCodeAppsAreCodeWithoutRule()
    {
        var session = CreateWith(new Settings { AppRules = [] });
        session.SetApp("com.apple.Terminal");
        Assert.Equal(AppProfile.Code, session.AppProfile);
        Assert.True(session.Direct, "行が無くても既定の一覧にある bundle ID はコード");

        var general = CreateWith(new Settings { AppRules = [new AppRule { Process = "com.apple.Terminal", Profile = AppProfile.General }] });
        general.SetApp("com.apple.Terminal");
        Assert.Equal(AppProfile.General, general.AppProfile);
        Assert.True(!general.Direct, "アプリ別設定の行があればそちらが優先");
    }

    [Test]
    public static void SetApp_DisabledAppPassesKeys()
    {
        var settings = new Settings { AppRules = [new AppRule { Process = "com.example.Remote", Enabled = false }] };
        var session = CreateWith(settings);
        session.SetApp("com.example.Remote");
        Assert.True(!session.AppEnabled, "アプリ別設定で OFF");
        Assert.True(!Type(session, "k")[0].Consumed, "OFF のアプリではキーをアプリへ渡す");
        Assert.True(!session.Reconvert("きょう").Consumed, "OFF のアプリでは再変換もしない");
        session.SetApp("com.apple.TextEdit");
        Assert.True(session.AppEnabled, "別のアプリに移ったら戻る");
        Assert.True(Type(session, "k")[0].Consumed, "一般のアプリでは日本語入力");
    }

    [Test]
    public static void SetApp_NullKeepsGeneral()
    {
        var session = Create();
        session.SetApp(null);
        Assert.Equal(AppProfile.General, session.AppProfile);
        Assert.True(session.AppEnabled && !session.Direct, "bundle ID が分からなければ何も変えない");
    }

    [Test]
    public static void SetApp_KindStartInEnglish()
    {
        var settings = new Settings
        {
            AppKinds = [new AppKind { Name = "チャット", StartInEnglish = true }],
            AppRules = [new AppRule { Process = "com.example.Chat", Kind = "チャット" }],
        };
        var session = CreateWith(settings);
        session.SetApp("com.example.Chat");
        Assert.True(session.Direct, "独自の種類で「最初は英数」なら英数から始める");
    }
}
