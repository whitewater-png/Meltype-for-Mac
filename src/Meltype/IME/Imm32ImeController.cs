// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.IME;

/// <summary>
/// IMM32 経由の IME 操作。ImmGetContext は別プロセスのウィンドウには使えないので、
/// 対象スレッドの既定 IME ウィンドウに WM_IME_CONTROL を送る方式を取る (TSF ベースの MS-IME でも有効)。
/// </summary>
public sealed class Imm32ImeController : IImeBackend
{
    private const int IMC_GETCONVERSIONMODE = 0x0001, IMC_SETCONVERSIONMODE = 0x0002;
    private const int IMC_GETOPENSTATUS = 0x0005, IMC_SETOPENSTATUS = 0x0006;
    private const ushort LangJapanese = 0x0411;
    private const uint QueryTimeoutMs = 100;

    public string Name => "IMM32";

    public ImeState GetState(ImeTarget target)
    {
        var hkl = Native.GetKeyboardLayout(target.ThreadId);
        var language = (hkl.ToInt64() & 0xFFFF) == LangJapanese ? InputLanguage.Japanese : InputLanguage.Other;
        var imeWindow = Native.ImmGetDefaultIMEWnd(target.EffectiveWindow);
        if (imeWindow == IntPtr.Zero) return new ImeState(language, ImeMode.Unknown, 0, hkl);

        if (!TrySend(imeWindow, IMC_GETOPENSTATUS, IntPtr.Zero, QueryTimeoutMs, out var open))
            return new ImeState(language, ImeMode.Unknown, 0, hkl);
        TrySend(imeWindow, IMC_GETCONVERSIONMODE, IntPtr.Zero, QueryTimeoutMs, out var conversion);
        return new ImeState(language, open != IntPtr.Zero ? ImeMode.Open : ImeMode.Closed, unchecked((int)conversion.ToInt64()), hkl);
    }

    public bool TrySetLanguage(ImeTarget target, InputLanguage language, int timeoutMs)
    {
        var hkl = language == InputLanguage.Japanese ? FindJapaneseLayout() : FindOtherLayout();
        if (hkl == IntPtr.Zero) return false;

        // WM_INPUTLANGCHANGEREQUEST は DefWindowProc が処理して ActivateKeyboardLayout する。
        Native.SendMessageTimeout(target.EffectiveWindow, Native.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hkl,
            Native.SMTO_ABORTIFHUNG, (uint)timeoutMs, out _);
        return ImeController.WaitFor(() => GetLanguage(target) == language, timeoutMs);
    }

    public bool TrySetOpen(ImeTarget target, bool open, int? conversionMode, int timeoutMs)
    {
        var imeWindow = Native.ImmGetDefaultIMEWnd(target.EffectiveWindow);
        if (imeWindow == IntPtr.Zero) return false;
        if (!TrySend(imeWindow, IMC_SETOPENSTATUS, open ? new IntPtr(1) : IntPtr.Zero, (uint)timeoutMs, out _)) return false;
        if (open && conversionMode is { } mode)
        {
            TrySend(imeWindow, IMC_SETCONVERSIONMODE, new IntPtr(mode), (uint)timeoutMs, out _);
        }
        return ImeController.WaitFor(() =>
        {
            if (!TrySend(imeWindow, IMC_GETOPENSTATUS, IntPtr.Zero, QueryTimeoutMs, out var current)) return false;
            return (current != IntPtr.Zero) == open;
        }, timeoutMs);
    }

    public static IntPtr FindJapaneseLayout() => InstalledLayouts().FirstOrDefault(h => (h.ToInt64() & 0xFFFF) == LangJapanese);

    private static IntPtr FindOtherLayout() => InstalledLayouts().FirstOrDefault(h => (h.ToInt64() & 0xFFFF) != LangJapanese);

    private static IntPtr[] InstalledLayouts()
    {
        var count = Native.GetKeyboardLayoutList(0, null);
        if (count <= 0) return [];
        var layouts = new IntPtr[count];
        Native.GetKeyboardLayoutList(count, layouts);
        return layouts;
    }

    private static InputLanguage GetLanguage(ImeTarget target) =>
        (Native.GetKeyboardLayout(target.ThreadId).ToInt64() & 0xFFFF) == LangJapanese ? InputLanguage.Japanese : InputLanguage.Other;

    private static bool TrySend(IntPtr imeWindow, int command, IntPtr value, uint timeoutMs, out IntPtr result) =>
        Native.SendMessageTimeout(imeWindow, Native.WM_IME_CONTROL, new IntPtr(command), value, Native.SMTO_ABORTIFHUNG, timeoutMs, out result) != IntPtr.Zero;
}
