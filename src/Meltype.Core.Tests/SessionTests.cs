// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Detection;
using Meltype.Input;
using Meltype.Learning;
using static Meltype.Tests.TestSupport;

namespace Meltype.Tests;

/// <summary>
/// 入力システムとしての安全性 (設計書 §31): 入力を失わない・二重入力しない・順序を変えない。
/// </summary>
internal static class SessionTests
{
    private static (InputSession Session, FakeEnvironment Env, Typist Typist) Create(Config.Settings? settings = null)
    {
        settings ??= DefaultSettings();
        var env = new FakeEnvironment();
        var session = new InputSession(CreateEngine(settings), () => settings, env);
        return (session, env, new Typist(session));
    }

    private static string Letters(IEnumerable<KeyEvent> events) =>
        new(events.Where(e => e.IsDown && VirtualKeys.IsLetter(e.Vk)).Select(e => VirtualKeys.ToLetter(e.Vk)).ToArray());

    [Test]
    public static void Japanese_HoldsThenReinjectsInOrder()
    {
        var (session, env, typist) = Create();
        typist.Type("konn");

        Assert.Equal(1, env.Flushes.Count, "4 文字目で判定");
        Assert.Equal(Verdict.Japanese, env.Flushes[0].Result.Verdict);
        Assert.Equal(SessionState.Flushing, session.State);
        Assert.Equal(0, typist.Passed.Count, "判定までは何も素通ししない");

        // IME 切替中に続きが打たれても、保留の末尾に積まれて順序が保たれる。
        typist.Type("ichi");
        var reinjected = typist.Drain();
        Assert.Equal("konnichi", Letters(reinjected));
        Assert.True(reinjected.SequenceEqual(typist.Swallowed), "保留した打鍵は 1 つも欠けず、順序どおりに再入力される");
        Assert.Equal(SessionState.Committed, session.State);

        // 判定後は同じセッション内で再判定しない。
        typist.Type("wa");
        Assert.Equal("wa", Letters(typist.Passed));
        Assert.Equal(1, env.Flushes.Count);
    }

    [Test]
    public static void English_FlushesAsSoonAsDecided()
    {
        var (session, env, typist) = Create();
        typist.Type("hel");
        Assert.Equal(Verdict.English, env.Flushes.Single().Result.Verdict);
        typist.Type("lo");
        var reinjected = typist.Drain();
        Assert.Equal("hello", Letters(reinjected));
        typist.Type(" world");
        Assert.Equal(2, env.Flushes.Count, "区切りの後は新しいセッションとして判定し直す");
    }

    [Test]
    public static void FirstKeyEnglish_IsNeverHeld()
    {
        var (session, env, typist) = Create();
        typist.Type("localhost");
        Assert.Equal(0, typist.Swallowed.Count, "l で英語と分かるので 1 打鍵も保留しない");
        Assert.Equal(0, env.Flushes.Count);
        Assert.Equal(SessionState.Committed, session.State);
    }

    [Test]
    public static void ModifierShortcuts_AreNotHeld()
    {
        var (_, env, typist) = Create();
        env.ModifierDown = true;
        typist.Type("c");
        Assert.Equal(0, typist.Swallowed.Count, "Ctrl+C などは保留しない");
    }

    [Test]
    public static void DeniedApps_AreNotHeld()
    {
        var (session, env, typist) = Create();
        env.Permission = CollectPermission.Deny("テスト");
        typist.Type("konnichiwa");
        Assert.Equal(0, typist.Swallowed.Count);
        Assert.Equal(SessionState.Committed, session.State);
    }

    [Test]
    public static void NonLetterKey_InterruptsAndKeepsOrder()
    {
        var (session, env, typist) = Create();
        typist.Type("ko\bu");
        Assert.Equal(Verdict.Unknown, env.Flushes.Single().Result.Verdict, "BackSpace で判定を打ち切る (切り替えない)");
        var reinjected = typist.Drain();
        Assert.True(reinjected.SequenceEqual(typist.Swallowed), "BackSpace も含めて元の順序で出力");
        Assert.Equal(VirtualKeys.Back, reinjected.First(e => !VirtualKeys.IsLetter(e.Vk)).Vk);
    }

    [Test]
    public static void SpaceDuringCollecting_DoesNotSwitch_AndEndsSession()
    {
        var (session, env, typist) = Create();
        typist.Type("ka ");
        Assert.Equal(Verdict.Unknown, env.Flushes.Single().Result.Verdict, "Space で確定したときは切り替えない (Space が変換として働くため)");
        typist.Drain();
        Assert.Equal(SessionState.Idle, session.State);
    }

    [Test]
    public static void IdleTimeout_FlushesPending()
    {
        var (session, env, typist) = Create();
        typist.Type("ka");
        session.OnTimer(typist.Now + 100);
        Assert.Equal(0, env.Flushes.Count, "まだ待つ");
        session.OnTimer(typist.Now + 1000);
        Assert.Equal(Verdict.Unknown, env.Flushes.Single().Result.Verdict);
        Assert.Equal("ka", Letters(typist.Drain()));
    }

    [Test]
    public static void SlowTyping_StillReachesDecision()
    {
        // 実機ログ: "him" の時点で保留が切れて hima を見逃した。500ms 間隔でも判定まで待てること。
        var (session, env, typist) = Create();
        foreach (var c in "hima")
        {
            typist.Type(c.ToString(), intervalMs: 500);
            session.OnTimer(typist.Now);
        }
        Assert.Equal(Verdict.Japanese, env.Flushes.Single().Result.Verdict);
    }

    [Test]
    public static void OldConfigIsMigrated()
    {
        var settings = new Config.Settings { SettingsVersion = 1, IdleFlushMs = 400, MaxHoldMs = 1200 };
        Assert.True(settings.Migrate(), "v1 は移行される");
        Assert.Equal(700, settings.IdleFlushMs);
        Assert.Equal(2500, settings.MaxHoldMs);
        var custom = new Config.Settings { SettingsVersion = 1, IdleFlushMs = 900 };
        custom.Migrate();
        Assert.Equal(900, custom.IdleFlushMs, "ユーザーが変えた値は触らない");
    }

    [Test]
    public static void MaxPendingKeys_LimitsHolding()
    {
        var (_, env, typist) = Create();
        typist.Type("tabemo");
        Assert.True(env.Flushes.Count == 1, "上限の文字数で必ず結論を出す");
    }

    [Test]
    public static void FocusChange_FlushesPending()
    {
        var (session, env, typist) = Create();
        typist.Type("ka");
        session.OnContextChanged(typist.Now);
        Assert.Equal(1, env.Flushes.Count);
        Assert.Equal("ka", Letters(typist.Drain()));
        Assert.Equal(SessionState.Idle, session.State);
    }

    [Test]
    public static void Abort_ReturnsPendingSoNothingIsLost()
    {
        var (session, _, typist) = Create();
        typist.Type("ka");
        var events = session.Abort();
        Assert.Equal("ka", Letters(events));
        Assert.Equal(SessionState.Idle, session.State);
    }

    [Test]
    public static void Feedback_ImeToggleAfterJapaneseIsFalsePositive()
    {
        var (_, env, typist) = Create();
        typist.Type("konn");
        typist.Drain();
        typist.Press(VirtualKeys.OemAuto); // 半角/全角
        typist.Type(" ");
        var summary = env.Ended.Single();
        Assert.True(summary.UserCorrected, "切替直後の 半角/全角 は誤判定のフィードバック");
        Assert.Equal(SessionOutcome.JapaneseRejected, summary.Outcome);
    }

    [Test]
    public static void Feedback_ImeToggleAfterUnknownIsMissedJapanese()
    {
        var (_, env, typist) = Create();
        typist.Type("zanzok");
        typist.Drain();
        typist.Press(VirtualKeys.OemAuto);
        typist.Type("\n");
        Assert.Equal(SessionOutcome.StayedRejected, env.Ended.Single().Outcome);
    }

    [Test]
    public static void Learning_UsesSessionOutcome()
    {
        var (_, env, typist) = Create();
        typist.Type("konn");
        typist.Drain();
        typist.Type("ichiwa ");
        Assert.Equal(SessionOutcome.JapaneseAccepted, env.Ended.Single().Outcome);
        Assert.Equal("konn", env.Ended.Single().Letters, "学習するのは判定に使った先頭部分だけ");
    }

    [Test]
    public static void ContinueEnglishAfterSpace_SkipsHolding()
    {
        var settings = DefaultSettings();
        settings.ContinueEnglishAfterSpace = true;
        var (_, _, typist) = Create(settings);
        typist.Type("hel");
        typist.Drain();
        typist.Type("lo ");
        var before = typist.Swallowed.Count;
        typist.Type("kyou");
        Assert.Equal(before, typist.Swallowed.Count, "英語の続きの単語は保留しない");
    }
}
