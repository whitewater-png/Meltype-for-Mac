// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;
using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>ユーザー辞書の取り込み・書き出し (ほかの日本語入力からの乗り換え)。</summary>
internal static class UserDictionaryFileTests
{
    [Test]
    public static void Import_MicrosoftImeAndGoogleFormats()
    {
        // Microsoft IME の「一覧の出力」(UTF-16 LE、BOM 付き)
        var msime = "!Microsoft IME Dictionary Tool\r\n!Version:\r\n!Format:WORDLIST\r\n\r\nゆきしろ\t雪代\t人名\r\nメルタイプ\tMeltype\t固有名詞\r\nあ\t亜\t名詞\r\n";
        var result = UserDictionaryFile.Parse([.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(msime)]);
        Assert.Equal("UTF-16", result.Encoding);
        Assert.Equal(2, result.Words.Count, "1 文字の読み (あ) は飛ばす");
        Assert.Equal(new UserWord("めるたいぷ", "Meltype"), result.Words[1], "カタカナの読みはひらがなにする");
        Assert.Equal(1, result.Skipped);

        // Google 日本語入力のエクスポート (UTF-8、BOM なし、4 列)
        var google = Encoding.UTF8.GetBytes("# コメント\nきごうとう\t記号等\t名詞\t\nkaomoji\t(^^)\t顔文字\tコメント\n");
        var g = UserDictionaryFile.Parse(google);
        Assert.Equal("UTF-8", g.Encoding);
        Assert.Equal(new UserWord("きごうとう", "記号等"), g.Words[0]);

        // BOM の無い UTF-16
        Assert.Equal(1, UserDictionaryFile.Parse(Encoding.Unicode.GetBytes("ゆきしろ\t雪代\t名詞\r\n")).Words.Count, "BOM の無い UTF-16");
    }

    [Test]
    public static void Export_RoundTrips_AndSkipsDuplicates()
    {
        var dictionary = new UserDictionary(null, builtIn: false);
        Assert.Equal(2, dictionary.AddRange([new UserWord("ゆきしろ", "雪代"), new UserWord("めるたいぷ", "Meltype"), new UserWord("ゆきしろ", "雪代")]), "同じ語は 1 回だけ");
        var bytes = UserDictionaryFile.Export(dictionary.Words);
        Assert.True(bytes is [0xFF, 0xFE, ..], "Microsoft IME と同じ UTF-16 LE (BOM 付き)");
        var back = UserDictionaryFile.Parse(bytes);
        Assert.Equal(2, back.Words.Count, "書き出したものを取り込める");
        Assert.Equal(0, dictionary.AddRange(back.Words), "登録済みの語は増やさない");
    }
}
