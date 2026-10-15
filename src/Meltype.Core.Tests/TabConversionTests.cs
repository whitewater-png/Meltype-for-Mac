// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Tests;

/// <summary>
/// 設定「Tab で変換」(本家 1.2.1 の issue #219)。入力中の Tab で、Space と同じく変換を始める。
/// 本家は既定 ON、Mac 版は既定 OFF。テストは本家の TabConversion_* 9 件を写し、OFF のとき今までどおりのテストを足している。
/// </summary>
internal static class TabConversionTests
{
    private static void ShiftTab(CompositionTests.Keyboard k)
    {
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Tab);
        k.Key(VirtualKeys.LShift, up: true);
    }

    [Test]
    public static void DefaultIsOff()
    {
        // Mac 版は既定 OFF (フォームで日本語を打って Tab で次の欄へ移る操作を変えない)。本家は ON
        Assert.True(!new Meltype.Config.Settings().TabConversion, "設定の既定は OFF");
        Assert.True(!new CompositionOptions().TabConversion(), "CompositionOptions の既定も OFF");
    }

    [Test]
    public static void Off_TabCommitsAndPassesTab_AsBefore()
    {
        var k = new CompositionTests.Keyboard();
        k.Type("kana");
        k.Press(VirtualKeys.Tab);
        Assert.True(k.Host.View?.Converting != true, "変換にはならない");
        Assert.Equal("かな", k.Host.Document, "確定する");
        Assert.True(k.Host.Events.Contains("down:09"), "Tab は渡す: " + string.Join("|", k.Host.Events));
    }

    [Test]
    public static void StartsConversionLikeSpace()
    {
        var space = new CompositionTests.Keyboard();
        space.Type("kana ");
        var k = new CompositionTests.Keyboard { TabConversion = true };
        k.Type("kana");
        k.Press(VirtualKeys.Tab);
        Assert.True(k.Host.View?.Converting == true, "変換が始まる");
        Assert.Equal(space.Showing, k.Showing);
        Assert.Equal(string.Join(",", space.Host.View!.Candidates), string.Join(",", k.Host.View!.Candidates));
        Assert.True(!k.Host.Events.Any(e => e == "down:09"), "Tab は渡さない: " + string.Join("|", k.Host.Events));
    }

    [Test]
    public static void TabMovesToNextAndShiftTabToPreviousCandidate()
    {
        var space = new CompositionTests.Keyboard();
        space.Type("kana  ");
        var k = new CompositionTests.Keyboard { TabConversion = true };
        k.Type("kana");
        k.Press(VirtualKeys.Tab);
        var first = k.Showing;
        k.Press(VirtualKeys.Tab);
        Assert.Equal(space.Showing, k.Showing);
        Assert.True(k.Showing != first, "候補が変わる");
        ShiftTab(k);
        Assert.Equal(first, k.Showing, "前の候補に戻る");
    }

    [Test]
    public static void EnterCommitsFirstCandidateWithoutPassingTab()
    {
        var space = new CompositionTests.Keyboard();
        space.Type("kana ");
        space.Press(VirtualKeys.Return);
        var k = new CompositionTests.Keyboard { TabConversion = true };
        k.Type("kana");
        k.Press(VirtualKeys.Tab);
        k.Press(VirtualKeys.Return);
        Assert.Equal(space.Host.Document, k.Host.Document);
        Assert.True(!k.Host.Events.Any(e => e == "down:09"), string.Join("|", k.Host.Events));
    }

    [Test]
    public static void TabInsideSpaceConversion_CommitsAndPassesTab()
    {
        var off = new CompositionTests.Keyboard();
        off.Type("kana ");
        off.Press(VirtualKeys.Tab);
        var k = new CompositionTests.Keyboard { TabConversion = true };
        k.Type("kana ");
        k.Press(VirtualKeys.Tab);
        Assert.Equal(string.Join("|", off.Host.Events), string.Join("|", k.Host.Events));
        Assert.True(k.Host.Events.Contains("down:09"), string.Join("|", k.Host.Events));
    }

    [Test]
    public static void EndingInEnglish_CommitsAndPassesTabWithoutSpace()
    {
        var k = new CompositionTests.Keyboard { TabConversion = true };
        k.Type("hello");
        k.Press(VirtualKeys.Tab);
        Assert.Equal("text:hello|down:09|passed-up:09", string.Join("|", k.Host.Events));
    }

    [Test]
    public static void PredictionTakesTabFirst()
    {
        var k = new CompositionTests.Keyboard(predictions: CompositionTests.OsewaPredictions) { TabConversion = true };
        k.Type("osewa");
        k.Press(VirtualKeys.Tab);
        Assert.True(k.Host.View?.Converting != true, "変換にはならない");
        Assert.Equal(0, k.Host.View!.SelectedPrediction, "予測を選ぶ");
    }

    [Test]
    public static void MisspellingTakesTabFirst()
    {
        var k = new CompositionTests.Keyboard { TabConversion = true };
        k.Type("buresureddo");
        k.Press(VirtualKeys.Tab);
        Assert.Equal("ぶれすれっと", k.Showing);
        Assert.True(k.Host.View?.Converting != true, "変換にはならない");
    }

    [Test]
    public static void EscThenTab_StartsConversionAgain()
    {
        var k = new CompositionTests.Keyboard { TabConversion = true };
        k.Type("kana");
        k.Press(VirtualKeys.Tab);
        k.Press(VirtualKeys.Escape);
        Assert.True(k.Host.View?.Converting != true, "かなに戻る");
        k.Press(VirtualKeys.Tab);
        Assert.True(k.Host.View?.Converting == true, "再び変換が始まる");
        Assert.True(!k.Host.Events.Any(e => e == "down:09"), string.Join("|", k.Host.Events));
    }

    [Test]
    public static void ShiftTabWithoutPrediction_CommitsAndPassesTab()
    {
        var k = new CompositionTests.Keyboard { TabConversion = true };
        k.Type("kana");
        ShiftTab(k);
        Assert.True(k.Host.View?.Converting != true, "変換にはならない");
        Assert.True(k.Host.Events.Contains("down:09"), string.Join("|", k.Host.Events));
    }

    [Test]
    public static void WithTokMode_OneSoundCommitThenTabStillConverts()
    {
        // 1 音確定 (Shift+↓) のあとの残りも、Tab で変換を始められ、続けて Tab で次の候補になる
        var k = new CompositionTests.Keyboard { TabConversion = true, TokMode = true };
        k.Type("kyouha");
        k.Key(VirtualKeys.LShift);
        k.Press(VirtualKeys.Down);
        k.Key(VirtualKeys.LShift, up: true);
        Assert.Equal("きょ", k.Host.Document, "前提: 1 音確定");
        k.Press(VirtualKeys.Tab);
        Assert.True(k.Host.View?.Converting == true, "残りの変換が始まる");
        var first = k.Showing;
        k.Press(VirtualKeys.Tab);
        Assert.True(k.Showing != first || k.Host.View?.Converting == true, "Tab は次の候補");
        Assert.True(!k.Host.Events.Any(e => e == "down:09"), string.Join("|", k.Host.Events));
    }

    [Test]
    public static void ToggleBoxFromF6_TabIsUnchanged_AndEscStillRestoresTheOriginalWord()
    {
        // 確定後の F6 で開いた変換ボックスは、最初から変換中 (Space で始めた変換と同じ扱い)。設定 ON でも OFF と同じく、Tab は確定して Tab を渡す。
        // Tab 変換の状態が残って、Esc 1 回で元の語に戻る動きも壊さない。
        static (string Doc, bool Composing) Run(bool tabConversion, bool tabThenEsc)
        {
            var doc = "";
            SessionResult Press(MeltypeSession session, int vk, char? ch = null)
            {
                var result = session.HandleKey(vk, ch, false, false, false, false, session.IsComposing ? null : (doc.Length > 20 ? doc[^20..] : doc));
                foreach (var edit in result.Commits)
                {
                    if (edit.DeleteBefore > 0) doc = doc[..^edit.DeleteBefore];
                    doc += edit.Text;
                }
                return result;
            }
            var options = new CompositionOptions { Languages = new LanguageMemory(null), TabConversion = () => tabConversion };
            var s = new MeltypeSession(CompositionTests.Detector, new CompositionTests.FakeConverter(), options, () => new Settings());
            foreach (var c in "amazon") Press(s, char.ToUpperInvariant(c), c);
            Press(s, VirtualKeys.Return);
            Assert.Equal("amazon", doc, "前提: 自動で英語として確定");
            Assert.True(Press(s, VirtualKeys.F6).Consumed && s.IsComposing, "前提: 確定後の F6 で変換ボックス");
            if (tabThenEsc)
            {
                // 通常の変換でなく、ボックスの中で Esc 1 回: 元の語に戻る
                Press(s, VirtualKeys.Escape);
                return (doc, s.IsComposing);
            }
            var tab = Press(s, VirtualKeys.Tab);
            return (doc + "|consumed=" + tab.Consumed, s.IsComposing);
        }
        Assert.Equal(Run(false, false), Run(true, false), "Tab: 設定 ON でも OFF と同じ");
        Assert.Equal(Run(false, true), Run(true, true), "Esc: 設定 ON でも OFF と同じ");
        Assert.Equal("amazon", Run(true, true).Doc, "Esc 1 回で元の語に戻る");
    }
}
