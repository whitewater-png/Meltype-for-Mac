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

    private static SessionResult CtrlH(MeltypeSession session) => session.HandleKey('H', '\b', false, true, false, false);

    private static SessionResult BackSpace(MeltypeSession session) => session.HandleKey(VirtualKeys.Back, '\u007f', false, false, false, false);

    [Test]
    public static void CtrlH_IsBackSpaceWhileComposing()
    {
        // 本家 #150 / PR #308: 入力中の Ctrl+H は BackSpace (確定しない)。変換中なら変換をやめてかなに戻す
        var session = Create();
        Type(session, "aiueo");
        var result = CtrlH(session);
        Assert.True(result.Consumed && result.Commits.Count == 0, "確定しない");
        Assert.Equal("あいうえ", result.View?.Text);

        Type(session, " ");
        Assert.True(session.IsComposing, "前提: 変換中 (Space のあと)");
        result = CtrlH(session);
        Assert.Equal("あいうえ", result.View?.Text);
        Assert.True(result.View is { Converting: false }, "変換をやめてかなに戻る");
        var enter = Type(session, "\n")[0];
        Assert.Equal("あいうえ", enter.Commits.Single().Text);

        // 入力が空なら、Ctrl+H はアプリの操作 (置換・Cocoa の削除) としてそのまま渡す
        var empty = CtrlH(session);
        Assert.True(!empty.Consumed && empty.Commits.Count == 0 && empty.View is null, "入力が空の Ctrl+H はアプリに渡す");
    }

    /// <summary>Ctrl+H と BackSpace を、同じ状態から押して結果 (JSON) と、そのあとの Enter の結果が同じか調べる。</summary>
    private static void AssertSameAsBackSpace(string name, Func<MeltypeSession> create, Action<MeltypeSession> setup, int presses = 1)
    {
        var back = create();
        var ctrlH = create();
        setup(back);
        setup(ctrlH);
        Assert.True(back.IsComposing && ctrlH.IsComposing, $"{name}: 前提: 入力中");
        for (var i = 0; i < presses; i++)
        {
            var expected = BackSpace(back);
            var actual = CtrlH(ctrlH);
            Assert.Equal(expected.ToJson(), actual.ToJson());
            Assert.True(actual.Consumed, $"{name}: Ctrl+H は使う");
        }
        Assert.Equal(back.IsComposing, ctrlH.IsComposing);
        Assert.Equal(Type(back, "\n")[0].ToJson(), Type(ctrlH, "\n")[0].ToJson());
    }

    [Test]
    public static void CtrlH_SameAsBackSpace_InEveryComposingState()
    {
        MeltypeSession Make(CompositionOptions? options = null) =>
            new(CompositionTests.Detector, new CompositionTests.FakeConverter(), options ?? new CompositionOptions(), () => new Settings());
        var predictionOptions = new CompositionOptions { PredictionMinLength = () => 1, Predictions = _ => ["きょうは", "きょうと"] };
        var continuing = new Func<MeltypeSession>(() => Make(new CompositionOptions { ContinueAfterConversion = () => true }));

        AssertSameAsBackSpace("入力中", () => Make(), s => Type(s, "aiueo"), presses: 2);
        AssertSameAsBackSpace("入力中 (1 音だけ)", () => Make(), s => Type(s, "ka"));
        AssertSameAsBackSpace("変換中", () => Make(), s => Type(s, "kyouha "));
        AssertSameAsBackSpace("変換中 (文節を選択)", () => Make(), s => { Type(s, "tanniwotoru "); s.HandleKey(VirtualKeys.Right, null, false, false, false, false); });
        AssertSameAsBackSpace("ライブ変換", () => Make(new CompositionOptions { LiveConversion = () => true }), s => Type(s, "kyouha"));
        AssertSameAsBackSpace("予測候補を表示中", () => Make(predictionOptions), s =>
        {
            var typed = Type(s, "kyou");
            Assert.True(typed[^1].View?.Predictions is { Count: > 0 }, "前提: 予測候補が出ている");
        });
        AssertSameAsBackSpace("予測候補を選択中", () => Make(predictionOptions), s =>
        {
            Type(s, "kyou");
            var selected = s.HandleKey(VirtualKeys.Tab, null, false, false, false, false);
            Assert.True(selected.View is { SelectedPrediction: >= 0 }, "前提: 予測候補を選んでいる");
        });
        AssertSameAsBackSpace("固定した文節 + 未変換", continuing, s => Type(s, "tanniwotoru ta"), presses: 2);
        AssertSameAsBackSpace("固定した文節 + 変換した続き", continuing, s => Type(s, "tanniwotoru tanni "), presses: 2);
        AssertSameAsBackSpace("固定した文節だけ (続きを消し切る)", continuing, s => Type(s, "tanniwotoru ta"), presses: 3);
    }

    [Test]
    public static void CtrlH_AfterShiftEnterCommit_PassesToApp()
    {
        // Shift+Enter で確定したあとは入力が空。Ctrl+H はアプリへ (Shift+Enter の設定が OFF でも同じ)
        foreach (var newline in new[] { true, false })
        {
            var session = new MeltypeSession(CompositionTests.Detector, new CompositionTests.FakeConverter(),
                new CompositionOptions { ShiftEnterNewline = () => newline }, () => new Settings());
            Type(session, "aiueo");
            session.HandleKey(VirtualKeys.Return, '\r', true, false, false, false);
            Assert.True(!session.IsComposing, "前提: 確定して空");
            Assert.True(!CtrlH(session).Consumed, $"ShiftEnterNewline={newline}: 空の Ctrl+H はアプリへ");
        }
    }

    [Test]
    public static void CtrlH_OtherModifiers_AreNotBackSpace()
    {
        // Ctrl+Shift+H・Option+H・Cmd+H は今までどおり (Ctrl+H だけが BackSpace)
        var session = Create();
        Type(session, "aiueo");
        var ctrlShift = session.HandleKey('H', '\b', true, true, false, false);
        Assert.True(!ctrlShift.Consumed && ctrlShift.Commits.Any(c => c.Text == "あいうえお"), "Ctrl+Shift+H は確定してアプリへ");

        session = Create();
        Type(session, "aiueo");
        var option = session.HandleKey('H', '˙', false, false, true, false);
        Assert.True(!option.Consumed && option.Commits.Any(c => c.Text == "あいうえお"), "Option+H は確定してアプリへ");

        session = Create();
        Type(session, "aiueo");
        var command = session.HandleKey('H', 'h', false, false, false, true);
        Assert.True(!command.Consumed && command.Commits.Any(c => c.Text == "あいうえお"), "Cmd+H (アプリを隠す) は確定してアプリへ");

        session = Create();
        Type(session, "aiueo");
        var ctrlOption = session.HandleKey('H', '\b', false, true, true, false);
        Assert.True(!ctrlOption.Consumed, "Ctrl+Option+H も BackSpace にしない");

        // 入力が空なら、どれもアプリへ
        Assert.True(!Create().HandleKey('H', 'h', false, false, false, true).Consumed, "空の Cmd+H はアプリへ");
        Assert.True(!Create().HandleKey('H', '\b', true, true, false, false).Consumed, "空の Ctrl+Shift+H はアプリへ");
    }

    [Test]
    public static void CtrlH_DirectAndDisabledApp_PassToApp()
    {
        var session = Create();
        session.Direct = true;
        var direct = CtrlH(session);
        Assert.True(!direct.Consumed && direct.Commits.Count == 0 && direct.View is null, "英数 (直接入力) の Ctrl+H はアプリへ");

        var settings = new Settings { AppRules = [new AppRule { Process = "com.example.Remote", Enabled = false }] };
        var off = new MeltypeSession(CompositionTests.Detector, new CompositionTests.FakeConverter(), new CompositionOptions(), () => settings);
        off.SetApp("com.example.Remote");
        Assert.True(!off.AppEnabled, "前提: アプリ別設定で OFF");
        Assert.True(!CtrlH(off).Consumed, "OFF のアプリの Ctrl+H はアプリへ");

        var disabled = new MeltypeSession(CompositionTests.Detector, new CompositionTests.FakeConverter(), new CompositionOptions(), () => new Settings { Enabled = false });
        Assert.True(!CtrlH(disabled).Consumed, "Meltype が OFF の Ctrl+H はアプリへ");
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
    public static void AutoCorrect_EditIsAlwaysFollowedByTheEditThatCarriesTheComposingText()
    {
        // Mac の Swift (InputController.apply) は、確定し直しの edit (deleteBefore > 0) の前に、入力欄の変換中の文字を取り除く
        // (取り除かないと、ホストが置き換え範囲を無視して変換中の範囲だけを置き換え、古い英語が消えない)。
        // 取り除いても変換中の文字が消えないのは、同じ結果の中で、確定し直しの edit の直後に deleteBefore = 0 の edit が続くから。
        // (確定し直し CorrectPreviousCommit の経路の約束。SwitchHeldToJapanese の純粋な削除 (text が空) は別で、そのとき変換中の文字は無い)
        var corrections = 0;
        foreach (var typed in new[] { "I love sushi gasuki\n", "New York no tenki\n", "i want\n", "I want to go susi\n" })
        {
            var session = Create();
            foreach (var result in Type(session, typed))
            {
                var edits = result.Commits.ToList();
                for (var i = 0; i < edits.Count; i++)
                {
                    if (edits[i].DeleteBefore == 0) continue;
                    corrections++;
                    Assert.True(i + 1 < edits.Count && edits[i + 1].DeleteBefore == 0 && edits[i + 1].Text.Length > 0,
                        typed.Trim() + ": 確定し直しの edit の直後に、変換中の文字を運ぶ edit が続く: " + result.ToJson());
                }
            }
        }
        Assert.True(corrections >= 3, "確定し直しの edit が実際に出ている (出なければ何も確かめていない): " + corrections);
    }

    [Test]
    public static void AutoCorrect_EnglishSentenceOnTheSecondLine_IsKept()
    {
        // 実機のトレース (2026-10-11): 自分の確定の記録は、アプリに素通ししたキー (変換中でない Enter の改行) を知らないので、
        // 2 行目の文頭の語が直前の行の最後の文字につながって見え (Hi therePlease take)、英文と読めず、take が たけ に直った。
        // 1 回目の Enter は確定、2 回目の Enter が改行 (アプリに渡す)。
        var session = Create();
        var results = Type(session, "Hi there\n\nPlease take susi\n");
        var corrections = results.SelectMany(r => r.Commits).Where(c => c.DeleteBefore > 0).Select(c => c.Original).ToList();
        Assert.True(!corrections.Contains("take "), "2 行目の Please の後ろの take を、後ろの日本語に合わせて直さない: " + string.Join("|", corrections));
        // 同じ行なら今までどおり
        var oneLine = Type(Create(), "Please take susi\n").SelectMany(r => r.Commits).Where(c => c.DeleteBefore > 0).Select(c => c.Original).ToList();
        Assert.True(!oneLine.Contains("take "), "1 行目でも直さない");

        // Tab (アプリに渡す) の直後の行頭も同じ
        var tab = Create();
        Type(tab, "Hi there\n");
        tab.HandleKey(VirtualKeys.Tab, null, false, false, false, false, null);
        var tabCorrections = Type(tab, "Please take susi\n").SelectMany(r => r.Commits).Where(c => c.DeleteBefore > 0).Select(c => c.Original).ToList();
        Assert.True(!tabCorrections.Contains("take "), "Tab の直後の行頭: " + string.Join("|", tabCorrections));

        // 改行を BackSpace で消したら、記録からも消える (I want ⏎⌫to go susi は同じ行の英文)
        var back = Create();
        var backResults = Type(back, "I want \n\bto go susi\n");
        // 記録に改行が残ると、to・go が最初の確定の時点で と・ご になる (I want とごすし)
        var backTexts = backResults.SelectMany(r => r.Commits).Select(c => c.Text).ToList();
        Assert.True(!backTexts.Any(t => t.Contains('と') || t.Contains('ご')), "BackSpace で消した改行は記録から戻す: " + string.Join("|", backTexts));

        // Tab を BackSpace で消した場合も同じ (査読 R16)
        var tabBack = Create();
        var tabBackResults = Type(tabBack, "I want ");
        tabBack.HandleKey(VirtualKeys.Tab, null, false, false, false, false, null);
        tabBackResults.AddRange(Type(tabBack, "\bto go susi\n"));
        var tabBackTexts = tabBackResults.SelectMany(r => r.Commits).Select(c => c.Text).ToList();
        Assert.True(!tabBackTexts.Any(t => t.Contains('と') || t.Contains('ご')), "BackSpace で消したタブも記録から戻す: " + string.Join("|", tabBackTexts));

        // Shift+Return は改行 (チャットアプリは Enter が送信で、改行は Shift+Enter。Hermes の実機のトレースで見つかった)
        var shiftReturn = Create();
        Type(shiftReturn, "Hi there\n");
        shiftReturn.HandleKey(VirtualKeys.Return, null, true, false, false, false, null);
        var shiftCorrections = Type(shiftReturn, "Please take susi\n").SelectMany(r => r.Commits).Where(c => c.DeleteBefore > 0).Select(c => c.Original).ToList();
        Assert.True(!shiftCorrections.Contains("take "), "Shift+Return の直後の行頭: " + string.Join("|", shiftCorrections));

        // 修飾キー付きの Return・Tab (Cmd+Return・Shift+Tab) は、改行・タブが入るとは限らないので記録に足さない (査読 R15・R16)
        foreach (var (vk, shift, command) in new[] { (VirtualKeys.Return, false, true), (VirtualKeys.Tab, true, false) })
        {
            var modified = Create();
            var modifiedResults = Type(modified, "I want ");
            modified.HandleKey(vk, null, shift, false, false, command, null);
            modifiedResults.AddRange(Type(modified, "to go susi\n"));
            var texts = modifiedResults.SelectMany(r => r.Commits).Select(c => c.Text).ToList();
            Assert.True(!texts.Any(t => t.Contains('と') || t.Contains('ご')), $"修飾キー付き (vk={vk}) は改行を足さない: " + string.Join("|", texts));
        }
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

    /// <summary>失敗させる回数を指定できる変換器 (failures が負なら毎回失敗)。</summary>
    private sealed class ThrowingConverter(int failures) : IKanjiConverter
    {
        private int _left = failures;
        private readonly CompositionTests.FakeConverter _inner = new();

        private void MaybeThrow()
        {
            if (_left == 0) return;
            if (_left > 0) _left--;
            throw new InvalidOperationException("テスト用の例外");
        }

        public string? Convert(string hiragana)
        {
            MaybeThrow();
            return _inner.Convert(hiragana);
        }

        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null)
        {
            MaybeThrow();
            return _inner.ConvertClauses(hiragana, context);
        }
    }

    private static MeltypeSession CreateWith(IKanjiConverter converter) =>
        new(CompositionTests.Detector, converter, new CompositionOptions(), () => new Settings());

    [Test]
    public static void Exception_ResetsAndDoesNotThrow()
    {
        var session = CreateWith(new ThrowingConverter(-1));
        Type(session, "kyou");
        var space = Type(session, " ")[0]; // 変換で例外になる
        Assert.True(!space.Consumed, "例外のときはキーをアプリに渡す");
        Assert.True(space.View is null, "例外のときは変換ボックスを閉じる");
        Assert.True(!session.IsComposing, "例外のあとは未確定の内容を捨てる");
        Assert.Equal("きょう", space.Commits.Single().Text); // 画面に出ていた未確定の文字は、消さずにそのまま確定する
    }

    [Test]
    public static void Exception_NextKeyWorks()
    {
        var session = CreateWith(new ThrowingConverter(1));
        Type(session, "kyou");
        Type(session, " "); // 1 回だけ例外
        var results = Type(session, "kyouha");
        Assert.True(results.All(r => r.Consumed), "例外のあとも普通に入力できる");
        Assert.Equal("きょうは", results[^1].View?.Text);
        var enter = Type(session, "\n")[0];
        Assert.Equal("きょうは", enter.Commits.Single().Text);
    }

    [Test]
    public static void KanaTyping_WorksWithoutTouchingConverter()
    {
        var session = CreateWith(new ThrowingConverter(-1));
        for (var round = 0; round < 3; round++)
        {
            var results = Type(session, "kyouha");
            Assert.True(results[0].Consumed, "かな入力は変換器を使わないので続けられる");
            Assert.Equal("きょうは", results[^1].View?.Text);
            var enter = Type(session, "\n")[0];
            Assert.Equal("きょうは", enter.Commits.Single().Text);
        }
    }

    [Test]
    public static void Exception_AfterReset_CommitPendingIsHarmless()
    {
        var session = CreateWith(new ThrowingConverter(-1));
        Type(session, "kyou");
        Type(session, " ");
        var result = session.CommitPending();
        Assert.True(result.View is null, "立て直したあとのフォーカス喪失の確定は何も残さず返る");
    }

    /// <summary>Armed の間 (と、predicate が当たるとき) だけ例外を投げる変換器。</summary>
    private sealed class ArmedConverter : IKanjiConverter
    {
        public bool Armed { get; set; }
        public Func<string, bool>? ThrowWhen { get; init; }
        private readonly CompositionTests.FakeConverter _inner = new();

        private void MaybeThrow(string hiragana)
        {
            if (Armed || ThrowWhen?.Invoke(hiragana) == true) throw new InvalidOperationException("テスト用の例外");
        }

        public string? Convert(string hiragana)
        {
            MaybeThrow(hiragana);
            return _inner.Convert(hiragana);
        }

        public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null)
        {
            MaybeThrow(hiragana);
            return _inner.ConvertClauses(hiragana, context);
        }
    }

    [Test]
    public static void Exception_KeepsTypedKanaAsCommit()
    {
        var converter = new ArmedConverter();
        var session = CreateWith(converter);
        Type(session, "kyouha");
        converter.Armed = true;
        var result = Type(session, " ")[0];
        Assert.Equal("きょうは", result.Commits.Single().Text);
        Assert.True(!result.Consumed && result.View is null, "キーはアプリに渡し、変換ボックスは閉じる");
    }

    [Test]
    public static void Exception_InLiveConversion_KeepsShownText()
    {
        // ライブ変換で、4 文字目の「は」を打った瞬間に変換器が例外を投げる。直前まで画面に出ていた文字を、1 度だけ確定する。
        var session = new MeltypeSession(CompositionTests.Detector, new ArmedConverter { ThrowWhen = h => h == "きょうは" },
            new CompositionOptions { LiveConversion = () => true }, () => new Settings());
        var typed = Type(session, "kyouh");
        var shown = typed[^1].View!.Text;
        var failed = Type(session, "a")[0];
        Assert.True(failed.Commits.Count == 1 && failed.Commits[0].Text == shown, $"画面に出ていた「{shown}」を 1 度だけ確定する (実際: {string.Concat(failed.Commits.Select(c => c.Text))})");
        Assert.True(!session.IsComposing && failed.View is null, "例外のあとは入力が空");
    }

    [Test]
    public static void SelectPrediction_WhenProviderWouldThrow_StillLeavesInputEmpty()
    {
        var armed = false;
        var session = new MeltypeSession(CompositionTests.Detector, new CompositionTests.FakeConverter(),
            new CompositionOptions
            {
                Predictions = _ => armed ? throw new InvalidOperationException("テスト用の例外") : ["きょうは", "きょうと"],
            }, () => new Settings());
        var typed = Type(session, "kyou");
        Assert.True(typed[^1].View?.Predictions is { Count: > 0 }, "前提: 予測候補が出ている");
        armed = true;
        var result = session.SelectPrediction(0);
        Assert.True(result.View is null && !session.IsComposing, "例外のあとは変換ボックスが空");
    }

    [Test]
    public static void Exception_InReconvert_IsRecovered()
    {
        var converter = new ArmedConverter { Armed = true };
        var session = CreateWith(converter);
        var result = session.Reconvert("きょうは");
        Assert.True(!result.Consumed && result.View is null && !session.IsComposing, "例外のあとは変換ボックスが空");
        converter.Armed = false;
        Assert.True(Type(session, "k")[0].Consumed, "例外のあとも入力できる");
    }

    [Test]
    public static void CommitPending_AfterEarlierFailure_LeavesInputEmpty()
    {
        var converter = new ArmedConverter();
        var session = CreateWith(converter);
        Type(session, "kyou");
        Type(session, " ");
        converter.Armed = true;
        var result = session.CommitPending();
        Assert.True(result.View is null && !session.IsComposing, "例外のあとは変換ボックスが空");
    }

    [Test]
    public static void Exception_Twice_DoesNotCommitShownTextAgain()
    {
        // 予測候補の関数が毎回例外になる。1 回目で画面の「きょう」を確定したら、2 回目で同じ文字をもう一度確定してはいけない。
        var session = new MeltypeSession(CompositionTests.Detector, new CompositionTests.FakeConverter(),
            new CompositionOptions { PredictionMinLength = () => 1, Predictions = h => h.Contains('あ') ? throw new InvalidOperationException("テスト用の例外") : [] }, () => new Settings());
        Type(session, "kyou");
        var first = Type(session, "a")[0];
        var second = Type(session, "a")[0];
        Assert.Equal("きょう", string.Concat(first.Commits.Select(c => c.Text)));
        Assert.Equal("", string.Concat(second.Commits.Select(c => c.Text)));
    }

    [Test]
    public static void Exception_AfterCommitInSameKey_DoesNotCommitShownTextAgain()
    {
        // 変換中に「a」を打つ → 「今日」を確定し、同じ回で新しい入力「あ」の予測候補を調べて例外。確定済みの「今日」をもう一度足さない。
        var session = new MeltypeSession(CompositionTests.Detector, new CompositionTests.FakeConverter(),
            new CompositionOptions { PredictionMinLength = () => 1, Predictions = h => h.Contains('あ') ? throw new InvalidOperationException("テスト用の例外") : [] }, () => new Settings());
        Type(session, "kyou");
        Type(session, " ");
        var result = Type(session, "a")[0];
        Assert.Equal("今日", string.Concat(result.Commits.Select(c => c.Text)));
    }

    // ---- かな入力 (JIS)。Mac では Swift が物理キーを JIS かな配列の仮想キーにして渡す ----

    /// <summary>本番 (MeltypeSession.CreateDefault) と同じく、かな入力かは設定の「入力方式」から決める。</summary>
    private static MeltypeSession CreateKana(InputStyle style)
    {
        var settings = new Settings { InputStyle = style };
        return new MeltypeSession(CompositionTests.Detector, new CompositionTests.FakeConverter(),
            new CompositionOptions { KanaInput = () => settings.InputStyle == InputStyle.Kana }, () => settings);
    }

    /// <summary>仮想キーを順に打つ (shift: その打鍵で Shift を押す。文字は Mac と同じく Shift 付きの文字を渡す)。</summary>
    private static List<SessionResult> TypeVk(MeltypeSession session, params (int Vk, char Ch, bool Shift)[] keys) =>
        keys.Select(k => session.HandleKey(k.Vk, k.Ch, k.Shift, false, false, false)).ToList();

    [Test]
    public static void KanaInput_Setting_Off_NumberKeyIsStillASymbol()
    {
        // 既定 (ローマ字入力) では、かなのキーの扱いは変わらない。数字キーは今までどおり記号・数字として始まる。
        var session = CreateKana(InputStyle.Romaji);
        var results = TypeVk(session, (0x34, '4', false));
        Assert.Equal("4", results[0].View?.Text, "ローマ字入力では数字のまま");
    }

    [Test]
    public static void KanaInput_TypesJisKanaFromVirtualKeys()
    {
        var session = CreateKana(InputStyle.Kana);
        // か (T) な (U) → 「かな」。文字 (ch) は英字のまま渡されるが、かなのキーなので仮想キーから決まる。
        var results = TypeVk(session, (0x54, 't', false), (0x55, 'u', false));
        Assert.True(results.All(r => r.Consumed), "かなのキーはアプリに渡さない");
        Assert.Equal("かな", results[^1].View?.Text);
        var enter = Type(session, "\n")[0];
        Assert.Equal("かな", enter.Commits.Single().Text);
    }

    [Test]
    public static void KanaInput_NumberKeyStartsComposition_AndDakutenCombines()
    {
        var session = CreateKana(InputStyle.Kana);
        // 数字キー (4 = う) でも入力を始める。か + ゛ (@ のキー = 0xC0) → が。
        var first = TypeVk(session, (0x34, '4', false));
        Assert.True(first[0].Consumed, "数字キーもかなのキーなので入力を始める");
        Assert.Equal("う", first[0].View?.Text);
        session.CommitPending();
        var results = TypeVk(session, (0x54, 't', false), (0xC0, '@', false));
        Assert.Equal("が", results[^1].View?.Text, "濁点は直前のかなに付く");
    }

    [Test]
    public static void KanaInput_ShiftGivesSmallKana()
    {
        var session = CreateKana(InputStyle.Kana);
        // き (G)・Shift + Z = っ・と (S) → きっと。辞書に無いかな列は英字として見せる判定なので、辞書にある語で確かめる。
        var results = TypeVk(session, (0x47, 'g', false), (0x5A, 'Z', true), (0x53, 's', false));
        Assert.Equal("きっと", results[^1].View?.Text);
    }

    [Test]
    public static void KanaInput_ControlKeysAreNotKana()
    {
        // Cmd+C などはアプリの操作。かな入力でも、変換ボックスが空なら触らない。
        var session = CreateKana(InputStyle.Kana);
        var result = session.HandleKey(0x43, 'c', false, false, false, true);
        Assert.True(!result.Consumed, "Command と一緒のキーは素通し");
        Assert.True(!session.IsComposing, "変換ボックスは開かない");
    }

    // ---- US 配列 (docs/US_LAYOUT_PLAN.md)。Swift (KeyMapping) は、打った文字をそのまま渡し、US 配列の記号キーは仮想キー 0x07 (文字を入力するキー) になる ----

    /// <summary>Mac の IME が、US 配列の記号キーを本体に渡す形で 1 文字打つ (Shift の有無は文字から決める)。</summary>
    private static SessionResult TypeUsSymbol(MeltypeSession session, char c) =>
        session.HandleKey(0x07, c, shift: c is '~' or '!' or '@' or '#' or '$' or '%' or '^' or '&' or '*' or '(' or ')' or '_' or '+' or '{' or '}' or '|' or ':' or '"' or '<' or '>' or '?',
            control: false, alt: false, command: false);

    [Test]
    public static void UsLayout_Backslash_StaysHalfWidthInJapanese()
    {
        // US 配列の \ キー。日本語の途中でも、全角の ￥ にせず半角の \ のまま入る (パス・エスケープ・正規表現)。
        var session = Create();
        Type(session, "kyouha");
        var result = TypeUsSymbol(session, '\\');
        Assert.True(result.Consumed, "\\ は変換ボックスに入れる (アプリには渡さない)");
        Assert.Equal("きょうは\\", result.View?.Text);
        var enter = Type(session, "\n")[0];
        Assert.Equal("きょうは\\", enter.Commits.Single().Text);
    }

    [Test]
    public static void UsLayout_Backslash_StartsComposition_AndStaysHalfWidthWithFullWidthSymbolsOn()
    {
        // 変換ボックスが空のときの \ でも入力を始める。「記号を全角にする」が ON (既定) でも \ は全角にならない。
        var session = CreateWith(new Settings { FullWidthSymbols = true });
        var first = TypeUsSymbol(session, '\\');
        Assert.True(first.Consumed, "変換ボックスが空でも \\ で入力を始める");
        Assert.Equal("\\", first.View?.Text);
        Assert.Equal("\\", Type(session, "\n")[0].Commits.Single().Text);
    }

    [Test]
    public static void UsLayout_SymbolKeys_BecomeTheExpectedCharacters()
    {
        // US 配列で打つ記号 (Shift を押すものを含む)。日本語の中での表記は JIS と同じ (括弧・: ; | @ # は半角、! ? ~ ` は全角)。
        // ` は後ろが英語になれば半角に戻る。既存の Backtick_BeforeEnglish_StaysHalfWidth を参照。
        // _ と + は、英字の識別子 (snake_case・C++) の一部と見て、全体を英字のままにする設計 (CompositionDetector の IsNameUnit・IsPart) なので、ここには入れない。
        // これは JIS でも US でも同じ。
        var cases = new (char Typed, string Expected)[]
        {
            ('`', "｀"), ('~', "～"), ('!', "！"), ('@', "@"), ('#', "#"), ('$', "＄"), ('%', "％"), ('^', "＾"), ('&', "＆"), ('*', "＊"),
            ('(', "("), (')', ")"), ('=', "＝"), ('{', "｛"), ('}', "｝"), ('|', "|"), (':', ":"), (';', ";"),
            ('"', "”"), ('\'', "’"), ('<', "＜"), ('>', "＞"), ('?', "？"), ('\\', "\\"),
        };
        foreach (var (typed, expected) in cases)
        {
            var session = Create();
            Type(session, "kyouha");
            var result = TypeUsSymbol(session, typed);
            Assert.Equal("きょうは" + expected, result.View?.Text, $"US 配列の「{typed}」");
        }
    }
}
