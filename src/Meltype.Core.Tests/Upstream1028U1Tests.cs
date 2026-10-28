// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Tests;

/// <summary>
/// 本家 #1231 (2026-10-28 取り込み U1): wakatte (分かって) が固有名 Wakatte の小文字照合で英字のまま確定されていた。
/// </summary>
internal static class Upstream1028U1Tests
{
    private static string Committed(string typed)
    {
        var k = new CompositionTests.Keyboard();
        k.Type(typed + "\n");
        return k.Host.Document;
    }

    [Test]
    public static void WakatteIsJapanese()
    {
        Assert.Equal("わかって", Committed("wakatte"), "wakatte");
        Assert.Equal("それわかって", Committed("sorewakatte"), "sorewakatte");
        Assert.Equal("わかってください", Committed("wakattekudasai"), "wakattekudasai");
    }

    /// <summary>変わってはいけないもの: Shift で打った Wakatte (英字のまま)、ドメインの保護、他の固有名。</summary>
    [Test]
    public static void WakatteGuards()
    {
        Assert.Equal("Wakatte", Committed("Wakatte"), "Wakatte");
        Assert.Equal("Tetr.TV", Committed("Tetr.TV"), "Tetr.TV");
        Assert.Equal("Tetr.TVで", Committed("Tetr.TVde"), "Tetr.TVde");
        Assert.Equal("tetr.io", Committed("tetr.io"), "tetr.io");
    }
}
