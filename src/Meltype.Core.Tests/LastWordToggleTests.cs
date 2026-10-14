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

        public Editor(bool ctrlKeys = false, Func<string, bool>? commonJapanese = null)
        {
            Memory = new LanguageMemory(null) { IsCommonJapanese = commonJapanese };
            var options = new CompositionOptions { Languages = Memory, ToggleLastWordCtrlKeys = () => ctrlKeys };
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
        Assert.Equal(true, e.Memory.Get("nihongo"), "明示的に直したので、次から英字");
        Assert.True(!e.Session.IsComposing, "変換ボックスは出さない");
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
        Thread.Sleep(1700); // OwnCommitTrustMs と同じ時間
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
        var e = new Editor(commonJapanese: word => word == "nihongo");
        e.Type("nihongo\n");
        e.F10();
        Assert.Equal("nihongo", e.Doc, "置き換え自体は 1 回目から効く");
        Assert.True(e.Memory.Get("nihongo") is null, "よく使う日本語は、1 回では英語として覚えない (2 回ルール)");
        Assert.Equal("もう一度英字にすると覚えます", e.Session.TakeSuggestionHint(), "1 回目は案内を出す");
        Assert.True(e.Session.TakeSuggestionHint() is null, "案内は 1 回だけ");
        // もう一度かなにして英字にすると覚える
        e.F6();
        e.Esc();
        e.Esc();
        Assert.Equal("nihongo", e.Doc, "Esc で書き戻す");
        e.F6();
        e.Enter();
        Assert.Equal("日本語", e.Doc, "かなの変換ボックスの先頭の候補で確定");
        e.F10();
        Assert.Equal(true, e.Memory.Get("nihongo"), "2 回目の英字で覚える。反転は Count = 2 で作るので、続けてもう 1 回は要らない");
        Assert.True(e.Session.TakeSuggestionHint() is null, "覚えたら案内は出さない");
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
        e.Esc(); // 変換をやめてかなに戻る
        Assert.True(e.Session.IsComposing && e.Doc == "hello ", "最初の Esc は変換をやめるだけ");
        var result = e.Esc();
        Assert.Equal("hello api", e.Doc, "もう一度 Esc で、消した語を書き戻す");
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
        e.Type("nihongo\n");
        var result = e.Session.HandleKey(VirtualKeys.F10, null, false, false, false, false, e.Doc);
        Assert.True(result.Consumed && result.Toggle && e.Memory.Get("nihongo") == true, "前提: 直して学習した (Swift は edit を当てない)");
        e.Cancel();
        Assert.True(e.Memory.Get("nihongo") is null, "取り消したので学習も戻る");
        Assert.True(e.Session.TakeSuggestionHint() is null, "案内も残さない");
        Assert.True(e.F10().Consumed, "記録も戻っているので、もう一度直せる");
        Assert.Equal("nihongo", e.Doc);
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
        Assert.True(e.F6().Consumed, "記録も戻っているので、もう一度直せる");
        Assert.Equal("hello ", e.Doc);
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
    public static void AutoCorrect_IsUnchanged_ByTheLastWordRecord()
    {
        // 自動の確定し直し (_correctable) は、確定後のトグル用の記録を足しても今までどおり: i を Space で確定し、続けて英語を打つと I に直る
        var e = new Editor();
        e.Type("i want ");
        Assert.True(e.Doc.StartsWith("I want", StringComparison.Ordinal), "従来の確定し直し: " + e.Doc);
    }
}
