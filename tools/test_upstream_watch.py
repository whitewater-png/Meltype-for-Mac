#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""tools/upstream_watch.py の純粋関数 (git を使わない部分) の自己テスト。python3 tools/test_upstream_watch.py"""

import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import upstream_watch as w  # noqa: E402


class ClassifyTests(unittest.TestCase):
    def test_each_category(self):
        self.assertEqual(w.classify(["src/Meltype.Core/Detection/A.cs"]), ("判定・変換", []))
        self.assertEqual(w.classify(["dictionaries/english.txt"]), ("判定・変換", []))
        self.assertEqual(w.classify(["src/Meltype.Core.Tests/QualityTests.cs"]), ("判定・変換", []))
        self.assertEqual(w.classify(["src/Meltype/App.cs"]), ("Windows", []))
        self.assertEqual(w.classify(["native/x.cpp"]), ("Windows", []))
        self.assertEqual(w.classify(["build.ps1"]), ("Windows", []))
        self.assertEqual(w.classify([".github/workflows/ci.yml"]), ("Windows", []))
        self.assertEqual(w.classify(["mac/Sources/A.swift"]), ("本家 Mac", []))
        self.assertEqual(w.classify(["src/Meltype.Mac.Native/a.c"]), ("本家 Mac", []))
        self.assertEqual(w.classify(["linux/fcitx/a.cpp"]), ("Linux", []))
        self.assertEqual(w.classify(["docs/X.md"]), ("文書", []))
        self.assertEqual(w.classify(["README.md"]), ("文書", []))

    def test_core_wins_and_others_listed(self):
        self.assertEqual(w.classify(["docs/X.md", "mac/a.swift", "src/Meltype.Core/A.cs", "src/Meltype/B.cs"]),
                         ("判定・変換", ["Windows", "本家 Mac", "文書"]))
        self.assertEqual(w.label_of("判定・変換", ["文書"]), "判定・変換 (+文書)")

    def test_core_is_not_confused_with_windows_prefix(self):
        # src/Meltype.Core/ は src/Meltype/ ではない
        self.assertEqual(w.category_of("src/Meltype.Core.Tests/A.cs"), "判定・変換")
        self.assertEqual(w.category_of("src/Meltype/A.cs"), "Windows")

    def test_unknown(self):
        self.assertEqual(w.classify(["LICENSE"]), ("その他", []))
        self.assertEqual(w.classify([]), ("その他", []))


class PrNumberTests(unittest.TestCase):
    def test_merge_subject(self):
        self.assertEqual(w.pr_number("Merge pull request #609 from ayappy-mugicha/fix/538-infobesity"), "609")

    def test_direct_commit_with_number(self):
        self.assertEqual(w.pr_number("fix: ts が崩れる (#129)"), "129")

    def test_first_one_wins_and_none(self):
        self.assertEqual(w.pr_number("Revert #12 of #11"), "12")
        self.assertIsNone(w.pr_number("typo を直す"))


class ExtractCasesTests(unittest.TestCase):
    def diff(self, *lines):
        return "diff --git a/x b/x\n--- a/x\n+++ b/x\n@@ -1 +1 @@\n" + "\n".join(lines) + "\n"

    def test_added_lines_only(self):
        d = self.diff('+        new("日本語", "arigatou", "ありがとう"),',
                      '-        new("日本語", "oldone", "古い"),',
                      '         new("日本語", "ctx", "文脈"),')
        self.assertEqual(w.extract_cases(d), ([("日本語", "arigatou", "ありがとう")], 0))

    def test_named_argument_is_counted_not_listed(self):
        d = self.diff('+        new("短い語", "no", "の", Before: "GitHub"),',
                      '+        new("短い語", "to", "to", Before: "I want "),',
                      '+        new("短い語", "ni", "に"),')
        self.assertEqual(w.extract_cases(d), ([("短い語", "ni", "に")], 2))

    def test_escapes(self):
        d = self.diff(r'+        new("記号", "say \"hi\"", "say \"hi\""),',
                      r'+        new("記号", "C:\\dir", "C:\\dir"),',
                      r'+        new("記号", "a\u0041", "aA"),')
        cases, skipped = w.extract_cases(d)
        self.assertEqual(cases, [("記号", 'say "hi"', 'say "hi"'), ("記号", "C:\\dir", "C:\\dir"), ("記号", "aA", "aA")])
        self.assertEqual(skipped, 0)

    def test_unsupported_escape_and_unrepresentable_are_skipped(self):
        d = self.diff(r'+        new("x", "a\qb", "ab"),',            # 知らないエスケープ
                      r'+        new("x", "a\tb", "ab"),',            # Tab は TSV に入れられない
                      '+        new("x", "かな", "かな"),',            # かなで打つ行
                      '+        new("x", "ok", ""),')                # 期待が空
        self.assertEqual(w.extract_cases(d), ([], 4))

    def test_comment_and_non_case_lines_ignored(self):
        d = self.diff('+        // new("日本語", "commented", "コメント"),',
                      '+        var x = 1;',
                      '+++ b/should-not-count',
                      '+        new("日本語", "trailing", "末尾コメント"), // メモ')
        self.assertEqual(w.extract_cases(d), ([("日本語", "trailing", "末尾コメント")], 0))

    def test_empty(self):
        self.assertEqual(w.extract_cases(""), ([], 0))


class DocTests(unittest.TestCase):
    SECTION = "## 2026-10-14 (a4b6f1a..db433da、2 件)\n\n| 表 |\n"

    def test_parse_and_set_seen(self):
        doc = "<!-- seen: abc1234 -->\n# 題\n"
        self.assertEqual(w.parse_seen(doc), "abc1234")
        self.assertEqual(w.set_seen(doc, "def5678"), "<!-- seen: def5678 -->\n# 題\n")
        self.assertIsNone(w.parse_seen("# 題\n"))
        self.assertEqual(w.set_seen("# 題\n", "def5678"), "<!-- seen: def5678 -->\n# 題\n")

    def test_insert_before_first_section(self):
        doc = "<!-- seen: a -->\n# 題\n説明\n\n## 2026-10-13 (旧)\n\n| 旧 |\n"
        out = w.insert_section(doc, self.SECTION)
        self.assertLess(out.index("2026-10-14"), out.index("2026-10-13"))
        self.assertIn("説明\n\n## 2026-10-14", out)
        self.assertTrue(out.endswith("| 旧 |\n"))
        self.assertEqual(out.count("## "), 2)

    def test_insert_when_no_section(self):
        out = w.insert_section("<!-- seen: a -->\n# 題\n説明\n", self.SECTION)
        self.assertTrue(out.startswith("<!-- seen: a -->\n# 題\n説明\n"))
        self.assertIn("\n## 2026-10-14", out)

    def test_h3_is_not_a_section(self):
        out = w.insert_section("# 題\n### 小見出し\n", self.SECTION)
        self.assertLess(out.index("### 小見出し"), out.index("## 2026-10-14"))

    def test_render_section_escapes_pipes(self):
        row = {"hash": "abc1234", "date": "2026-10-14", "pr": "#1", "subject": "a | b", "category": "文書",
               "imported": "未", "applies": "-", "newcases": "-"}
        text = w.render_section("## H", [row], "候補ファイル: x")
        self.assertIn("a \\| b", text)
        self.assertTrue(text.startswith("## H\n\n| hash |"))
        self.assertIn("候補ファイル: x", text)


if __name__ == "__main__":
    unittest.main(verbosity=1)
