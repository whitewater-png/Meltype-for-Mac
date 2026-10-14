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

        public Editor(bool ctrlKeys = false, Func<string, bool>? commonJapanese = null, bool continueAfterConversion = false)
        {
            Memory = new LanguageMemory(null) { IsCommonJapanese = commonJapanese };
            var options = new CompositionOptions { Languages = Memory, ToggleLastWordCtrlKeys = () => ctrlKeys, ContinueAfterConversion = () => continueAfterConversion };
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

        public SessionResult Key(int vk, char? ch = null, bool shift = false, bool control = false) =>
            Apply(Session.HandleKey(vk, ch, shift, control, false, false, Before));

        public void Type(string text)
        {
            foreach (var c in text)
            {
                if (c == '\n') Key(VirtualKeys.Return);
                else if (c == ' ') Key(VirtualKeys.Space);
                else Key(char.IsAsciiLetter(c) ? char.ToUpperInvariant(c) : c, c, char.IsAsciiLetterUpper(c));
            }
        }

        public SessionResult F10() => Key(VirtualKeys.F10);
        public SessionResult F6() => Key(VirtualKeys.F6);
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
        Assert.True(e.Memory.Get("nihongo") is null, "変換しない確定は文節が分からないので、1 語として曖昧な語 (sushi) でなければ覚えない");
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
        // 査読 R44-8: 変換して確定したなら日本語の文節が 1 つのときだけ、変換しない確定は 1 語として曖昧な語のときだけ覚える
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

        // 変換しない確定 (文節が分からない): 1 語として曖昧な語 (sushi) でなければ覚えない
        var sentence = new Editor();
        sentence.Type("kyouhasamui\n");
        Assert.True(sentence.F10().Toggle, "前提: F10 そのものは効く");
        Assert.True(sentence.Memory.Get("kyouhasamui") is null, "曖昧な語でないので覚えない");
        Assert.True(sentence.Session.TakeSuggestionHint() is null, "案内も出さない");
    }

    [Test]
    public static void AutoCorrect_IsUnchanged_ByTheLastWordRecord()
    {
        // 自動の確定し直し (_correctable) は、確定後のトグル用の記録を足しても今までどおり: i を Space で確定し、続けて英語を打つと I に直る
        var e = new Editor();
        e.Type("i want ");
        Assert.True(e.Doc.StartsWith("I want", StringComparison.Ordinal), "従来の確定し直し: " + e.Doc);
    }
}
