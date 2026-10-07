// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 MedeiaBeliar

using Meltype.Config;
using Meltype.IME;
using Meltype.Input;
using InputLanguage = Meltype.IME.InputLanguage;
using ImeMode = Meltype.IME.ImeMode;

namespace Meltype.Tests;

internal static class KeyboardLayoutTests
{
    [Test]
    public static void JapaneseOnly_ChecksLanguageRatherThanPhysicalLayout()
    {
        var settings = new Settings { JapaneseKeyboardOnly = true };
        foreach (var hkl in new[] { 0x0411, 0x04110411, unchecked((int)0xE0010411) })
            Assert.True(KeyboardLayoutPolicy.AllowsInput(settings, new IntPtr(hkl)), "日本語の配列・IME を許可する");
        foreach (var hkl in new[] { 0, 0x04090409, unchecked((int)0xE0010412), 0x04110412, 0x08040804 })
            Assert.True(!KeyboardLayoutPolicy.AllowsInput(settings, new IntPtr(hkl)), "英語・韓国語・中国語・取得失敗は通す");
        Assert.True(!KeyboardLayoutPolicy.AllowsInput(settings, (ImeTarget?)null), "入力先がなければ動作しない");

        settings.JapaneseKeyboardOnly = false;
        Assert.True(KeyboardLayoutPolicy.AllowsInput(settings, new IntPtr(0x04090409)), "OFF なら従来どおり");
        Assert.True(KeyboardLayoutPolicy.AllowsInput(settings, IntPtr.Zero), "OFF なら入力言語を制限しない");
    }

    [Test]
    public static void JapaneseOnly_DoesNotSwitchOrCloseOtherLanguages()
    {
        var settings = new Settings { JapaneseKeyboardOnly = true };
        foreach (var layout in new[] { 0, 0x04090409, unchecked((int)0xE0010412) })
        {
            foreach (var mode in Enum.GetValues<ImeMode>())
            {
                var backend = new FakeIme(new ImeState(InputLanguage.Other, mode, 0, new IntPtr(layout)));
                var controller = new ImeController(backend, backend, () => settings);
                var result = controller.EnsureJapanese(Target);
                Assert.Equal(SwitchOutcome.InputLanguageExcluded, result.Outcome);
                Assert.Equal(0, backend.Changes.Count, "入力先が変わっても言語・IME の開閉を操作しない");
            }
        }
    }

    [Test]
    public static void JapaneseOnly_ResumesWhenJapaneseIsSelected()
    {
        var settings = new Settings { JapaneseKeyboardOnly = true };
        var backend = new FakeIme(new ImeState(InputLanguage.Other, ImeMode.Open, 0, new IntPtr(0x04120412)));
        var controller = new ImeController(backend, null, () => settings);
        Assert.Equal(SwitchOutcome.InputLanguageExcluded, controller.EnsureJapanese(Target).Outcome);

        backend.State = new ImeState(InputLanguage.Japanese, ImeMode.Closed, 0, new IntPtr(0x04110411));
        Assert.True(controller.EnsureJapanese(Target).Success, "日本語の A 状態からは変換を開始できる");
        Assert.Equal("open:True", string.Join(",", backend.Changes), "日本語への言語切替は不要");
    }

    [Test]
    public static void JapaneseOnly_OffPreservesAutomaticLanguageSwitching()
    {
        var backend = new FakeIme(new ImeState(InputLanguage.Other, ImeMode.Closed, 0, new IntPtr(0x04090409)));
        var controller = new ImeController(backend, null, () => new Settings());
        Assert.True(controller.EnsureJapanese(Target).Success, "既定値では従来の自動切替を保つ");
        Assert.Equal("language:Japanese,open:True", string.Join(",", backend.Changes));
    }

    private static readonly ImeTarget Target = new(new IntPtr(1), new IntPtr(2), 3, 4);

    private sealed class FakeIme(ImeState state) : IImeBackend
    {
        public string Name => "test";
        public ImeState State { get; set; } = state;
        public List<string> Changes { get; } = [];
        public ImeState GetState(ImeTarget target) => State;
        public bool TrySetLanguage(ImeTarget target, InputLanguage language, int timeoutMs)
        {
            Changes.Add($"language:{language}");
            State = State with { Language = language, KeyboardLayout = new IntPtr(0x04110411) };
            return true;
        }
        public bool TrySetOpen(ImeTarget target, bool open, int? conversionMode, int timeoutMs)
        {
            Changes.Add($"open:{open}");
            State = State with { Mode = open ? ImeMode.Open : ImeMode.Closed, ConversionMode = conversionMode ?? State.ConversionMode };
            return true;
        }
    }
}
