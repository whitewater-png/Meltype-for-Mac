// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Update;

namespace Meltype.Tests;

/// <summary>更新の判定 (UpdateCheck.Evaluate) のテスト。ネットワークには出ず、JSON の文字列だけを渡す。</summary>
internal static class UpdateCheckTests
{
    private const string Sha = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789ABCDEF";
    private const string Base = "https://github.com/whitewater-png/Meltype-for-Mac/releases/download/";

    private static string Json(string tag = "v1.0.4-mac", string version = "1.0.4", bool draft = false, bool prerelease = false,
        string? url = null, string? digest = Sha, long size = 5_000_000, string? name = null)
    {
        url ??= $"{Base}{tag}/Meltype-mac-{version}.zip";
        name ??= $"Meltype-mac-{version}.zip";
        var digestPart = digest is null ? "" : $",\"digest\":\"{digest}\"";
        return $"{{\"tag_name\":\"{tag}\",\"draft\":{(draft ? "true" : "false")},\"prerelease\":{(prerelease ? "true" : "false")}," +
               $"\"html_url\":\"https://github.com/whitewater-png/Meltype-for-Mac/releases/tag/{tag}\"," +
               $"\"assets\":[{{\"name\":\"other.txt\",\"size\":1}},{{\"name\":\"{name}\",\"size\":{size},\"browser_download_url\":\"{url}\"{digestPart}}}]}}";
    }

    [Test]
    public static void Newer_ReturnsInfo()
    {
        var info = UpdateCheck.Evaluate(Json(), "1.0.3");
        Assert.True(info is not null, "新しい版は有効");
        Assert.Equal("1.0.4", info!.Version);
        Assert.Equal(5_000_000L, info.Size);
        Assert.Equal(Sha["sha256:".Length..].ToLowerInvariant(), info.Sha256);
        Assert.True(info.DownloadUrl.EndsWith("/Meltype-mac-1.0.4.zip", StringComparison.Ordinal), "URL: " + info.DownloadUrl);
        Assert.True(info.ReleaseUrl.Contains("/releases/tag/v1.0.4-mac", StringComparison.Ordinal), "リリースページ: " + info.ReleaseUrl);
        Assert.Equal(5, info.ToLines().Split('\n').Length);
    }

    [Test]
    public static void SameOrOlder_ReturnsNull()
    {
        Assert.True(UpdateCheck.Evaluate(Json(), "1.0.4") is null, "同じ版は通知しない");
        Assert.True(UpdateCheck.Evaluate(Json(), "1.0.5") is null, "今の版のほうが新しいなら通知しない");
        Assert.True(UpdateCheck.Evaluate(Json(), "1.1.0") is null, "マイナーが上なら通知しない");
    }

    [Test]
    public static void Compare_IsNumeric()
    {
        Assert.True(UpdateCheck.Evaluate(Json("v1.0.10-mac", "1.0.10"), "1.0.9") is not null, "1.0.10 は 1.0.9 より新しい (文字列比較ではない)");
        Assert.True(UpdateCheck.Evaluate(Json("v1.0.9-mac", "1.0.9"), "1.0.10") is null, "1.0.9 は 1.0.10 より古い");
        Assert.True(UpdateCheck.Evaluate(Json("v1.0.0.1", "1.0.0.1"), "1.0.0") is not null, "桁が足りない側は 0 とみなす");
    }

    [Test]
    public static void DraftAndPrerelease_AreIgnored()
    {
        Assert.True(UpdateCheck.Evaluate(Json(prerelease: true), "1.0.3") is null, "prerelease は無視");
        Assert.True(UpdateCheck.Evaluate(Json(draft: true), "1.0.3") is null, "draft は無視");
    }

    [Test]
    public static void NoDigest_Or_BadDigest_IsNotUpdatable()
    {
        Assert.True(UpdateCheck.Evaluate(Json(digest: null), "1.0.3") is null, "digest が無ければ更新不可");
        Assert.True(UpdateCheck.Evaluate(Json(digest: "sha256:abc"), "1.0.3") is null, "桁が足りない digest は更新不可");
        Assert.True(UpdateCheck.Evaluate(Json(digest: "sha1:" + new string('a', 64)), "1.0.3") is null, "sha256 以外は更新不可");
        Assert.True(UpdateCheck.Evaluate(Json(digest: "sha256:" + new string('g', 64)), "1.0.3") is null, "16 進でない digest は更新不可");
    }

    [Test]
    public static void ForeignUrl_IsRejected()
    {
        Assert.True(UpdateCheck.Evaluate(Json(url: "https://evil.example.com/Meltype-mac-1.0.4.zip"), "1.0.3") is null, "別ホストは不可");
        Assert.True(UpdateCheck.Evaluate(Json(url: "https://github.com/someone-else/Meltype-for-Mac/releases/download/v1.0.4-mac/Meltype-mac-1.0.4.zip"), "1.0.3") is null, "別リポジトリは不可");
        Assert.True(UpdateCheck.Evaluate(Json(url: "http://github.com/whitewater-png/Meltype-for-Mac/releases/download/v1.0.4-mac/Meltype-mac-1.0.4.zip"), "1.0.3") is null, "http は不可");
        Assert.True(UpdateCheck.Evaluate(Json(url: Base + "v1.0.4-mac/other.zip"), "1.0.3") is null, "URL の末尾のファイル名が違えば不可");
        Assert.True(UpdateCheck.Evaluate(Json(url: Base + "v1.0.4-mac/Meltype-mac-1.0.4.zip?x=1"), "1.0.3") is null, "クエリ付きは不可");
    }

    [Test]
    public static void TrickyUrls_AreRejected()
    {
        var good = Base + "v1.0.4-mac/Meltype-mac-1.0.4.zip";
        Assert.True(UpdateCheck.Evaluate(Json(url: good), "1.0.3") is not null, "基準の URL は有効");
        Assert.True(UpdateCheck.Evaluate(Json(url: Base + "v1.0.4-mac/../v1.0.3-mac/Meltype-mac-1.0.4.zip"), "1.0.3") is null, "../ は不可");
        Assert.True(UpdateCheck.Evaluate(Json(url: Base + "v1.0.4-mac/%2e%2e/x/Meltype-mac-1.0.4.zip"), "1.0.3") is null, "%2e%2e は不可");
        Assert.True(UpdateCheck.Evaluate(Json(url: Base + "v1.0.4-mac/Meltype-mac-1.0.4%2ezip"), "1.0.3") is null, "パーセントエンコードは不可");
        Assert.True(UpdateCheck.Evaluate(Json(url: Base + "v1.0.4-mac\\Meltype-mac-1.0.4.zip"), "1.0.3") is null, "バックスラッシュは不可");
        Assert.True(UpdateCheck.Evaluate(Json(url: Base + "v1.0.4-mac/Meltype-mac-1.0.4.zip\n"), "1.0.3") is null, "改行は不可");
        Assert.True(UpdateCheck.Evaluate(Json(url: Base + "v1.0.4-mac/Meltype-mac-1.0.4.zip "), "1.0.3") is null, "空白は不可");
        Assert.True(UpdateCheck.Evaluate(Json(url: Base + "v1.0.3-mac/Meltype-mac-1.0.4.zip"), "1.0.3") is null, "別タグのパスは不可");
        Assert.True(UpdateCheck.Evaluate(Json(url: "https://github.com/whitewater-png/Meltype-for-Mac2/releases/download/v1.0.4-mac/Meltype-mac-1.0.4.zip"), "1.0.3") is null, "別リポジトリは不可");
        Assert.True(UpdateCheck.Evaluate(Json(url: "https://github.com:8443/whitewater-png/Meltype-for-Mac/releases/download/v1.0.4-mac/Meltype-mac-1.0.4.zip"), "1.0.3") is null, "ポート指定は不可");
        Assert.True(UpdateCheck.Evaluate(Json(url: good + "#a"), "1.0.3") is null, "fragment は不可");
        Assert.True(UpdateCheck.Evaluate(Json(url: good.Replace("github.com", "github.com@evil.example")), "1.0.3") is null, "ユーザー情報は不可");
    }

    [Test]
    public static void AssetNameMismatch_Or_Missing_IsRejected()
    {
        Assert.True(UpdateCheck.Evaluate(Json(name: "Meltype-mac-1.0.5.zip"), "1.0.3") is null, "タグの版と資産名が違えば不可");
        Assert.True(UpdateCheck.Evaluate("{\"tag_name\":\"v1.0.4\",\"assets\":[]}", "1.0.3") is null, "資産が無ければ不可");
        Assert.True(UpdateCheck.Evaluate("{\"tag_name\":\"v1.0.4\"}", "1.0.3") is null, "assets が無ければ不可");
    }

    [Test]
    public static void Size_Limits()
    {
        Assert.True(UpdateCheck.Evaluate(Json(size: UpdateCheck.MaxAssetSize), "1.0.3") is not null, "ちょうど上限は有効");
        Assert.True(UpdateCheck.Evaluate(Json(size: UpdateCheck.MaxAssetSize + 1), "1.0.3") is null, "上限超過は不可");
        Assert.True(UpdateCheck.Evaluate(Json(size: 0), "1.0.3") is null, "0 バイトは不可");
    }

    [Test]
    public static void BrokenJson_ReturnsNull_WithoutThrowing()
    {
        Assert.True(UpdateCheck.Evaluate("{ not json", "1.0.3") is null, "壊れた JSON");
        Assert.True(UpdateCheck.Evaluate("", "1.0.3") is null, "空");
        Assert.True(UpdateCheck.Evaluate(null, "1.0.3") is null, "null");
        Assert.True(UpdateCheck.Evaluate("[1,2]", "1.0.3") is null, "配列");
        Assert.True(UpdateCheck.Evaluate("{\"tag_name\":5,\"assets\":\"x\"}", "1.0.3") is null, "型違い");
        Assert.True(UpdateCheck.Evaluate(Json(), "abc") is null, "今の版が読めなければ判定しない");
        Assert.True(UpdateCheck.Evaluate(Json(), null) is null, "今の版が null なら判定しない");
    }

    [Test]
    public static void TooLargeJson_IsRejected()
    {
        var big = Json().TrimEnd('}') + ",\"pad\":\"" + new string('x', UpdateCheck.MaxJsonBytes) + "\"}";
        Assert.True(UpdateCheck.Evaluate(big, "1.0.3") is null, "1MB を超える JSON は読まない");
    }

    [Test]
    public static void TagFormats()
    {
        Assert.True(UpdateCheck.Evaluate(Json("v1.0.4", "1.0.4"), "1.0.3") is not null, "v1.0.4");
        Assert.True(UpdateCheck.Evaluate(Json("1.0.4", "1.0.4"), "1.0.3") is not null, "v なし");
        Assert.True(UpdateCheck.Evaluate(Json("v1.0.4-beta", "1.0.4"), "1.0.3") is null, "-beta は不可");
        Assert.True(UpdateCheck.Evaluate(Json("latest", "1.0.4"), "1.0.3") is null, "数字でないタグは不可");
        Assert.True(UpdateCheck.Evaluate(Json("v1", "1"), "1.0.3") is null, "桁が 1 つだけは不可");
        Assert.True(UpdateCheck.Evaluate(Json("v1.0.4-mac-extra", "1.0.4"), "1.0.3") is null, "余計な後ろは不可");
        Assert.True(UpdateCheck.Evaluate(Json("v99999999999.0.0", "99999999999.0.0"), "1.0.3") is null, "桁あふれは不可");
    }
}
