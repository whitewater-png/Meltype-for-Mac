// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;
using Meltype.Config;

namespace Meltype.Tests;

/// <summary>設定・学習データ・ユーザー辞書のバックアップ (別の PC への移行)。</summary>
internal static class BackupTests
{
    [Test]
    public static void Backup_RoundTrips_AndStaysInsideDataFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "meltype-backup-test-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source");
        var target = Path.Combine(root, "target");
        try
        {
            Directory.CreateDirectory(Path.Combine(source, "dictionaries"));
            File.WriteAllText(Path.Combine(source, "config.json"), "{\"Mode\":\"Keyboard\"}");
            File.WriteAllText(Path.Combine(source, "userdict.txt"), "ゆきしろ\t雪代\n");
            File.WriteAllText(Path.Combine(source, "dictionaries", "candidates.txt"), "てすと テスト\n");
            File.WriteAllText(Path.Combine(source, "meltype.log"), "ログは入れない");

            var (content, files) = Backup.Create(source, "0.2.0");
            Assert.Equal(3, files, "ログは入れない");
            var (_, app, names) = Backup.Inspect(content);
            Assert.Equal("0.2.0", app);
            Assert.True(names.Contains("dictionaries/candidates.txt"), "ユーザーの辞書も入る");

            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "userdict.txt"), "まえ\t前\n");
            Assert.Equal(3, Backup.Restore(content, target));
            Assert.Equal("ゆきしろ\t雪代\n", File.ReadAllText(Path.Combine(target, "userdict.txt")));
            Assert.True(File.Exists(Path.Combine(target, "userdict.txt.before-restore")), "今までのファイルを残す");

            // データフォルダーの外を指す名前は書かない
            var evil = Encoding.UTF8.GetBytes("{\"format\":\"meltype-backup\",\"version\":1,\"app\":\"x\",\"created\":\"x\",\"files\":{\"../evil.txt\":\"AA==\",\"sub/x.txt\":\"AA==\"}}");
            Assert.Equal(0, Backup.Restore(evil, target), "../ や知らないフォルダーは無視");
            Assert.True(!File.Exists(Path.Combine(root, "evil.txt")), "データフォルダーの外に書かない");

            // 決まった名前 (バックアップに入れるもの) 以外は戻さない
            foreach (var name in new[] { "evil.exe", "C:evil.txt", "config.json:ads", "dictionaries/a:b.txt", "dictionaries/../x.txt", "dictionaries/sub/x.txt", "dictionaries/con.txt", "dictionaries/x.ps1", "..\\evil.txt", "/etc/x.txt" })
            {
                Assert.True(!Backup.IsRestorableName(name), name);
            }
            Assert.True(Backup.IsRestorableName("config.json") && Backup.IsRestorableName("dictionaries/candidates.txt"), "バックアップに入れるものは戻す");

            Assert.True(Throws(() => Backup.Inspect(Encoding.UTF8.GetBytes("{\"a\":1}"))), "Meltype のバックアップでないファイルは読まない");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    /// <summary>自作のローマ字表 dictionaries/romaji.tsv (Q25) も、利用者の辞書 dictionaries/*.txt と一緒にバックアップに入り、戻る。</summary>
    [Test]
    public static void Backup_IncludesCustomRomajiTable_AndUserDictionaries()
    {
        var root = Path.Combine(Path.GetTempPath(), "meltype-backup-test-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source");
        var target = Path.Combine(root, "target");
        try
        {
            Directory.CreateDirectory(Path.Combine(source, "dictionaries"));
            File.WriteAllText(Path.Combine(source, "dictionaries", "candidates.txt"), "てすと テスト\n");
            File.WriteAllText(Path.Combine(source, "dictionaries", Meltype.Detection.RomajiTable.FileName), "ka\tか\n");
            File.WriteAllText(Path.Combine(source, "dictionaries", "notes.md"), "対象外");

            var (content, files) = Backup.Create(source, "0.2.0");
            Assert.Equal(2, files, "辞書とローマ字表 (他の拡張子は入れない)");
            var (_, _, names) = Backup.Inspect(content);
            Assert.True(names.Contains("dictionaries/candidates.txt"), "ユーザーの辞書は引き続き入る");
            Assert.True(names.Contains("dictionaries/romaji.tsv"), "自作のローマ字表も入る");
            Assert.True(Backup.IsRestorableName("dictionaries/romaji.tsv"), "戻してよい名前");
            Assert.True(!Backup.IsRestorableName("dictionaries/notes.md") && !Backup.IsRestorableName("dictionaries/x.tsv.exe"), "txt と tsv 以外は戻さない");

            Directory.CreateDirectory(target);
            Assert.Equal(2, Backup.Restore(content, target));
            Assert.Equal("ka\tか\n", File.ReadAllText(Path.Combine(target, "dictionaries", "romaji.tsv")));
            Assert.Equal("てすと テスト\n", File.ReadAllText(Path.Combine(target, "dictionaries", "candidates.txt")));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static bool Throws(Action action)
    {
        try { action(); return false; } catch { return true; }
    }
}
