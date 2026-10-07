// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 MedeiaBeliar

using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Tests;

internal static class InputSuspensionTests
{
    [Test]
    public static void JapaneseOnly_PersistsAsSharedSetting()
    {
        var path = Path.Combine(Path.GetTempPath(), $"meltype-keyboard-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{}");
            Assert.True(!Settings.Load(path).JapaneseKeyboardOnly, "既存の設定ファイルでは OFF");
            var settings = new Settings { JapaneseKeyboardOnly = true };
            settings.Clone().Normalize().Save(path);
            Assert.True(Settings.Load(path).JapaneseKeyboardOnly, "保存・複製・読み込みで設定が残る");
            var profile = settings.Normalize().AddProfile("仕事用")!;
            profile.JapaneseKeyboardOnly = false;
            Assert.True(!profile.SwitchProfile(Settings.DefaultProfileName).JapaneseKeyboardOnly, "全プロファイル共通の設定");
        }
        finally { File.Delete(path); }
    }

    [Test]
    public static void Suspend_ReplaysUnprocessedKeysAndClicksInOrder()
    {
        var keyboard = new CompositionTests.Keyboard();
        keyboard.Gate.OnKey(new KeyEvent('K', 0, false, false, false, 1), _ => true);
        keyboard.Gate.OnKey(new KeyEvent('K', 0, false, true, false, 2), _ => true);
        keyboard.Gate.OnMouseButton(new MouseButtonEvent(0x201, 10, 20, 0));
        keyboard.Controller.SuspendInput();

        Assert.Equal("down:4B,up:4B,mouse:201", string.Join(",", keyboard.Host.Events));
        Assert.Equal(0, keyboard.Host.Output.Count, "処理前のキーを変換・確定しない");
        Assert.True(!keyboard.Gate.IsCaptured, "キューにしか入力がなくても関所を開ける");
    }

    [Test]
    public static void Suspend_CancelsCompositionAndCanResume()
    {
        var keyboard = new CompositionTests.Keyboard();
        keyboard.Type("kyo");
        Assert.True(keyboard.Controller.IsComposing, "変換中に切り替える");
        keyboard.Controller.SuspendInput();

        Assert.True(!keyboard.Controller.IsComposing && !keyboard.Gate.IsCaptured, "変換と保留を解除する");
        Assert.True(keyboard.Host.View is null, "古い変換を残さない");
        Assert.Equal(0, keyboard.Host.Output.Count, "他言語の入力欄へ確定しない");

        keyboard.Type("neko\n");
        Assert.Equal("ねこ", string.Concat(keyboard.Host.Output), "再開後に前の入力を持ち越さない");
    }

    [Test]
    public static void Suspend_StopsDirectModeDetectionBeforeIdleTimer()
    {
        var keyboard = new CompositionTests.Keyboard(direct: true);
        keyboard.Type("ko");
        Assert.True(keyboard.Controller.IsComposing, "直接入力で判定待ち");
        keyboard.Controller.SuspendInput();
        var events = keyboard.Host.Events.Count;
        keyboard.Controller.Tick(keyboard.Now + 1000);

        Assert.True(keyboard.Direct && !keyboard.Gate.IsCaptured, "タイマーで日本語に戻さない");
        Assert.Equal(events, keyboard.Host.Events.Count, "送信済みの文字を再入力・削除しない");
    }
}
