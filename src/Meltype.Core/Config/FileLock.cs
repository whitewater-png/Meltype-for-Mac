// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Config;

/// <summary>
/// 別のプロセスが同じファイルを「読み直して書き直す」とき、片方の変更を消さないための排他ロック
/// (Mac では IME と「Meltype 辞書」の画面が別のプロセスで、同じ userdict.txt・config.json・terms-excluded.txt を書く)。
/// 対象のファイルの隣の隠しファイル「.名前.lock」(0600) を FileShare.None で開いている間だけ持つ
/// (.NET は Mac / Linux で flock(LOCK_EX) を、Windows では共有モードを使う。どちらも同じプロセスの中でも効く)。
/// プロセスが落ちると OS が外すので、残ったロックで止まり続けることはない。ロックのファイルは消さない
/// (消すと、待っていた別のプロセスが別の inode をロックして、同時に書けてしまう)。
/// 使い方: 読み直し → 変更 → 保存 (SafeFile) を using (FileLock.Acquire(path)) の中で行う。
/// </summary>
internal static class FileLock
{
    /// <summary>ほかのプロセスが持っているとき、待つ時間の上限 (ミリ秒)。テストが短くする。</summary>
    internal static int TimeoutMs { get; set; } = 3000;

    /// <summary>ロックのファイルのパス (対象と同じフォルダーの隠しファイル)。</summary>
    internal static string LockPath(string target)
    {
        var full = Path.GetFullPath(target);
        return Path.Combine(Path.GetDirectoryName(full)!, "." + Path.GetFileName(full) + ".lock");
    }

    /// <summary>
    /// ロックを取る。取れたら、閉じると外れる FileStream を返す。待っても取れなければ <see cref="FileLockTimeoutException"/>。
    /// 権限が無いなど、待っても変わらない失敗はそのまま例外にする。
    /// </summary>
    public static FileStream Acquire(string target)
    {
        var path = LockPath(target);
        SafeFile.EnsureDirectory(Path.GetDirectoryName(path)!);
        var options = new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        var deadline = Environment.TickCount64 + Math.Max(0, TimeoutMs);
        while (true)
        {
            try
            {
                return new FileStream(path, options);
            }
            catch (IOException) when (File.Exists(path) && Environment.TickCount64 < deadline)
            {
                // ほかのプロセス (または同じプロセスの別のインスタンス) が持っている。少し待ってやり直す。
                Thread.Sleep(15);
            }
            catch (IOException ex) when (File.Exists(path))
            {
                throw new FileLockTimeoutException(Path.GetFileName(target), ex);
            }
        }
    }
}

/// <summary>ほかのプロセスがロックを持ったままで、待っても取れなかった。</summary>
internal sealed class FileLockTimeoutException(string name, Exception inner)
    : IOException($"ほかの画面 (またはプロセス) が {name} を保存中のため、変更できませんでした。少し待ってからやり直してください。", inner);

/// <summary>
/// ファイルの「版」(存在・大きさ・更新時刻・作成時刻)。前に読んだときと違えば、ほかのプロセスが書き換えたとみなして読み直す。
/// SafeFile は一時ファイルからの置き換えで保存するので、書き換わると更新時刻 (APFS ではナノ秒) が変わり、作成時刻も新しいファイルのものになる
/// (更新時刻が 1 秒単位のファイルシステムで、同じ秒に同じ大きさで 2 回書き換わっても、置き換えなら作成時刻で気づける)。
/// 不変のクラスにしてあるので、参照ごと入れ替えればスレッドの間でちぎれない。
/// </summary>
internal sealed record FileStamp(bool Exists, long Length, long WriteTicks, long CreationTicks)
{
    public static readonly FileStamp Missing = new(false, 0, 0, 0);

    /// <summary>今のファイルの版。調べられないとき (権限など) は存在しない扱い。</summary>
    public static FileStamp Of(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? new FileStamp(true, info.Length, info.LastWriteTimeUtc.Ticks, info.CreationTimeUtc.Ticks) : Missing;
        }
        catch
        {
            return Missing;
        }
    }
}
