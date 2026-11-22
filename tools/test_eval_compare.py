#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""tools/eval_compare.py の突き合わせの鍵 (file, typed, 前の文字, 何回目か) と終了コードの自己テスト。python3 tools/test_eval_compare.py"""

import contextlib
import io
import json
import os
import sys
import tempfile
import unittest

sys.dont_write_bytecode = True  # tools/__pycache__ を作らない
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import eval_compare as c  # noqa: E402


def row(typed, ok, before=None, file="a.tsv"):
    r = {"file": file, "typed": typed, "expected": typed, "actual": typed if ok else "x", "mode": "full", "ok": ok}
    if before is not None:
        r["before"] = before
    return r


def run(base, new):
    """base・new の行の一覧を一時ファイルの JSON にして main を呼ぶ。(終了コード, 標準出力の Markdown) を返す。"""
    with tempfile.TemporaryDirectory() as d:
        paths = []
        for name, rows in (("base.json", base), ("new.json", new)):
            p = os.path.join(d, name)
            with open(p, "w", encoding="utf-8") as f:
                json.dump(rows, f)
            paths.append(p)
        out = io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(io.StringIO()):
            code = c.main(paths)
        return code, out.getvalue()


def counts(base, new):
    _, _, broken, fixed, added, removed = c.compare(base, new)
    return len(broken), len(fixed), len(added), len(removed)


class KeyTests(unittest.TestCase):
    def test_different_before_are_separate_rows(self):
        # 同じ打鍵で前の文字だけが違う 2 行。base は両方 ok、new は片方だけ外れる。
        base = [row("sushi", True, "I like "), row("sushi", True, "今日は")]
        new = [row("sushi", False, "I like "), row("sushi", True, "今日は")]
        _, _, broken, fixed, added, removed = c.compare(base, new)
        self.assertEqual([k[2] for k, _, _ in broken], ["I like "])
        self.assertEqual((len(fixed), len(added), len(removed)), (0, 0, 0))
        # 逆向きなら、その行だけが fixed
        self.assertEqual(counts(new, base), (0, 1, 0, 0))

    def test_old_json_without_before_equals_new_json_without_or_empty_before(self):
        old = [row("hello", True)]  # キー before が無い
        for same in ([row("hello", True)], [row("hello", True, "")]):
            self.assertEqual(counts(old, same), (0, 0, 0, 0))
            self.assertEqual(counts(same, old), (0, 0, 0, 0))
        # 外れても、同じ鍵なので added ではなく broken として数える
        self.assertEqual(counts(old, [row("hello", False, "")]), (1, 0, 0, 0))

    def test_repeated_same_key_is_numbered(self):
        base = [row("a", True), row("a", False)]
        new = [row("a", True), row("a", True)]
        self.assertEqual(counts(base, new), (0, 1, 0, 0))

    def test_files_are_separate(self):
        self.assertEqual(counts([row("a", True, file="x.tsv")], [row("a", False, file="y.tsv")]), (0, 0, 1, 1))


class ExitCodeTests(unittest.TestCase):
    def test_added_row_with_before_keeps_exit_zero(self):
        base = [row("sushi", True)]
        new = [row("sushi", True), row("sushi", False, "I like ")]  # 前の文字つきの行が増え、外れていても added
        self.assertEqual(counts(base, new), (0, 0, 1, 0))
        code, md = run(base, new)
        self.assertEqual(code, 0)
        self.assertIn("added 1", md)

    def test_one_broken_exits_one(self):
        code, md = run([row("a", True), row("b", True)], [row("a", True), row("b", False)])
        self.assertEqual(code, 1)
        self.assertIn("broken 1", md)

    def test_no_broken_exits_zero(self):
        self.assertEqual(run([row("a", False)], [row("a", True)])[0], 0)

    def test_broken_by_before_only_exits_one(self):
        code, _ = run([row("s", True, "A"), row("s", True, "B")], [row("s", True, "A"), row("s", False, "B")])
        self.assertEqual(code, 1)


if __name__ == "__main__":
    unittest.main()
