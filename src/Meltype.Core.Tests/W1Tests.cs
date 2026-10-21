// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Detection;

namespace Meltype.Tests;

/// <summary>
/// W1: 本家 #895 (2026-10-19) 追従の回帰テスト。辞書に語を足すだけの取り込み。
/// 各テストは、取り込む前は赤・取り込んだ後は緑 (辞書は dictionaries/english.txt)。
/// </summary>
internal static class W1Tests
{
    /// <summary>同梱の英単語表のスペルチェッカー (アプリ・--repro と同じ) で打って Enter で確定した結果を確かめる。</summary>
    private static void ExpectCommitted(params (string Typed, string Expected)[] cases)
    {
        foreach (var (typed, expected) in cases) Assert.Equal(expected, Committed(typed), typed);
    }

    /// <summary>打って Enter で確定した結果を返す。</summary>
    private static string Committed(string typed)
    {
        var k = new CompositionTests.Keyboard();
        k.Type(typed + "\n");
        return k.Host.Document;
    }

    [Test]
    public static void Issue895_ExeStaysEnglish()
    {
        // 本家 #887 / #895: xe (ぇ) の小書きの綴りで最後まで読めても、実行ファイルの拡張子 exe は英字のまま
        // (辞書に無いので えぇ になっていた。本体は辞書に exe を足すだけで、判定の規則は変えない)。
        ExpectCommitted(("exe", "exe"));
    }

    [Test]
    public static void Issue895_ExeFollowedByJapanese()
    {
        // 英字のまま + 日本語 (datawo → dataを と同じ形)。exe が辞書に入ったので、後ろのローマ字は日本語に戻る。
        // exetool は exe + tool で、tool も辞書にあるので英字のままつながる。
        ExpectCommitted(("exewo", "exeを"), ("exede", "exeで"), ("exeha", "exeは"), ("exehitotu", "exeひとつ"),
            ("exetool", "exetool"));
    }

    [Test]
    public static void Issue895_NeighboursAreUnchanged()
    {
        // exe を辞書に足しても、同じ行にある exercise・exist と、先頭のドットの規則 (#34) は変えない。
        ExpectCommitted(("exercise", "exercise"), ("exist", "exist"), (".exe", ".exe"), (".exedesu", ".exeです"));
    }
}
