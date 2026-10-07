// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;
using System.Text.Json;

namespace Meltype.Update;

/// <summary>見つかった新しい版 (更新してよいと判定できたものだけ)。</summary>
public sealed record UpdateInfo(string Version, string DownloadUrl, string Sha256, long Size, string ReleaseUrl)
{
    /// <summary>Swift に渡す形 (版・URL・SHA-256・サイズ・リリースページを改行でつなぐ)。</summary>
    public string ToLines() => string.Join('\n', Version, DownloadUrl, Sha256, Size.ToString(System.Globalization.CultureInfo.InvariantCulture), ReleaseUrl);
}

/// <summary>
/// GitHub の最新 Release の JSON (releases/latest) から、「更新してよい新しい版か」を判定する純粋なロジック。
/// 通信は Swift 側 (URLSession) が行い、ここは文字列を受け取るだけ (System.Net は使わない)。
/// 取得元が乗っ取られた・壊れた場合に備え、URL・ファイル名・サイズ・SHA-256 を厳しく見て、1 つでも外れたら更新不可 (null) にする。
/// </summary>
public static class UpdateCheck
{
    /// <summary>ダウンロードを許す URL の先頭。ここ以外 (別ホスト・別リポジトリ) の資産は使わない。</summary>
    public const string DownloadPrefix = "https://github.com/whitewater-png/Meltype-for-Mac/releases/download/";

    /// <summary>リリースページとして開いてよい URL の先頭。</summary>
    public const string ReleasePagePrefix = "https://github.com/whitewater-png/Meltype-for-Mac/releases";

    /// <summary>zip のサイズの上限 (100 MB)。</summary>
    public const long MaxAssetSize = 100L * 1024 * 1024;

    /// <summary>受け取る JSON のサイズの上限 (1 MB)。releases/latest は通常数 KB なので、これを超えるものは異常として読まない。</summary>
    public const int MaxJsonBytes = 1024 * 1024;

    /// <summary>
    /// releaseJson を読み、currentVersion より新しい更新可能な版があればその情報を返す。無い・不正・判定できないときは null。
    /// 例外は外に出さない (IME を落とさない)。
    /// </summary>
    public static UpdateInfo? Evaluate(string? releaseJson, string? currentVersion)
    {
        try
        {
            if (string.IsNullOrEmpty(releaseJson) || Encoding.UTF8.GetByteCount(releaseJson) > MaxJsonBytes) return null;
            if (!TryParseVersion(currentVersion, out var current)) return null;

            using var document = JsonDocument.Parse(releaseJson, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            // 下書き・先行版は、利用者に見せない
            if (GetBool(root, "draft") || GetBool(root, "prerelease")) return null;

            var tag = GetString(root, "tag_name");
            if (!TryParseTag(tag, out var versionText, out var latest)) return null;
            if (Compare(latest, current) <= 0) return null;

            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
            var expectedName = $"Meltype-mac-{versionText}.zip";
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.ValueKind != JsonValueKind.Object || GetString(asset, "name") != expectedName) continue;

                var url = GetString(asset, "browser_download_url");
                if (!IsExpectedDownloadUrl(url, tag!, expectedName)) return null;

                if (!asset.TryGetProperty("size", out var sizeElement) || !sizeElement.TryGetInt64(out var size) || size <= 0 || size > MaxAssetSize) return null;

                var sha = ParseDigest(GetString(asset, "digest"));
                if (sha is null) return null;

                var releaseUrl = GetString(root, "html_url");
                if (releaseUrl is null || !releaseUrl.StartsWith(ReleasePagePrefix, StringComparison.Ordinal))
                    releaseUrl = ReleasePagePrefix;
                return new UpdateInfo(versionText, url!, sha, size, releaseUrl);
            }
            return null;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// ダウンロード URL が、このリポジトリの Release の「そのタグ・そのファイル名」だけを指しているか。
    /// 接頭辞の一致だけだと `../` や `%2e%2e` で別のパスへ逃げられるので、パス全体を完全一致で見て、
    /// パーセントエンコード・バックスラッシュ・空白・制御文字・`..`・ポート指定・query・fragment・ユーザー情報は全て拒否する。
    /// </summary>
    private static bool IsExpectedDownloadUrl(string? url, string tag, string fileName)
    {
        if (url is null || url.Length > 300) return false;
        foreach (var c in url)
            if (c is '%' or '\\' or ' ' or '#' or '?' or '@' || char.IsControl(c) || char.IsWhiteSpace(c) || c > '~') return false;
        if (url.Contains("..", StringComparison.Ordinal)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps || uri.Host != "github.com" || !uri.IsDefaultPort) return false;
        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) return false;
        return uri.AbsolutePath == $"/whitewater-png/Meltype-for-Mac/releases/download/{tag}/{fileName}" && url == DownloadPrefix + tag + "/" + fileName;
    }

    /// <summary>"sha256:" + 64 桁の 16 進だけを受け付け、小文字の 64 桁にして返す。</summary>
    private static string? ParseDigest(string? digest)
    {
        const string prefix = "sha256:";
        if (digest is null || !digest.StartsWith(prefix, StringComparison.Ordinal) || digest.Length != prefix.Length + 64) return null;
        var hex = digest[prefix.Length..];
        foreach (var c in hex)
            if (!(c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F')) return null;
        return hex.ToLowerInvariant();
    }

    /// <summary>タグ (v1.0.4 / v1.0.4-mac / 1.0.4) から版の数字を取り出す。それ以外の形 (-beta など) は受け付けない。</summary>
    private static bool TryParseTag(string? tag, out string versionText, out int[] parts)
    {
        versionText = "";
        parts = [];
        if (string.IsNullOrEmpty(tag) || tag.Length > 40) return false;
        var text = tag;
        if (text[0] is 'v' or 'V') text = text[1..];
        if (text.EndsWith("-mac", StringComparison.Ordinal)) text = text[..^4];
        if (!TryParseVersion(text, out parts)) return false;
        versionText = text;
        return true;
    }

    /// <summary>"1.0.4" のように、数字を . でつないだ 2〜4 個の形だけを数にする。</summary>
    private static bool TryParseVersion(string? text, out int[] parts)
    {
        parts = [];
        if (string.IsNullOrEmpty(text) || text.Length > 30) return false;
        var pieces = text.Split('.');
        if (pieces.Length is < 2 or > 4) return false;
        var numbers = new int[pieces.Length];
        for (var i = 0; i < pieces.Length; i++)
        {
            var piece = pieces[i];
            if (piece.Length is 0 or > 6) return false;
            foreach (var c in piece) if (c is < '0' or > '9') return false;
            numbers[i] = int.Parse(piece, System.Globalization.CultureInfo.InvariantCulture);
        }
        parts = numbers;
        return true;
    }

    /// <summary>数として比較する (1.0.10 > 1.0.9。桁が足りない側は 0 とみなす)。</summary>
    private static int Compare(int[] a, int[] b)
    {
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var x = i < a.Length ? a[i] : 0;
            var y = i < b.Length ? b[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool GetBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
