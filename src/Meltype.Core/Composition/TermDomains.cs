// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Config;

namespace Meltype.Composition;

/// <summary>
/// 専門用語集 (dictionaries/terms-*.txt) の「分野」(ファイル 1 つ)。ID はファイル名から terms- と .txt を除いたもの (ai, civil, …)。
/// Count は語数 (不正な行・重複を除く。TermDictionary.Count と同じ)。
/// </summary>
public sealed record TermDomain(string Id, string Name, int Count, bool Enabled);

/// <summary>
/// 専門用語集の、分野ごとの有効/無効。ContinueAfterConversionSetting と同じく、状態はプロセスで 1 つ持ち、
/// 切り替えがすべての入力欄 (すべての UserDictionary インスタンス) にすぐ反映される。
///   - 既定は全分野 OFF (専門語が日常の変換を巻き込まないため)。有効にした分野の語だけが <see cref="Current"/> に入る。
///   - 有効な分野の ID の一覧を config.json (Settings.EnabledTermDomains) に保存する。読めない config.json は上書きしない。未知の ID は無視する。
///   - UserDictionary は <see cref="Revision"/> を見て、変わっていれば <see cref="Current"/> に入れ替える (Version も進める)。
/// 性能: 有効な分野の全ファイルを 1 つの TermDictionary にまとめて持つ (Parse(IEnumerable&lt;string&gt;))。
/// 探す側 (Split の最長一致・Lookup・予測の二分探索) が引く辞書は常に 1 つなので、1 キーあたりの時間 (5 万語で約 0.1 ms) は
/// 分野の数に依らない。分野ごとの辞書を持って順に引くと、探索が分野の数だけ増え、同じ読みの語の順序・重複の整理も分野をまたいで崩れる。
/// 分野ごとの辞書は持たない (メモリが 2 重になるため)。OFF の分野のファイルは、語数を数えるときに 1 回だけ読んですぐ捨て、保持しない。
/// </summary>
internal static class TermDomains
{
    private static readonly object Lock = new();

    /// <summary>設定ファイルの場所。テストが差し替える。</summary>
    internal static Func<string> ConfigPath { get; set; } = () => AppPaths.ConfigFile;

    /// <summary>分野の一覧 (ID と、中身を読む関数)。既定は同梱の terms-*.txt (terms-template.txt は雛形なので除く)。テストが差し替える。</summary>
    internal static Func<IReadOnlyList<(string Id, Func<string> ReadText)>> Source { get; set; } = EmbeddedSource;

    private static IReadOnlyList<(string Id, Func<string> ReadText)> EmbeddedSource() =>
        Detection.DictionarySource.ListEmbedded("terms-")
            .Select(file => (Id: file["terms-".Length..^".txt".Length], File: file))
            .Where(d => d.Id.Length > 0 && d.Id != "template")
            .Select(d => (d.Id, (Func<string>)(() => Detection.DictionarySource.ReadEmbedded(d.File))))
            .ToList();

    // 設定に保存されている ID (未知のものも含む。保存し直すときに失わないよう、そのまま持つ)。null = まだ読んでいない
    private static List<string>? s_stored;
    private static List<(string Id, string Name, int Count)>? s_meta;
    private static TermDictionary? s_current;
    private static string? s_currentKey;
    private static volatile int s_revision;

    /// <summary>有効な分野が変わるたびに増える。UserDictionary が、自分の持つ辞書が古いかを確かめるのに使う。</summary>
    public static int Revision => s_revision;

    /// <summary>今有効な分野の語をまとめた辞書。1 つも有効でなければ空。</summary>
    public static TermDictionary Current
    {
        get
        {
            lock (Lock)
            {
                var ids = EnabledIds();
                var key = string.Join('\n', ids);
                if (s_current is not null && s_currentKey == key) return s_current;
                TermDictionary terms;
                if (ids.Count == 0)
                {
                    terms = TermDictionary.Empty;
                }
                else
                {
                    try
                    {
                        terms = TermDictionary.Parse(Source().Where(d => ids.Contains(d.Id)).Select(d => d.ReadText()).ToList());
                    }
                    catch (Exception ex)
                    {
                        Diagnostics.Log.Warn($"専門用語集を読めませんでした: {ex.Message}");
                        terms = TermDictionary.Empty;
                    }
                }
                s_current = terms;
                s_currentKey = key;
                return terms;
            }
        }
    }

    /// <summary>分野の一覧 (ID の昇順)。名称は先頭の「# 名称: …」の行 (無ければ ID)。語数は最初に 1 回だけ数えて覚える。</summary>
    public static IReadOnlyList<TermDomain> List()
    {
        lock (Lock)
        {
            var enabled = EnabledIds();
            return Meta().Select(m => new TermDomain(m.Id, m.Name, m.Count, enabled.Contains(m.Id))).ToList();
        }
    }

    /// <summary>分野を有効/無効にして config.json に保存する。保存できなければ (読めない config.json・未知の ID など) false で、状態は変えない。</summary>
    public static bool Set(string id, bool on)
    {
        lock (Lock)
        {
            try
            {
                if (Source().All(d => d.Id != id)) return false;
                var path = ConfigPath();
                // 読めない設定は、既定値で上書きして失わないよう、何もしない。
                if (Settings.LoadForUpdate(path) is not { } settings) return false;
                var stored = (settings.EnabledTermDomains ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal).ToList();
                stored.Remove(id);
                if (on) stored.Add(id);
                stored.Sort(StringComparer.Ordinal);
                settings.EnabledTermDomains = stored;
                SafeFile.EnsureDirectory(Path.GetDirectoryName(path)!);
                settings.Save(path);
                s_stored = stored;
                s_revision++;
                return true;
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Warn($"専門用語集の設定を保存できませんでした: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>FFI (meltype_term_domains) の文字列: 1 行 1 分野の「ID Tab 名称 Tab 語数 Tab 有効なら 1」を改行でつなぐ。名称の Tab・改行は空白にする (区切りと衝突しないように)。</summary>
    internal static string FormatForFfi(IEnumerable<TermDomain> domains) =>
        string.Join('\n', domains.Select(d => $"{d.Id}\t{d.Name.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ')}\t{d.Count}\t{(d.Enabled ? 1 : 0)}"));

    /// <summary>覚えた状態を捨てて、次に使うときに読み直す (テスト用)。</summary>
    internal static void Reset()
    {
        lock (Lock)
        {
            s_stored = null;
            s_meta = null;
            s_current = null;
            s_currentKey = null;
            s_revision++;
        }
    }

    // 保存されている ID のうち、実在する分野だけ (未知の ID は無視)。Lock の中で呼ぶ。
    private static HashSet<string> EnabledIds()
    {
        if (s_stored is null)
        {
            try
            {
                s_stored = (Settings.LoadForUpdate(ConfigPath())?.EnabledTermDomains ?? []).ToList();
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Warn($"専門用語集の設定を読めませんでした (すべて OFF にします): {ex.Message}");
                s_stored = [];
            }
        }
        var known = Source().Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        return s_stored.Where(known.Contains).ToHashSet(StringComparer.Ordinal);
    }

    private static List<(string Id, string Name, int Count)> Meta()
    {
        if (s_meta is not null) return s_meta;
        var meta = new List<(string, string, int)>();
        foreach (var (id, read) in Source().OrderBy(d => d.Id, StringComparer.Ordinal))
        {
            try
            {
                var text = read();
                // メニューを開くたびの待ち時間を避けるため、Parse せずに語の行 (コメント・空行以外でタブを含む行) を数える。
                // 重複や不正な行は引かない概数で、有効にしたときの実際の語数とは数語ずれることがある。
                meta.Add((id, ParseName(text) ?? id, CountEntryLines(text)));
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Warn($"専門用語集 {id} を読めませんでした: {ex.Message}");
                meta.Add((id, id, 0));
            }
        }
        return s_meta = meta;
    }

    internal static int CountEntryLines(string text)
    {
        var count = 0;
        var span = text.AsSpan();
        while (span.Length > 0)
        {
            var newline = span.IndexOf('\n');
            var line = newline < 0 ? span : span[..newline];
            span = newline < 0 ? [] : span[(newline + 1)..];
            if (line.Length > 0 && line[0] != '#' && line[0] != '\uFEFF' && line.IndexOf('\t') > 0) count++;
        }
        return count;
    }

    /// <summary>先頭のコメントの並び (空行は許す。最初の語の行まで) の「# 名称: …」の値。無い・空なら null。</summary>
    internal static string? ParseName(string text)
    {
        var span = text.AsSpan();
        while (span.Length > 0)
        {
            var newline = span.IndexOf('\n');
            var line = (newline < 0 ? span : span[..newline]).TrimEnd('\r');
            span = newline < 0 ? [] : span[(newline + 1)..];
            if (line.Length > 0 && line[0] == '\uFEFF') line = line[1..];
            if (line.Trim().Length == 0) continue;
            if (line[0] != '#') return null;
            var body = line[1..].TrimStart(" \t");
            if (!body.StartsWith("名称", StringComparison.Ordinal)) continue;
            body = body[2..].TrimStart(" \t");
            if (body.Length == 0 || (body[0] != ':' && body[0] != '：')) continue;
            var value = body[1..].Trim().ToString().Replace('\t', ' ');
            return value.Length == 0 ? null : value;
        }
        return null;
    }
}
