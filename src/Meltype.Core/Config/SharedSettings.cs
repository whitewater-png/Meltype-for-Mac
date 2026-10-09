// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Config;

/// <summary>
/// config.json が前に見たときから変わったかを、短い間隔でしか調べない見張り役。
/// 変換の設定は 1 打鍵のうちに何度も読まれるので、そのたびにファイルを調べないよう、調べるのは <see cref="IntervalMs"/> に 1 回までにする。
/// 版の比べ方は <see cref="FileStamp"/> (更新時刻・大きさ・作成時刻)。スレッドの安全は呼ぶ側が持つ (ロックの中で呼ぶ)。
/// </summary>
internal sealed class ConfigFileWatch
{
    /// <summary>ファイルを調べる間隔 (ミリ秒)。テストが 0 にする。</summary>
    internal static int IntervalMs { get; set; } = 500;

    private string? _path;
    private FileStamp? _stamp;
    private long _checkedAt;

    /// <summary>
    /// 前に見たときから変わったか (初めて見るファイル・パスが変わったときも true)。変わっていれば今の版を覚える。
    /// force なら間隔を待たずに調べる。間隔の内側なら調べずに false。
    /// </summary>
    public bool Poll(string path, bool force = false)
    {
        var now = Environment.TickCount64;
        if (!force && _stamp is not null && _path == path && now - _checkedAt < IntervalMs) return false;
        _checkedAt = now;
        var stamp = FileStamp.Of(path);
        var changed = _path != path || _stamp != stamp;
        _path = path;
        _stamp = stamp;
        return changed;
    }
}

/// <summary>
/// Mac の入力で使う、config.json の「今の設定」を 1 つだけ持つ入れ物。
/// 入力欄ごとに MeltypeSession が作られるが、設定はここを毎回見るので、別のプロセス (「Meltype 辞書」の「設定」タブ) が config.json を書き換えると、
/// 開いたままの入力欄にも反映される。ファイルを調べるのは <see cref="ConfigFileWatch.IntervalMs"/> に 1 回まで。
/// 読み直せない (壊れている・大きすぎる) ときは、最後に読めた値のまま動く。返す Settings は書き換えない。
/// </summary>
internal sealed class SharedSettings(Func<string> configPath, Action<Settings>? loaded = null)
{
    /// <summary>Mac の入力で使う共有の設定。ログの設定 (ファイルに書く・入力した文字を残す) は、読み込むたびにここで反映する。</summary>
    public static SharedSettings Shared { get; } = new(() => AppPaths.ConfigFile, ApplyLog);

    private readonly object _lock = new();
    private readonly ConfigFileWatch _watch = new();
    private Settings? _current;
    private string? _path;

    /// <summary>今の設定。最初は <see cref="Settings.Load"/> で読む (古い形式の移行もここ)。以後は config.json が変わったときだけ読み直す。</summary>
    public Settings Current
    {
        get
        {
            lock (_lock)
            {
                var path = configPath();
                if (_current is null || _path != path)
                {
                    _path = path;
                    _watch.Poll(path, force: true);
                    return Publish(Settings.Load(path));
                }
                if (_watch.Poll(path) && Settings.LoadForUpdate(path) is { } fresh) return Publish(fresh);
                return _current;
            }
        }
    }

    private Settings Publish(Settings settings)
    {
        _current = settings;
        try
        {
            loaded?.Invoke(settings);
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn($"設定の反映に失敗しました: {ex.Message}");
        }
        return settings;
    }

    /// <summary>覚えた設定を捨てて、次に使うときに読み直す (テスト用)。</summary>
    internal void Reset()
    {
        lock (_lock)
        {
            _current = null;
            _path = null;
        }
    }

    private static void ApplyLog(Settings settings)
    {
        Diagnostics.Log.SetFileOutput(settings.FileLog ? AppPaths.LogFile : null);
        Diagnostics.Log.RecordText = settings.LogTypedText;
    }
}
