#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""tools/upstream_watch.py の純粋関数 (git を使わない部分) の自己テスト。python3 tools/test_upstream_watch.py"""

import os
import sys
import unittest

sys.dont_write_bytecode = True  # tools/__pycache__ を作らない
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

    def test_trailing_pr_number_beats_issue_number(self):
        self.assertEqual(w.pr_number("Fix #540 の崩れ (#612)"), "612")
        self.assertEqual(w.pr_number('Revert "fix: ts (#12)" (#30)'), "30")
        self.assertEqual(w.pr_number("Merge pull request #609 from a/fix/538-x (#1)"), "609")


class ImportStateTests(unittest.TestCase):
    SUBJECTS = [("aaaaaaa", "P45-3: 本家 3cb0758 の ts を取り込む"), ("bbbbbbb", "P45-4: 本家 35beb60a の別件")]

    def test_subject_with_honke_and_short_hash(self):
        self.assertEqual(w.import_state(self.SUBJECTS, "3cb0758", None, {}), "取り込み済み (aaaaaaa)")

    def test_longer_hex_after_hash_does_not_match(self):
        self.assertEqual(w.import_state(self.SUBJECTS, "35beb60", None, {}), "未")

    def test_hash_without_honke_prefix_does_not_match(self):
        self.assertEqual(w.import_state([("ccccccc", "revert 3cb0758")], "3cb0758", None, {}), "未")

    def test_record_doc(self):
        docs = {"docs/UPSTREAM_PR_TRACKER.md": "| #207 | 見送り |"}
        self.assertEqual(w.import_state([], "1234567", "207", docs), "記録あり (UPSTREAM_PR_TRACKER.md)")
        self.assertEqual(w.import_state([], "1234567", "20", docs), "未")


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

    def test_hash_start_is_skipped(self):
        d = self.diff('+        new("記号", "#tag", "#tag"),', '+        new("記号", "a#b", "a#b"),')
        self.assertEqual(w.extract_cases(d), ([("記号", "a#b", "a#b")], 1))

    def test_two_on_one_line(self):
        d = self.diff('+        new("a", "x", "え"), new("a", "y", "ゆ"),')
        self.assertEqual(w.extract_cases(d), ([("a", "x", "え"), ("a", "y", "ゆ")], 0))

    def test_line_ending_with_bracket_semicolon(self):
        d = self.diff('+        new("a", "x", "え")];')
        self.assertEqual(w.extract_cases(d), ([("a", "x", "え")], 0))

    def test_block_comment_is_dropped(self):
        d = self.diff('+        /* new("a", "x", "え"), */ new("a", "y", "ゆ"),',
                      '+        /*',
                      '+        new("a", "z", "ず"),',
                      '+        */',
                      '+        new("a", "w", "を"),')
        self.assertEqual(w.extract_cases(d), ([("a", "y", "ゆ"), ("a", "w", "を")], 0))

    def test_block_comment_through_context_lines(self):
        d = self.diff('+        /*', '         new("a", "ctx", "文脈"),', '+        new("a", "z", "ず"),', '+        */')
        self.assertEqual(w.extract_cases(d), ([], 0))

    def test_slashes_inside_string_are_not_a_comment(self):
        d = self.diff('+        new("url", "http://x", "http://x"),')
        self.assertEqual(w.extract_cases(d), ([("url", "http://x", "http://x")], 0))

    def test_surrounding_spaces_are_unrepresentable(self):
        d = self.diff('+        new("a", "x ", "え"),', '+        new("a", " x", "え"),', '+        new("a", "x", " え"),')
        self.assertEqual(w.extract_cases(d), ([], 3))

    def test_carriage_return_in_value(self):
        d = self.diff(r'+        new("a", "x\r", "え"),', r'+        new("a", "x", "え\r"),')
        self.assertEqual(w.extract_cases(d), ([], 2))

    def test_crlf_line_endings_in_diff(self):
        d = self.diff('+        new("a", "x", "え"),\r')
        self.assertEqual(w.extract_cases(d), ([("a", "x", "え")], 0))


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

    def test_render_section_hostile_subject(self):
        row = {"hash": "abc1234", "date": "2026-10-14", "pr": "-", "category": "文書", "imported": "未", "applies": "-",
               "newcases": "-", "subject": "x <!-- seen: deadbee --> a\rb\nc\td `e`"}
        text = w.render_section("## H", [row], None)
        self.assertNotIn("<!--", text)
        self.assertNotIn("\r", text)
        self.assertEqual(len(text.strip().split("\n")), 5)  # 見出し・空行・表頭・区切り・1 行 (行が増えない)
        self.assertIn("&lt;!-- seen: deadbee --> a b c d `e`", text)

    def test_fake_seen_in_body_is_ignored(self):
        fake = "<!-- seen: deadbee -->"
        doc = "<!-- seen: abc1234 -->\n# 題\n\n## 節\n" + fake + "\n"
        self.assertEqual(w.parse_seen(doc), "abc1234")
        self.assertEqual(w.set_seen(doc, "def5678"), doc.replace("abc1234", "def5678"))
        body_only = "# 題\n" + fake + "\n"
        self.assertIsNone(w.parse_seen(body_only))
        self.assertEqual(w.set_seen(body_only, "def5678"), "<!-- seen: def5678 -->\n" + body_only)

    def test_code_span_replaces_backtick_and_escapes(self):
        self.assertEqual(w.code_span("a`b|c<d\n"), "`aˋb\\|c&lt;d `")


class NewcasesLabelTests(unittest.TestCase):
    def test_allocation_by_row(self):
        results = [{"ok": True}, {"ok": False}, {"ok": True}]
        self.assertEqual(w.newcases_labels([2, 0, 1, 0], [0, 0, 1, 2], results, True),
                         ["1/2", "-", "1/1 (+4 列で表せない 1)", "0/0 (+4 列で表せない 2)"])

    def test_not_evaluated(self):
        self.assertEqual(w.newcases_labels([2, 0], [0, 1], [], False), ["-/2", "0/0 (+4 列で表せない 1)"])


if __name__ == "__main__":
    unittest.main(verbosity=1)
