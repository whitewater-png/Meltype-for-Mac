// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Config;
using Meltype.Input;

namespace Meltype.IME;

public enum InputLanguage { Japanese, Other }

public enum ImeMode { Open, Closed, Unknown }

/// <summary>
/// 入力言語と IME の開閉を独立に持つ (設計書 §8)。
/// Meltype が必要とするのは Japanese + Open (+ ひらがな等のネイティブ入力モード)。
/// </summary>
public readonly record struct ImeState(InputLanguage Language, ImeMode Mode, int ConversionMode, IntPtr KeyboardLayout)
{
    public const int CModeNative = 0x0001, CModeKatakana = 0x0002, CModeFullShape = 0x0008, CModeRoman = 0x0010;

    public static ImeState Unknown { get; } = new(InputLanguage.Other, ImeMode.Unknown, 0, IntPtr.Zero);

    public bool IsJapaneseReady => Language == InputLanguage.Japanese && Mode == ImeMode.Open && (ConversionMode & CModeNative) != 0;

    public override string ToString() =>
        $"{Language}/{Mode}/conv=0x{ConversionMode:X2}/hkl=0x{KeyboardLayout.ToInt64() & 0xFFFFFFFF:X8}";
}

/// <summary>操作対象のウィンドウ。IME の状態はフォーカスのあるコントロールのスレッドに属する。</summary>
public sealed record ImeTarget(IntPtr TopLevel, IntPtr Focus, uint ThreadId, uint ProcessId)
{
    public IntPtr EffectiveWindow => Focus != IntPtr.Zero ? Focus : TopLevel;

    public static ImeTarget? FromForeground()
    {
        var top = Native.GetForegroundWindow();
        if (top == IntPtr.Zero) return null;
        var thread = Native.GetWindowThreadProcessId(top, out var process);
        var info = new Native.GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.GUITHREADINFO>() };
        var focus = Native.GetGUIThreadInfo(thread, ref info) ? info.hwndFocus : IntPtr.Zero;
        if (focus != IntPtr.Zero) thread = Native.GetWindowThreadProcessId(focus, out process);
        return new ImeTarget(top, focus, thread, process);
    }
}

/// <summary>IME を操作する実装の共通口 (設計書 §10)。</summary>
public interface IImeBackend
{
    string Name { get; }
    ImeState GetState(ImeTarget target);
    bool TrySetLanguage(ImeTarget target, InputLanguage language, int timeoutMs);
    bool TrySetOpen(ImeTarget target, bool open, int? conversionMode, int timeoutMs);
}

public enum SwitchOutcome { AlreadyJapanese, Switched, NoJapaneseLayout, LanguageFailed, OpenFailed, NoTarget, InputLanguageExcluded }

public sealed record SwitchResult(SwitchOutcome Outcome, ImeState Before, ImeState After, string Detail)
{
    public bool Success => Outcome is SwitchOutcome.AlreadyJapanese or SwitchOutcome.Switched;
}

/// <summary>
/// IME 操作を 1 か所にまとめる (設計書 §9)。アプリごとの差は各 backend に閉じ込め、
/// 失敗したら次の手段にフォールバックする:
///   入力言語: IMM32 (WM_INPUTLANGCHANGEREQUEST) → TSF (プロファイル切替)
///   IME 開閉: IMM32 (WM_IME_CONTROL)            → VK_IME_ON の送出
/// どれも失敗した場合は切り替えずに保留分をそのまま出力する (入力を失わないことを優先)。
/// </summary>
public sealed class ImeController
{
    private readonly IImeBackend _imm32;
    private readonly IImeBackend? _tsf;
    private readonly Func<Settings> _settings;

    public ImeController(IImeBackend imm32, IImeBackend? tsf, Func<Settings> settings)
    {
        _imm32 = imm32;
        _tsf = tsf;
        _settings = settings;
    }

    public ImeState GetState(ImeTarget target) => _imm32.GetState(target);

    public SwitchResult EnsureJapanese(ImeTarget? target)
    {
        if (target is null) return new SwitchResult(SwitchOutcome.NoTarget, ImeState.Unknown, ImeState.Unknown, "前面ウィンドウがない");
        var settings = _settings();
        var timeout = settings.ImeTimeoutMs;
        var before = _imm32.GetState(target);
        // 判定待ちの間に Win+Space などで切り替わっても、日本語へ勝手に戻さない。
        if (!KeyboardLayoutPolicy.AllowsInput(settings, before.KeyboardLayout))
            return new SwitchResult(SwitchOutcome.InputLanguageExcluded, before, before, "日本語キーボード以外では動作しない設定");
        if (before.IsJapaneseReady) return new SwitchResult(SwitchOutcome.AlreadyJapanese, before, before, "既に日本語入力");

        var steps = new List<string>();
        if (before.Language != InputLanguage.Japanese)
        {
            var ok = _imm32.TrySetLanguage(target, InputLanguage.Japanese, timeout);
            steps.Add($"{_imm32.Name}.言語={ok}");
            if (!ok && settings.UseTsf && _tsf is not null)
            {
                ok = _tsf.TrySetLanguage(target, InputLanguage.Japanese, timeout);
                steps.Add($"{_tsf.Name}.言語={ok}");
            }
            if (!ok)
            {
                var outcome = Imm32ImeController.FindJapaneseLayout() == IntPtr.Zero ? SwitchOutcome.NoJapaneseLayout : SwitchOutcome.LanguageFailed;
                return new SwitchResult(outcome, before, _imm32.GetState(target), string.Join(", ", steps));
            }
        }

        var current = _imm32.GetState(target);
        if (!KeyboardLayoutPolicy.AllowsInput(settings, current.KeyboardLayout))
            return new SwitchResult(SwitchOutcome.InputLanguageExcluded, before, current, "IME の確認中に入力言語が変わった");
        var conversion = DesiredConversionMode(current.ConversionMode, settings.InputStyle);
        var opened = _imm32.TrySetOpen(target, true, conversion, timeout);
        steps.Add($"{_imm32.Name}.開く={opened}");
        if (!opened)
        {
            current = _imm32.GetState(target);
            if (!KeyboardLayoutPolicy.AllowsInput(settings, current.KeyboardLayout))
                return new SwitchResult(SwitchOutcome.InputLanguageExcluded, before, current, "IME の切替中に入力言語が変わった");
            // WM_IME_CONTROL が効かないアプリ向け。VK_IME_ON は「開く」だけなので、トグル系キーと違い閉じてしまう心配がない。
            KeyInjector.SendKey(VirtualKeys.ImeOn);
            opened = WaitFor(() => _imm32.GetState(target).Mode == ImeMode.Open, timeout);
            steps.Add($"VK_IME_ON={opened}");
        }

        var after = _imm32.GetState(target);
        // 開閉状態が読めないアプリでも VK_IME_ON 自体は届いている可能性が高いので、言語が日本語なら成功扱いにする。
        var success = after.IsJapaneseReady || (after.Language == InputLanguage.Japanese && after.Mode == ImeMode.Unknown && opened);
        return new SwitchResult(success ? SwitchOutcome.Switched : SwitchOutcome.OpenFailed, before, after, string.Join(", ", steps));
    }

    /// <summary>必要に応じて元の入力状態を復元する。</summary>
    public bool Restore(ImeTarget target, ImeState snapshot)
    {
        var timeout = _settings().ImeTimeoutMs;
        var ok = true;
        var current = _imm32.GetState(target);
        if (snapshot.Mode != ImeMode.Unknown)
        {
            ok &= _imm32.TrySetOpen(target, snapshot.Mode == ImeMode.Open, snapshot.ConversionMode, timeout);
        }
        if (snapshot.Language != current.Language)
        {
            ok &= _imm32.TrySetLanguage(target, snapshot.Language, timeout);
        }
        return ok;
    }

    /// <summary>ひらがな入力にする。ローマ字/かなの別はユーザー設定に合わせ、Both なら今の設定を触らない。</summary>
    public static int DesiredConversionMode(int current, InputStyle style)
    {
        var mode = (current | ImeState.CModeNative | ImeState.CModeFullShape) & ~ImeState.CModeKatakana;
        return style switch
        {
            InputStyle.Romaji => mode | ImeState.CModeRoman,
            InputStyle.Kana => mode & ~ImeState.CModeRoman,
            _ => mode,
        };
    }

    internal static bool WaitFor(Func<bool> condition, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (true)
        {
            if (condition()) return true;
            if (Environment.TickCount64 >= deadline) return false;
            Thread.Sleep(10);
        }
    }
}
