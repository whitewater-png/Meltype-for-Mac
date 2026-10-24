// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;
using Meltype.Diagnostics;

namespace Meltype.Detection;

/// <summary>
/// ユーザーが定義するカスタムローマ字表 (本家 #979 の Phase 1 最小版)。
/// 形式は TSV: <c>入力(英字)&lt;TAB&gt;出力(かな・記号)</c> 1 行 1 定義。# で始まる行と空行は無視。
/// 入力は英字だけ (大文字は小文字にする)。不正な行は無視して理由を警告に残す。重複キーは後の行が勝つ (警告つき)。
/// </summary>
public sealed class RomajiTable
{
    public const int MaxKeyLengthLimit = 8;
    public const int MaxValueLength = 16;
    public const string FileName = "romaji.tsv";

    private readonly Dictionary<string, string> _map;
    private readonly HashSet<string> _partials;

    private RomajiTable(Dictionary<string, string> map)
    {
        _map = map;
        _partials = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in map.Keys)
        {
            MaxKeyLength = Math.Max(MaxKeyLength, key.Length);
            for (var i = 1; i < key.Length; i++) _partials.Add(key[..i]);
        }
    }

    public int Count => _map.Count;
    public int MaxKeyLength { get; }
    public bool TryGet(string key, out string kana) => _map.TryGetValue(key, out kana!);
    public bool IsPartial(string letters) => _partials.Contains(letters);

    /// <summary>テキストから表を作る。有効な行が 1 つも無ければ null。warnings に無視した行の理由を足す。</summary>
    public static RomajiTable? Parse(string text, List<string>? warnings = null)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var number = 0;
        foreach (var raw in text.Split((char)10))
        {
            number++;
            var line = raw.TrimEnd((char)13);
            if (line.Length == 0 || line.TrimStart().StartsWith((char)35)) continue;
            var tab = line.IndexOf((char)9);
            if (tab < 0) { warnings?.Add($"{number} 行目: タブ区切りでない (無視)"); continue; }
            var key = line[..tab].Trim().ToLowerInvariant();
            var value = line[(tab + 1)..].Trim();
            if (key.Length == 0) { warnings?.Add($"{number} 行目: 入力が空 (無視)"); continue; }
            if (!key.All(c => c is >= 'a' and <= 'z')) { warnings?.Add($"{number} 行目: 入力は英字だけ (無視)"); continue; }
            if (key.Length > MaxKeyLengthLimit) { warnings?.Add($"{number} 行目: 入力は {MaxKeyLengthLimit} 文字まで (無視)"); continue; }
            if (value.Length == 0) { warnings?.Add($"{number} 行目: 出力が空 (無視)"); continue; }
            if (value.Length > MaxValueLength || value.Contains('\t')) { warnings?.Add($"{number} 行目: 出力が長すぎる、またはタブを含む (無視)"); continue; }
            if (value.Any(char.IsControl)) { warnings?.Add($"{number} 行目: 出力に制御文字 (無視)"); continue; }
            if (map.ContainsKey(key)) warnings?.Add($"{number} 行目: 入力 {key} が重複 (後の行を使う)");
            map[key] = value;
        }
        return map.Count == 0 ? null : new RomajiTable(map);
    }

    private static readonly object Gate = new();
    private static string? s_path;
    private static DateTime s_stamp;
    private static long s_length = -1;
    private static RomajiTable? s_table;

    /// <summary>
    /// ファイルから読む (更新日時と大きさが同じなら前回の結果を使い回す)。無い・空・読めないときは null (従来どおりの動き)。
    /// 読めない・無視した行は警告ログに 1 回だけ残す。
    /// </summary>
    public static RomajiTable? Load(string path)
    {
        try
        {
            var info = new FileInfo(path);
            lock (Gate)
            {
                if (!info.Exists) { s_path = null; s_table = null; return null; }
                if (s_path == path && s_stamp == info.LastWriteTimeUtc && s_length == info.Length) return s_table;
                s_path = path; s_stamp = info.LastWriteTimeUtc; s_length = info.Length; s_table = null;
                if (info.Length > 1_000_000) { Log.Warn("カスタムローマ字表が大きすぎるので使わない (1MB まで)"); return null; }
                var warnings = new List<string>();
                s_table = Parse(File.ReadAllText(path, Encoding.UTF8), warnings);
                foreach (var warning in warnings) Log.Warn($"カスタムローマ字表 {warning}");
                if (s_table is null) Log.Warn("カスタムローマ字表に有効な行が無いので使わない");
                return s_table;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"カスタムローマ字表を読めないので使わない: {ex.Message}");
            return null;
        }
    }
}
