// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text.Json;
using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>
/// 本家 #990: 日本語を確定した直後に / を打つと中黒 (・) にする設定 (SlashAsNakaguro)。
/// 既定は OFF (今までの挙動を変えない)。ON でも英語の直後・行頭・空白の直後・URL の途中は / のまま。
/// </summary>
internal static class SlashAsNakaguroTests
{
    /// <summary>実機 (と --repro) と同じく、同梱の英単語の一覧をスペルチェッカーとして使って打つ。</summary>
    private static T WithWords<T>(Func<T> body)
    {
        var previous = CompositionTests.Detector.SpellChecker;
        CompositionTests.Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try { return body(); }
        finally { CompositionTests.Detector.SpellChecker = previous; }
    }

    /// <summary>設定を ON/OFF して打ち、Enter で確定した文字列。</summary>
    private static string Committed(bool on, string keys) => WithWords(() =>
    {
        var k = new CompositionTests.Keyboard { SlashAsNakaguro = on };
        k.Type(keys + "\n");
        return k.Host.Document;
    });

    private static JsonElement Item(string json, string key)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("items").EnumerateArray().First(i => i.GetProperty("key").GetString() == key).Clone();
    }

    // ---- 設定そのもの ----

    [Test]
    public static void Setting_DefaultsToOff()
    {
        // 既存の利用者の挙動を変えない (SettingsVersion の変更も移行もしない)。
        Assert.Equal(false, new Settings().SlashAsNakaguro);
    }

    [Test]
    public static void Setting_IsInMacCatalog_WithDefaultOff()
    {
        var json = MacSettingsCatalog.ToJson(new Settings());
        var item = Item(json, nameof(Settings.SlashAsNakaguro));
        Assert.Equal("日本語の直後の / を ・ にする", item.GetProperty("label").GetString());
        Assert.Equal("bool", item.GetProperty("kind").GetString());
        Assert.Equal(false, item.GetProperty("value").GetBoolean());
        // 「入力」の組に出す (入力の変換の設定の並び)
        Assert.Equal("入力", item.GetProperty("group").GetString());
        // 設定タブの値が Settings と同じ (DisplayName / Description の一致は MacSettingsTests が見る)
        var attribute = typeof(Settings).GetProperty(nameof(Settings.SlashAsNakaguro))!
            .GetCustomAttributes(typeof(System.ComponentModel.DisplayNameAttribute), false)
            .Cast<System.ComponentModel.DisplayNameAttribute>().Single();
        Assert.Equal(attribute.DisplayName, item.GetProperty("label").GetString());
    }

    // ---- ON: 日本語の直後の / を ・ にする ----

    [Test]
    public static void On_ConvertsSlashAfterJapanese()
    {
        // かなで確定した直後 (きょう → /)
        Assert.Equal("きょう・", Committed(true, "kyou\n/"));
    }

    [Test]
    public static void On_ConvertsSlashAfterKanji()
    {
        // Space で変換して漢字で確定した直後 (今日 → /)
        Assert.Equal("今日・", Committed(true, "kyou \n/"));
    }

    // ---- OFF: 今までどおり / のまま ----

    [Test]
    public static void Off_KeepsSlashAfterJapanese()
    {
        Assert.Equal("きょう/", Committed(false, "kyou\n/"));
    }

    [Test]
    public static void Off_KeepsSlashAfterKanji()
    {
        Assert.Equal("今日/", Committed(false, "kyou \n/"));
    }

    // ---- ON でも守る (英語・行頭・空白・URL・入力の途中) ----

    [Test]
    public static void On_KeepsSlashAfterEnglish()
    {
        // 英語の直後は URL・パスを打てるように / のまま
        Assert.Equal("hello/", Committed(true, "hello\n/"));
    }

    [Test]
    public static void On_KeepsSlashAtStart()
    {
        // 行頭は / のまま
        Assert.Equal("/", Committed(true, "/"));
    }

    [Test]
    public static void On_KeepsSlashAfterWhitespace()
    {
        // 空白の直後は / のまま
        Assert.Equal("hello /", Committed(true, "hello \n/"));
    }

    [Test]
    public static void On_KeepsSlashInUrl()
    {
        // URL の途中 (http://) は / のまま
        Assert.Equal("http://", Committed(true, "http://"));
    }

    [Test]
    public static void On_KeepsSlashInMiddleOfInput()
    {
        // 変換ボックスの途中の / は今までどおり半角 (最初の打鍵のときだけ ・ にする)
        Assert.Equal("きょう/あ", Committed(true, "kyou/a"));
    }
}
