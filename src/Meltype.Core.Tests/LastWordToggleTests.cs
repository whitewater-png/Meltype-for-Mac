// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>
/// 確定した直後の語を 1 キーで日英トグル (F10 = 英字 / F6 = かな。1-2) と、学習の強度 (1-6)。
/// 入力欄は文字列 (Doc) で代え、本体が返した edit (消す文字数・元の文字・入れる文字) をそのまま当てて確かめる。
/// </summary>
internal static class LastWordToggleTests
{
    /// <summary>入力欄のつもり。Swift と同じく、消す前に中身 (Original) を確かめ、変換中でなければキャレットの前の文字 (最大 20 文字) を本体に渡す。</summary>
    private sealed class Editor
    {
        public MeltypeSession Session { get; }
        public LanguageMemory Memory { get; }
        public string Doc { get; private set; } = "";
        public SessionResult? Last { get; private set; }

        /// <summary>アプリに渡ったキー (Space・Enter・BackSpace) の文字を入力欄にも反映する (実際のアプリの入力欄と同じ。既定は反映しない)。</summary>
        public bool EchoPassedKeys { get; init; }

        public Editor(bool ctrlKeys = false, Func<string, bool>? commonJapanese = null, bool continueAfterConversion = false, bool tokMode = false)
        {
            Memory = new LanguageMemory(null) { IsCommonJapanese = commonJapanese };
            var options = new CompositionOptions { Languages = Memory, ToggleLastWordCtrlKeys = () => ctrlKeys, ContinueAfterConversion = () => continueAfterConversion, TokMode = () => tokMode };
            Session = new MeltypeSession(CompositionTests.Detector, new CompositionTests.FakeConverter(), options, () => new Settings());
        }

        private string? Before => Session.IsComposing ? null : Doc.Length > 20 ? Doc[^20..] : Doc;

        private SessionResult Apply(SessionResult result)
        {
            foreach (var edit in result.Commits)
            {
                if (edit.DeleteBefore > 0)
                {
                    Assert.True(Doc.Length >= edit.DeleteBefore && (edit.Original is null || Doc.EndsWith(edit.Original, StringComparison.Ordinal)), $"消す文字が入力欄と違う: Doc={Doc} edit={edit}");
                    Doc = Doc[..^edit.DeleteBefore];
                }
                Doc += edit.Text;
            }
            return Last = result;
        }

        public SessionResult Key(int vk, char? ch = null, bool shift = false, bool control = false)
        {
            var result = Apply(Session.HandleKey(vk, ch, shift, control, false, false, Before));
            if (EchoPassedKeys && !result.Consumed && !control)
            {
                if (vk == VirtualKeys.Space) Doc += " ";
                else if (shift) { }
                else if (vk == VirtualKeys.Return) Doc += "\n";
                else if (vk == VirtualKeys.Back && Doc.Length > 0) Doc = Doc[..^1];
            }
            return result;
        }

        public void Type(string text)
        {
            foreach (var c in text)
            {
                if (c == '\n') Key(VirtualKeys.Return);
                else if (c == ' ') Key(VirtualKeys.Space);
                else if (c == '\b') Key(VirtualKeys.Back);
                else if (!char.IsAsciiLetterOrDigit(c) && CompositionTests.FakeHost.JisKeys.TryGetValue(c, out var jis)) Key(jis.Vk, c, jis.Shift);
                else Key(char.IsAsciiLetter(c) ? char.ToUpperInvariant(c) : c, c, char.IsAsciiLetterUpper(c));
            }
        }

        public SessionResult F10() => Key(VirtualKeys.F10);
        public SessionResult F6() => Key(VirtualKeys.F6);
        public SessionResult F7() => Key(VirtualKeys.F7);
        public SessionResult Enter() => Key(VirtualKeys.Return);
        public SessionResult Esc() => Key(VirtualKeys.Escape);

        /// <summary>フォーカス移動・クリックなどの暗黙の確定。</summary>
        public SessionResult Pending() => Apply(Session.CommitPending());

        /// <summary>Swift が書き換えを確かめられず飛ばしたつもり: edit は当てず、本体に取消を伝える。</summary>
        public void Cancel() => Session.CancelToggle();
    }

    [Test]
    public static void F10_TurnsTheLastJapaneseWordIntoItsTyping_AndLearnsEnglish()
    {
        var e = new Editor();
        e.Type("nihongo\n");
        Assert.Equal("にほんご", e.Doc, "前提: ローマ字は日本語で確定");
        var result = e.F10();
        Assert.True(result.Consumed && result.Toggle, "直せる語があるので F10 は使う (トグル由来の印つき)");
        Assert.Equal("nihongo", e.Doc, "打った英字に戻る");
        var edit = result.Commits.Single();
        Assert.True(edit.DeleteBefore == 4 && edit.Original == "にほんご" && edit.Text == "nihongo", "消す文字と元の文字を添えて 1 回で置き換える (Swift が確かめられる)");
        Assert.True(result.ToJson().Contains("\"toggle\":true"), "JSON にもトグル由来の印");
        Assert.Equal(true, e.Memory.Get("nihongo"), "明示的に直したので、次から英字 (変換しない確定は、日本語の区間が 1 つなら 1 語として覚える)");
        Assert.True(!e.Session.IsComposing, "変換ボックスは出さない");

        var learned = new Editor();
        learned.Type("sushi\n");
        learned.F10();
        Assert.Equal("sushi", learned.Doc);
        Assert.Equal(true, learned.Memory.Get("sushi"), "1 語として曖昧な語は、明示的に直したので次から英字");
    }

    [Test]
    public static void OrdinaryResults_DoNotCarryTheToggleMark()
    {
        var e = new Editor();
        e.Type("nihongo");
        Assert.True(e.Last is { Toggle: false } && !e.Last.ToJson().Contains("toggle"), "ふつうの入力の JSON は今までと同じ形");
    }

    [Test]
    public static void F10_WithNothingToFix_PassesTheKeyThrough()
    {
        var e = new Editor();
        var result = e.F10();
        Assert.True(!result.Consumed && result.Commits.Count == 0, "確定した語が無ければ素通し");
        var english = new Editor();
        english.Type("hello api\n");
        Assert.Equal("hello api", english.Doc, "前提: 英語で確定");
        Assert.True(!english.F10().Consumed, "もう英字の語を F10 で英字にはできないので素通し");
        var japanese = new Editor();
        japanese.Type("nihongo\n");
        Assert.True(!japanese.F6().Consumed, "もうかなの語を F6 でかなにはできないので素通し");
        Assert.Equal("にほんご", japanese.Doc, "何も変えない");
    }

    [Test]
    public static void Toggle_NeedsTheInputFieldToShowTheWord_OtherwisePassesThrough()
    {
        // 入力欄の前の文字が読めない (null) / 記録と違うときは、キーを飲み込まない (R41-4)
        var session = new MeltypeSession(CompositionTests.Detector, new CompositionTests.FakeConverter(), new CompositionOptions { Languages = new LanguageMemory(null) }, () => new Settings());
        foreach (var c in "nihongo") session.HandleKey(char.ToUpperInvariant(c), c, false, false, false, false, "");
        session.HandleKey(VirtualKeys.Return, null, false, false, false, false, "");
        var unreadable = session.HandleKey(VirtualKeys.F10, null, false, false, false, false, null);
        Assert.True(!unreadable.Consumed && unreadable.Commits.Count == 0, "前の文字が読めないホストでは飲み込まない");

        var other = new MeltypeSession(CompositionTests.Detector, new CompositionTests.FakeConverter(), new CompositionOptions { Languages = new LanguageMemory(null) }, () => new Settings());
        foreach (var c in "nihongo") other.HandleKey(char.ToUpperInvariant(c), c, false, false, false, false, "");
        other.HandleKey(VirtualKeys.Return, null, false, false, false, false, "");
        var moved = other.HandleKey(VirtualKeys.F10, null, false, false, false, false, "べつのばしょ");
        Assert.True(!moved.Consumed && moved.Commits.Count == 0, "キャレットの前が記録と違うなら飲み込まない");
    }

    [Test]
    public static void Toggle_IsForgottenLikeAutoCorrect_AfterAPassedThroughKey_Click_AndTime()
    {
        var arrow = new Editor();
        arrow.Type("nihongo\n");
        arrow.Key(VirtualKeys.Right);
        Assert.True(!arrow.F10().Consumed, "キーをアプリに渡したあとは、キャレットが動いたかもしれないので直さない");

        var click = new Editor();
        click.Type("nihongo\n");
        click.Session.ForgetLastCommit();
        Assert.True(!click.F10().Consumed, "クリックのあとは直さない");

        var late = new Editor();
        late.Type("nihongo\n");
        Thread.Sleep(3200); // LastWordTrustMs (3 秒) を過ぎる
        Assert.True(!late.F10().Consumed, "時間が経ったら直さない (マウス押下が届かないアプリに備える)");
        Assert.Equal("にほんご", late.Doc, "何も変えない");

        var typed = new Editor();
        typed.Type("nihongo\nsu");
        typed.Esc();
        Assert.True(!typed.Session.IsComposing, "前提: 別の語を打って取り消した");
        Assert.True(typed.F10().Consumed, "取り消しただけなら、入力欄の中身は変わらないので、直前の語はまだ直せる");
        Assert.Equal("nihongo", typed.Doc);
    }

    [Test]
    public static void ReconvertedText_IsNotRecorded()
    {
        // 再変換 (選択 + Shift+Space) で入れ替えた文字は、打った英字ではないので対象にしない
        var e = new Editor();
        var result = e.Session.Reconvert("にほんご");
        Assert.True(result.Consumed, "前提: 再変換が始まる");
        e.Key(VirtualKeys.Return);
        Assert.True(!e.F10().Consumed, "再変換の確定は、確定後の F10 の対象にしない");
    }

    [Test]
    public static void SecondRule_FirstF10OnCommonJapaneseWordDoesNotLearnYet_AndExplainsOnce()
    {
        var e = new Editor(commonJapanese: word => word == "sushi");
        e.Type("sushi\n");
        e.F10();
        Assert.Equal("sushi", e.Doc, "置き換え自体は 1 回目から効く");
        Assert.True(e.Memory.Get("sushi") is null, "よく使う日本語は、1 回では英語として覚えない (2 回ルール)");
        Assert.Equal("もう一度英字にすると覚えます", e.Session.TakeSuggestionHint(), "1 回目は案内を出す");
        Assert.True(e.Session.TakeSuggestionHint() is null, "案内は 1 回だけ");
        // もう一度かなにして (Esc で書き戻して)、また同じ語を打って英字にすると覚える
        e.F6();
        e.Esc();
        Assert.Equal("sushi", e.Doc, "Esc 1 回で書き戻す");
        e.Type("kyouha\nsushi\n");
        Assert.Equal("sushiきょうはすし", e.Doc, "前提: 1 回目は覚えていないので、日本語の流れでは日本語で確定");
        e.F10();
        Assert.Equal("sushiきょうはsushi", e.Doc);
        Assert.Equal(true, e.Memory.Get("sushi"), "2 回目の英字で覚える");
        Assert.True(e.Session.TakeSuggestionHint() is null, "覚えたら案内は出さない");
    }

    [Test]
    public static void SecondRule_ExplicitFlipToEnglish_OfCommonJapaneseWord_StillNeedsTwoTimes()
    {
        // 日本語として覚えた語 (F6 → Enter) を、うっかり F10 1 回で英語にしない (R43-5。設計 §4-7)
        var e = new Editor(commonJapanese: word => word == "sushi");
        e.Type("sushi\n");
        e.F10();
        e.F6();
        e.Enter();
        Assert.Equal(false, e.Memory.Get("sushi"), "前提: 日本語として覚えた");
        e.F10();
        Assert.True(e.Memory.Get("sushi") is null, "F10 を 1 回押しただけでは、英語として効かない (2 回ルール)");
    }

    [Test]
    public static void F6_OpensAConversionBoxFromTheTyping_EnterLearnsJapanese()
    {
        var e = new Editor();
        e.Type("hello api\n");
        Assert.Equal("hello api", e.Doc, "前提: 英語で確定");
        var result = e.F6();
        Assert.True(result.Consumed && result.Toggle, "直せる語があるので F6 は使う");
        Assert.Equal("hello ", e.Doc, "入力欄の語は消す (変換ボックスに移る)");
        var delete = result.Commits.Single();
        Assert.True(delete.DeleteBefore == 3 && delete.Original == "api" && delete.Text == "", "消す文字と元の文字を添える");
        Assert.Equal("あぴ", result.View?.Text, "打った英字を読みにした変換ボックス");
        Assert.True(e.Session.IsComposing, "変換ボックスが開いている");
        Assert.True(e.Memory.Get("api") is null, "Enter で確定するまで学習しない");
        e.Enter();
        Assert.Equal("hello あぴ", e.Doc, "Enter で確定");
        Assert.Equal(false, e.Memory.Get("api"), "確定したとき、日本語として学習する");
        Assert.True(e.Last is { Toggle: false }, "確定の結果はふつうの結果");
    }

    [Test]
    public static void F6_Esc_PutsTheOriginalBack_WithoutLearning()
    {
        var e = new Editor();
        e.Type("hello api\n");
        e.F6();
        var result = e.Esc();
        Assert.Equal("hello api", e.Doc, "Esc 1 回で、消した語を書き戻す (R43-8)");
        Assert.True(!e.Session.IsComposing && result.View is null, "変換ボックスは閉じる");
        Assert.True(e.Memory.Get("api") is null, "Esc では学習しない");
        Assert.True(e.F6().Consumed, "書き戻した語は、また直せる");
    }

    [Test]
    public static void F10_InsideTheF6Box_ReturnsToTheTyping_AndDoesNotLearn()
    {
        // 往復: F6 で開いた変換ボックスで F10 は、打った英字どおりに戻す (R41-8)
        var e = new Editor();
        e.Type("hello api\n");
        e.F6();
        e.F10();
        Assert.Equal("hello api", e.Doc, "打った英字どおりに戻る");
        Assert.True(!e.Session.IsComposing, "変換ボックスは閉じる");
        Assert.True(e.Memory.Get("api") is null, "戻しただけなので、日本語としては覚えていない");
        // 戻した語は、もう一度 F6 で直せる
        Assert.True(e.F6().Consumed, "往復の続きができる");
        e.Enter();
        Assert.Equal("hello あぴ", e.Doc);
    }

    [Test]
    public static void Toggle_ClearsTheAutoCorrectRecord_SoTheHandFixedWordIsNotChangedBack()
    {
        // 英語の文脈で sushi を確定 → F6 でかなに直す → 続けて英語を打っても、自動の確定し直しが sushi に戻さない (R41-2)
        foreach (var next in new[] { "world", "the", "hello", "great", "meeting" })
        {
            var e = new Editor();
            e.Type("I love sushi ");
            Assert.True(e.Doc.EndsWith("sushi ", StringComparison.Ordinal), "前提: 英語の後なので sushi は英語 (Space で確定): " + e.Doc);
            e.F6();
            e.Enter();
            Assert.True(e.Doc.EndsWith("すし ", StringComparison.Ordinal), "かなに直した (空白は残る): " + e.Doc);
            e.Type(next + "\n");
            Assert.True(e.Doc.Contains("すし") && !e.Doc.Contains("sushi"), $"手で直した語を、自動が元に戻さない ({next}): " + e.Doc);
        }
    }

    [Test]
    public static void F6_ThenContinueTyping_BackspacingTheNewReadingAway_DoesNotPutTheOriginalBack()
    {
        // Q19 (査読 F3-1): F6 の箱で変換 → 続けて入力 (ContinueAfterConversion ON) → 新しい読みを消し切っても、
        // 前の文節が固定されている間は元の語を書き戻さない (書き戻すと二重になる)
        var e = new Editor(continueAfterConversion: true);
        e.Type("hello api\n");
        e.F6();
        e.Key(VirtualKeys.Space);
        Assert.True(e.Last is { View: { Converting: true } }, "前提: 箱の中で変換中");
        e.Type("neko");
        for (var i = 0; i < 4; i++) e.Key(VirtualKeys.Back);
        Assert.Equal("hello ", e.Doc, "固定した文節が残っている間は書き戻さない");
        Assert.True(e.Session.IsComposing, "前の文節の選択に戻るだけ");
        e.Type("\n");
        Assert.True(!e.Doc.Contains("api"), "確定しても元の語は二重にならない: " + e.Doc);
    }

    [Test]
    public static void F6_BackspacingTheReadingAway_PutsTheOriginalBack_WithoutLearning()
    {
        // Q19: F6 の変換ボックスで BackSpace (変換取消) → BackSpace で読みを消し切ったら、Esc と同じく消した語を書き戻す (消えたままにしない)
        var e = new Editor();
        e.Type("hello api\n");
        e.F6();
        Assert.Equal("hello ", e.Doc, "前提: 入力欄の語は変換ボックスに移った");
        e.Key(VirtualKeys.Back);
        Assert.True(e.Session.IsComposing && e.Last is { View: { Converting: false } }, "1 回目の BackSpace は変換の取消 (読みに戻る)");
        e.Key(VirtualKeys.Back);
        Assert.True(e.Session.IsComposing, "読みが 1 文字残っている");
        var result = e.Key(VirtualKeys.Back);
        Assert.True(result.Consumed, "消し切った BackSpace はキーを使う (アプリに渡して hello の o を消さない)");
        Assert.Equal("hello api", e.Doc, "読みを消し切ったら、消した語を書き戻す (Esc と同じ)");
        Assert.True(!e.Session.IsComposing && result.View is null, "変換ボックスは閉じる");
        Assert.True(e.Memory.Get("api") is null, "書き戻しでは学習しない");
        Assert.True(e.F6().Consumed, "書き戻した語は、また直せる (Esc と同じ記録の戻し)");
        e.Esc();
        Assert.Equal("hello api", e.Doc);
        // 続きの確定はふつうに通る (api の読みが次の確定に混ざらない)
        e.Type("neko\n");
        Assert.Equal("hello apiねこ", e.Doc, "次の確定は正常");
        Assert.True(e.Memory.Get("api") is null && e.Memory.Get("neko") is null, "学習に api が漏れない");

        // Space で確定した語 (末尾に半角スペース) も、空白ごと書き戻す
        var spaced = new Editor();
        spaced.Type("hello api ");
        spaced.F6();
        Assert.Equal("hello ", spaced.Doc, "前提");
        spaced.Type("\b\b\b");
        Assert.Equal("hello api ", spaced.Doc, "末尾の空白も F6 の前のまま");
        Assert.True(!spaced.Session.IsComposing, "変換ボックスは閉じる");
        spaced.Type("neko\n");
        Assert.Equal("hello api ねこ", spaced.Doc, "次の確定は正常");
    }

    [Test]
    public static void Reconvert_BackspacingTheReadingAway_StaysDeleted()
    {
        // ガード (Q19): 選択の再変換 (選択 + Shift+Space) は F6 の箱ではないので、読みを消し切ったら今までどおり書き戻さない (選択は消える)
        var e = new Editor();
        var result = e.Session.Reconvert("にほんご");
        Assert.True(result.Consumed && result.View is { Converting: true }, "前提: 再変換が始まる");
        e.Key(VirtualKeys.Back);
        Assert.True(e.Session.IsComposing && e.Last is { View: { Converting: false } }, "1 回目の BackSpace は変換の取消");
        SessionResult? last = null;
        for (var i = 0; i < 4 && e.Session.IsComposing; i++) last = e.Key(VirtualKeys.Back);
        Assert.True(!e.Session.IsComposing && last is { Consumed: true, View: null }, "読みを消し切ったら閉じる");
        Assert.Equal("", e.Doc, "書き戻さない (今までどおり)");
        Assert.Equal(0, last!.Commits.Count, "入力欄への書き込みは無い");
    }

    [Test]
    public static void Toggle_KeepsTheSpacesAroundTheWord()
    {
        // Space で確定した語 (末尾に半角スペース) は、直しても空白を残す
        var e = new Editor();
        e.Type("hello api ");
        Assert.Equal("hello api ", e.Doc, "前提: 英語 + Space で確定");
        e.F6();
        e.Enter();
        Assert.Equal("hello あぴ ", e.Doc, "かなにしても末尾の空白は残る");
        e.F10();
        Assert.Equal("hello api ", e.Doc, "英字に戻しても残る");
    }

    [Test]
    public static void CancelToggle_RestoresRecordAndLearning_AfterF10()
    {
        var e = new Editor();
        e.Type("sushi\n");
        var result = e.Session.HandleKey(VirtualKeys.F10, null, false, false, false, false, e.Doc);
        Assert.True(result.Consumed && result.Toggle && e.Memory.Get("sushi") == true, "前提: 直して学習した (Swift は edit を当てない)");
        e.Cancel();
        Assert.True(e.Memory.Get("sushi") is null, "取り消したので学習も戻る");
        Assert.True(e.Session.TakeSuggestionHint() is null, "案内も残さない");
        Assert.True(!e.F10().Consumed, "取消のあとは記録を捨てる (R43-7)");
        Assert.Equal("すし", e.Doc);
    }

    [Test]
    public static void CancelToggle_ClosesTheBox_AfterF6()
    {
        var e = new Editor();
        e.Type("hello api\n");
        var result = e.Session.HandleKey(VirtualKeys.F6, null, false, false, false, false, e.Doc);
        Assert.True(result.Consumed && result.Toggle && e.Session.IsComposing, "前提: 変換ボックスが開いた (Swift は消せず、edit を当てない)");
        e.Cancel();
        Assert.True(!e.Session.IsComposing, "取り消したので変換ボックスは閉じる");
        Assert.Equal("hello api", e.Doc, "入力欄は元のまま");
        Assert.True(e.Memory.Get("api") is null, "学習していない");
        Assert.True(!e.F6().Consumed, "取消のあとは記録を捨てる: 確かめられないアプリでもう一度押しても、また飛ばして取り消すだけ (R43-7)");
        Assert.Equal("hello api", e.Doc);
    }

    [Test]
    public static void CancelToggle_WithoutAToggle_DoesNothing()
    {
        var e = new Editor();
        e.Type("nihongo\n");
        e.Cancel();
        Assert.True(e.F10().Consumed, "取消の控えが無ければ何もしない。記録はそのまま");
        e.Type("api\n");
        e.Cancel(); // 直前はトグルではない
        Assert.Equal("nihongoapi", e.Doc);
    }

    [Test]
    public static void CtrlKeys_AreOffByDefault_AndWorkOnlyWhenSwitchedOn()
    {
        var off = new Editor();
        off.Type("nihongo\n");
        Assert.True(!off.Key(VirtualKeys.Oem7, control: true).Consumed, "既定では Ctrl+' は素通し (Ctrl は変換ボックスが空ならアプリの操作)");
        Assert.Equal("にほんご", off.Doc);

        var on = new Editor(ctrlKeys: true);
        on.Type("nihongo\n");
        Assert.True(on.Key(VirtualKeys.Oem7, control: true).Consumed, "ON なら Ctrl+' は F10 と同じ");
        Assert.Equal("nihongo", on.Doc);
        Assert.True(on.Key(0x4A, 'j', control: true).Consumed, "ON なら Ctrl+J は F6 と同じ");
        Assert.True(on.Session.IsComposing, "かなの変換ボックスが開く");

        var none = new Editor(ctrlKeys: true);
        Assert.True(!none.Key(0x4A, 'j', control: true).Consumed, "直せる語が無ければ、ON でも素通し");
    }

    [Test]
    public static void Toggle_TargetsTheWholeCommit_MixedEnglishAndJapanese()
    {
        // 記録は「確定 1 回分」まるごと (R43-1): 英語の区間があれば F6、日本語の区間があれば F10 が効く
        var e = new Editor();
        e.Type("kyouhaSushidesu\n");
        Assert.Equal("きょうはSushiです", e.Doc, "前提: 大文字で始めた Sushi だけ英字の確定: " + e.Doc);
        var f6 = e.F6();
        Assert.True(f6.Consumed && f6.Toggle, "英語の区間があるので F6 が効く");
        Assert.Equal("きょうはすしです", f6.View?.Text, "確定 1 回分まるごとをかなにして変換ボックスに出す");
        e.Enter();
        Assert.Equal("きょうはすしです", e.Doc);

        var j = new Editor();
        j.Type("kyouhasushidesu\n");
        Assert.Equal("きょうはすしです", j.Doc, "前提: 全部日本語の確定: " + j.Doc);
        var noEnglish = new Editor();
        noEnglish.Type("kyouhasushidesu\n");
        Assert.True(!noEnglish.F6().Consumed, "英語の区間が無いので F6 は素通し");
        var f10 = j.F10();
        Assert.True(f10.Consumed && f10.Toggle, "日本語の区間があるので F10 が効く");
        Assert.Equal("kyouhasushidesu", j.Doc, "確定 1 回分まるごとを打った英字に戻す");
    }

    [Test]
    public static void F6_Box_ImplicitCommit_CommitsTheCharactersButDoesNotLearn()
    {
        // フォーカス移動・クリック・英数キー・Ctrl ショートカットでの確定は、学習しない (R43-3)
        var implicitly = new Editor();
        implicitly.Type("hello api\n");
        implicitly.F6();
        implicitly.Pending();
        Assert.Equal("hello あぴ", implicitly.Doc, "文字は確定する");
        Assert.True(implicitly.Memory.Get("api") is null, "暗黙の確定では日本語として学習しない");

        var ctrl = new Editor();
        ctrl.Type("hello api\n");
        ctrl.F6();
        ctrl.Key(0x11);
        Assert.Equal("hello あぴ", ctrl.Doc, "Ctrl を押すと確定する");
        Assert.True(ctrl.Memory.Get("api") is null, "Ctrl ショートカットでの確定でも学習しない");

        var space = new Editor();
        space.Type("hello api ");
        space.F6();
        space.Enter();
        Assert.Equal(false, space.Memory.Get("api"), "Enter は学習する");
    }

    [Test]
    public static void F6_Box_ContinuedTyping_KeepsTheTrailingSpace_AndLearns()
    {
        // ContinueAfterConversion が ON: F6 の変換ボックスに続けて打つと文節を固定する。末尾の空白は消えず、学習も走る (R43-4)
        var e = new Editor(continueAfterConversion: true);
        e.Type("hello api ");
        Assert.Equal("hello api ", e.Doc, "前提: 英語 + Space で確定");
        e.F6();
        e.Type("wa");
        e.Enter();
        Assert.True(e.Doc.StartsWith("hello あぴ", StringComparison.Ordinal) && e.Doc.EndsWith(" ", StringComparison.Ordinal), "末尾の空白は消えない: " + e.Doc);
        Assert.Equal(false, e.Memory.Get("api"), "続けて打っても、Enter で日本語として学習する");
    }

    /// <summary>判定が Editor の学習を引くように、共有の検出器に付ける (テストの後で外す)。</summary>
    private static void WithMemory(Editor e, Action body)
    {
        CompositionTests.Detector.Memory = e.Memory;
        try { body(); }
        finally { CompositionTests.Detector.Memory = null; }
    }

    [Test]
    public static void F6_Enter_LearnsEachEnglishWord_NotTheWholeTyping()
    {
        // 実機の不具合 B: kyouhasushidesu → F6 → Enter で、まるごと (kyouhasushidesu) を覚えても、判定は区間の語 (sushi) で引くので効かなかった
        var e = new Editor();
        WithMemory(e, () =>
        {
            e.Memory.Remember("sushi", english: true, explicitChoice: true);
            e.Type("kyouhasushidesu\n");
            Assert.Equal("きょうはsushiです", e.Doc, "前提: sushi は英語として覚えてあるので、英語の区間になる: " + e.Doc);
            e.F6();
            e.Enter();
            Assert.Equal("きょうはすしです", e.Doc, "F6 → Enter で日本語に");
            Assert.Equal(false, e.Memory.Get("sushi"), "英語だった区間の語 (sushi) を日本語として覚える");
            Assert.True(e.Memory.Get("kyouhasushidesu") is null, "確定 1 回分まるごとは覚えない");
            e.Type("kyouhasushidesu\n");
            Assert.Equal("きょうはすしです" + "きょうはすしです", e.Doc, "もう一度打つと、sushi の区間も日本語になる: " + e.Doc);
        });
    }

    [Test]
    public static void F10_LearnsOnlyWhenTheCommitIsOneJapaneseSegment()
    {
        // 日本語の区間が 1 つだけ: 英字として覚える。もう一度打っても英字のまま
        var e = new Editor();
        WithMemory(e, () =>
        {
            e.Type("sushi\n");
            e.F10();
            Assert.Equal(true, e.Memory.Get("sushi"), "日本語 1 区間の確定は覚える");
            e.Type("sushi\n");
            Assert.Equal("sushisushi", e.Doc, "もう一度打っても英字のまま: " + e.Doc);
        });

        // 英語と日本語が混ざった確定: どの語を覚えるか決められないので何も覚えない (案内も出さない)
        var mixed = new Editor();
        WithMemory(mixed, () =>
        {
            mixed.Type("kyouhaSushidesu\n");
            Assert.True(mixed.F10().Toggle, "前提: 混ざった確定でも F10 は効く");
            Assert.Equal("kyouhaSushidesu", mixed.Doc);
            Assert.True(mixed.Memory.Get("sushi") is null && mixed.Memory.Get("kyouha") is null && mixed.Memory.Get("kyouhasushidesu") is null && mixed.Memory.Get("desu") is null, "何も覚えない");
            Assert.True(mixed.Session.TakeSuggestionHint() is null, "学習しないので 2 回ルールの案内も出さない");
        });
    }

    [Test]
    public static void F6_Enter_AfterSpaceCommit_LearnsTheEnglishWord()
    {
        // 査読 R44-1: Space で確定する経路 (今日はsushi + 空白) でも、F6 → Enter で sushi を日本語として覚える
        var e = new Editor();
        WithMemory(e, () =>
        {
            e.Memory.Remember("sushi", english: true, explicitChoice: true);
            e.Type("kyouhasushi ");
            Assert.Equal("きょうはsushi ", e.Doc, "前提: sushi は英語の区間のまま Space で確定: " + e.Doc);
            e.F6();
            e.Enter();
            Assert.Equal("きょうはすし ", e.Doc, "F6 → Enter で日本語に: " + e.Doc);
            Assert.Equal(false, e.Memory.Get("sushi"), "Space の確定でも、英語だった区間の語を日本語として覚える");
            Assert.True(e.Memory.Get("kyouhasushi") is null, "確定 1 回分まるごとは覚えない");
        });
    }

    [Test]
    public static void F10_AfterF6Enter_OfAMixedCommit_DoesNotLearnTheWholeTyping()
    {
        // 査読 R44-2: kyouhasushidesu → F6 → Enter → F10 で、文まるごとを英語として覚えない
        var e = new Editor();
        WithMemory(e, () =>
        {
            e.Memory.Remember("sushi", english: true, explicitChoice: true);
            e.Type("kyouhasushidesu\n");
            e.F6();
            e.Enter();
            Assert.Equal("きょうはすしです", e.Doc, "前提: F6 → Enter で日本語に");
            var before = e.Memory.Get("sushi");
            Assert.True(e.F10().Consumed, "前提: 3 秒以内なので F10 は文まるごとを英字に戻す");
            Assert.Equal("kyouhasushidesu", e.Doc);
            Assert.True(e.Memory.Get("kyouhasushidesu") is null, "文まるごとを英語として覚えない");
            Assert.Equal(before, e.Memory.Get("sushi"), "F6 で覚えた sushi はそのまま");
            Assert.True(e.Session.TakeSuggestionHint() is null, "学習しないので 2 回ルールの案内も出さない");
        });

        // 区間が 1 つで打った英字まるごと (sushi) を F6 → Enter した後の F10 は、その語を英語として覚え直せる
        var single = new Editor();
        WithMemory(single, () =>
        {
            single.Memory.Remember("sushi", english: true, explicitChoice: true);
            single.Type("sushi\n");
            single.F6();
            single.Enter();
            Assert.Equal("すし", single.Doc);
            single.F10();
            Assert.Equal("sushi", single.Doc);
            Assert.Equal(true, single.Memory.Get("sushi"), "区間が 1 つで打った英字まるごとなら、F10 で英語として覚え直せる");
        });
    }

    [Test]
    public static void F6_Enter_AfterF10_OfAMixedCommit_StillLearnsTheEnglishWord()
    {
        // 査読 R44-9: F10 → F6 → Enter でも、元の英語の区間 (sushi) を日本語として覚える
        var e = new Editor();
        WithMemory(e, () =>
        {
            e.Memory.Remember("sushi", english: true, explicitChoice: true);
            e.Type("kyouhasushidesu\n");
            Assert.True(e.F10().Toggle, "前提: 混ざった確定でも F10 は効く");
            Assert.Equal("kyouhasushidesu", e.Doc);
            e.F6();
            e.Enter();
            Assert.Equal("きょうはすしです", e.Doc, "F10 を挟んでも F6 → Enter で日本語に");
            Assert.Equal(false, e.Memory.Get("sushi"), "F10 を挟んでも、英語だった区間の語を日本語として覚える");
            Assert.True(e.Memory.Get("kyouhasushidesu") is null, "まるごとは覚えない");
        });
    }

    [Test]
    public static void F6_Enter_AdjacentEnglishWords_AreLearnedOneByOne()
    {
        // 査読 R44-3: 続く英語の語はつなげない (判定は語ごとに学習を引く)
        var e = new Editor();
        WithMemory(e, () =>
        {
            e.Memory.Remember("sushi", english: true, explicitChoice: true);
            e.Memory.Remember("ramen", english: true, explicitChoice: true);
            e.Type("kyouhasushiramen\n");
            Assert.Equal("きょうはsushiramen", e.Doc, "前提: 続く英語の語はどちらも英語の区間: " + e.Doc);
            e.F6();
            e.Enter();
            Assert.Equal(false, e.Memory.Get("sushi"), "語ごとに日本語として覚える (sushi)");
            Assert.Equal(false, e.Memory.Get("ramen"), "語ごとに日本語として覚える (ramen)");
            Assert.True(e.Memory.Get("sushiramen") is null, "つないだ語 (sushiramen) は覚えない");
        });
    }

    [Test]
    public static void F10_AfterCommit_LearnsOnlyOneWord()
    {
        // 査読 R44-8: 変換して確定したなら日本語の文節が 1 つのときだけ覚える。変換しない確定は文節が分からないので、日本語の区間が 1 つなら覚える
        var one = new Editor();
        one.Type("atsui ");
        one.Enter();
        Assert.Equal("熱い", one.Doc, "前提: 文節 1 つで変換して確定");
        one.F10();
        Assert.Equal(true, one.Memory.Get("atsui"), "変換して確定した日本語の文節が 1 つなら、F10 で覚える");

        var two = new Editor();
        two.Type("hiruka ");
        two.Enter();
        Assert.Equal("昼か", two.Doc, "前提: 文節 2 つで変換して確定");
        Assert.True(two.F10().Toggle, "前提: F10 そのものは効く");
        Assert.Equal("hiruka", two.Doc);
        Assert.True(two.Memory.Get("hiruka") is null, "文節が 2 つ以上なら文として扱い、覚えない");
        Assert.True(two.Session.TakeSuggestionHint() is null, "案内も出さない");

        // 変換しない確定 (文節が分からない): 日本語の区間が 1 つなら 1 語として覚える (nihongo を覚えるため。文まるごとも覚えるのは KNOWN_LIMITS)
        var sentence = new Editor();
        sentence.Type("kyouhasamui\n");
        Assert.True(sentence.F10().Toggle, "前提: F10 そのものは効く");
        Assert.Equal(true, sentence.Memory.Get("kyouhasamui"), "変換しない確定は、日本語の区間が 1 つなら覚える");
    }

    [Test]
    public static void AutoCorrect_IsUnchanged_ByTheLastWordRecord()
    {
        // 自動の確定し直し (_correctable) は、確定後のトグル用の記録を足しても今までどおり: i を Space で確定し、続けて英語を打つと I に直る
        var e = new Editor();
        e.Type("i want ");
        Assert.True(e.Doc.StartsWith("I want", StringComparison.Ordinal), "従来の確定し直し: " + e.Doc);
    }

    [Test]
    public static void AfterFirstSoundCommit_TheRestIsNotATarget()
    {
        // 1 音確定 (Shift+↓。ATOK スタイルのキー操作) のあとの残りの確定は、語の途中から始まる (kyouha の uha)。
        // 確定後の F10 で uha に置き換えたり、uha を英語として覚えたりしない
        var e = new Editor(tokMode: true);
        e.Type("kyouha");
        e.Key(VirtualKeys.Down, null, shift: true);
        Assert.Equal("きょ", e.Doc, "前提: 先頭の 1 音だけ確定");
        e.Enter();
        Assert.Equal("きょうは", e.Doc, "前提: 残りを Enter で確定");
        var result = e.F10();
        Assert.True(!result.Consumed, "残りの確定は確定後の F10 の対象にしない (キーはアプリへ)");
        Assert.Equal("きょうは", e.Doc);
        Assert.True(e.Memory.Get("uha") is null, "語の途中 (uha) を英語として覚えない");
    }

    [Test]
    public static void WhileTypingChoice_ThenAfterCommitToggle_DoesNotLearnTheWholeTyping()
    {
        // 入力中の F6 / F10 で決めて確定 → 確定後の F10 / F6 でも、打った英字まるごと (kyouhaamazondesu) を覚えない (R46-3)
        var kana = new Editor();
        kana.Type("kyouhaamazondesu");
        kana.F6();
        kana.Enter();
        Assert.True(!kana.Doc.Contains("amazon", StringComparison.Ordinal), "前提: 入力中の F6 でかなにして確定: " + kana.Doc);
        Assert.True(kana.F10().Toggle, "前提: 確定後の F10 そのものは効く");
        Assert.Equal("kyouhaamazondesu", kana.Doc);
        Assert.True(kana.Memory.Get("kyouhaamazondesu") is null && kana.Memory.Entries().All(e => e.Word != "kyouhaamazondesu"), "英語と日本語が混ざっていた入力は、確定後の F10 で文まるごとを英語として覚えない");

        var single = new Editor();
        single.Type("nihongo");
        single.F7();
        single.Enter();
        Assert.True(single.F10().Toggle, "前提: 確定後の F10 そのものは効く");
        Assert.True(single.Memory.Entries().Any(e => e.Word == "nihongo" && e.English), "自動の区間が 1 つなら、確定後の F10 で英語として覚える");

        var alpha = new Editor();
        alpha.Type("kyouhaamazondesu");
        alpha.F10();
        alpha.Enter();
        Assert.Equal("kyouhaamazondesu", alpha.Doc, "前提: 入力中の F10 で英字のまま確定");
        Assert.True(alpha.F6().Consumed, "前提: 確定後の F6 で変換ボックス");
        alpha.Enter();
        Assert.True(alpha.Memory.Entries().All(e => e.Word != "kyouhaamazondesu"), "確定後の F6 → Enter でも、打った英字まるごとを日本語として覚えない");
        Assert.True(alpha.Memory.Get("amazon") == false, "自動でも英語だった amazon は、日本語として覚える");
    }

    [Test]
    public static void FirstSoundCommit_RestIsNotAutoCorrected_AndForgetsThePreviousRecord()
    {
        // R46-1: 1 音確定の残り (kai の i) は語の途中なので、後ろの英語を見て I に直さない
        var rest = new Editor(tokMode: true);
        rest.Type("kai");
        rest.Key(VirtualKeys.Down, null, shift: true);
        rest.Enter();
        Assert.Equal("かい", rest.Doc, "前提: か を 1 音確定、残りの い を Enter で確定");
        rest.Type("want ");
        Assert.True(rest.Doc.StartsWith("かい", StringComparison.Ordinal), "残りの い を I に書き換えない: " + rest.Doc);

        // R46-2: 1 音確定は、前の確定の記録 (確定後の F10 の対象) を消す。はは → ha の は を 1 音確定すると末尾が「はは」に一致しても直さない
        var e = new Editor(tokMode: true);
        e.Type("haha\n");
        Assert.Equal("はは", e.Doc, "前提");
        e.Type("ha");
        e.Key(VirtualKeys.Down, null, shift: true);
        Assert.Equal("ははは", e.Doc, "前提: 最後の 1 音まで確定して入力は終わった");
        Assert.True(!e.Session.IsComposing, "前提: 入力中でない");
        var result = e.F10();
        Assert.True(!result.Consumed, "前の確定 (はは) の記録は消えているので、F10 はアプリへ");
        Assert.Equal("ははは", e.Doc);
        Assert.True(e.Memory.Get("haha") is null, "haha を覚えない");
    }

    [Test]
    public static void ToggleBox_ShiftDown_DoesNotCommitTheFirstSound()
    {
        // R47-1: 確定後の F6 で開いた変換ボックスでは、Shift+↓ で 1 音確定しない (F10 / Esc で元の語に戻すので、一部だけ確定すると あamazon のように二重になる)
        var e = new Editor(tokMode: true);
        e.Type("amazon\n");
        Assert.Equal("amazon", e.Doc, "前提: 自動で英語として確定");
        Assert.True(e.F6().Consumed && e.Session.IsComposing, "前提: 確定後の F6 で変換ボックス");
        e.Key(VirtualKeys.Back);
        e.Key(VirtualKeys.Down, null, shift: true);
        Assert.Equal("", e.Doc, "変換ボックスの中では 1 音確定しない (入力欄は空のまま): " + e.Doc);
        Assert.True(e.Session.IsComposing, "Shift+↓ は今までどおり文節の選択へ (入力は続く)");
        e.F10();
        e.Enter();
        Assert.Equal("amazon", e.Doc, "二重にならない");
        Assert.True(e.Memory.Get("amazon") is not false, "日本語として覚えない");
    }

    /// <summary>製品と同じく、英単語の判定にスペルチェッカーを使う。終わったら元に戻す (ほかのテストに漏らさない)。</summary>
    private static void WithSpellChecker(Action body)
    {
        var previous = CompositionTests.Detector.SpellChecker;
        CompositionTests.Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try { body(); }
        finally { CompositionTests.Detector.SpellChecker = previous; }
    }

    [Test]
    public static void ProtectedSpan_AfterCommit_F6_DoesNotLearn()
    {
        // 査読 R57 推奨 B: 確定後の F6 (TryToggleLastWord) は MeltypeSession からだけ呼ばれる。経路に届いたこと (Consumed・Toggle) を確かめてから、学習しないことを確かめる
        WithSpellChecker(() =>
        {
            foreach (var typed in new[] { "--Sushi", "~/Sushi", "$SUSHI" })
            {
                var e = new Editor();
                e.Type(typed + "\n");
                var f6 = e.F6();
                Assert.True(f6.Consumed && f6.Toggle, $"前提: 確定後の F6 が経路に届いた ({typed}): " + f6.ToJson());
                e.Enter();
                Assert.True(e.Memory.Get(typed) is null && e.Memory.Get("Sushi") is null && e.Memory.Get("sushi") is null && e.Memory.Get("SUSHI") is null, $"確定後の F6 → Enter で学習しない ({typed})");
            }
        });
    }

    [Test]
    public static void ProtectedSpan_AfterASpaceThePassedToTheApp_IsStillProtected()
    {
        // 査読 R57 必須 A: 日本語を確定してから、入力していない状態で Space (アプリに渡る) → /tmp/foo。
        // アプリに渡した Space を自分の確定の記録が知らないと、空白の後なのに語の頭と読めず、守れない
        WithSpellChecker(() =>
        {
            var spaced = new Editor { EchoPassedKeys = true };
            spaced.Type("fairuha");
            spaced.Key(VirtualKeys.Space);
            spaced.Enter();
            spaced.Key(VirtualKeys.Space);
            var before = spaced.Doc;
            Assert.True(before.EndsWith(' '), "前提: 確定のあと、アプリに渡した Space が入力欄に入っている: " + before);
            spaced.Type("/tmp/foo\n");
            Assert.Equal(before + "/tmp/foo", spaced.Doc, "空白の後の /tmp/foo は崩れない");

            // 空白が無ければ守らない (日本語の打ちかけ)
            var plain = new Editor { EchoPassedKeys = true };
            plain.Type("fairuha\n");
            plain.Type("/fuka\n");
            Assert.True(plain.Doc.EndsWith("ふか") && !plain.Doc.Contains("fuka"), "確定の直後の /fuka は守らない: " + plain.Doc);

            // アプリに渡した Space を BackSpace で消したら、記録からも消える
            var erased = new Editor { EchoPassedKeys = true };
            erased.Type("fairuha");
            erased.Key(VirtualKeys.Space);
            erased.Enter();
            erased.Key(VirtualKeys.Space);
            erased.Key(VirtualKeys.Back);
            erased.Type("/fuka\n");
            Assert.True(erased.Doc.EndsWith("ふか") && !erased.Doc.Contains("fuka"), "消した空白は記録から戻す: " + erased.Doc);

            // 査読 R62 任意 1: Shift+Space も選択が無ければアプリに空白が入るので、記録に足す
            var shifted = new Editor { EchoPassedKeys = true };
            shifted.Type("fairuha");
            shifted.Key(VirtualKeys.Space);
            shifted.Enter();
            shifted.Key(VirtualKeys.Space, shift: true);
            var beforeShifted = shifted.Doc;
            Assert.True(beforeShifted.EndsWith(' '), "前提: Shift+Space の空白が入力欄に入っている: " + beforeShifted);
            shifted.Type("/tmp/foo\n");
            Assert.Equal(beforeShifted + "/tmp/foo", shifted.Doc, "Shift+Space の後の /tmp/foo も崩れない");
        });
    }

    [Test]
    public static void Option_AfterANewline_DoesNotCountThePreviousLineAsPrecedingEnglish()
    {
        // 実機確認 A-5: 前の行が英語 (/tmp/foo) で終わり、改行したあとの --sorekara は「ーー」。同じ行の英語の後ろ (git --name) は今までどおり守る
        WithSpellChecker(() =>
        {
            var newline = new Editor { EchoPassedKeys = true };
            newline.Type("/tmp/foo\n");
            newline.Enter();
            newline.Type("--sorekara\n");
            Assert.Equal("/tmp/foo\nーーそれから", newline.Doc, "改行のあとは前の行の英語を直前とみなさない");

            var sameLine = new Editor { EchoPassedKeys = true };
            sameLine.Type("git --name\n");
            Assert.Equal("git --name", sameLine.Doc, "同じ行の英語の後ろは今までどおり守る");

            var short1 = new Editor { EchoPassedKeys = true };
            short1.Type("/tmp/foo\n");
            short1.Enter();
            short1.Type("-la\n");
            Assert.Equal("/tmp/foo\n-la", short1.Doc, "1〜2 文字は直前に関係なく守る");

            var unreadable = new Editor { EchoPassedKeys = true };
            unreadable.Type("/tmp/foo\n");
            unreadable.Enter();
            unreadable.Type("--no-install\n");
            Assert.Equal("/tmp/foo\n--no-install", unreadable.Doc, "読めない部分があるオプションは改行のあとも守る");
        });
    }

    [Test]
    public static void Option_AfterANewlineAndIndent_IsTreatedAsTheStartOfALine()
    {
        // 査読 R66: 改行のあとの字下げ (半角空白・タブ・全角空白) だけの行頭も、行頭とみなす。同じ行の英語の後ろは今までどおり
        WithSpellChecker(() =>
        {
            foreach (var (before, label) in new[] { ("/tmp/foo\n　", "全角空白"), ("/tmp/foo\n  ", "半角空白 2 つ"), ("/tmp/foo\n\t", "タブ"), ("/tmp/foo\r\n  ", "CRLF のあと") })
            {
                var k = new CompositionTests.Keyboard();
                k.Host.PrecedingText = before;
                k.Type("--sorekara\n");
                Assert.Equal("ーーそれから", k.Host.Document, $"改行 + {label} のあとは行頭");
            }

            var sameLine = new CompositionTests.Keyboard();
            sameLine.Host.PrecedingText = "git ";
            sameLine.Type("--name\n");
            Assert.Equal("--name", sameLine.Host.Document, "同じ行の英語の後ろは今までどおり守る");

            var noNewline = new CompositionTests.Keyboard();
            noNewline.Host.PrecedingText = "/tmp/foo  ";
            noNewline.Type("--sorekara\n");
            Assert.Equal("--sorekara", noNewline.Host.Document, "改行が無ければ空白だけでも行頭ではない");
        });
    }
}
