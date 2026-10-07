// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Detection;
using Meltype.Learning;
using static Meltype.Tests.TestSupport;

namespace Meltype.Tests;

internal static class UserModelTests
{
    [Test]
    public static void MissedJapanese_IsLearned()
    {
        var model = new UserModel(null);
        var engine = CreateEngine(user: model);
        Assert.True(Classify(engine, "zanzok").Verdict != Verdict.Japanese, "学習前は判断できない");

        // 判定不能で素通しした後、ユーザーが IME を ON にした (見逃し) を学習する。
        model.Learn("zanzo", SessionOutcome.StayedRejected, decidedEnglish: false);
        Assert.Equal(Verdict.Japanese, Classify(engine, "zanzok").Verdict, "学習後は日本語と判定");
    }

    [Test]
    public static void FalsePositive_LowersWeight()
    {
        var model = new UserModel(null);
        var engine = CreateEngine(user: model);
        Assert.Equal(Verdict.Japanese, Classify(engine, "kyou").Verdict);

        model.Learn("kyo", SessionOutcome.JapaneseRejected, decidedEnglish: false);
        Assert.True(Classify(engine, "kyou").Verdict != Verdict.Japanese, "誤爆と言われたパターンは切り替えない");
    }

    [Test]
    public static void OnlyShortPrefixesAreStored()
    {
        var model = new UserModel(null);
        model.Learn("konnichiwa", SessionOutcome.JapaneseAccepted, decidedEnglish: false);
        Assert.True(model.Get("konnic") is not null, "最大 6 文字の prefix だけを保存");
        Assert.True(model.Get("konnichiwa") is null, "入力内容そのものは保存しない");
        model.Learn("ka", SessionOutcome.JapaneseAccepted, decidedEnglish: false);
        Assert.True(model.Get("ka") is null, "短すぎる prefix は保存しない");
    }

    [Test]
    public static void SaveAndLoad_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"meltype-model-{Guid.NewGuid():N}.json");
        try
        {
            var model = new UserModel(path);
            model.Learn("konn", SessionOutcome.JapaneseAccepted, decidedEnglish: false);
            model.Learn("dev", SessionOutcome.StayedAccepted, decidedEnglish: true);
            model.Save();
            var loaded = new UserModel(path);
            Assert.Equal(1, loaded.Get("konn")!.Japanese);
            Assert.Equal(1, loaded.Get("dev")!.English);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
