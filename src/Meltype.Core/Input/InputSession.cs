// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;
using Meltype.Config;
using Meltype.Detection;
using Meltype.Learning;

namespace Meltype.Input;

public enum SessionState
{
    /// <summary>入力待機中。</summary>
    Idle,
    /// <summary>入力開始直後。打鍵を保留しながら判定材料を集める。</summary>
    Collecting,
    /// <summary>判定済み。IME 切替と保留分の再入力が終わるまで、届いた打鍵を保留の末尾に積む。</summary>
    Flushing,
    /// <summary>判定終了。同じセッションでは再判定しない。</summary>
    Committed,
}

/// <summary>セッション開始時に打鍵を保留してよいかの判断。</summary>
public readonly record struct CollectPermission(bool Allowed, string Reason)
{
    public static CollectPermission Allow { get; } = new(true, "");
    public static CollectPermission Deny(string reason) => new(false, reason);
}

/// <summary>判定が付いたので保留分を処理してほしい、という依頼。</summary>
public sealed record FlushRequest(long SessionId, DetectionResult Result);

/// <summary>終わったセッションの要約 (学習用)。</summary>
public sealed record SessionSummary(long SessionId, string Letters, Verdict Verdict, bool UserCorrected)
{
    public SessionOutcome Outcome => (Verdict, UserCorrected) switch
    {
        (Verdict.Japanese, false) => SessionOutcome.JapaneseAccepted,
        (Verdict.Japanese, true) => SessionOutcome.JapaneseRejected,
        (_, false) => SessionOutcome.StayedAccepted,
        (_, true) => SessionOutcome.StayedRejected,
    };
}

/// <summary>InputSession が外界とやり取りするための口。テストでは偽物に差し替える。</summary>
public interface ISessionEnvironment
{
    /// <summary>Ctrl / Alt / Win / Shift のいずれかが押されているか (Idle のときだけ呼ばれる)。</summary>
    bool IsModifierDown();

    CollectPermission CanCollect();

    /// <summary>ロック内から呼ばれる。ブロックせずキューに積むだけにすること。</summary>
    void RequestFlush(FlushRequest request);

    /// <summary>ロック内から呼ばれる。重い処理はしないこと。</summary>
    void SessionEnded(SessionSummary summary);
}

/// <summary>
/// 入力セッションの状態機械 (設計書 §5〜§7)。フックのスレッド・再入力のワーカー・タイマーから
/// 同時に呼ばれるため、公開メソッドはすべて内部ロックで直列化する。ロック内では I/O をしない。
/// </summary>
public sealed class InputSession
{
    private readonly object _gate = new();
    private readonly ScoreEngine _engine;
    private readonly Func<Settings> _settings;
    private readonly ISessionEnvironment _environment;
    private readonly PendingInput _pending = new();
    private readonly StringBuilder _letters = new();
    private readonly List<int> _keys = [];

    private SessionState _state = SessionState.Idle;
    private long _sessionId;
    private long _firstKeyTime;
    private long _lastKeyTime;
    private long _decisionTime;
    private DetectionResult? _decision;
    private bool _learnable;
    private bool _userCorrected;
    private bool _endAfterFlush;
    private bool _endedBySpace;
    private bool _previousEndedEnglishBySpace;

    public InputSession(ScoreEngine engine, Func<Settings> settings, ISessionEnvironment environment)
    {
        _engine = engine;
        _settings = settings;
        _environment = environment;
    }

    public SessionState State { get { lock (_gate) return _state; } }

    public DetectionResult? LastDecision { get { lock (_gate) return _decision; } }

    /// <summary>
    /// フックから呼ばれる。true を返したらその打鍵は握りつぶし (保留に入れた)、false なら素通しする。
    /// </summary>
    public bool OnKey(KeyEvent e)
    {
        lock (_gate)
        {
            var settings = _settings();
            switch (_state)
            {
                case SessionState.Flushing:
                    // 再入力が終わるまでは、後から来た打鍵を素通しすると順序が入れ替わる。末尾に積む。
                    _pending.Add(e);
                    if (e.IsDown)
                    {
                        _lastKeyTime = e.TimeMs;
                        MarkBoundary(e.Vk);
                        if (VirtualKeys.IsImeToggle(e.Vk)) _userCorrected = true;
                    }
                    return true;

                case SessionState.Collecting:
                    return OnCollectingKey(e, settings);

                case SessionState.Committed:
                    if (e.IsUp) return false;
                    if (e.TimeMs - _lastKeyTime > settings.SessionIdleMs)
                    {
                        EndSession(endedBySpace: false);
                        return OnIdleKey(e, settings);
                    }
                    _lastKeyTime = e.TimeMs;
                    if (VirtualKeys.IsImeToggle(e.Vk) && e.TimeMs - _decisionTime <= settings.FeedbackWindowMs)
                    {
                        _userCorrected = true;
                    }
                    if (VirtualKeys.IsSessionBoundary(e.Vk)) EndSession(endedBySpace: e.Vk == VirtualKeys.Space);
                    return false;

                default:
                    return OnIdleKey(e, settings);
            }
        }
    }

    private bool OnIdleKey(KeyEvent e, Settings settings)
    {
        if (e.IsUp || e.Injected) return false;
        _lastKeyTime = e.TimeMs;
        if (!IsTextKey(e.Vk, settings))
        {
            // 英語直後の Space の判定は、次に来るのが単語の先頭である場合だけ意味がある。
            if (!VirtualKeys.IsModifier(e.Vk)) _previousEndedEnglishBySpace = false;
            return false;
        }

        StartSession(e.TimeMs);

        if (_environment.IsModifierDown())
        {
            Commit("修飾キーと同時押し (ショートカットか大文字)");
            return false;
        }

        if (settings.ContinueEnglishAfterSpace && _previousEndedEnglishBySpace)
        {
            Commit("英語入力の続き");
            return false;
        }

        var permission = _environment.CanCollect();
        if (!permission.Allowed)
        {
            Commit(permission.Reason);
            return false;
        }

        Append(e.Vk);
        var result = Evaluate(settings, isFinal: false);
        if (result.Verdict is Verdict.English or Verdict.Unknown)
        {
            // 1 打目で英語と分かった (l, q, v, x …)。保留する必要がないので素通しする。
            _state = SessionState.Committed;
            _decision = result;
            _decisionTime = e.TimeMs;
            _learnable = false;
            return false;
        }

        _state = SessionState.Collecting;
        _pending.Add(e);
        if (result.Verdict != Verdict.Undecided) BeginFlush(result, e.TimeMs);
        return true;
    }

    private bool OnCollectingKey(KeyEvent e, Settings settings)
    {
        // 保留中は、キーアップも含めてすべてを届いた順に保留する。
        _pending.Add(e);
        if (e.IsUp) return true;
        _lastKeyTime = e.TimeMs;

        if (!e.Injected && IsTextKey(e.Vk, settings) && !_environment.IsModifierDown())
        {
            Append(e.Vk);
            var result = Evaluate(settings, isFinal: false);
            if (result.Verdict != Verdict.Undecided) BeginFlush(result, e.TimeMs);
            return true;
        }

        // 英字以外のキー (区切り・BackSpace・記号・修飾キー・IME 切替キー) で判定を打ち切る。
        // 保留分はそのキーも含めて元の順序で出力する。区切りで日本語に切り替えると
        // Space が変換として働いてしまうので、ここでは切り替えない。
        MarkBoundary(e.Vk);
        if (VirtualKeys.IsImeToggle(e.Vk)) _userCorrected = true;
        var interrupted = Evaluate(settings, isFinal: true) with { Verdict = Verdict.Unknown, Summary = $"キー {e.Vk:X2} で判定を打ち切り" };
        BeginFlush(interrupted, e.TimeMs);
        return true;
    }

    /// <summary>タイマーから定期的に呼ばれる。無入力が続いたら保留分を出力し、セッションを区切る。</summary>
    public void OnTimer(long nowMs)
    {
        lock (_gate)
        {
            var settings = _settings();
            if (_state == SessionState.Collecting &&
                (nowMs - _lastKeyTime >= settings.IdleFlushMs || nowMs - _firstKeyTime >= settings.MaxHoldMs))
            {
                var result = Evaluate(settings, isFinal: true);
                if (result.Verdict is Verdict.Undecided or Verdict.Unknown)
                {
                    var reason = nowMs - _lastKeyTime >= settings.IdleFlushMs
                        ? $"{settings.IdleFlushMs}ms 入力がなかった"
                        : $"最大保留時間 {settings.MaxHoldMs}ms に達した";
                    result = result with { Verdict = Verdict.Unknown, Summary = $"判断できないまま{reason}" };
                }
                BeginFlush(result, nowMs);
            }
            else if (_state == SessionState.Committed && nowMs - _lastKeyTime > settings.SessionIdleMs)
            {
                EndSession(endedBySpace: false);
            }
        }
    }

    /// <summary>フォーカス変更・マウスクリック。キャレットが動いた可能性があるので新しいセッションにする。</summary>
    public void OnContextChanged(long nowMs)
    {
        lock (_gate)
        {
            _previousEndedEnglishBySpace = false;
            switch (_state)
            {
                case SessionState.Collecting:
                    _endAfterFlush = true;
                    BeginFlush(Evaluate(_settings(), isFinal: true) with { Verdict = Verdict.Unknown, Summary = "フォーカスが変わった" }, nowMs);
                    break;
                case SessionState.Flushing:
                    _endAfterFlush = true;
                    break;
                case SessionState.Committed:
                    EndSession(endedBySpace: false);
                    break;
            }
        }
    }

    /// <summary>
    /// 再入力ワーカーが呼ぶ。保留分を取り出す。空なら Flushing を終えて null を返す。
    /// 取り出しと状態遷移を同じロックで行うので、その間に届いた打鍵は必ずどちらかに入る。
    /// </summary>
    public List<KeyEvent>? TakePendingForFlush()
    {
        lock (_gate)
        {
            if (_state != SessionState.Flushing) return null;
            if (_pending.Count > 0) return _pending.TakeAll();

            if (_endAfterFlush)
            {
                EndSession(_endedBySpace);
            }
            else
            {
                _state = SessionState.Committed;
            }
            return null;
        }
    }

    /// <summary>自動切替を止めたとき・終了時。保留中のものがあれば出力してもらうために返す。</summary>
    public List<KeyEvent> Abort()
    {
        lock (_gate)
        {
            var events = _pending.TakeAll();
            ResetFields();
            _state = SessionState.Idle;
            return events;
        }
    }

    private void MarkBoundary(int vk)
    {
        if (!VirtualKeys.IsSessionBoundary(vk) || _endAfterFlush) return;
        _endAfterFlush = true;
        _endedBySpace = vk == VirtualKeys.Space;
    }

    private void StartSession(long now)
    {
        _sessionId++;
        _firstKeyTime = now;
        _lastKeyTime = now;
        _letters.Clear();
        _keys.Clear();
        _decision = null;
        _learnable = true;
        _userCorrected = false;
        _endAfterFlush = false;
        _endedBySpace = false;
    }

    private void Commit(string reason)
    {
        _state = SessionState.Committed;
        _learnable = false;
        _decision = new DetectionResult(Verdict.Unknown, "", 0, 0, [], reason);
        _decisionTime = _lastKeyTime;
    }

    private void BeginFlush(DetectionResult result, long now)
    {
        _state = SessionState.Flushing;
        _decision = result;
        _decisionTime = now;
        _environment.RequestFlush(new FlushRequest(_sessionId, result));
    }

    private void EndSession(bool endedBySpace)
    {
        var decision = _decision;
        if (_learnable && decision is not null && _letters.Length > 0)
        {
            _environment.SessionEnded(new SessionSummary(_sessionId, _letters.ToString(), decision.Verdict, _userCorrected));
        }
        _previousEndedEnglishBySpace = endedBySpace && decision?.Verdict == Verdict.English;
        ResetFields();
        _state = SessionState.Idle;
    }

    private void ResetFields()
    {
        _pending.Clear();
        _letters.Clear();
        _keys.Clear();
        _learnable = false;
        _userCorrected = false;
        _endAfterFlush = false;
        _endedBySpace = false;
    }

    private void Append(int vk)
    {
        _letters.Append(VirtualKeys.IsLetter(vk) ? VirtualKeys.ToLetter(vk) : '#');
        _keys.Add(vk);
    }

    private DetectionResult Evaluate(Settings settings, bool isFinal) =>
        _engine.Evaluate(new DetectionInput(_letters.ToString(), _keys.ToArray(), isFinal));

    private static bool IsTextKey(int vk, Settings settings) =>
        VirtualKeys.IsLetter(vk) || (settings.InputStyle != InputStyle.Romaji && KanaDetector.IsKanaKey(vk));
}
