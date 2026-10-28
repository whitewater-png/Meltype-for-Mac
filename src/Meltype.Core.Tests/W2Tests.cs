// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text.Json;
using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>
/// B3 (本家 #1008): z + 記号 (/ . , - [ ]) の変換 (・ … ‥ ～ 『 』) を設定で ON/OFF できるようにする。
/// 既定は ON (今までの挙動を変えない)。OFF では打ったまま残す
/// (1000Hz. が 1000H… にならない)。z + h j k l の矢印は、ON/OFF どちらでも今までどおり。
/// </summary>
internal static class W2Tests
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
        var k = new CompositionTests.Keyboard { ZSymbolConversion = on };
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
    public static void Setting_DefaultsToOn()
    {
        // 既存の利用者の挙動を変えない (SettingsVersion の変更も移行もしない)。
        Assert.True(new Settings().ZSymbolConversion, "既定は ON");
    }

    [Test]
    public static void Setting_IsInMacCatalog_WithDefaultOn()
    {
        var json = MacSettingsCatalog.ToJson(new Settings());
        var item = Item(json, nameof(Settings.ZSymbolConversion));
        Assert.Equal("z + 記号を変換する", item.GetProperty("label").GetString());
        Assert.Equal("bool", item.GetProperty("kind").GetString());
        Assert.True(item.GetProperty("value").GetBoolean(), "設定タブでも既定は ON");
        Assert.True(item.GetProperty("description").GetString()!.Contains("z/ → z/"), "OFF のときにどうなるかを書く");
        // 「記号・句読点」の組に出す (入力の変換の設定の並び)
        Assert.Equal("記号・句読点", item.GetProperty("group").GetString());
        // 設定タブの値が Settings と同じ (DisplayName / Description の一致は MacSettingsTests が見る)
        var attribute = typeof(Settings).GetProperty(nameof(Settings.ZSymbolConversion))!
            .GetCustomAttributes(typeof(System.ComponentModel.DisplayNameAttribute), false)
            .Cast<System.ComponentModel.DisplayNameAttribute>().Single();
        Assert.Equal(attribute.DisplayName, item.GetProperty("label").GetString());
    }

    // ---- ON (今までどおり) ----

    [Test]
    public static void On_ConvertsSymbols()
    {
        Assert.Equal("・", Committed(true, "z/"));
        Assert.Equal("…", Committed(true, "z."));
        Assert.Equal("‥", Committed(true, "z,"));
        Assert.Equal("～", Committed(true, "z-"));
        Assert.Equal("『", Committed(true, "z["));
        Assert.Equal("』", Committed(true, "z]"));
    }

    [Test]
    public static void On_IchimanHz_IsStillConverted()
    {
        // #1008 で報告された見え方 (この設定を ON のままにした人の今までの結果)。
        Assert.Equal("1000h…", Committed(true, "1000hz."));
        Assert.Equal("1000H…", Committed(true, "1000Hz."));
        Assert.Equal("10お…", Committed(true, "10oz."));
        Assert.Equal("じゃっ…", Committed(true, "jazz."));
        Assert.Equal("～axis", Committed(true, "z-axis"));
    }

    // ---- OFF (打ったまま) ----

    [Test]
    public static void Off_KeepsTypedSymbols()
    {
        Assert.Equal("z/", Committed(false, "z/"));
        Assert.Equal("z.", Committed(false, "z."));
        Assert.Equal("z,", Committed(false, "z,"));
        Assert.Equal("z-", Committed(false, "z-"));
        Assert.Equal("z[", Committed(false, "z["));
        Assert.Equal("z]", Committed(false, "z]"));
    }

    [Test]
    public static void Off_Issue1008Examples_StayAsTyped()
    {
        Assert.Equal("1000hz.", Committed(false, "1000hz."));
        Assert.Equal("1000Hz.", Committed(false, "1000Hz."));
        Assert.Equal("10oz.", Committed(false, "10oz."));
        Assert.Equal("quiz.", Committed(false, "quiz."));
        Assert.Equal("jazz.", Committed(false, "jazz."));
        Assert.Equal("quartz.", Committed(false, "quartz."));
        Assert.Equal("z-axis", Committed(false, "z-axis"));
        // 前の , は日本語の句読点 (、) のまま。z + , だけが打ったまま残る。
        Assert.Equal("x、y、z,", Committed(false, "x,y,z,"));
    }

    /// <summary>
    /// OFF でも、直前に打った英字が英単語でなければ、かなはかなのまま残す (日本語が英字に戻らない)。
    /// 戻すのは「単位の Raw + 保留中の文字」が知っている英単語のときだけ。保留中の文字と記号は打ったとおりに足す。
    /// </summary>
    [Test]
    public static void Off_JapaneseBeforeZSuffix_StaysKana()
    {
        Assert.Equal("きょうz.", Committed(false, "kyouz."));
        Assert.Equal("にほんごz.", Committed(false, "nihongoz."));
        Assert.Equal("かにz.", Committed(false, "kaniz."));
        Assert.Equal("ずっとz.", Committed(false, "zuttoz."));
        // z を落とさない (かぞえかたz.)
        Assert.Equal("かぞえかたz.", Committed(false, "kazoekataz."));
        Assert.Equal("きょうz/", Committed(false, "kyouz/"));
        Assert.Equal("にほんごz[", Committed(false, "nihongoz["));
        // 英単語のときは打った綴りに戻す (今までどおり)。
        Assert.Equal("10oz.", Committed(false, "10oz."));
        Assert.Equal("1000hz.", Committed(false, "1000hz."));
        Assert.Equal("jazz.", Committed(false, "jazz."));
        Assert.Equal("ずっとjazz.", Committed(false, "zuttojazz."));
        Assert.Equal("にほんごjazz.", Committed(false, "nihongojazz."));
    }

    /// <summary>
    /// OFF で z + 記号を打ったあと続けて打っても、打った z が消えない。
    /// 記号を打ったときに足した英字が次の打鍵で保護から外れ、確定の直前の打ち間違いの直し
    /// (kazoekataz → kazoekata) が末尾の z を落としていた (かぞえかたz.です が かぞえかた.です になっていた)。
    /// </summary>
    [Test]
    public static void Off_KeepsTheTypedZ_WhenTypingContinues()
    {
        Assert.Equal("かぞえかたz.です", Committed(false, "kazoekataz.desu"));
        Assert.Equal("かぞえかたz.ですね", Committed(false, "kazoekataz.desune"));
        // 近い例 (英単語ではない日本語のあとの z)
        Assert.Equal("かにz.です", Committed(false, "kaniz.desu"));
        Assert.Equal("ずっとz.です", Committed(false, "zuttoz.desu"));
        Assert.Equal("きょうz.です", Committed(false, "kyouz.desu"));
        Assert.Equal("きょうz.だね", Committed(false, "kyouz.dane"));
        // 空白で区切って続けても同じ (テストの変換の偽物が きょう → 今日 にするので、ここは かに で見る)
        Assert.Equal("かにz.だね", Committed(false, "kaniz. dane"));
        // 同じ入力の中で 2 回続けても 1 回目が消えない
        Assert.Equal("かにz.かにz.", Committed(false, "kaniz.kaniz."));
    }

    /// <summary>ON のときの挙動 (今までどおり) を固定する。</summary>
    [Test]
    public static void On_ZSymbolExamples_AreUnchanged()
    {
        Assert.Equal("きょう…", Committed(true, "kyouz."));
        Assert.Equal("じゃっ…", Committed(true, "jazz."));
        Assert.Equal("1000h…", Committed(true, "1000hz."));
        Assert.Equal("zebra.", Committed(true, "zebra."));
    }

    [Test]
    public static void Off_ShowsTheTypedTextWhileComposing() => WithWords(() =>
    {
        // Enter の前 (変換ボックス) でも、打ったままに見えること。
        var off = new CompositionTests.Keyboard { ZSymbolConversion = false };
        off.Type("1000hz.");
        Assert.Equal("1000hz.", off.Showing, "OFF の変換ボックス");
        off.Type("z/");
        Assert.Equal("1000hz.z/", off.Showing, "OFF で続けて打っても打ったまま");
        var on = new CompositionTests.Keyboard { ZSymbolConversion = true };
        on.Type("1000hz.");
        Assert.Equal("1000h…", on.Showing, "ON の変換ボックスは今までどおり");
        return 0;
    });

    // ---- 矢印は ON/OFF で変わらない ----

    [Test]
    public static void Arrows_AreTheSameOnBothSettings()
    {
        foreach (var on in new[] { true, false })
        {
            var setting = on ? "ON" : "OFF";
            Assert.Equal("←", Committed(on, "zh"), "zh (" + setting + ")");
            Assert.Equal("↓", Committed(on, "zj"), "zj (" + setting + ")");
            Assert.Equal("↑", Committed(on, "zk"), "zk (" + setting + ")");
            Assert.Equal("→", Committed(on, "zl"), "zl (" + setting + ")");
        }
    }

    [Test]
    public static void Z_AloneAndEnglishWords_AreTheSameOnBothSettings()
    {
        foreach (var on in new[] { true, false })
        {
            var setting = on ? "ON" : "OFF";
            Assert.Equal("z", Committed(on, "z"), "z (" + setting + ")");
            Assert.Equal("zebra", Committed(on, "zebra"), "zebra (" + setting + ")");
            Assert.Equal("ずっと", Committed(on, "zutto"), "zutto (" + setting + ")");
            Assert.Equal("zlib", Committed(on, "zlib"), "zlib (" + setting + ")");
        }
    }

    // ---- 設定は次に打つ文字から効く ----

    [Test]
    public static void Off_TakesEffectOnTheNextKey_WithoutRecreatingTheSession() => WithWords(() =>
    {
        var settings = new Settings();
        var session = new MeltypeSession(CompositionTests.Detector, new CompositionTests.FakeConverter(),
            new CompositionOptions { ZSymbolConversion = () => settings.ZSymbolConversion, History = new ConversionHistory(null) },
            () => settings);
        // z と / を打って Enter。変換ボックスを閉じるので、毎回まっさらから打てる。
        string ZSlash()
        {
            session.HandleKey('Z', 'z', false, false, false, false);
            session.HandleKey(0xBF, '/', false, false, false, false);
            return session.HandleKey(VirtualKeys.Return, null, false, false, false, false).Commits.Single().Text;
        }

        Assert.Equal("・", ZSlash());
        // 変換ボックスの外で設定を変えた (設定タブ)。セッションは作り直さない。
        settings.ZSymbolConversion = false;
        Assert.Equal("z/", ZSlash(), "次の文字から OFF が効く");
        settings.ZSymbolConversion = true;
        Assert.Equal("・", ZSlash(), "ON に戻せる");
        return 0;
    });
}
