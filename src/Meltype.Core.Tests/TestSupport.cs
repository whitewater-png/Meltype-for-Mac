// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Config;
using Meltype.Detection;
using Meltype.Input;
using Meltype.Learning;

namespace Meltype.Tests;

internal static class TestSupport
{
    /// <summary>英単語の判定に使うスペルチェッカー (Windows のテストランナーが Windows のものを入れる。無ければ同梱の辞書だけ)。</summary>
    public static IWordChecker? WordChecker { get; set; }

    public static Settings DefaultSettings() => new Settings().Normalize();

    public static ScoreEngine CreateEngine(Settings? settings = null, UserModel? user = null)
    {
        settings ??= DefaultSettings();
        return ScoreEngine.CreateDefault(user, () => settings);
    }

    /// <summary>実際の動作と同じく 1 文字ずつ判定し、最初に Undecided 以外になった結果を返す。語末まで来たら Space で確定した扱い。</summary>
    public static DetectionResult Classify(ScoreEngine engine, string word)
    {
        var letters = new string(word.ToLowerInvariant().Where(c => c is >= 'a' and <= 'z').ToArray());
        for (var i = 1; i <= letters.Length; i++)
        {
            var isFinal = i == letters.Length;
            var result = engine.Evaluate(new DetectionInput(letters[..i], letters[..i].Select(c => (int)char.ToUpperInvariant(c)).ToArray(), isFinal));
            if (result.Verdict != Verdict.Undecided) return result;
        }
        return engine.Evaluate(new DetectionInput(letters, [], true));
    }

    public static int Vk(char c) => char.ToUpperInvariant(c);
}

/// <summary>InputSession 用の偽の環境。時計はテストが手で進める。</summary>
internal sealed class FakeEnvironment : ISessionEnvironment
{
    public bool ModifierDown { get; set; }
    public CollectPermission Permission { get; set; } = CollectPermission.Allow;
    public List<FlushRequest> Flushes { get; } = [];
    public List<SessionSummary> Ended { get; } = [];

    public bool IsModifierDown() => ModifierDown;
    public CollectPermission CanCollect() => Permission;
    public void RequestFlush(FlushRequest request) => Flushes.Add(request);
    public void SessionEnded(SessionSummary summary) => Ended.Add(summary);
}

/// <summary>打鍵を順に流し、フックが握りつぶしたか・素通ししたかを記録する。</summary>
internal sealed class Typist(InputSession session)
{
    public long Now { get; set; } = 10_000;
    public List<KeyEvent> Swallowed { get; } = [];
    public List<KeyEvent> Passed { get; } = [];

    public void Type(string text, int intervalMs = 120)
    {
        foreach (var c in text)
        {
            var vk = c switch
            {
                ' ' => VirtualKeys.Space,
                '\n' => VirtualKeys.Return,
                '\b' => VirtualKeys.Back,
                _ => TestSupport.Vk(c),
            };
            Press(vk, intervalMs);
        }
    }

    public void Press(int vk, int intervalMs = 120)
    {
        Send(new KeyEvent(vk, 0, false, false, false, Now));
        Now += 40;
        Send(new KeyEvent(vk, 0, false, true, false, Now));
        Now += Math.Max(0, intervalMs - 40);
    }

    private void Send(KeyEvent e)
    {
        if (session.OnKey(e)) Swallowed.Add(e);
        else Passed.Add(e);
    }

    /// <summary>再入力ワーカーの代わりに保留分をすべて取り出す。</summary>
    public List<KeyEvent> Drain()
    {
        var all = new List<KeyEvent>();
        while (session.TakePendingForFlush() is { } events) all.AddRange(events);
        return all;
    }
}
