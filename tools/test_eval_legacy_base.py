#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""tools/eval_legacy_base.py の純粋関数 (dotnet を使わない部分) の自己テスト。python3 tools/test_eval_legacy_base.py"""

import os
import sys
import tempfile
import unittest

sys.dont_write_bytecode = True  # tools/__pycache__ を作らない
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import eval_legacy_base as b  # noqa: E402


def parse(text):
    with tempfile.TemporaryDirectory() as d:
        path = os.path.join(d, "t.tsv")
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write(text)
        return b.parse_cases(path)


class ParseCasesTests(unittest.TestCase):
    def test_four_columns(self):
        cases = parse("# 見出し\nhello\thello\tfull\t出典\n")
        self.assertEqual([c["typed"] for c in cases], ["hello"])
        self.assertEqual(cases[0]["mode"], "full")

    def test_five_columns_are_skipped(self):
        cases = parse('hello\thello\tfull\t出典\nsushi\tsushi\tfull\t出典\t"I like "\nsushi\tすし\tfull\t出典\t今日は\n')
        self.assertEqual([c["typed"] for c in cases], ["hello"], "前の文字つきの行は本家の木では回せないので飛ばす")

    def test_five_columns_only_table_is_not_legacy(self):
        # 5 列の行だけの表を古い 2 列の表と見なすと、黙って 0 件になる (Checks.ParseCases と同じ判定にする)
        cases = parse('sushi\tsushi\tfull\t出典\t"I like "\n')
        self.assertEqual(cases, [])

    def test_wrong_column_count_exits(self):
        with self.assertRaises(SystemExit):
            parse("hello\thello\tfull\t出典\nbad\trow\tfull\n")

    def test_two_column_table_still_legacy(self):
        cases = parse("a\tb\n# c\td\n")
        self.assertEqual([c["typed"] for c in cases], ["a", "# c"])


if __name__ == "__main__":
    unittest.main()
