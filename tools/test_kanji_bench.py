#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""tools/kanji-bench.py (かな漢字の比較ベンチ。要件 034) の自己テスト。python3 tools/test_kanji_bench.py
(Swift・モデルは使わない。変換のバイナリは、決まった出力を返す偽のシェルスクリプトで置き換える。表の形も確かめる)"""

import contextlib
import importlib.util
import io
import json
import os
import re
import stat
import sys
import tempfile
import unittest

sys.dont_write_bytecode = True  # tools/__pycache__ を作らない
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
spec = importlib.util.spec_from_file_location("kanji_bench", os.path.join(HERE, "kanji-bench.py"))
kb = importlib.util.module_from_spec(spec)
spec.loader.exec_module(kb)

MIXED = os.path.join(ROOT, "eval", "kanji", "bench-mixed.tsv")
HOMO = os.path.join(ROOT, "eval", "kanji", "bench-homophones.tsv")
BASELINE = os.path.join(ROOT, "eval", "kanji", "bench-baseline.json")
DOC = os.path.join(ROOT, "docs", "requirements", "034-kanji-bench-cases.md")


def rows(*specs):
    return [{"id": i, "reading": "よみ", "accepted": a.split("|"), "context": ""} for i, a in specs]


class ParseTests(unittest.TestCase):
    def test_load_table_skips_comments_and_reads_alternatives(self):
        with tempfile.TemporaryDirectory() as d:
            path = os.path.join(d, "t.tsv")
            with open(path, "w", encoding="utf-8") as f:
                f.write("# 見出し\n\nA1\tこの\tこの\t左\nA2\tにんげん\t人間|ニンゲン\t\nA3\t\t空の読み\t\nA4\tよみ\t\t\n")
            got = kb.load_table(path)
        self.assertEqual([r["id"] for r in got], ["A1", "A2"])   # 読みの無い行・正解の無い行は ConvertEval と同じく飛ばす
        self.assertEqual(got[0]["context"], "左")
        self.assertEqual(got[1]["accepted"], ["人間", "ニンゲン"])

    def test_duplicate_ids_stop(self):
        with tempfile.TemporaryDirectory() as d:
            path = os.path.join(d, "t.tsv")
            with open(path, "w", encoding="utf-8") as f:
                f.write("A1\tあ\tあ\t\nA2\tい\tい\t\nA1\tう\tう\t\n")
            with self.assertRaises(SystemExit) as cm:
                kb.load_table(path)
        self.assertIn("id の重複", str(cm.exception))
        self.assertIn("A1", str(cm.exception))

    def test_missing_table_file_stops(self):
        with self.assertRaises(SystemExit) as cm:
            kb.load_table("/nonexistent/none.tsv")
        self.assertIn("表のファイルがありません", str(cm.exception))

    def test_sentence_of(self):
        self.assertEqual(kb.sentence_of("M01_1"), "M01")
        self.assertEqual(kb.sentence_of("Z001"), "Z001")

    def test_parse_engine_output_ignores_summary_and_keeps_tabs_in_top_out(self):
        out = "# summary Zenzai なし\nA1\t1\t今日は\t3.6\nA2\t0\t下\t1.1\n# summary 行数 2\n"
        got = kb.parse_engine_output(out)
        self.assertEqual([(r["id"], r["top"], r["ms"]) for r in got], [("A1", "今日は", 3.6), ("A2", "下", 1.1)])


class ScoreTests(unittest.TestCase):
    def test_strict_vs_lenient_and_sentence_grouping(self):
        table = rows(("M01_0", "今日は"), ("M01_1", "で検索した"), ("M02_0", "二人で|ふたりで"), ("Z001", "革の財布|皮の財布"))
        tops = {"M01_0": "今日は", "M01_1": "で検索した", "M02_0": "ふたりで", "Z001": "皮の財布"}
        s = kb.score(table, tops, {"M01_0": 1.0, "M01_1": 3.0, "M02_0": 2.0, "Z001": 4.0})
        self.assertEqual((s["rows"], s["rows_strict"], s["rows_lenient"]), (4, 2, 4))
        # 文は M01・M02・Z001 の 3 つ。M02 と Z001 は許容でだけ当たり
        self.assertEqual((s["sentences"], s["sentences_strict"], s["sentences_lenient"]), (3, 1, 3))
        self.assertEqual((s["p50_ms"], s["p95_ms"]), (2.0, 4.0))

    def test_one_missed_part_misses_the_sentence(self):
        table = rows(("M01_0", "今日は"), ("M01_1", "で検索した"))
        s = kb.score(table, {"M01_0": "今日は", "M01_1": "出検索した"})
        self.assertEqual((s["rows_lenient"], s["sentences_lenient"]), (1, 0))

    def test_missing_result_counts_as_miss(self):
        s = kb.score(rows(("Z001", "あ")), {})
        self.assertEqual(s["rows_lenient"], 0)


class DiffTests(unittest.TestCase):
    def test_broken_fixed_and_changed_miss(self):
        table = rows(("a", "良"), ("b", "悪"), ("c", "正"), ("d", "正"))
        old = {"a": "良", "b": "x", "c": "x", "d": "正"}
        new = {"a": "y", "b": "悪", "c": "z", "d": "正"}
        d = kb.diff_records(table, old, new)
        self.assertEqual([e["id"] for e in d["broken"]], ["a"])
        self.assertEqual([e["id"] for e in d["fixed"]], ["b"])
        self.assertEqual([e["id"] for e in d["changed_miss"]], ["c"])   # 外れのまま別の外れ
        self.assertEqual(d["common"], 4)

    def test_rescoring_uses_current_table_so_new_alternative_is_not_an_engine_change(self):
        # 前回は表に許容が無く外れ。今の表は許容を足した。エンジンの出力は変わらない → 差に出ない
        d = kb.diff_records(rows(("a", "革|皮")), {"a": "皮"}, {"a": "皮"})
        self.assertEqual((d["broken"], d["fixed"]), ([], []))

    def test_rows_not_in_baseline_are_reported_not_compared(self):
        d = kb.diff_records(rows(("a", "x"), ("b", "y")), {"a": "x"}, {"a": "x", "b": "y"})
        self.assertEqual((d["common"], d["not_in_baseline"]), (1, ["b"]))

    def test_strict_changes_counted_separately(self):
        d = kb.diff_records(rows(("a", "革|皮")), {"a": "革"}, {"a": "皮"})
        self.assertEqual((d["broken"], d["strict_lost"], d["strict_gained"]), ([], 1, 0))


FAKE = """#!/bin/bash
# 偽の MeltypeIME。--convert-eval <tsv> [--weights <gguf>]。FAKE_OFF / FAKE_ON (id=top の Tab 区切りファイル) の通りに返す
table="$2"; mode=OFF; [ "$3" = "--weights" ] && mode=ON
[ -n "$FAKE_FAIL" ] && { echo 失敗; exit 1; }
echo "# summary 偽"
python3 - "$table" "$mode" <<'PY'
import os, sys
table, mode = sys.argv[1], sys.argv[2]
tops = dict(l.rstrip("\\n").split("\\t") for l in open(os.environ["FAKE_" + mode], encoding="utf-8") if "\\t" in l)
skip = int(os.environ.get("FAKE_SKIP", "0"))
rows = [l.split("\\t")[0] for l in open(table, encoding="utf-8") if l.strip() and not l.startswith("#")]
for i in rows[skip:]:
    print(f"{i}\\t0\\t{tops.get(i, 'ー')}\\t2.5")
PY
"""


class EndToEndTests(unittest.TestCase):
    def setUp(self):
        self.d = tempfile.TemporaryDirectory()
        self.addCleanup(self.d.cleanup)
        d = self.d.name
        os.makedirs(os.path.join(d, "bin", kb.DICT_BUNDLE))
        self.binary = os.path.join(d, "bin", "MeltypeIME")
        with open(self.binary, "w") as f:
            f.write(FAKE)
        os.chmod(self.binary, os.stat(self.binary).st_mode | stat.S_IXUSR)
        self.weights = os.path.join(d, "w.gguf")
        open(self.weights, "w").close()
        self.table = os.path.join(d, "mini.tsv")
        with open(self.table, "w", encoding="utf-8") as f:
            f.write("# 表\nM01_0\tきょうは\t今日は\t\nM01_1\tで\tで\t今日は\nZ001\tかわ\t川|河\t\n")

    def tops(self, name, **tops):
        path = os.path.join(self.d.name, name)
        with open(path, "w", encoding="utf-8") as f:
            f.writelines(f"{k}\t{v}\n" for k, v in tops.items())
        return path

    def run_main(self, *extra, env=None):
        out, err = io.StringIO(), io.StringIO()
        saved = dict(os.environ)
        os.environ.update(env or {})
        try:
            with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
                try:
                    code = kb.main(["--binary", self.binary, "--tables", self.table, *extra])
                except SystemExit as e:
                    code = e.code
        finally:
            os.environ.clear()
            os.environ.update(saved)
        return code, out.getvalue(), err.getvalue()

    def test_off_on_save_then_diff_and_fail_on_regression(self):
        same = self.tops("same", M01_0="今日は", M01_1="で", Z001="川")
        record = os.path.join(self.d.name, "rec.json")
        code, out, _ = self.run_main("--weights", self.weights, "--save", record, env={"FAKE_OFF": same, "FAKE_ON": same})
        self.assertEqual(code, 0)
        self.assertIn("OFF (Zenzai なし) | 3/3 (100.0%)", out)
        with open(record, encoding="utf-8") as f:
            saved = json.load(f)
        self.assertEqual(sorted(saved["tables"]["mini"]), ["off", "on"])
        self.assertEqual(saved["tables"]["mini"]["on"]["Z001"]["top"], "川")
        # 同じ出力 → 差なし (--fail-on-regression でも 0)
        code, out, _ = self.run_main("--baseline", record, "--fail-on-regression", env={"FAKE_OFF": same})
        self.assertEqual(code, 0)
        self.assertIn("当たり → 外れ: **0 行**", out)
        # OFF の 1 行が悪化 → 1。行が差の表に出る
        worse = self.tops("worse", M01_0="今日は", M01_1="出", Z001="川")
        code, out, _ = self.run_main("--baseline", record, "--fail-on-regression", env={"FAKE_OFF": worse})
        self.assertEqual(code, 1)
        self.assertIn("当たり → 外れ: **1 行**", out)
        self.assertIn("| M01_1 |", out)
        # --fail-on-regression 無しなら差が出ても 0
        code, _, _ = self.run_main("--baseline", record, env={"FAKE_OFF": worse})
        self.assertEqual(code, 0)

    def record_of(self, **mods):
        """FAKE の出力を --save で記録にして、読み込んで返す (mods で作り替えて baseline にする)。"""
        same = self.tops("same", M01_0="今日は", M01_1="で", Z001="川")
        path = os.path.join(self.d.name, "full.json")
        code, _, _ = self.run_main("--weights", self.weights, "--save", path, env={"FAKE_OFF": same, "FAKE_ON": same})
        self.assertEqual(code, 0)
        with open(path, encoding="utf-8") as f:
            return json.load(f), same

    def write_json(self, name, data):
        path = os.path.join(self.d.name, name)
        with open(path, "w", encoding="utf-8") as f:
            json.dump(data, f)
        return path

    def test_fail_on_regression_without_baseline_is_exit_2(self):
        _, same = self.record_of()
        code, _, err = self.run_main("--fail-on-regression", env={"FAKE_OFF": same})
        self.assertEqual(code, 2)
        self.assertIn("--baseline が無い", err)

    def test_fail_on_regression_with_empty_baseline_or_missing_table_is_exit_2(self):
        _, same = self.record_of()
        for name, data, word in (("empty.json", {}, "空"), ("notables.json", {"version": 1}, "空"),
                                 ("othertable.json", {"tables": {"other": {"off": {}}}}, "この表が無い")):
            code, _, err = self.run_main("--baseline", self.write_json(name, data), "--fail-on-regression", env={"FAKE_OFF": same})
            self.assertEqual(code, 2, name)
            self.assertIn(word, err, name)

    def test_fail_on_regression_when_baseline_has_no_on_but_on_was_measured_is_exit_2(self):
        full, same = self.record_of()
        off_only = {"version": 1, "tables": {"mini": {"off": full["tables"]["mini"]["off"]}}}
        code, _, err = self.run_main("--weights", self.weights, "--baseline", self.write_json("offonly.json", off_only),
                                     "--fail-on-regression", env={"FAKE_OFF": same, "FAKE_ON": same})
        self.assertEqual(code, 2)
        self.assertIn("ON (Zenzai) が無い", err)
        # 同じ baseline でも OFF だけ測るなら比べられる
        code, _, _ = self.run_main("--baseline", self.write_json("offonly.json", off_only), "--fail-on-regression", env={"FAKE_OFF": same})
        self.assertEqual(code, 0)

    def test_fewer_compared_rows_than_table_warns_and_fails_with_flag(self):
        full, same = self.record_of()
        del full["tables"]["mini"]["off"]["Z001"]
        path = self.write_json("short.json", full)
        code, _, err = self.run_main("--baseline", path, env={"FAKE_OFF": same})
        self.assertEqual(code, 0)   # 旗が無ければ警告だけ
        self.assertIn("警告", err)
        self.assertIn("2 / 表 3 行", err)
        code, _, err = self.run_main("--baseline", path, "--fail-on-regression", env={"FAKE_OFF": same})
        self.assertEqual(code, 2)

    def test_missing_baseline_file_and_missing_table_path_are_clear_errors(self):
        _, same = self.record_of()
        code, _, _ = self.run_main("--baseline", os.path.join(self.d.name, "nothing.json"), env={"FAKE_OFF": same})
        self.assertIn("前回の記録がありません", str(code))
        out = io.StringIO()
        with contextlib.redirect_stderr(out), self.assertRaises(SystemExit) as cm:
            kb.main(["--binary", self.binary, "--tables", os.path.join(self.d.name, "nothing.tsv")])
        self.assertIn("表のファイルがありません", str(cm.exception))

    def test_off_only_without_weights(self):
        same = self.tops("same", M01_0="今日は", M01_1="で", Z001="河")
        code, out, _ = self.run_main(env={"FAKE_OFF": same})
        self.assertEqual(code, 0)
        self.assertNotIn("ON (Zenzai)", out)
        self.assertIn("行 許容あり", out)
        self.assertIn("2/3 (66.7%) | 3/3 (100.0%)", out)   # 厳密 2、許容あり 3 (河は許容)

    def test_missing_rows_from_engine_stops(self):
        same = self.tops("same", M01_0="今日は", M01_1="で", Z001="川")
        code, _, _ = self.run_main(env={"FAKE_OFF": same, "FAKE_SKIP": "1"})
        self.assertIn("返ってきた行", str(code))

    def test_engine_failure_stops(self):
        code, _, _ = self.run_main(env={"FAKE_FAIL": "1", "FAKE_OFF": self.tops("x")})
        self.assertIn("終了コード 1", str(code))

    def test_missing_bundle_stops(self):
        os.rmdir(os.path.join(self.d.name, "bin", kb.DICT_BUNDLE))
        code, _, _ = self.run_main(env={"FAKE_OFF": self.tops("x")})
        self.assertIn("辞書のバンドル", str(code))


class CommittedTableTests(unittest.TestCase):
    """リポジトリに入れた表 (要件 034) の形。件数・出典・変えた行の記録を機械で見張る。"""

    @classmethod
    def setUpClass(cls):
        cls.mixed, cls.homo = kb.load_table(MIXED), kb.load_table(HOMO)

    def header(self, path):
        with open(path, encoding="utf-8") as f:
            return "".join(l for l in f if l.startswith("#"))

    def test_counts(self):
        self.assertEqual(len(self.mixed), 73)
        self.assertEqual(len({kb.sentence_of(r["id"]) for r in self.mixed}), 60)
        self.assertEqual(len(self.homo), 120)
        self.assertEqual(sum(r["id"].startswith("Z") for r in self.homo), 80)
        self.assertEqual(sum(r["id"].startswith("H") for r in self.homo), 40)

    def test_ids_unique_and_readings_are_kana(self):
        kana = re.compile(r"^[ぁ-ゖー゛]+$")
        for table in (self.mixed, self.homo):
            ids = [r["id"] for r in table]
            self.assertEqual(len(ids), len(set(ids)))
            for r in table:
                self.assertRegex(r["reading"], kana, r["id"])

    def test_first_alternative_is_never_duplicated_and_nothing_removed(self):
        # 1 つ目 = 原文の期待。許容は足すだけ (正解を減らす方向の変更はしない)。重複と、原文と同じ許容を足した行が無いこと
        for r in self.mixed + self.homo:
            self.assertEqual(len(r["accepted"]), len(set(r["accepted"])), r["id"])

    def test_mixed_context_is_expected_prefix(self):
        # 左文脈 = その部分より前の期待 (英単語を含む)。次の部分の左文脈は、前の左文脈 + 前の部分の期待で始まり、残りは英字だけ
        by_sentence = {}
        for r in self.mixed:
            by_sentence.setdefault(kb.sentence_of(r["id"]), []).append(r)
        for rows_ in by_sentence.values():
            for previous, r in zip(rows_, rows_[1:]):
                head = previous["context"] + previous["accepted"][0]
                self.assertTrue(r["context"].startswith(head), r["id"])
                self.assertRegex(r["context"][len(head):], r"^[A-Za-z0-9]+$", r["id"])
        self.assertEqual(sum(1 for r in self.mixed if r["context"] == ""), 11)   # 文の先頭の日本語の部分だけ空
        for rows_ in by_sentence.values():
            self.assertTrue(all(r["context"] for r in rows_[1:]))

    def test_headers_record_source_license_and_overfitting_warning(self):
        h = self.header(MIXED)
        self.assertIn("yksr-melt/Meltype", h)
        self.assertIn("01672c87", h)
        self.assertIn("GPL-3.0", h)
        h = self.header(HOMO)
        self.assertIn("Serugu/Meltype-Zenzai-Vulkan", h)
        self.assertIn("6973b307", h)
        self.assertIn("GPL-3.0", h)
        self.assertIn("同音異義語に偏", h)
        self.assertIn("過学習", h)

    def test_every_row_with_added_alternatives_is_listed_in_the_doc(self):
        with open(DOC, encoding="utf-8") as f:
            doc = f.read()
        changed = [r for r in self.mixed + self.homo if len(r["accepted"]) > 1]
        self.assertEqual(len(changed), 18)   # 2 + 16。増やしたら文書の表も直す
        for r in changed:
            self.assertIn(f"`{r['id']}`", doc, r["id"])
            for extra in r["accepted"][1:]:
                self.assertIn(extra, doc, f"{r['id']}: {extra}")

    def test_baseline_covers_every_row_in_both_modes(self):
        with open(BASELINE, encoding="utf-8") as f:
            record = json.load(f)
        self.assertEqual(sorted(record["meta"]["modes"]), ["off", "on"])
        for name, table in (("bench-mixed", self.mixed), ("bench-homophones", self.homo)):
            for mode in ("off", "on"):
                self.assertEqual(set(record["tables"][name][mode]), {r["id"] for r in table}, f"{name} {mode}")


if __name__ == "__main__":
    unittest.main()
