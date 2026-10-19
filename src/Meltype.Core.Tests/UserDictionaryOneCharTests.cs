// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;
using Meltype.Composition;

namespace Meltype.Tests;

/// <summary>
/// P54 (本家 #763): ユーザー辞書に 1 文字の読みを登録できる。
/// 1 文字の読みは、変換する読みがちょうど 1 文字のときだけ使う (語の途中では使わない)。
/// 取り込み (ほかの日本語入力・plist) の最小長は 2 文字のまま。
/// </summary>
internal static class UserDictionaryOneCharTests
{
    [Test]
    public static void Validate_AcceptsOneChar_AndRejectsEmpty()
    {
        Assert.True(UserDictionary.Validate("わ", "輪") is null, "1 文字の読みを登録できる");
        Assert.Equal("読みを入力してください。", UserDictionary.Validate("", "輪"));
        Assert.True(UserDictionary.Validate("わた", "綿") is null, "2 文字以上は今までどおり");
    }

    [Test]
    public static void RegisterOneChar_IsUsedOnlyForTheWholeReading()
    {
        var dictionary = new UserDictionary(null, builtIn: false);
        Assert.True(dictionary.AddNew("わ", "輪") is null, "1 文字の読みを登録できる");

        Assert.Equal("輪", string.Join("|", dictionary.Lookup("わ")));
        Assert.Equal("わ:輪", string.Join("|", dictionary.Split("わ")!.Select(p => p.Reading + ":" + p.Word)));
        Assert.True(dictionary.Split("わたし") is null, "1 文字の読みは語の途中では使わない");
    }

    [Test]
    public static void UserDictFile_OneCharReading_IsLoaded()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meltype-onechar-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "userdict.txt");
        try
        {
            File.WriteAllText(path, "わ\t輪\nわた\t綿\n", new UTF8Encoding(false));
            var dictionary = new UserDictionary(path, builtIn: false);
            Assert.Equal("輪", string.Join("|", dictionary.Lookup("わ")));
            Assert.Equal("綿", string.Join("|", dictionary.Lookup("わた")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public static void Import_StillSkipsOneCharReadings()
    {
        // ほかの日本語入力からの取り込みは、今までどおり 2 文字以上 (UserDictionaryFile は MinReadingLength を使う)。
        var result = UserDictionaryFile.Parse(Encoding.UTF8.GetBytes("わ\t輪\nわた\t綿\n"));
        Assert.Equal(1, result.Words.Count);
        Assert.Equal(new UserWord("わた", "綿"), result.Words[0]);
        Assert.Equal(1, result.Skipped);
    }
}
