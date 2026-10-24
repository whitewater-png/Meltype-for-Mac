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
    private static bool s_failureLogged;

    public const int MaxFileBytes = 1_000_000;

    /// <summary>
    /// ファイルから読む (更新日時と大きさが同じなら前回の結果を使い回す)。無い・空・読めないときは null (従来どおりの動き)。
    /// 入力スレッドから打鍵ごとに呼ばれるので、ブロックしうるものは読まない:
    /// シンボリックリンクは辿った先を見て、通常ファイルでない (FIFO・デバイス・ディレクトリ・大きさ 0) ものは開かない。
    /// 読むのは最大 1MB+1 バイトだけ。読み込みに失敗したら更新日時を覚えず、次の打鍵で再試行する (警告ログは連続で出さない)。
    /// </summary>
    public static RomajiTable? Load(string path)
    {
        lock (Gate)
        {
            try
            {
                FileSystemInfo info = new FileInfo(path);
                if (info.LinkTarget is not null)
                {
                    var target = info.ResolveLinkTarget(returnFinalTarget: true);
                    if (target is not FileInfo) { s_path = null; s_table = null; return null; }
                    info = target;
                }
                if (info is not FileInfo file || !file.Exists) { s_path = null; s_table = null; return null; }
                var length = file.Length;
                var stamp = file.LastWriteTimeUtc;
                if (s_path == path && s_stamp == stamp && s_length == length) return s_table;
                // 大きさ 0 は空ファイルか FIFO・デバイス。開くと固まりうるので読まない。
                if (length == 0) { Remember(path, stamp, length, null); return null; }
                if (length > MaxFileBytes) { Remember(path, stamp, length, null); Log.Warn("カスタムローマ字表が大きすぎるので使わない (1MB まで)"); return null; }

                var buffer = new byte[MaxFileBytes + 1];
                var total = 0;
                using (var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    int read;
                    while (total < buffer.Length && (read = stream.Read(buffer, total, buffer.Length - total)) > 0) total += read;
                }
                if (total > MaxFileBytes) { Remember(path, stamp, length, null); Log.Warn("カスタムローマ字表が大きすぎるので使わない (1MB まで)"); return null; }

                var warnings = new List<string>();
                // UTF-8 BOM (メモ帳など) は読み飛ばす。残すと 1 行目のキーが非英字扱いで無視される。
                var skip = total >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF ? 3 : 0;
                var table = Parse(new UTF8Encoding(false).GetString(buffer, skip, total - skip), warnings);
                Remember(path, stamp, length, table);
                s_failureLogged = false;
                foreach (var warning in warnings) Log.Warn($"カスタムローマ字表 {warning}");
                if (table is null) Log.Warn("カスタムローマ字表に有効な行が無いので使わない");
                return table;
            }
            catch (Exception ex)
            {
                // 一時的な失敗 (アクセス拒否・保存途中) を固定しない: 更新日時を覚えず次の打鍵で再試行する。
                s_path = null; s_table = null;
                if (!s_failureLogged) { s_failureLogged = true; Log.Warn($"カスタムローマ字表を読めないので使わない (次の入力で再試行): {ex.Message}"); }
                return null;
            }
        }
    }

    private static void Remember(string path, DateTime stamp, long length, RomajiTable? table)
    {
        s_path = path; s_stamp = stamp; s_length = length; s_table = table;
    }
}
