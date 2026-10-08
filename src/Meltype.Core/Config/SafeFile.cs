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

    /// <summary>読み込めず退避もできなかったファイル (保存しない)。</summary>
    private static readonly HashSet<string> Blocked = new(StringComparer.Ordinal);

    /// <summary>テスト用: 退避を必ず失敗させる。</summary>
    internal static bool FailBackupForTests { get; set; }

    /// <summary>テスト用: 保存を止めたパスの記録を空にする。</summary>
    internal static void ResetBlockedForTests() { lock (Blocked) Blocked.Clear(); }

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
            if (Blocked.Contains(Path.GetFullPath(path)))
            {
                Diagnostics.Log.Warn($"{Path.GetFileName(path)} は退避できなかったので、保存しません (元のファイルを守るため)。");
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
