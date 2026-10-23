// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Config;

namespace Meltype.Tests;

/// <summary>
/// 本家 #1039: 日本語の文の中に英字の選手名 (Koki Ogawa) を打つ。
/// 設定「英単語の後ろの英字の名前を英語にする」で、英単語の続きの名前 (Ogawa) が
/// 「Oがわ」に割れず、英語のまま確定できること。既定 (OFF) は今までどおり。
/// </summary>
internal static class NameInJapaneseTests
{
    /// <summary>共有の判定器の設定を切り替えて、元に戻す。</summary>
    private static void WithSetting(bool on, Action body)
    {
        var previous = CompositionTests.Detector.KeepEnglishNameAfterEnglish;
        CompositionTests.Detector.KeepEnglishNameAfterEnglish = () => on;
        try { body(); }
        finally { CompositionTests.Detector.KeepEnglishNameAfterEnglish = previous; }
    }

    [Test]
    public static void DefaultIsOff()
    {
        Assert.True(!new Settings().KeepEnglishNameAfterEnglish, "設定の既定は OFF (今までの動きを変えない)");
    }

    [Test]
    public static void Off_KokiOgawaSplitsAsBefore()
    {
        var k = new CompositionTests.Keyboard();
        k.Type("Koki Ogawa\n");
        Assert.Equal("Koki Oがわ", k.Host.Document, "OFF のときは今までどおり O と がわ に分かれる");
    }

    [Test]
    public static void On_KokiOgawaStaysEnglish()
    {
        WithSetting(true, () =>
        {
            var k = new CompositionTests.Keyboard();
            k.Type("Koki Ogawa\n");
            Assert.Equal("Koki Ogawa", k.Host.Document, "ON のときは英単語の続きの名前が英語のまま");
        });
    }

    [Test]
    public static void On_KokiOgawaGaStaysEnglish()
    {
        WithSetting(true, () =>
        {
            var k = new CompositionTests.Keyboard();
            k.Type("Koki Ogawa ga\n");
            Assert.Equal("Koki Ogawa が", k.Host.Document, "ON のときは名前の後ろの助詞だけが日本語");
        });
    }

    [Test]
    public static void On_MessiAndRonaldoUnchanged()
    {
        WithSetting(true, () =>
        {
            var k = new CompositionTests.Keyboard();
            k.Type("Messi ga\n");
            Assert.Equal("Messi が", k.Host.Document, "Messi は今までどおり英語のまま (変わらない)");
            k.Type("Ronaldo ga\n");
            Assert.Equal("Messi がRonaldo が", k.Host.Document, "Ronaldo も今までどおり英語のまま");
        });
    }

    [Test]
    public static void On_JapaneseSentenceUnaffected()
    {
        WithSetting(true, () =>
        {
            var k = new CompositionTests.Keyboard();
            k.Type("kyouhagoogle\n");
            Assert.Equal("きょうはgoogle", k.Host.Document, "小文字の英単語は今までどおり");
            k.Type("Anisiyouka\n");
            Assert.Equal("きょうはgoogleAにしようか", k.Host.Document, "大文字で始まる日本語の文は今までどおり (Aにしようか)");
        });
    }
}
