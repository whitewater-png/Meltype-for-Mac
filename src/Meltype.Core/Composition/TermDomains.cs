// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;
using Meltype.Config;

namespace Meltype.Composition;

/// <summary>
/// 専門用語集 (dictionaries/terms-*.txt) の「分野」(ファイル 1 つ)。ID はファイル名から terms- と .txt を除いたもの (ai, civil, …)。
/// Count は語数 (不正な行・重複を除く。TermDictionary.Count と同じ)。
/// </summary>
public sealed record TermDomain(string Id, string Name, int Count, bool Enabled);

/// <summary>
/// 専門用語集の、分野ごとの有効/無効と、利用者が除外した語。ContinueAfterConversionSetting と同じく、状態はプロセスで 1 つ持ち、
/// 切り替えがすべての入力欄 (すべての UserDictionary インスタンス) にすぐ反映される。
///   - 既定は全分野 OFF (専門語が日常の変換を巻き込まないため)。有効にした分野の語だけが <see cref="Current"/> に入る。
///   - 有効な分野の ID の一覧を config.json (Settings.EnabledTermDomains) に保存する。読めない config.json は上書きしない。未知の ID は無視する。
///   - 除外した語 (同梱のファイルは読み取り専用なので、「削除」は除外で表す) は、config.json とは別の terms-excluded.txt
///     (1 行に「読み[Tab]語」、0600) に保存し、<see cref="Current"/> を作るときに除く (強制型・候補追加型・予測のどれにも出ない)。
///   - UserDictionary は <see cref="Revision"/> を見て、変わっていれば <see cref="Current"/> に入れ替える (Version も進める)。
///   - Mac では「Meltype 辞書」の画面 (別のプロセス) も同じファイルを書くので、config.json と terms-excluded.txt の版 (FileStamp) を
///     <see cref="ExternalCheckIntervalMs"/> ごとに確かめ、書き換わっていれば読み直して Revision を進める。書くときはプロセスをまたぐロック (FileLock) の中で読み直してから書く。
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

    /// <summary>除外した語のファイルの場所。テストが差し替える。</summary>
    internal static Func<string> ExclusionPath { get; set; } = () => AppPaths.TermExclusionFile;

    /// <summary>ほかのプロセスが config.json・terms-excluded.txt を書き換えていないかを確かめる間隔 (ミリ秒)。テストが 0 にする。</summary>
    internal static int ExternalCheckIntervalMs { get; set; } = 500;

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
    // s_stored を読んだ (書いた) ときの config.json の版
    private static FileStamp s_configStamp = FileStamp.Missing;
    // 除外した語 (正規化した読み・語)。null = まだ読んでいない
    private static HashSet<(string Reading, string Word)>? s_excluded;
    private static FileStamp s_exclusionStamp = FileStamp.Missing;
    // 除外した語が変わるたびに増える (Current のキャッシュの鍵に入れる)
    private static int s_exclusionVersion;
    private static List<(string Id, string Name, int Count)>? s_meta;
    private static TermDictionary? s_current;
    private static string? s_currentKey;
    private static volatile int s_revision;
    private static long s_nextExternalCheck;

    /// <summary>有効な分野・除外した語が変わるたびに増える。UserDictionary が、自分の持つ辞書が古いかを確かめるのに使う。</summary>
    public static int Revision
    {
        get
        {
            CheckExternal(force: false);
            return s_revision;
        }
    }

    /// <summary>今有効な分野の語 (除外した語を除く) をまとめた辞書。1 つも有効でなければ空。</summary>
    public static TermDictionary Current
    {
        get
        {
            lock (Lock)
            {
                var ids = EnabledIds();
                var excluded = ExcludedSet();
                var key = string.Join('\n', ids.Order(StringComparer.Ordinal)) + "\n#" + s_exclusionVersion;
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
                        terms = TermDictionary.Parse(Source().Where(d => ids.Contains(d.Id)).Select(d => d.ReadText()).ToList(), excluded);
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
        CheckExternal(force: true);
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
                SafeFile.EnsureDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                // ほかのプロセス (Mac の IME と辞書の画面) が同時に config.json を書いても、互いの変更を消さないよう、ロックの中で読み直して書く。
                using (FileLock.Acquire(path))
                {
                    // 読めない設定は、既定値で上書きして失わないよう、何もしない。
                    if (Settings.LoadForUpdate(path) is not { } settings) return false;
                    var stored = (settings.EnabledTermDomains ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal).ToList();
                    stored.Remove(id);
                    if (on) stored.Add(id);
                    stored.Sort(StringComparer.Ordinal);
                    settings.EnabledTermDomains = stored;
                    settings.Save(path);
                    s_stored = stored;
                    s_configStamp = FileStamp.Of(path);
                }
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

    /// <summary>分野 (ID) の語をファイルの順に (除外した語には印を付けて) 返す。管理画面の一覧用。未知の ID なら null。</summary>
    public static IReadOnlyList<(TermEntry Entry, bool Excluded)>? Words(string id)
    {
        CheckExternal(force: true);
        var source = Source().FirstOrDefault(d => d.Id == id);
        if (source.Id is null) return null;
        var entries = TermDictionary.Entries(source.ReadText());
        HashSet<(string, string)> excluded;
        lock (Lock) excluded = [.. ExcludedSet()];
        return entries.Select(e => (e, excluded.Contains((e.Reading, e.Word)))).ToList();
    }

    /// <summary>除外した語 (読みの順)。</summary>
    public static IReadOnlyList<(string Reading, string Word)> Excluded()
    {
        CheckExternal(force: true);
        lock (Lock)
        {
            return [.. ExcludedSet().OrderBy(e => e.Reading, StringComparer.Ordinal).ThenBy(e => e.Word, StringComparer.Ordinal)];
        }
    }

    /// <summary>
    /// 語を除外する (excluded: true) / 除外をやめて元に戻す (false)。読みは正規化してから比べる。terms-excluded.txt に保存し、すべての入力欄にすぐ反映する。
    /// ファイルはロックの中で読み直してから書く (読めないファイルは上書きしない)。だめなら理由を返す。
    /// </summary>
    public static string? SetExcluded(IEnumerable<(string Reading, string Word)> items, bool excluded)
    {
        var targets = items
            .Select(i => (Reading: TermDictionary.Normalize(i.Reading.Trim()), Word: i.Word.Trim()))
            .Where(i => UserDictionary.Validate(i.Reading, i.Word) is null)
            .ToList();
        if (targets.Count == 0) return null;
        lock (Lock)
        {
            var path = ExclusionPath();
            try
            {
                SafeFile.EnsureDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                using (FileLock.Acquire(path))
                {
                    if (!TryReadExclusions(path, out var current, out var stamp, out var error)) return error;
                    var changed = false;
                    foreach (var target in targets) changed |= excluded ? current.Add(target) : current.Remove(target);
                    if (changed)
                    {
                        if (SafeFile.IsBlocked(path)) return "除外した語のファイルが大きすぎて退避できなかったため、保存を止めています。";
                        var lines = new List<string> { "# Meltype 専門用語集で使わない語 (「Meltype 辞書」の画面で除外したもの): 1 行に「読み<Tab>語」" };
                        lines.AddRange(current.OrderBy(e => e.Reading, StringComparer.Ordinal).ThenBy(e => e.Word, StringComparer.Ordinal).Select(e => $"{e.Reading}\t{e.Word}"));
                        SafeFile.WriteAllLines(path, lines, new UTF8Encoding(false));
                        stamp = FileStamp.Of(path);
                    }
                    Adopt(current, stamp);
                }
                return null;
            }
            catch (FileLockTimeoutException ex)
            {
                return ex.Message;
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Warn($"除外した語を保存できませんでした: {ex.Message}");
                return $"除外した語を保存できませんでした: {ex.Message}";
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
            s_configStamp = FileStamp.Missing;
            s_excluded = null;
            s_exclusionStamp = FileStamp.Missing;
            s_exclusionVersion++;
            s_meta = null;
            s_current = null;
            s_currentKey = null;
            s_nextExternalCheck = 0;
            s_revision++;
        }
    }

    /// <summary>
    /// ほかのプロセスが config.json (有効な分野) か terms-excluded.txt (除外した語) を書き換えていれば読み直し、中身が変わっていれば Revision を進める。
    /// force でなければ <see cref="ExternalCheckIntervalMs"/> に 1 回だけ。読めないファイルは、今の状態のまま (壊れた設定で全部 OFF にしない)。
    /// まだ読んでいないもの (使っていないもの) は確かめない (最初に使うときに読む)。
    /// </summary>
    private static void CheckExternal(bool force)
    {
        var now = Environment.TickCount64;
        if (!force && now < Volatile.Read(ref s_nextExternalCheck)) return;
        Volatile.Write(ref s_nextExternalCheck, now + ExternalCheckIntervalMs);
        lock (Lock)
        {
            if (s_stored is not null)
            {
                var path = ConfigPath();
                var stamp = FileStamp.Of(path);
                if (!stamp.Equals(s_configStamp))
                {
                    s_configStamp = stamp;
                    List<string>? ids = null;
                    try
                    {
                        ids = Settings.LoadForUpdate(path)?.EnabledTermDomains?.ToList();
                    }
                    catch (Exception ex)
                    {
                        Diagnostics.Log.Warn($"専門用語集の設定を読み直せませんでした (今の設定のまま): {ex.Message}");
                    }
                    if (ids is not null && !Normalized(ids).SequenceEqual(Normalized(s_stored)))
                    {
                        s_stored = ids;
                        s_revision++;
                    }
                }
            }
            if (s_excluded is not null)
            {
                var path = ExclusionPath();
                var stamp = FileStamp.Of(path);
                if (!stamp.Equals(s_exclusionStamp))
                {
                    if (TryReadExclusions(path, out var set, out var read, out var error)) Adopt(set, read);
                    else
                    {
                        Diagnostics.Log.Warn(error!);
                        s_exclusionStamp = stamp;
                    }
                }
            }
        }
    }

    private static IEnumerable<string> Normalized(IEnumerable<string> ids) =>
        ids.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);

    /// <summary>読み直した・書いた除外の一覧を取り込む。中身が変わっていれば Revision を進める。Lock の中で呼ぶ。</summary>
    private static void Adopt(HashSet<(string Reading, string Word)> set, FileStamp stamp)
    {
        s_exclusionStamp = stamp;
        if (s_excluded is not null && s_excluded.SetEquals(set)) return;
        s_excluded = set;
        s_exclusionVersion++;
        s_revision++;
    }

    /// <summary>除外した語 (まだ読んでいなければ読む)。読めないときは空 (除外しない。ファイルは上書きしない: 書くときに読み直して断る)。Lock の中で呼ぶ。</summary>
    private static HashSet<(string Reading, string Word)> ExcludedSet()
    {
        if (s_excluded is not null) return s_excluded;
        var path = ExclusionPath();
        if (TryReadExclusions(path, out var set, out var stamp, out var error))
        {
            s_excluded = set;
            s_exclusionStamp = stamp;
        }
        else
        {
            Diagnostics.Log.Warn(error!);
            s_excluded = [];
            s_exclusionStamp = stamp;
        }
        return s_excluded;
    }

    /// <summary>terms-excluded.txt を読む。無ければ空 (成功)。不正な行は飛ばす。読めなければ false と理由。stamp は読む前の版。</summary>
    private static bool TryReadExclusions(string path, out HashSet<(string Reading, string Word)> set, out FileStamp stamp, out string? error)
    {
        set = [];
        error = null;
        stamp = FileStamp.Of(path);
        if (!stamp.Exists) return true;
        try
        {
            // 文字コードは厳密に読む (不正なバイトを U+FFFD にして書き戻すと、元のバイト列を失う。読めなければ書かない)
            if (SafeFile.ReadAllTextStrict(path) is not { } text) return true;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length > 0 && line[0] == '﻿') line = line[1..];
                if (line.Length == 0 || line[0] == '#') continue;
                if (TermDictionary.TryParseLine(line, out var reading, out var word)) set.Add((reading, word));
            }
            return true;
        }
        catch (FileNotFoundException)
        {
            return true;
        }
        catch (DecoderFallbackException)
        {
            error = "除外した語のファイル (terms-excluded.txt) の文字コードが UTF-8 / UTF-16 ではないため読めません (上書きしません。UTF-8 で保存し直してください)。";
            return false;
        }
        catch (Exception ex)
        {
            error = $"除外した語のファイル (terms-excluded.txt) を読めません (上書きしません): {ex.Message}";
            return false;
        }
    }

    // 保存されている ID のうち、実在する分野だけ (未知の ID は無視)。Lock の中で呼ぶ。
    private static HashSet<string> EnabledIds()
    {
        if (s_stored is null)
        {
            try
            {
                var path = ConfigPath();
                var stamp = FileStamp.Of(path);
                s_stored = (Settings.LoadForUpdate(path)?.EnabledTermDomains ?? []).ToList();
                s_configStamp = stamp;
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
            if (line.Length > 0 && line[0] != '#' && line[0] != '﻿' && line.IndexOf('\t') > 0) count++;
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
            if (line.Length > 0 && line[0] == '﻿') line = line[1..];
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
