// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Globalization;
using System.Text;
using Meltype.Composition;
using Meltype.Config;

namespace Meltype.Tests;

/// <summary>
/// 本家 #1157: 日時と数値の書式を地域設定 (タイ語の仏暦・アラビア語の別の数字など) に依存させない。
/// 各テストは、直す前は赤・直した後は緑。
/// </summary>
internal static class Upstream1028U8Tests
{
    /// <summary>CurrentCulture を culture に切り替えて action を流し、終わったら戻す。</summary>
    private static void WithCulture(string culture, Action action)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            action();
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Test]
    public static void Export_TimestampIsGregorianInThaiAndArabicLocales()
    {
        foreach (var culture in new[] { "th-TH", "ar-SA" })
        {
            WithCulture(culture, () =>
            {
                var exported = Encoding.Unicode.GetString(UserDictionaryFile.Export([new UserWord("てすと", "テスト")]));
                var stamp = exported.Split("!DateTime:", StringSplitOptions.None)[1].Split('\r')[0];
                var date = DateTime.ParseExact(stamp, "yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture);
                Assert.Equal(DateTime.Now.Year, date.Year, culture + ": 書き出す日時は西暦");
                Assert.Equal(new UserWord("てすと", "テスト"), UserDictionaryFile.Parse(Encoding.Unicode.GetBytes(exported)).Words[0]);
            });
        }
    }

    [Test]
    public static void Backup_TimestampIsGregorianInThaiAndArabicLocales()
    {
        foreach (var culture in new[] { "th-TH", "ar-SA" })
        {
            var root = Path.Combine(Path.GetTempPath(), "meltype-u8-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                WithCulture(culture, () =>
                {
                    var (content, _) = Backup.Create(root, "test");
                    var (created, _, _) = Backup.Inspect(content);
                    var date = DateTime.ParseExact(created, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
                    Assert.Equal(DateTime.Now.Year, date.Year, culture + ": 保存する日時は西暦");
                });
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public static void NumberAndEraCandidates_DoNotDependOnCurrentCulture()
    {
        var expected = string.Join("|", NumberCandidates.For("２０２６"));
        var eraExpected = string.Join("|", CompositionController.EraToYear("れいわ８ねん")!);
        Assert.Equal("2026年|2026", eraExpected, "地域設定が既定のときの元号の読み");
        Assert.True(expected.Contains("令和8年"), "既定の地域設定で 2026 は 令和8年");
        foreach (var culture in new[] { "th-TH", "ar-SA", "fa-IR" })
        {
            WithCulture(culture, () =>
            {
                Assert.Equal(expected, string.Join("|", NumberCandidates.For("２０２６")), culture + ": 数値の候補");
                Assert.Equal("令和元年", NumberCandidates.ToEra(2019), culture + ": 元年");
                Assert.Equal("令和8年", NumberCandidates.ToEra(2026), culture + ": 元号の年");
                Assert.Equal(eraExpected, string.Join("|", CompositionController.EraToYear("れいわ８ねん")!), culture + ": 元号から西暦");
                Assert.Equal("1989年|1989", string.Join("|", CompositionController.EraToYear("へいせいがんねん")!), culture + ": 元年から西暦");
            });
        }
    }

    [Test]
    public static void NumberCandidates_Guards()
    {
        Assert.Equal(0, NumberCandidates.For("abc").Count, "数字でなければ候補なし");
        Assert.Equal("二〇二六", NumberCandidates.For("2026")[0], "漢数字 (位取り)");
        Assert.True(NumberCandidates.For("1,234").Contains("千二百三十四"), "コンマ付き");
        Assert.True(NumberCandidates.For("0")[0] == "〇", "0");
    }
}
