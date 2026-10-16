// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;

namespace Meltype.Config;

/// <summary>
/// 学習データ・ユーザー辞書など、打った内容に近いファイルの読み書きの共通処理。
/// 書くときは「同じフォルダーの一時ファイル → 置き換え」で、Mac / Linux では本人だけが読める権限 (ファイル 0600・フォルダー 0700) にする
/// (学習データには打った語が入るので、同じ Mac の別ユーザーや他のアプリから読めないようにするため)。Windows は保存先がユーザーのフォルダーなので何もしない。
/// 読むときは大きさの上限を見る (壊れた・巨大なファイルで起動時にメモリを使い切らないため)。
/// </summary>
internal static class SafeFile
{
    /// <summary>読み込んでよいファイルの大きさの上限 (バイト)。テストが小さい値に差し替える。</summary>
    internal static long MaxReadBytes { get; set; } = 20L * 1024 * 1024;

    private const UnixFileMode OwnerFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode OwnerDirectory = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <summary>フォルダーを作る。新しく作るときは Mac / Linux で 0700 にする (Windows では普通に作るだけ)。</summary>
    public static void EnsureDirectory(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
            return;
        }
        Directory.CreateDirectory(directory, OwnerDirectory);
        // データの保存場所そのものは、前の版が 0755 で作っていても 0700 に直す (他のフォルダーの権限は触らない)。
        if (Path.GetFullPath(directory).TrimEnd('/') == Path.GetFullPath(AppPaths.DataDirectory).TrimEnd('/')) Restrict(directory, OwnerDirectory);
    }

    /// <summary>Mac / Linux でだけ、ファイルかフォルダーの権限を本人だけにする。失敗しても (権限が変えられない場所でも) 続ける。</summary>
    public static void Restrict(string path, UnixFileMode? mode = null)
    {
        if (OperatingSystem.IsWindows()) return;
        try
        {
            File.SetUnixFileMode(path, mode ?? OwnerFile);
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"{Path.GetFileName(path)} の権限を本人だけにできませんでした: {ex.Message}");
        }
    }

    /// <summary>
    /// 読めなかった (壊れた) ファイルを「名前.broken」に残す。読めないまま空で動き、次の保存で上書きしても、元の中身は .broken に残る
    /// (本家 #551 の考え方。LanguageMemory は保存の直前に同じことをするが、ConversionHistory・TranslationHistory は読んだ時点で残す)。
    /// 一時ファイルに書いて置き換える (WriteAllBytes と同じ。.broken がリンクでもリンク先は書き換えず、最初から本人だけが読める権限)。
    /// 中身がない (0 バイト・空白だけの) ファイルは残すものがないので、前の .broken を空で潰さない。
    /// 残せた (または残すものがなかった) なら true。残せなければ false (呼び出し側は、このパスへの保存を止める)。
    /// </summary>
    internal static bool KeepBroken(string path)
    {
        try
        {
            if (new FileInfo(path).Length > MaxReadBytes) return false;
            var bytes = File.ReadAllBytes(path);
            if (IsBlank(bytes)) return true;
            var backup = path + ".broken";
            WriteAllBytes(backup, bytes);
            Diagnostics.Log.Warn($"読めなかった {Path.GetFileName(path)} を {Path.GetFileName(backup)} に残しました。");
            return true;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"{Path.GetFileName(path)} を .broken に残せませんでした: {ex.GetType().Name}");
            return false;
        }
    }

    /// <summary>空白 (と UTF-8 の BOM) だけの中身か。</summary>
    private static bool IsBlank(byte[] bytes)
    {
        foreach (var b in bytes)
            if (b is not (0x20 or 0x09 or 0x0A or 0x0D or 0xEF or 0xBB or 0xBF)) return false;
        return true;
    }

    /// <summary>このパスへの保存を止める (読み込めず、退避もできなかったファイルを、新しい内容で上書きしないため)。</summary>
    internal static void Block(string path) { lock (Blocked) Blocked.Add(Path.GetFullPath(path)); }

    /// <summary>
    /// このパスへの保存の停止を解く。「学習データをすべて消去」が、止めていたファイルを消したあとに呼ぶ
    /// (中身ごと消すと利用者が選んだので、守るものがなくなる)。止めていなければ何もしない。
    /// </summary>
    internal static void Unblock(string path)
    {
        lock (Blocked)
        {
            var full = Path.GetFullPath(path);
            Blocked.Remove(full);
            BlockWarned.Remove(full);
        }
    }

    /// <summary>
    /// 学習データ (ConversionHistory・TranslationHistory) を読めなかったときの後始末。
    /// 壊れていた (JSON として読めない) なら .broken に残して、空で続ける。残せなければ、元のファイルを守るため保存を止める。
    /// 読めなかっただけ (権限・所有者・I/O。壊れているとは限らない) なら、残しようがないので、やはり保存を止める。
    /// 読もうとした時点でファイルが無くなっていたときは、守るものがないので何もしない。
    /// </summary>
    internal static void ProtectUnreadable(string path, Exception ex)
    {
        if (ex is FileNotFoundException or DirectoryNotFoundException) return;
        if (ex is IOException or UnauthorizedAccessException) { Block(path); return; }
        if (!KeepBroken(path)) Block(path);
    }

    /// <summary>
    /// ログに出す例外の説明。JsonException.Message は "Path: $.打った読み.Used" のようにキー (= 打った文字) を含み、
    /// 「ログに入力した文字を残す」が OFF でも漏れるので、種類と位置 (と、原因の切り分けに役立つ HResult) だけにする。
    /// </summary>
    internal static string Describe(Exception ex) =>
        ex is System.Text.Json.JsonException json
            ? $"JsonException (行 {json.LineNumber?.ToString() ?? "?"}、位置 {json.BytePositionInLine?.ToString() ?? "?"})"
            : $"{ex.GetType().Name} (0x{ex.HResult:X8})";

    /// <summary>読み込めず退避もできなかったファイル (保存しない)。</summary>
    private static readonly HashSet<string> Blocked = new(StringComparer.Ordinal);

    /// <summary>「保存を止めています」の警告を出した、止めているパス (保存のたびに出すと、ログの上限の 1000 件が埋まるので、止めてから 1 回だけ出す)。</summary>
    private static readonly HashSet<string> BlockWarned = new(StringComparer.Ordinal);

    /// <summary>テスト用: 退避を必ず失敗させる。</summary>
    internal static bool FailBackupForTests { get; set; }

    /// <summary>テスト用: 保存を止めたパスの記録を空にする。</summary>
    internal static void ResetBlockedForTests() { lock (Blocked) { Blocked.Clear(); BlockWarned.Clear(); } }

    internal static bool IsBlocked(string path) { lock (Blocked) return Blocked.Contains(Path.GetFullPath(path)); }

    /// <summary>
    /// 大きすぎるファイルを「名前.oversize」(あれば .oversize.1, .2 …) にコピーする。同じ大きさの退避が既にあれば、起動のたびに増えないよう何もしない。
    /// 成功か、既に退避済みなら true。
    /// </summary>
    private static bool BackUpOversize(string path, long length)
    {
        try
        {
            if (FailBackupForTests) throw new IOException("テストで退避を失敗させた");
            for (var i = 0; ; i++)
            {
                var target = i == 0 ? path + ".oversize" : $"{path}.oversize.{i}";
                if (!File.Exists(target))
                {
                    File.Copy(path, target);
                    Restrict(target);
                    Diagnostics.Log.Warn($"大きすぎる {Path.GetFileName(path)} を {Path.GetFileName(target)} に退避しました。");
                    return true;
                }
                if (new FileInfo(target).Length == length) return true;
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"大きすぎる {Path.GetFileName(path)} を退避できませんでした: {ex.Message}");
            return false;
        }
    }

    public static void WriteAllText(string path, string text, Encoding? encoding = null)
    {
        encoding ??= new UTF8Encoding(false);
        var body = encoding.GetBytes(text);
        var preamble = encoding.GetPreamble();
        WriteAllBytes(path, preamble.Length == 0 ? body : [.. preamble, .. body]);
    }

    public static void WriteAllLines(string path, IEnumerable<string> lines, Encoding? encoding = null) =>
        WriteAllText(path, string.Concat(lines.Select(l => l + Environment.NewLine)), encoding);

    /// <summary>
    /// 一時ファイルに書いてから置き換える (書き込み中に落ちても元のファイルを壊さない)。
    /// 一時ファイルの名前は毎回変える (複数のプロセスが同時に保存しても、同じ .tmp を奪い合って壊し合わない)。
    /// 一時ファイルは最初から 0600 で作るので、置き換えた後のファイルも 0600 になる (前の版が 0644 で作ったものもここで直る)。失敗したら一時ファイルを残さない。
    /// </summary>
    public static void WriteAllBytes(string path, byte[] bytes)
    {
        // 読み込めず退避もできなかったファイルには書かない (元のデータを空に近い内容で上書きしないため)。
        lock (Blocked)
        {
            var full = Path.GetFullPath(path);
            if (Blocked.Contains(full))
            {
                // 止めてから 1 回だけ警告する (原因は、止めた側が読めなかった・退避できなかった理由として書いている)。
                if (BlockWarned.Add(full)) Diagnostics.Log.Warn($"{Path.GetFileName(path)} は、読めなかった・退避できなかったので、保存を止めています (元のファイルを守るため)。");
                return;
            }
        }
        EnsureDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = OwnerFile;
            using (var stream = new FileStream(temp, options))
            {
                stream.Write(bytes);
                // 置き換える前にディスクまで書く (電源断で、置き換えただけで中身が空のファイルになるのを防ぐ)。
                stream.Flush(true);
            }
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(temp); } catch { }
            throw;
        }
    }

    /// <summary>
    /// 上限以下ならファイルの中身を返す。上限を超えていたら警告を出して null (呼び出し側は空で続ける)。
    /// 読めないときは ReadAllText と同じ例外を投げる (呼び出し側が今まで通り握る)。
    /// </summary>
    public static string? ReadAllText(string path)
    {
        if (IsOversize(path)) return null;
        return File.ReadAllText(path);
    }

    /// <summary>
    /// <see cref="ReadAllText"/> と同じだが、文字コードを厳密に読む: BOM があれば UTF-8 / UTF-16 (LE・BE)、無ければ UTF-8 とみなし、
    /// 不正なバイト (Shift_JIS で保存したファイルなど) があれば <see cref="DecoderFallbackException"/> を投げる。
    /// File.ReadAllText は不正なバイトを黙って U+FFFD に置き換えるので、それを読み直して書き戻すと元のバイト列が失われる。
    /// 読み直してから書き戻すファイル (ユーザー辞書・除外した専門用語) はこちらで読み、読めなければ書かない。
    /// </summary>
    public static string? ReadAllTextStrict(string path)
    {
        if (IsOversize(path)) return null;
        return DecodeStrict(File.ReadAllBytes(path));
    }

    internal static string DecodeStrict(byte[] bytes)
    {
        if (bytes is [0xEF, 0xBB, 0xBF, ..]) return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes, 3, bytes.Length - 3);
        if (bytes is [0xFF, 0xFE, ..]) return new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true).GetString(bytes, 2, bytes.Length - 2);
        if (bytes is [0xFE, 0xFF, ..]) return new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true).GetString(bytes, 2, bytes.Length - 2);
        return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
    }

    /// <summary>上限を超えていれば警告を出し、.oversize に退避して true (呼び出し側は空で続ける)。退避できなければ、このパスへの保存を止める。</summary>
    private static bool IsOversize(string path)
    {
        var length = new FileInfo(path).Length;
        if (length <= MaxReadBytes) return false;
        Diagnostics.Log.Warn($"{Path.GetFileName(path)} が大きすぎる ({length / 1024 / 1024} MB、上限 {MaxReadBytes / 1024 / 1024} MB) ので読み込まず、空で続けます。");
        // 空で続けると次の保存で元のファイルが上書きされるので、先に .oversize へコピーして残す (元は消さない)。
        // 退避できなかったときは、このパスへの保存を止める。
        if (!BackUpOversize(path, length))
        {
            lock (Blocked) Blocked.Add(Path.GetFullPath(path));
        }
        return true;
    }
}
