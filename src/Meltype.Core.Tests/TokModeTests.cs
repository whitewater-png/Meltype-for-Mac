// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>
/// ATOK スタイルのキー操作 (Settings.TokMode)。ATOK の「キーの機能一覧：ATOKスタイル」の文節操作・確定操作に合わせる:
/// ← / → = 文節の区切りを前 / うしろへ (文節を縮める / 伸ばす)、↓ = 部分確定、Shift+← / → = 注目文節を前 / うしろへ、
/// Ctrl+← / → = 注目文節を先頭 / 最後へ。OFF (既定) は今までどおり。
/// </summary>
internal static class TokModeTests
{
    /// <summary>「単位を|取る」に変換した状態 (擬似エンジン)。先頭の文節を選んでいる。</summary>
    private static CompositionTests.Keyboard Converted(bool tok)
    {
        var k = new CompositionTests.Keyboard { TokMode = tok };
        k.Type("tanniwotoru ");
        Assert.Equal("単位を|取る", Clauses(k), "前提: Space で文節に区切って変換");
        Assert.Equal(0, k.Host.View!.SelectedClause, "前提: 先頭の文節を選んでいる");
        return k;
    }

    private static string Clauses(CompositionTests.Keyboard k) => string.Join("|", k.Host.View!.Clauses!);

    private static void ShiftPress(CompositionTests.Keyboard k, int vk)
    {
        k.Key(VirtualKeys.LShift);
        k.Press(vk);
        k.Key(VirtualKeys.LShift, up: true);
    }

    [Test]
    public static void Default_IsOff()
    {
        Assert.True(!new Settings().TokMode, "既定は OFF");
        Assert.True(!new CompositionOptions().TokMode(), "CompositionOptions の既定も OFF");
    }

    [Test]
    public static void Off_KeepsCurrentBehavior()
    {
        var k = Converted(tok: false);
        k.Press(VirtualKeys.Right);
        Assert.Equal(1, k.Host.View!.SelectedClause, "OFF: → で次の文節へ");
        k.Press(VirtualKeys.Left);
        Assert.Equal(0, k.Host.View!.SelectedClause, "OFF: ← で前の文節へ");
        k.Press(VirtualKeys.Down);
        Assert.Equal(0, k.Host.Output.Count, "OFF: ↓ は次の候補で、確定しない");
        Assert.True(Clauses(k) != "単位を|取る", "OFF: ↓ で候補が変わる");
    }

    [Test]
    public static void On_Left_ShrinksClause_MovesBoundaryForward()
    {
        var k = Converted(tok: true);
        k.Press(VirtualKeys.Left);
        Assert.Equal(0, k.Host.View!.SelectedClause, "← で文節は移らない");
        Assert.Equal(2, k.Host.View.Clauses!.Count, "文節の数は同じ");
        Assert.Equal("単位", k.Host.View.Clauses[0], "← で先頭の文節が 1 文字縮む (たんいを → たんい。読みから変換し直して 単位)");
        Assert.True(k.Host.View.Clauses[1].StartsWith('を'), $"縮めた分は次の文節へ: {Clauses(k)}");
        Assert.Equal(0, k.Host.Output.Count, "確定しない");
    }

    [Test]
    public static void On_Right_ExtendsClause_MovesBoundaryBackward()
    {
        var k = Converted(tok: true);
        k.Press(VirtualKeys.Right);
        Assert.Equal(0, k.Host.View!.SelectedClause, "→ で文節は移らない");
        Assert.True(k.Host.View.Clauses![0].EndsWith('と'), $"→ で先頭の文節が 1 文字伸びる (たんいを + と): {Clauses(k)}");
        Assert.Equal("る", k.Host.View.Clauses[1], "伸ばした分は次の文節から取る");
    }

    [Test]
    public static void On_LeftThenRight_RestoresBoundary()
    {
        var k = Converted(tok: true);
        k.Press(VirtualKeys.Left);
        k.Press(VirtualKeys.Right);
        Assert.Equal(2, k.Host.View!.Clauses!.Count);
        // 区切りが元の位置 (たんいを|とる) に戻る。変換し直した文字は擬似エンジン次第 (たんいを・とる を単独では変換しない) なので、読みの長さで見る
        Assert.Equal("たんいを", k.Host.View.Clauses[0], "← → で元の区切りに戻る (前の文節)");
        Assert.Equal("とる", k.Host.View.Clauses[1], "← → で元の区切りに戻る (後ろの文節)");
    }

    [Test]
    public static void On_Right_AbsorbsWholeNextClause()
    {
        var k = Converted(tok: true);
        k.Press(VirtualKeys.Right);
        k.Press(VirtualKeys.Right);
        Assert.Equal(1, k.Host.View!.Clauses!.Count, "→ を続けると次の文節を全部取り込んで 1 文節になる");
        k.Press(VirtualKeys.Right);
        Assert.Equal(1, k.Host.View.Clauses!.Count, "最後まで伸ばしたら何もしない");
        Assert.Equal(0, k.Host.Output.Count, "確定しない");
    }

    [Test]
    public static void On_Left_StopsAtOneCharacter()
    {
        var k = Converted(tok: true);
        for (var i = 0; i < 6; i++) k.Press(VirtualKeys.Left);
        Assert.Equal("た", k.Host.View!.Clauses![0], "1 文字より短くはしない");
        Assert.Equal(0, k.Host.Output.Count, "確定しない");
    }

    [Test]
    public static void On_ShiftLeftRight_MoveFocusedClause()
    {
        var k = Converted(tok: true);
        ShiftPress(k, VirtualKeys.Right);
        Assert.Equal(1, k.Host.View!.SelectedClause, "Shift+→ で次の文節へ");
        Assert.Equal("単位を|取る", Clauses(k), "区切りは変わらない");
        ShiftPress(k, VirtualKeys.Right);
        Assert.Equal(1, k.Host.View!.SelectedClause, "最後で止まる");
        ShiftPress(k, VirtualKeys.Left);
        Assert.Equal(0, k.Host.View!.SelectedClause, "Shift+← で前の文節へ");
        ShiftPress(k, VirtualKeys.Left);
        Assert.Equal(0, k.Host.View!.SelectedClause, "先頭で止まる");
    }

    [Test]
    public static void On_Down_CommitsThroughFocusedClause_KeepsRestConverting()
    {
        var k = Converted(tok: true);
        k.Press(VirtualKeys.Down);
        Assert.Equal("単位を", k.Host.Document, "↓ で先頭の文節だけ確定 (部分確定)");
        Assert.True(k.Host.View!.Converting, "残りは変換中のまま");
        Assert.Equal("取る", Clauses(k), "残りの文節");
        Assert.Equal(0, k.Host.View.SelectedClause, "残りの先頭を選ぶ");
        Assert.True(k.Gate.IsCaptured, "変換ボックスは開いたまま");
        k.Press(VirtualKeys.Return);
        Assert.Equal("単位を取る", k.Host.Document, "Enter で残りも確定");
        Assert.True(k.Showing is null, "変換ボックスは閉じる");
    }

    [Test]
    public static void On_Down_OnLastClause_CommitsAll()
    {
        var k = Converted(tok: true);
        ShiftPress(k, VirtualKeys.Right);
        k.Press(VirtualKeys.Down);
        Assert.Equal("単位を取る", k.Host.Document, "最後の文節で ↓ を押すと全部確定");
        Assert.True(k.Showing is null, "変換ボックスは閉じる");
    }

    [Test]
    public static void On_Down_AfterResize_CommitsAdjustedClause()
    {
        var k = Converted(tok: true);
        k.Press(VirtualKeys.Left);
        k.Press(VirtualKeys.Down);
        Assert.Equal("単位", k.Host.Document, "区切りを変えた文節を部分確定");
        Assert.True(k.Host.View!.Converting && k.Host.View.Clauses![0].StartsWith('を'), $"残り: {Clauses(k)}");
    }

    [Test]
    public static void On_Down_ThenEscape_RestoresReadingOfRest()
    {
        var k = Converted(tok: true);
        k.Press(VirtualKeys.Down);
        k.Press(VirtualKeys.Escape);
        Assert.Equal("とる", k.Showing, "Esc で残りの文節だけ読みに戻る (確定した分は戻らない)");
        Assert.Equal("単位を", k.Host.Document);
    }

    [Test]
    public static void On_Down_ThenF10_ShowsTypedLettersOfRest()
    {
        // 部分確定のあとも、残りの読みは打った英字を覚えている (F10 で打ったままの英字)
        var k = Converted(tok: true);
        k.Press(VirtualKeys.Down);
        k.Press(VirtualKeys.Escape);
        k.Press(VirtualKeys.F10);
        Assert.Equal("toru", k.Showing, "残りの文節の打った英字");
    }

    [Test]
    public static void On_UpAndSpace_StillMoveCandidates()
    {
        var k = Converted(tok: true);
        k.Type(" ");
        Assert.True(k.Host.View!.Clauses![0] != "単位を", "Space で次の候補");
        k.Press(VirtualKeys.Up);
        Assert.Equal("単位を", k.Host.View!.Clauses![0], "↑ で前の候補");
        Assert.Equal(0, k.Host.Output.Count);
    }

    [Test]
    public static void On_ArrowsBeforeConversion_StillEnterClauseSelection()
    {
        // 変換前の矢印 (文節の選択に入る) は今までどおり。変換に入ったあとの操作が ATOK スタイル
        var k = new CompositionTests.Keyboard { TokMode = true };
        k.Type("tanniwotoru");
        k.Press(VirtualKeys.Right);
        Assert.True(k.Host.View!.Converting && k.Host.Output.Count == 0, "→ で文節の選択に入る (確定しない)");
    }

    // ---- MeltypeSession 経由 (Mac / Linux の経路。Ctrl+← → はここで受ける) ----

    private static MeltypeSession Session(bool tok)
    {
        var session = new MeltypeSession(CompositionTests.Detector, new CompositionTests.FakeConverter(),
            new CompositionOptions { TokMode = () => tok }, () => new Settings());
        foreach (var c in "tanniwotoru ")
        {
            var vk = c == ' ' ? VirtualKeys.Space : char.ToUpperInvariant(c);
            session.HandleKey(vk, c, false, false, false, false);
        }
        return session;
    }

    private static SessionResult Press(MeltypeSession session, int vk, bool shift = false, bool control = false) =>
        session.HandleKey(vk, null, shift, control, false, false);

    [Test]
    public static void Session_On_CtrlArrows_JumpToFirstAndLastClause()
    {
        var session = Session(tok: true);
        var last = Press(session, VirtualKeys.Right, control: true);
        Assert.True(last.Consumed && last.Commits.Count == 0, "Ctrl+→ は消費して確定しない");
        Assert.Equal(1, last.View!.SelectedClause, "Ctrl+→ で最後の文節へ");
        Assert.Equal(0, Press(session, VirtualKeys.Left, control: true).View!.SelectedClause, "Ctrl+← で先頭の文節へ");
    }

    [Test]
    public static void Session_On_ShiftArrows_MoveClause_PlainArrowsResize()
    {
        var session = Session(tok: true);
        Assert.Equal(1, Press(session, VirtualKeys.Right, shift: true).View!.SelectedClause, "Shift+→ で次の文節");
        var resized = Press(session, VirtualKeys.Left);
        Assert.Equal(1, resized.View!.SelectedClause, "← で文節は移らない");
        Assert.Equal("と", resized.View.Clauses![1], "← で選んでいる文節 (とる) が縮む");
    }

    [Test]
    public static void Session_On_Down_CommitsPart()
    {
        var result = Press(Session(tok: true), VirtualKeys.Down);
        Assert.True(result.Consumed, "↓ は消費する");
        Assert.Equal("単位を", string.Concat(result.Commits.Select(c => c.Text)), "部分確定した文字を入力欄へ");
        Assert.True(result.View is { Converting: true, Text: "取る" }, "残りは変換中");
    }

    [Test]
    public static void Session_Off_CtrlLeft_CommitsAsBefore()
    {
        var result = Press(Session(tok: false), VirtualKeys.Left, control: true);
        Assert.True(!result.Consumed && result.Commits.Count > 0, "OFF: Ctrl+← は確定してアプリへ");
    }

    [Test]
    public static void Session_On_CtrlLeft_WhenNotConverting_PassesToApp()
    {
        var session = new MeltypeSession(CompositionTests.Detector, new CompositionTests.FakeConverter(),
            new CompositionOptions { TokMode = () => true }, () => new Settings());
        Assert.True(!Press(session, VirtualKeys.Left, control: true).Consumed, "変換中でなければアプリへ (単語単位のカーソル移動など)");
    }

    [Test]
    public static void Catalog_HasTokMode()
    {
        var item = MacSettingsCatalog.Items.Single(i => i.Key == nameof(Settings.TokMode));
        Assert.True(item.Group.Length > 0, "グループがある");
        var settings = new Settings();
        Assert.Equal<string?>(null, item.Apply(settings, System.Text.Json.JsonDocument.Parse("true").RootElement), "true を受け付ける");
        Assert.True(settings.TokMode, "カタログ経由で設定を書ける");
    }
}
