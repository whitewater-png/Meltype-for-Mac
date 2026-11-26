#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""要件 049 の道具の自己テスト。python3 tools/test_right_context_items.py
- tools/make-right-context-items.py (文を「左・真ん中・右」に分ける) の分け方と、リポジトリの表が最新か
- tools/ajimee-eval.py の右文脈の列 (無い表は今までと同じ 4 列、ある表は 5 列。--ignore-right-context を IME に渡す)
(Swift・モデルは使わない。IME は、渡された引数と表を見て決まった出力を返す偽のシェルスクリプトで置き換える)"""

import contextlib
import importlib.util
import io
import json
import os
import stat
import sys
import tempfile
import unittest

sys.dont_write_bytecode = True  # tools/__pycache__ を作らない
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)


def load(name: str, file: str):
    spec = importlib.util.spec_from_file_location(name, os.path.join(HERE, file))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


mk = load("make_right_context_items", "make-right-context-items.py")
aj = load("ajimee_eval", "ajimee-eval.py")


class SplitTests(unittest.TestCase):
    def test_first_kanji_bunsetsu_with_something_after_is_the_middle(self):
        self.assertEqual(mk.split("しかくをとるためにべんきょうする", "資格を取るために勉強する"),
                         ("", "しかくを", "資格を", "取るために勉強する"))

    def test_leading_kana_goes_to_the_left(self):
        self.assertEqual(mk.split("このほんはおもしろい", "この本は面白い"), ("この", "ほんは", "本は", "面白い"))

    def test_honorific_prefix_stays_with_the_next_bunsetsu(self):
        self.assertEqual(mk.split("おちゃがあつい", "お茶が熱い"), ("", "おちゃが", "お茶が", "熱い"))

    def test_katakana_in_the_sentence_matches_hiragana_reading(self):
        self.assertEqual(mk.split("かばんをかった", "カバンを買った"), "後ろに文節が残る、かな以外を含む文節が無い")
        self.assertEqual(mk.split("かばんをかってかえる", "鞄を買って帰る"), ("", "かばんを", "鞄を", "買って帰る"))

    def test_ambiguous_alignment_is_skipped(self):
        # 「母は歯は」(ははははは) は、母 = はは・歯 = は と、母 = は・歯 = はは の 2 通りに読めるので使わない
        self.assertEqual(mk.split("ははははは", "母は歯は"), "読みとの対応が 1 通りでない")

    def test_reading_that_does_not_match_is_skipped(self):
        self.assertEqual(mk.split("あめがふる", "雪を降る"), "読みと対応しない")

    def test_last_bunsetsu_only_is_skipped(self):
        self.assertEqual(mk.split("きれいだ", "綺麗だ"), "後ろに文節が残る、かな以外を含む文節が無い")

    def test_alternatives_kept_only_when_left_and_right_are_the_same(self):
        rows = [{"id": "K1", "reading": "しかくのかたちをかいた", "expected": "四角の形を書いた",
                 "alternatives": ["四角の形を描いた", "しかくの形を書いた"], "left": "左", "source": "s"}]
        items, skipped = mk.build(rows)
        self.assertEqual(skipped, [])
        self.assertEqual(items[0]["expected_output"], ["四角の", "しかくの"])
        self.assertEqual(items[0]["context_text"], "左")
        self.assertEqual(items[0]["right_context"], "形を書いた")

    def test_repository_table_is_up_to_date(self):
        rows = mk.load_kanji_cases(mk.KANJI_CASES) + mk.load_homophones(mk.HOMOPHONES)
        items, _ = mk.build(rows)
        with open(mk.DEFAULT_OUT, encoding="utf-8") as handle:
            stored = json.load(handle)
        self.assertEqual(stored, items, "eval/kanji/right-context-items.json が古い。python3 tools/make-right-context-items.py で作り直す")
        self.assertTrue(all(item["right_context"] for item in stored))
        self.assertEqual(len({item["index"] for item in stored}), len(stored))


FAKE = """#!/bin/sh
# 引数を # の行で返し、表の各行を「当たり」として返す
echo "# args $*"
grep -v '^#' "$2" | while IFS='	' read -r id rest; do printf '%s\\t1\\tx\\t1.0\\n' "$id"; done
"""


class AjimeeEvalTests(unittest.TestCase):
    def write_table(self, items):
        with tempfile.TemporaryDirectory() as d:
            path = os.path.join(d, "t.tsv")
            aj.build_table(items, path, 0)
            with open(path, encoding="utf-8") as handle:
                return handle.read()

    def test_table_without_right_context_keeps_four_columns(self):
        text = self.write_table([{"index": 1, "input": "キョウ", "expected_output": ["今日"], "context_text": "左"}])
        self.assertEqual(text, "# id\treading\taccepted\tleft_context\n1\tきょう\t今日\t左\n")

    def test_table_with_right_context_has_fifth_column(self):
        text = self.write_table([{"index": "a", "input": "きょう", "expected_output": ["今日"], "context_text": "", "right_context": "は晴れ"},
                                 {"index": "b", "input": "あめ", "expected_output": ["雨"], "context_text": ""}])
        self.assertEqual(text, "# id\treading\taccepted\tleft_context\tright_context\na\tきょう\t今日\t\tは晴れ\nb\tあめ\t雨\t\t\n")

    def test_ignore_flag_is_passed_to_the_ime(self):
        with tempfile.TemporaryDirectory() as d:
            binary = os.path.join(d, "MeltypeIME")
            with open(binary, "w") as handle:
                handle.write(FAKE)
            os.chmod(binary, os.stat(binary).st_mode | stat.S_IEXEC)
            os.mkdir(os.path.join(d, aj.DICT_BUNDLE))
            table = os.path.join(d, "t.tsv")
            aj.build_table([{"index": "a", "input": "あめ", "expected_output": ["雨"], "right_context": "が降る"}], table, 0)
            for ignore in (False, True):
                err = io.StringIO()
                with contextlib.redirect_stderr(err):
                    results = aj.run_engine(binary, table, 0, ignore_right_context=ignore)
                self.assertEqual([r["id"] for r in results], ["a"])
                self.assertEqual("--ignore-right-context" in err.getvalue(), ignore)


if __name__ == "__main__":
    unittest.main()
