// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;
using Meltype.Config;

namespace Meltype.Composition;

/// <summary>ユーザー辞書から専門用語集に移した結果 (<see cref="TermDomains.MoveFromUserDictionary"/>)。</summary>
internal sealed record MoveResult(
    IReadOnlyList<(int Index, UserWord Word)> RemovedFromUser,
    IReadOnlyList<TermEntry> AddedToDomain,
    IReadOnlyList<(UserWord Word, string Reason)> Skipped);

/// <summary>
/// 自作の専門用語集 (利用者がユーザー辞書の語を集めて作る分野)。
///   - 保存: データフォルダーの terms/terms-user-xxxxxxxx.txt (xxxxxxxx は 16 進 8 桁。ID は user-xxxxxxxx で、同梱の分野の ID とは決して重ならない)。
///     1 分野 1 ファイルで、同梱の terms-*.txt と同じ形式 (先頭に「# 名称: …」)。Mac / Linux ではファイル 0600・フォルダー 0700 (SafeFile)。
///   - 書き換えは、ユーザー辞書と同じく FileLock の中でファイルを読み直してから行う (ほかのプロセスの変更を消さない)。読めないファイルは上書きしない。
///     分野を作る・名前を変える・消す・取り込むは、名前の重なりを防ぐため、フォルダー全体のロックも取る。
///   - 変換での扱い: ユーザー辞書の語と同じ (TermDictionary.Parse の userTexts: 読みの長さ・語が ASCII か・日常語の読みかに関わらず強制型。後に書いた語が先)。
///   - 新しく作った分野は、すぐ有効にする (有効にしないと、移した語が黙って変換に使われなくなるため)。
///   - ほかのプロセスが足した・消した・書き換えたファイルは、フォルダーの版 (CheckExternal) で気づき、一覧・語数・辞書を作り直して Revision を進める。
/// 同梱の分野は読み取り専用のまま (除外・直す・複製)。
/// </summary>
internal static partial class TermDomains
{
    /// <summary>自作の分野のフォルダー。テストが差し替える。</summary>
    internal static Func<string> UserDirectory { get; set; } = () => AppPaths.UserTermsDirectory;

    internal const string UserIdPrefix = "user-";

    /// <summary>名前の長さの上限。</summary>
    internal const int MaxUserNameLength = 50;

    /// <summary>自作の分野の数の上限。</summary>
    internal const int MaxUserDomains = 100;

    /// <summary>1 つの分野の語数の上限 (巨大なファイルで変換が重くならないように)。</summary>
    internal const int MaxUserDomainWords = 200_000;

    internal const string DomainNotFoundMessage = "その専門用語集が見つかりません (ほかの画面で削除された可能性があります)。一覧を読み直してください。";

    // 自作の分野のファイルの版 (名前・大きさ・更新時刻の並び)。null = まだ使っていない (使い始めたら CheckExternal が見張る)
    private static string? s_userSignature;
    // 自作の分野が変わるたびに増える (Current のキャッシュの鍵に入れる)
    private static int s_userVersion;
    // 自作の分野のフォルダーの更新時刻 (安い確認用)。全ファイルの版を調べる時刻
    private static long s_userDirectoryTicks;
    private static long s_nextFullUserCheck;

    /// <summary>フォルダーの更新時刻が変わっていなくても、全ファイルの版を調べる間隔 (ミリ秒)。テストが 0 にする。</summary>
    internal static int UserFullCheckIntervalMs { get; set; } = 5000;

    private static long UserDirectoryTicks()
    {
        try
        {
            return Directory.GetLastWriteTimeUtc(UserDirectory()).Ticks;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>自作の分野の ID か (user- と 16 進 8 桁)。</summary>
    internal static bool IsUserId(string id) =>
        id.Length == UserIdPrefix.Length + 8 && id.StartsWith(UserIdPrefix, StringComparison.Ordinal) && id.AsSpan(UserIdPrefix.Length).IndexOfAnyExcept("0123456789abcdef") < 0;

    private static string UserPath(string id) => Path.Combine(UserDirectory(), $"terms-{id}.txt");

    // 同梱 + 自作。自作の分野のファイルの版 (s_userSignature) を触るので、Lock の中で呼ぶ。
    private static IReadOnlyList<(string Id, Func<string> ReadText)> AllSource() => [.. Source(), .. UserSource()];

    /// <summary>自作の分野 (ID の昇順)。ファイルが読めなければ、その分野の中身は空として扱う (ほかの分野を巻き込まない)。Lock の中で呼ぶ。</summary>
    private static IReadOnlyList<(string Id, Func<string> ReadText)> UserSource()
    {
        s_userSignature ??= UserSignature();
        return [.. UserFiles().Select(f => (Id: Path.GetFileNameWithoutExtension(f)["terms-".Length..], File: f))
            .Select(f => (f.Id, (Func<string>)(() => ReadUserText(f.File))))];
    }

    private static IEnumerable<string> UserFiles()
    {
        try
        {
            var directory = UserDirectory();
            if (!Directory.Exists(directory)) return [];
            return Directory.EnumerateFiles(directory, "terms-user-*.txt")
                .Where(f => IsUserId(Path.GetFileNameWithoutExtension(f)["terms-".Length..]))
                .Order(StringComparer.Ordinal).ToList();
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"自作の専門用語集のフォルダーを読めませんでした: {ex.Message}");
            return [];
        }
    }

    private static string UserSignature() =>
        string.Join('|', UserFiles().Select(f =>
        {
            var stamp = FileStamp.Of(f);
            return $"{Path.GetFileName(f)}:{stamp.Length}:{stamp.WriteTicks}:{stamp.CreationTicks}";
        }));

    private static string ReadUserText(string file)
    {
        try
        {
            return SafeFile.ReadAllTextStrict(file) ?? "";
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"自作の専門用語集 {Path.GetFileName(file)} を読めませんでした: {ex.Message}");
            return "";
        }
    }

    /// <summary>自作の分野のファイルが、ほかのプロセスで変わった: 一覧・語数・辞書を作り直す。Lock の中で呼ぶ。</summary>
    private static void InvalidateUser()
    {
        s_meta = null;
        s_userVersion++;
        s_revision++;
    }

    /// <summary>
    /// 自分が自作の分野のファイルを書いた・消した: InvalidateUser に加えて、ファイルの版を今のものに更新する
    /// (しないと、次の確認で自分の変更を「外からの変更」と見て、もう一度作り直してしまう)。Lock の中で呼ぶ。
    /// 書いてから版を取るまでの間にほかのプロセスが別の分野を書いていても、その変更は次の確認 (間隔ごとの全ファイルの確認) までは拾えないが、いずれ拾う。
    /// </summary>
    private static void AfterOwnWrite()
    {
        InvalidateUser();
        s_userSignature = UserSignature();
        s_userDirectoryTicks = UserDirectoryTicks();
    }

    /// <summary>作った日時 (ticks)。先頭のコメントの「# 作成: …」。無い・読めなければ 0。</summary>
    internal static long ParseCreated(string text)
    {
        // 先頭のコメントの行だけを見る (語の行に着いたら終わり。ファイル全体を切り分けない)
        var span = text.AsSpan();
        while (span.Length > 0)
        {
            var newline = span.IndexOf('\n');
            var line = (newline < 0 ? span : span[..newline]).TrimEnd('\r');
            span = newline < 0 ? [] : span[(newline + 1)..];
            if (line.Length > 0 && line[0] == '\uFEFF') line = line[1..];
            if (line.Trim().Length == 0) continue;
            if (line[0] != '#') return 0;
            var body = line[1..].TrimStart(" \t");
            if (!body.StartsWith("作成", StringComparison.Ordinal)) continue;
            body = body[2..].TrimStart(" \t");
            if (body.Length == 0 || (body[0] != ':' && body[0] != '：')) continue;
            return DateTime.TryParse(body[1..].Trim(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var created) ? created.ToUniversalTime().Ticks : 0;
        }
        return 0;
    }

    /// <summary>有効な自作の分野の中身を、作った順 (古いものが先) に並べる (同じ読みでは後に読んだ分野の語が先になるので、新しい分野が先に使われる)。Lock の中で呼ぶ。</summary>
    private static List<string> OrderedUserTexts(HashSet<string> ids) =>
        UserSource().Where(d => ids.Contains(d.Id)).Select(d => (d.Id, Text: d.ReadText()))
            .OrderBy(d => ParseCreated(d.Text)).ThenBy(d => d.Id, StringComparer.Ordinal).Select(d => d.Text).ToList();

    /// <summary>名前を maxLength 文字以内に切る (サロゲートペアの途中では切らない)。</summary>
    private static string TruncateName(string name, int maxLength)
    {
        if (name.Length <= maxLength) return name;
        var length = char.IsHighSurrogate(name[maxLength - 1]) ? maxLength - 1 : maxLength;
        return name[..length];
    }

    /// <summary>ほかの分野とかぶらない名前にする (かぶるときは「名前 (2)」「(3)」…)。Lock の中で呼ぶ。</summary>
    private static string UniqueName(string name, string? exceptId)
    {
        var result = name;
        for (var n = 2; CheckUnique(result, exceptId) is not null && n < 1000; n++)
        {
            var suffix = $" ({n})";
            result = TruncateName(name, MaxUserNameLength - suffix.Length) + suffix;
        }
        return result;
    }

    /// <summary>行の先頭のコメントの「# 名称: …」を name にする (無ければ先頭に足す)。</summary>
    private static void SetNameLine(List<string> lines, string name)
    {
        var header = lines.TakeWhile(l => l.Length == 0 || l[0] == '#' || l[0] == '\uFEFF').Count();
        var index = lines.Take(header).ToList().FindIndex(IsNameLine);
        if (index >= 0) lines[index] = $"# 名称: {name}";
        else lines.Insert(0, $"# 名称: {name}");
    }

    // ---- 入力の検め ----

    /// <summary>名前として使えるかを検め、整えた名前を返す。だめなら理由 (name は null)。</summary>
    internal static string? CheckUserName(string? raw, out string? name)
    {
        name = null;
        var value = (raw ?? "").Trim();
        if (value.Length == 0) return "専門用語集の名前を入力してください。";
        if (value.Length > MaxUserNameLength) return $"名前は {MaxUserNameLength} 文字までにしてください。";
        if (value.Any(c => char.IsControl(c) || c is '\u2028' or '\u2029')) return "名前に改行・タブ・制御文字は使えません。";
        name = value;
        return null;
    }

    /// <summary>名前がほかの分野 (同梱・自作) とかぶっていないか (大文字小文字は区別しない)。exceptId の分野は除く。Lock の中で呼ぶ。</summary>
    private static string? CheckUnique(string name, string? exceptId)
    {
        foreach (var meta in Meta())
        {
            if (meta.Id != exceptId && string.Equals(meta.Name, name, StringComparison.OrdinalIgnoreCase)) return $"「{name}」という名前の専門用語集が、すでにあります。";
        }
        return null;
    }

    /// <summary>自作の分野に入れる 1 語を整える (読み: カタカナ→ひらがな・全角英数→半角・英大文字→小文字)。入れられなければ理由。</summary>
    internal static string? NormalizeUserWord(string reading, string word, out string normalizedReading, out string normalizedWord)
    {
        normalizedReading = TermDictionary.Normalize(UserDictionary.NormalizeReading(reading));
        normalizedWord = word.Trim();
        if (UserDictionary.Validate(normalizedReading, normalizedWord) is { } invalid) return invalid;
        // ファイルの 1 行に書いて、読み直したときに同じ語になること (TryParseLine と同じ決まり)
        return TermDictionary.TryParseLine($"{normalizedReading}\t{normalizedWord}", out var r, out var w) && r == normalizedReading && w == normalizedWord
            ? null
            : "この語は専門用語集に入れられません。";
    }

    // ---- ファイルの読み書き (自作の分野 1 つ) ----

    private static string DomainLockTarget() => Path.Combine(UserDirectory(), "domains");

    /// <summary>自作の分野のファイルを、行のまま読む (コメントも残す)。無い・読めない・大きすぎるときは理由。</summary>
    private static string? ReadUserLines(string id, out List<string> lines)
    {
        lines = [];
        var path = UserPath(id);
        if (!File.Exists(path)) return DomainNotFoundMessage;
        try
        {
            if (SafeFile.ReadAllTextStrict(path) is not { } text)
                return "専門用語集のファイルが大きすぎて読めません (上書きしません)。";
            lines = [.. text.Split('\n').Select(l => l.TrimEnd('\r'))];
            if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
            if (lines.Count > 0 && lines[0].Length > 0 && lines[0][0] == '\uFEFF') lines[0] = lines[0][1..];
            return null;
        }
        catch (DecoderFallbackException)
        {
            return "専門用語集のファイルの文字コードが UTF-8 / UTF-16 ではないため読めません (上書きしません。UTF-8 で保存し直してください)。";
        }
        catch (FileNotFoundException)
        {
            return DomainNotFoundMessage;
        }
        catch (Exception ex)
        {
            return $"専門用語集のファイルを読めません (上書きしません): {ex.Message}";
        }
    }

    private static string? WriteUserLines(string id, IEnumerable<string> lines)
    {
        var path = UserPath(id);
        if (SafeFile.IsBlocked(path)) return "専門用語集のファイルが大きすぎて退避できなかったため、保存を止めています (元のファイルを守るため)。";
        var all = lines.ToList();
        // 読み込めない大きさのファイルを作らない (読み込みの上限 SafeFile.MaxReadBytes を超えると、次に読むときに退避されて空として扱われる)
        long bytes = 0;
        foreach (var line in all) bytes += Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;
        if (bytes > SafeFile.MaxReadBytes) return $"専門用語集が大きくなりすぎます (上限 {Math.Max(1, SafeFile.MaxReadBytes / 1024 / 1024)} MB)。語を減らしてください。";
        SafeFile.WriteAllLines(path, all, new UTF8Encoding(false));
        AfterOwnWrite();
        return null;
    }

    /// <summary>行の並びから、語の行だけを (行の位置・語) で取り出す (不正な行・コメントは飛ばす)。</summary>
    private static List<(int Line, TermEntry Entry)> EntryLines(List<string> lines)
    {
        var result = new List<(int, TermEntry)>();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.Length == 0 || line.TrimStart().Length == 0 || line.TrimStart()[0] == '#') continue;
            if (TermDictionary.TryParseLine(line, out var reading, out var word, out var note)) result.Add((i, new TermEntry(reading, word, note)));
        }
        return result;
    }

    private static string EntryText(TermEntry entry) => entry.Note.Length == 0 ? $"{entry.Reading}\t{entry.Word}" : $"{entry.Reading}\t{entry.Word}\t{entry.Note}";

    private static string CleanNote(string note) => note.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static List<string> HeaderLines(string name) =>
    [
        $"# 名称: {name}",
        "# 出典: 自作",
        $"# 作成: {DateTime.UtcNow:O}",
        "# 1 行に「読み<Tab>語」(3 つ目の欄は注記。省略できます)。「Meltype 辞書」の画面で作った専門用語集です。",
    ];

    private static bool IsNameLine(string line)
    {
        if (line.Length == 0 || line[0] != '#') return false;
        var body = line[1..].TrimStart(' ', '\t');
        if (!body.StartsWith("名称", StringComparison.Ordinal)) return false;
        body = body[2..].TrimStart(' ', '\t');
        return body.Length > 0 && (body[0] == ':' || body[0] == '：');
    }

    // 1 つの分野を変える共通処理: 分野のロックを取り、読み直して change を当て、変わったら保存する。Lock の中で呼ぶ。
    private static string? MutateUser(string id, Func<List<string>, string?> change)
    {
        if (!IsUserId(id)) return DomainNotFoundMessage;
        try
        {
            var path = UserPath(id);
            if (!File.Exists(path)) return DomainNotFoundMessage;
            using (FileLock.Acquire(path))
            {
                if (ReadUserLines(id, out var lines) is { } readError) return readError;
                var before = string.Join('\n', lines);
                if (change(lines) is { } reason) return reason;
                if (string.Join('\n', lines) == before) return null;
                return WriteUserLines(id, lines);
            }
        }
        catch (FileLockTimeoutException ex)
        {
            return ex.Message;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"専門用語集を保存できませんでした: {ex.Message}");
            return $"専門用語集を保存できませんでした: {ex.Message}";
        }
    }

    // ---- 分野の作成・名前の変更・削除 ----

    /// <summary>
    /// 自作の分野を作る (すぐ有効にする)。名前が空・長すぎる・ほかの分野 (同梱も) とかぶるときは理由。
    /// 有効にできなかったとき (config.json を読めない) は、作らずに理由を返す。
    /// </summary>
    public static string? CreateUserDomain(string name, out string id)
    {
        id = "";
        if (CheckUserName(name, out var clean) is { } invalid) return invalid;
        return CreateWithLines(clean!, dedupeName: false, name => HeaderLines(name), out id, out _);
    }

    // dedupeName なら、同じ名前の分野があるときは「名前 (2)」「(3)」… にする (取り込み)。そうでなければ理由を返す。
    private static string? CreateWithLines(string name, bool dedupeName, Func<string, List<string>> makeLines, out string id, out string finalName)
    {
        id = "";
        finalName = name;
        CheckExternal(force: true);
        lock (Lock)
        {
            try
            {
                SafeFile.EnsureDirectory(UserDirectory());
                using var _ = FileLock.Acquire(DomainLockTarget());
                CheckExternal(force: true);
                if (UserFiles().Count() >= MaxUserDomains) return $"自作の専門用語集は {MaxUserDomains} 個までです。";
                if (dedupeName) finalName = UniqueName(name, exceptId: null);
                if (CheckUnique(finalName, exceptId: null) is { } duplicate) return duplicate;
                var existing = AllSource().Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
                string candidate;
                do candidate = UserIdPrefix + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
                while (existing.Contains(candidate) || File.Exists(UserPath(candidate)));
                if (WriteUserLines(candidate, makeLines(finalName)) is { } writeError) return writeError;
                if (!Set(candidate, true))
                {
                    try { File.Delete(UserPath(candidate)); AfterOwnWrite(); } catch { }
                    return "設定ファイル (config.json) を読めないため、専門用語集を作りませんでした。";
                }
                id = candidate;
                return null;
            }
            catch (FileLockTimeoutException ex)
            {
                return ex.Message;
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Warn($"専門用語集を作れませんでした: {ex.Message}");
                return $"専門用語集を作れませんでした: {ex.Message}";
            }
        }
    }

    /// <summary>自作の分野の名前を変える。同梱の分野・無い分野は変えられない。</summary>
    public static string? RenameUserDomain(string id, string name)
    {
        if (CheckUserName(name, out var clean) is { } invalid) return invalid;
        if (!IsUserId(id)) return "同梱の専門用語集の名前は変えられません。";
        CheckExternal(force: true);
        lock (Lock)
        {
            try
            {
                SafeFile.EnsureDirectory(UserDirectory());
                using var _ = FileLock.Acquire(DomainLockTarget());
                CheckExternal(force: true);
                if (!File.Exists(UserPath(id))) return DomainNotFoundMessage;
                if (CheckUnique(clean!, exceptId: id) is { } duplicate) return duplicate;
                return MutateUser(id, lines =>
                {
                    SetNameLine(lines, clean!);
                    return null;
                });
            }
            catch (FileLockTimeoutException ex)
            {
                return ex.Message;
            }
        }
    }

    /// <summary>
    /// 自作の分野を消す: ファイルを消し、有効な分野の一覧 (config.json) からも外す。消す前の中身を content に、有効だったかを wasEnabled に返す
    /// (画面が ⌘Z で <see cref="RestoreUserDomain"/> に渡して戻す)。同梱の分野は消せない。
    /// </summary>
    public static string? DeleteUserDomain(string id, out string content, out bool wasEnabled)
    {
        content = "";
        wasEnabled = false;
        if (!IsUserId(id)) return "同梱の専門用語集は消せません。";
        CheckExternal(force: true);
        lock (Lock)
        {
            try
            {
                SafeFile.EnsureDirectory(UserDirectory());
                using var _ = FileLock.Acquire(DomainLockTarget());
                CheckExternal(force: true);
                var path = UserPath(id);
                using var fileLock = FileLock.Acquire(path);
                if (!File.Exists(path)) return DomainNotFoundMessage;
                // 戻せるよう、読めるときだけ消す
                if (ReadUserLines(id, out var lines) is { } readError) return readError;
                content = string.Join('\n', lines) + "\n";
                wasEnabled = EnabledIds().Contains(id);
                if (wasEnabled && !Set(id, false)) return "設定ファイル (config.json) を読めないため、専門用語集を消しませんでした。";
                try
                {
                    File.Delete(path);
                }
                catch (Exception ex)
                {
                    if (wasEnabled) Set(id, true);
                    return $"専門用語集のファイルを消せませんでした: {ex.Message}";
                }
                AfterOwnWrite();
                return null;
            }
            catch (FileLockTimeoutException ex)
            {
                return ex.Message;
            }
            catch (Exception ex)
            {
                return $"専門用語集を消せませんでした: {ex.Message}";
            }
        }
    }

    /// <summary>消した自作の分野を、同じ ID・中身で戻す (DeleteUserDomain の content / wasEnabled)。同じ名前の分野ができていれば戻せない。</summary>
    public static string? RestoreUserDomain(string id, string content, bool enabled)
    {
        if (!IsUserId(id)) return "戻せない専門用語集です。";
        CheckExternal(force: true);
        lock (Lock)
        {
            try
            {
                SafeFile.EnsureDirectory(UserDirectory());
                using var _ = FileLock.Acquire(DomainLockTarget());
                CheckExternal(force: true);
                // すでに同じ ID のファイルがあれば、前に戻したときに書けている (有効にする段階で失敗した・取り消しをやり直したなど)。
                // 失敗として返すと、取り消しが消せなくなるので、成功として扱う (有効にしたいのに無効のままなら、もう一度有効にしてみる)。
                if (File.Exists(UserPath(id)))
                {
                    if (enabled && !EnabledIds().Contains(id) && !Set(id, true)) Diagnostics.Log.Warn("戻した専門用語集を有効にできませんでした (設定ファイルを読めません)。");
                    return null;
                }
                // 消したあとに同じ名前の分野ができていても、戻せなくならない (名前を「名前 (2)」にして戻す: 戻す語を失わない)
                var name = ParseName(content) ?? id;
                var finalName = UniqueName(name, exceptId: null);
                var lines = content.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
                if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
                if (finalName != name) SetNameLine(lines, finalName);
                if (WriteUserLines(id, lines) is { } writeError) return writeError;
                // 戻せている。有効にできなかったとき (config.json を読めない) は、失敗にしない: 失敗にすると、取り消しをやり直しても
                // 「すでにある」で止まり続ける。使う設定は、あとで左のチェックで入れられる。
                if (enabled && !Set(id, true)) Diagnostics.Log.Warn("戻した専門用語集を有効にできませんでした (設定ファイルを読めません)。左のチェックで使う設定にしてください。");
                return null;
            }
            catch (FileLockTimeoutException ex)
            {
                return ex.Message;
            }
            catch (Exception ex)
            {
                return $"専門用語集を戻せませんでした: {ex.Message}";
            }
        }
    }

    // ---- 語の追加・編集・削除 ----

    /// <summary>
    /// 自作の分野に語を足す。読みは整える (カタカナ → ひらがななど)。入れられない語 (不正・上限) は skipped に理由つきで返し、
    /// すでにある語は黙って飛ばす。実際に足した語を added に返す (取り消すときに使う)。保存できなければ理由 (added は空)。
    /// </summary>
    public static string? AddUserWords(string id, IEnumerable<(string Reading, string Word, string Note)> items, out IReadOnlyList<TermEntry> added,
        out IReadOnlyList<((string Reading, string Word) Item, string Reason)> skipped)
    {
        var addedList = new List<TermEntry>();
        var skippedList = new List<((string, string), string)>();
        var incoming = items.ToList();
        string? error;
        CheckExternal(force: true);
        lock (Lock)
        {
            error = MutateUser(id, lines =>
            {
                addedList.Clear();
                skippedList.Clear();
                var entries = EntryLines(lines);
                var seen = entries.Select(e => (e.Entry.Reading, e.Entry.Word)).ToHashSet();
                foreach (var (reading, word, note) in incoming)
                {
                    if (NormalizeUserWord(reading, word, out var r, out var w) is { } invalid)
                    {
                        skippedList.Add(((reading, word), invalid));
                        continue;
                    }
                    if (!seen.Add((r, w))) continue;
                    if (seen.Count > MaxUserDomainWords)
                    {
                        seen.Remove((r, w));
                        skippedList.Add(((reading, word), $"1 つの専門用語集に入れられる語は {MaxUserDomainWords} 語までです。"));
                        continue;
                    }
                    var entry = new TermEntry(r, w, CleanNote(note));
                    lines.Add(EntryText(entry));
                    addedList.Add(entry);
                }
                return null;
            });
        }
        added = error is null ? addedList : [];
        skipped = skippedList;
        return error;
    }

    /// <summary>
    /// 自作の分野の語を消す (同じ読み・語は全部)。消した語と、消す前の位置 (語の並びの中の 0 始まりの位置) を removed に返す (<see cref="RestoreUserWords"/> に渡して戻す)。
    /// </summary>
    public static string? RemoveUserWords(string id, IEnumerable<(string Reading, string Word)> items, out IReadOnlyList<(int Index, TermEntry Entry)> removed)
    {
        var targets = items.Select(i => (Reading: TermDictionary.Normalize(UserDictionary.NormalizeReading(i.Reading)), Word: i.Word.Trim())).ToHashSet();
        var list = new List<(int, TermEntry)>();
        string? error;
        CheckExternal(force: true);
        lock (Lock)
        {
            error = MutateUser(id, lines =>
            {
                list.Clear();
                var entries = EntryLines(lines);
                var drop = new HashSet<int>();
                for (var i = 0; i < entries.Count; i++)
                {
                    if (!targets.Contains((entries[i].Entry.Reading, entries[i].Entry.Word))) continue;
                    list.Add((i, entries[i].Entry));
                    drop.Add(entries[i].Line);
                }
                for (var i = lines.Count - 1; i >= 0; i--) if (drop.Contains(i)) lines.RemoveAt(i);
                return null;
            });
        }
        removed = error is null ? list : [];
        return error;
    }

    /// <summary>消した語を元の位置に戻す (RemoveUserWords の removed)。同じ語がもうあれば飛ばす。位置が今の数より後ろなら末尾に足す。</summary>
    public static string? RestoreUserWords(string id, IEnumerable<(int Index, TermEntry Entry)> items)
    {
        var ordered = items.OrderBy(i => i.Index).ToList();
        CheckExternal(force: true);
        lock (Lock)
        {
            return MutateUser(id, lines =>
            {
                foreach (var (index, entry) in ordered)
                {
                    if (NormalizeUserWord(entry.Reading, entry.Word, out var r, out var w) is not null) continue;
                    var entries = EntryLines(lines);
                    if (entries.Any(e => e.Entry.Reading == r && e.Entry.Word == w)) continue;
                    var text = EntryText(new TermEntry(r, w, CleanNote(entry.Note)));
                    if (index < entries.Count) lines.Insert(entries[Math.Max(0, index)].Line, text);
                    else lines.Add(text);
                }
                return null;
            });
        }
    }

    /// <summary>自作の分野の語を直す (位置・注記はそのまま)。元の語が無ければ <see cref="UserDictionary.NotFoundMessage"/>、ほかの語と重なれば <see cref="UserDictionary.DuplicateMessage"/>。</summary>
    public static string? UpdateUserWord(string id, (string Reading, string Word) old, string reading, string word)
    {
        if (NormalizeUserWord(reading, word, out var r, out var w) is { } invalid) return invalid;
        var oldReading = TermDictionary.Normalize(UserDictionary.NormalizeReading(old.Reading));
        var oldWord = old.Word.Trim();
        CheckExternal(force: true);
        lock (Lock)
        {
            return MutateUser(id, lines =>
            {
                var entries = EntryLines(lines);
                var index = entries.FindIndex(e => e.Entry.Reading == oldReading && e.Entry.Word == oldWord);
                if (index < 0) return UserDictionary.NotFoundMessage;
                if (oldReading == r && oldWord == w) return null;
                if (entries.Any(e => e.Entry.Reading == r && e.Entry.Word == w)) return UserDictionary.DuplicateMessage;
                // 読みと語の欄だけを入れ替える (注記・4 つ目の欄 (強制) はそのまま)
                var fields = lines[entries[index].Line].Split('\t');
                fields[0] = r;
                fields[1] = w;
                lines[entries[index].Line] = string.Join('\t', fields);
                return null;
            });
        }
    }

    /// <summary>自作の分野に登録・編集してよいか (保存はしない)。不正な入力と、同じ分野の中での重なりを理由で返す。except は編集中の元の語。</summary>
    public static string? CheckUserWord(string id, string reading, string word, (string Reading, string Word)? except)
    {
        if (NormalizeUserWord(reading, word, out var r, out var w) is { } invalid) return invalid;
        if (!IsUserId(id)) return DomainNotFoundMessage;
        CheckExternal(force: true);
        lock (Lock)
        {
            if (ReadUserLines(id, out var lines) is { } readError) return readError;
            var exceptKey = except is { } e ? (TermDictionary.Normalize(UserDictionary.NormalizeReading(e.Reading)), e.Word.Trim()) : default((string, string)?);
            if (exceptKey == (r, w)) return null;
            return EntryLines(lines).Any(x => x.Entry.Reading == r && x.Entry.Word == w) ? UserDictionary.DuplicateMessage : null;
        }
    }

    // ---- ユーザー辞書から移す ----

    /// <summary>
    /// ユーザー辞書の語を、自作の分野へ移す (移した語はユーザー辞書から消える)。
    /// 専門用語集に入れられない語 (不正な読み・語) は飛ばして、ユーザー辞書に残す (skipped に理由つき)。分野にすでにある語は、
    /// 分野には足さず、ユーザー辞書からだけ消す (移したことになる)。
    /// 移し先が無効なら有効にする (config.json を書けなければ、何も移さずに理由を返す)。選んだ語がもうユーザー辞書に無ければ、飛ばして理由を返す。
    /// 順序: 先に分野のファイルへ書き、次にユーザー辞書から消す。2 つ目が失敗したときは、語が両方にある状態のまま (消えない) 理由を返し、
    /// result には分野に足した語を入れて返す (画面が取り消せるように)。
    /// 取り消し: 分野から AddedToDomain を消し (<see cref="RemoveUserWords"/>)、ユーザー辞書へ RemovedFromUser を元の位置に戻す (<see cref="UserDictionary.Restore"/>)。
    /// </summary>
    public static string? MoveFromUserDictionary(UserDictionary dictionary, string id, IReadOnlyList<UserWord> words, out MoveResult result)
    {
        result = new MoveResult([], [], []);
        if (!IsUserId(id)) return "移し先は自作の専門用語集だけです。";
        // 画面が選んだ語が、今もユーザー辞書にあるか (ほかの画面・IME が消していないか) を、ファイルを読み直して確かめる。
        // 語は、画面の並び (新しい順) に関わらず、ユーザー辞書のファイルの順 (古い順) に書く: 同じ読みでは後に書いた語が先に使われるので、
        // 新しく登録した語が先、というユーザー辞書の優先順をそのまま保つため。
        dictionary.Refresh();
        var position = new Dictionary<UserWord, int>();
        var current = dictionary.Words;
        for (var i = 0; i < current.Count; i++) position.TryAdd(current[i], i);
        var skipped = new List<(UserWord, string)>();
        var movable = new List<(int Index, UserWord Word)>();
        var seen = new HashSet<UserWord>();
        foreach (var word in words)
        {
            var key = new UserWord(word.Reading.Trim(), word.Word.Trim());
            if (!seen.Add(key)) continue;
            if (!position.TryGetValue(key, out var index))
            {
                skipped.Add((word, "ユーザー辞書にもうありません (ほかの画面で変更・削除された可能性があります)。"));
                continue;
            }
            if (NormalizeUserWord(key.Reading, key.Word, out _, out _) is { } reason) skipped.Add((word, reason));
            else movable.Add((index, key));
        }
        movable.Sort((x, y) => x.Index.CompareTo(y.Index));
        if (movable.Count == 0)
        {
            result = new MoveResult([], [], skipped);
            return null;
        }
        // 移し先が無効なら、移す前に有効にする (有効でないと、移した語が黙って変換に使われなくなる)。
        // config.json を書けないときは、ファイルに触る前に断る。
        CheckExternal(force: true);
        bool enabled;
        lock (Lock)
        {
            if (!File.Exists(UserPath(id))) return DomainNotFoundMessage;
            enabled = EnabledIds().Contains(id);
        }
        if (!enabled && !Set(id, true)) return "設定ファイル (config.json) を読めないため、専門用語集を有効にできず、移しませんでした。";
        var error = AddUserWords(id, movable.Select(m => (m.Word.Reading, m.Word.Word, "")), out var added, out var addSkipped);
        if (error is not null)
        {
            // 書けなかった: 移すために有効にしたのなら、元の無効に戻す (戻せなければ、そのことも伝える)
            if (!enabled && !Set(id, false)) error += " (移すために有効にした設定も、元に戻せませんでした。左のチェックで確かめてください)";
            return error;
        }
        foreach (var (item, reason) in addSkipped) skipped.Add((new UserWord(item.Reading, item.Word), reason));
        var skippedSet = skipped.Select(s => s.Item1).ToHashSet();
        var toRemove = movable.Select(m => m.Word).Where(w => !skippedSet.Contains(w)).ToList();
        var removeError = dictionary.RemoveRange(toRemove, out var removed);
        if (removeError is not null)
        {
            result = new MoveResult([], added, skipped);
            return $"専門用語集には移しましたが、ユーザー辞書から消せませんでした (語は両方に残っています): {removeError}";
        }
        result = new MoveResult(removed, added, skipped);
        return null;
    }

    // ---- 書き出し・取り込み ----

    /// <summary>自作の分野を、同梱と同じ形式 (先頭に「# 名称:」「# 出典: 自作」) のテキストファイルに書き出す。書き出した語数を count に返す。</summary>
    public static string? ExportUserDomain(string id, string path, out int count)
    {
        count = 0;
        if (!IsUserId(id)) return "書き出せるのは自作の専門用語集だけです。";
        // 自作の専門用語集のフォルダーの中へは書き出せない (今使っている分野のファイルを上書きしかねない)
        try
        {
            var target = Path.GetFullPath(path);
            var folder = Path.GetFullPath(UserDirectory()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (target.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) return "自作の専門用語集のフォルダーの中には書き出せません。別の場所を選んでください。";
        }
        catch (Exception ex)
        {
            return $"書き出せませんでした: {ex.Message}";
        }
        CheckExternal(force: true);
        lock (Lock)
        {
            try
            {
                if (ReadUserLines(id, out var lines) is { } readError) return readError;
                var name = ParseName(string.Join('\n', lines)) ?? id;
                var entries = EntryLines(lines);
                var output = HeaderLines(name);
                output.AddRange(entries.Select(e => EntryText(e.Entry)));
                SafeFile.WriteAllLines(path, output, new UTF8Encoding(false));
                count = entries.Count;
                return null;
            }
            catch (Exception ex)
            {
                return $"書き出せませんでした: {ex.Message}";
            }
        }
    }

    /// <summary>
    /// ファイルを新しい自作の分野として取り込む (すぐ有効にする)。名前は先頭の「# 名称:」(無ければファイル名)。同じ名前の分野があれば「名前 (2)」にする。
    /// 語は 1 行ずつ検め、不正な行は飛ばして数える。summary は「取り込んだ語数 Tab 飛ばした行数 Tab 重複して省いた数 Tab 分野の名前」。取り込める語が無ければ作らない。
    /// </summary>
    public static string? ImportUserDomain(string path, out string id, out string summary)
    {
        id = "";
        summary = "";
        string text;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return "ファイルが見つかりません。";
            // 取り込むと、見出し (名称・出典・作成) の行が増える。保存できる大きさ (SafeFile.MaxReadBytes) に収まらないものは、先に断る。
            var limit = Math.Min(DictionaryManagement.MaxImportBytes, SafeFile.MaxReadBytes) - 4096;
            if (info.Length > limit) return $"ファイルが大きすぎます ({Math.Max(1, limit / 1024 / 1024)} MB まで)。";
            text = SafeFile.DecodeStrict(File.ReadAllBytes(path));
        }
        catch (DecoderFallbackException)
        {
            return "ファイルの文字コードが UTF-8 / UTF-16 ではないため読めません (UTF-8 で保存し直してください)。";
        }
        catch (Exception ex)
        {
            return $"取り込めませんでした: {ex.Message}";
        }
        var entries = new List<TermEntry>();
        var seen = new HashSet<(string, string)>();
        var skipped = 0;
        var duplicates = 0;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.AsSpan().TrimEnd('\r');
            if (line.Length > 0 && line[0] == '\uFEFF') line = line[1..];
            if (line.Length == 0 || line.TrimStart().Length == 0 || line.TrimStart()[0] == '#') continue;
            if (!TermDictionary.TryParseLine(line, out var reading, out var word, out var note) || NormalizeUserWord(reading, word, out var r, out var w) is not null)
            {
                skipped++;
                continue;
            }
            if (!seen.Add((r, w)))
            {
                duplicates++;
                continue;
            }
            if (entries.Count >= MaxUserDomainWords) return $"語が多すぎます (1 つの専門用語集に入れられるのは {MaxUserDomainWords} 語までです)。";
            entries.Add(new TermEntry(r, w, CleanNote(note)));
        }
        if (entries.Count == 0) return "取り込める語がありませんでした (「読み<Tab>語」の行が必要です)。";
        var baseName = ParseName(text) ?? Path.GetFileNameWithoutExtension(path).Replace("terms-", "", StringComparison.Ordinal);
        if (CheckUserName(TruncateName(baseName.Trim(), MaxUserNameLength), out var cleanBase) is not null) cleanBase = "取り込んだ専門用語集";
        var error = CreateWithLines(cleanBase!, dedupeName: true, name =>
        {
            var lines = HeaderLines(name);
            lines.AddRange(entries.Select(EntryText));
            return lines;
        }, out id, out var finalName);
        if (error is not null) return error;
        summary = $"{entries.Count}\t{skipped}\t{duplicates}\t{finalName}";
        return null;
    }
}
