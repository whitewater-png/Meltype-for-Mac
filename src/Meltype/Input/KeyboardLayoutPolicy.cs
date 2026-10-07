// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 MedeiaBeliar

using Meltype.Config;
using Meltype.IME;

namespace Meltype.Input;

/// <summary>物理配列ではなく、入力先のスレッドで今選ばれている入力言語を調べる。</summary>
internal static class KeyboardLayoutPolicy
{
    public static bool AllowsInput(Settings settings) =>
        !settings.JapaneseKeyboardOnly || AllowsInput(settings, ImeTarget.FromForeground());

    public static bool AllowsInput(Settings settings, ImeTarget? target) =>
        !settings.JapaneseKeyboardOnly ||
        (target is { ThreadId: not 0 } && AllowsInput(settings, Native.GetKeyboardLayout(target.ThreadId)));

    // HKL の下位ワードが言語 ID。上位ワード (配列・IME の識別子) と開閉状態には依存しない。
    public static bool AllowsInput(Settings settings, IntPtr layout) =>
        !settings.JapaneseKeyboardOnly || (layout.ToInt64() & 0xFFFF) == 0x0411;
}
