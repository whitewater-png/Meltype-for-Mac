// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Tests;

/// <summary>
/// 要件 053: 語の頭の # に続く 16 進のカラーコード (#30b0e8・#fff) を英字のまま確定する (本家 #2383)。
/// 正解表は # で始まる行を注釈として読み飛ばすので、# で始まる入力はここで守る。正解表: eval/cases/hex-color-053.tsv。
/// 文書: docs/requirements/053-hex-color-code.md
/// </summary>
internal static class HexColor053Tests
{
    /// <summary>実機 (と --repro) と同じく、同梱の英単語の一覧をスペルチェッカーとして使って打つ。</summary>
    private static string Typed(string keys)
    {
        var previous = CompositionTests.Detector.SpellChecker;
        CompositionTests.Detector.SpellChecker = Detection.BuiltInWordChecker.Shared;
        try
        {
            var k = new CompositionTests.Keyboard();
            k.Type(keys);
            return k.Host.Document.TrimEnd();
        }
        finally { CompositionTests.Detector.SpellChecker = previous; }
    }

    private static void Check(Dictionary<string, string> cases)
    {
        foreach (var (typed, expected) in cases) Assert.Equal(expected, Typed(typed + "\n"), typed);
    }

    [Test]
    public static void Req053_HexColorCodes_StayEnglish()
    {
        Check(new()
        {
            ["#30b0e8"] = "#30b0e8",
            ["#ff00e8"] = "#ff00e8",
            ["#e8e8e8"] = "#e8e8e8",
            ["#30B0E8"] = "#30B0E8",
            ["#fff"] = "#fff",
            ["#abc"] = "#abc",
            ["#deaf"] = "#deaf",
            ["#30b0e8ff"] = "#30b0e8ff",
            ["#30b0e8;"] = "#30b0e8;",
            ["color: #30b0e8;"] = "color: #30b0e8;",
            ["(#fff)"] = "(#fff)",
            // 桁の終わりの de は、2 桁短くてもカラーコードなら助詞の で
            ["#30b0e8de"] = "#30b0e8で",
            ["#e8e8e8de"] = "#e8e8e8で",
        });
    }

    [Test]
    public static void Req053_HashtagsAndReadableShapes_StayAsBefore()
    {
        Check(new()
        {
            // 日本語のハッシュタグ・英字だけで読める形 (ハッシュタグと見分けられない)
            ["#nihongo"] = "#にほんご",
            ["#ekimae"] = "#えきまえ",
            ["#abe"] = "#あべ",
            ["#cafe"] = "#かふぇ",
            ["#ace"] = "#あせ",
            ["#eee"] = "#えええ",
            // 数字 + 読める英字 (1で・10ば)
            ["#1de"] = "#1で",
            ["#10ba"] = "#10ば",
            // 後ろに英字が続く形・桁の数が合わない形は今までどおり
            ["#30b0e8wo"] = "#30b0え8を",
            ["#ff"] = "#っf",
            ["#e"] = "#え",
            // C の指令・言語の名前・数字だけ
            ["#endif"] = "#endif",
            ["#define"] = "#でふぃね",
            ["C#"] = "C#",
            ["F#de"] = "F#で",
            ["#123"] = "#123",
            ["#beef"] = "#beef",
            ["#beefde"] = "#beefで",
        });
    }
}
