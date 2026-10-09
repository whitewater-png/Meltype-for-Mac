// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Config;

namespace Meltype.Tests;

internal static class ReportUrlTests
{
    [Test]
    public static void Fallback_ForMac_DoesNotGoToUpstream()
    {
        // 報告の URL を作れなかったときも、Mac 版の報告を本家へ誘導しない
        var mac = ProjectInfo.FallbackReportUrl("Mac");
        Assert.True(mac.StartsWith(ProjectInfo.MacSourceUrl, StringComparison.Ordinal) && !mac.Contains("yksr-melt"), "Mac 版は Mac 版のリポジトリへ");
        Assert.True(ProjectInfo.FallbackReportUrl("Linux").StartsWith(ProjectInfo.SourceUrl, StringComparison.Ordinal), "Linux は今までどおり");
    }

    [Test]
    public static void Mac_OpensMacRepositoryIssues()
    {
        var url = ProjectInfo.ReportUrl("Mac (プレビュー版)", "1.0.1", "OS: macOS");
        Assert.True(url.StartsWith(ProjectInfo.MacSourceUrl + "/issues/new?template=1-bug.yml", StringComparison.Ordinal), "Mac の報告は Mac 版のリポジトリを開く: " + url);
        Assert.True(!url.Contains("yksr-melt", StringComparison.Ordinal), "本家のリポジトリは開かない: " + url);
        Assert.True(url.Contains("version=1.0.1", StringComparison.Ordinal), "版が入る: " + url);
    }

    [Test]
    public static void Other_StillOpensUpstreamIssues()
    {
        var url = ProjectInfo.ReportUrl("Windows 11", "1.0.0", "env");
        Assert.True(url.StartsWith(ProjectInfo.SourceUrl, StringComparison.Ordinal), "Mac 以外は従来どおり: " + url);
    }
}
