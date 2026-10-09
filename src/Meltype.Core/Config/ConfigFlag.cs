// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Config;

/// <summary>
/// Mac の入力メニューから切り替える、config.json の bool 設定 1 つ分。入力欄ごとに MeltypeSession が作られるが、
/// 切り替えがすべての入力欄にすぐ反映されるよう、値はここで 1 つだけ持つ (各セッションは Func で毎回ここを見る)。
/// 切り替えは config.json に保存する。IME と画面 (別プロセス) が同時に config.json を変えても消し合わないよう、
/// ロックの中で読み直してから書く。読めない config.json は上書きしない。
/// </summary>
internal sealed class ConfigFlag(string label, bool defaultValue, Func<Settings, bool> read, Action<Settings, bool> write)
{
    private readonly object _lock = new();
    private readonly ConfigFileWatch _watch = new();
    private bool? _value;

    /// <summary>設定ファイルの場所。テストが差し替える。</summary>
    internal Func<string> ConfigPath { get; set; } = () => AppPaths.ConfigFile;

    /// <summary>
    /// 今の値。最初に使うときに config.json から読む (項目が無ければ既定値。読めなければ既定値)。
    /// 以後も、別のプロセス (「Meltype 辞書」の「設定」タブ) が config.json を書き換えたら読み直す (調べるのは <see cref="ConfigFileWatch.IntervalMs"/> に 1 回まで)。
    /// 読み直せないときは、前の値のまま。
    /// </summary>
    public bool IsOn
    {
        get
        {
            lock (_lock)
            {
                var path = ConfigPath();
                if (!_watch.Poll(path) && _value is { } value) return value;
                bool on;
                try
                {
                    on = Settings.LoadForUpdate(path) is { } settings ? read(settings) : _value ?? defaultValue;
                }
                catch (Exception ex)
                {
                    Diagnostics.Log.Warn($"{label}の設定を読めませんでした ({((_value ?? defaultValue) ? "ON" : "OFF")} にします): {ex.Message}");
                    on = _value ?? defaultValue;
                }
                _value = on;
                return on;
            }
        }
    }

    /// <summary>切り替えて config.json に保存する。保存できなければ false (値は変えない)。</summary>
    public bool Set(bool on)
    {
        lock (_lock)
        {
            try
            {
                var path = ConfigPath();
                SafeFile.EnsureDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                // Mac の「Meltype 辞書」の画面 (別のプロセス) も config.json を書く (専門用語集の分野) ので、ロックの中で読み直して書く。
                using (FileLock.Acquire(path))
                {
                    // 読めない設定は、既定値で上書きして失わないよう、何もしない。
                    if (Settings.LoadForUpdate(path) is not { } settings) return false;
                    write(settings, on);
                    settings.Save(path);
                }
                _value = on;
                return true;
            }
            catch (Exception ex)
            {
                Diagnostics.Log.Warn($"{label}の設定を保存できませんでした: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>覚えた値を捨てて、次に使うときに読み直す (テスト用)。</summary>
    internal void Reset()
    {
        lock (_lock) _value = null;
    }
}
