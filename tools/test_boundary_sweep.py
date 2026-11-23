#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""tools/boundary_sweep.py (入力の一覧の作成) と tools/boundary_compare.py (違いの分類) の自己テスト。python3 tools/test_boundary_sweep.py"""

import json
import os
import re
import sys
import tempfile
import unittest

sys.dont_write_bytecode = True  # tools/__pycache__ を作らない
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import boundary_compare as c  # noqa: E402
import boundary_sweep as s  # noqa: E402

REPO = os.path.dirname(HERE)


def read(path):
    with open(path, encoding="utf-8") as f:
        return f.read()


class RomajiTests(unittest.TestCase):
    """かな → ローマ字。src/Meltype.Core.Tests/Jht.cs の最初の打ち方 (ヘボン式) と同じ答えになること。"""

    def test_plain_and_hepburn(self):
        for kana, expected in [
            ("あ", "a"), ("しんぶん", "shinbun"), ("しゃしん", "shashin"), ("じぶんで", "jibunde"),
            ("ふじさん", "fujisan"), ("ちょっと", "chotto"), ("らーめん", "ra-men"), ("ゔぁいおりん", "vaiorin"),
            ("てぃっしゅ", "thisshu"),
        ]:
            self.assertEqual(s.romaji(kana), expected, kana)

    def test_sokuon(self):
        self.assertEqual(s.romaji("がっこう"), "gakkou")
        self.assertEqual(s.romaji("まっちゃ"), "matcha")  # ち系の前は t
        self.assertEqual(s.romaji("あっ"), "axtu")  # 語末は xtu
        self.assertEqual(s.romaji("あっあ"), "axtua")  # 母音の前は xtu
        self.assertEqual(s.romaji("きっぷ"), "kippu")

    def test_n(self):
        self.assertEqual(s.romaji("ほん"), "hon")  # 語末は n
        self.assertEqual(s.romaji("ほんや"), "honnya")  # や行の前は nn
        self.assertEqual(s.romaji("こんにちは"), "konnnichiha")  # な行の前は nn
        self.assertEqual(s.romaji("ほんい"), "honni")  # 母音の前は nn
        self.assertEqual(s.romaji("ほんだ"), "honda")

    def test_unreadable(self):
        self.assertIsNone(s.romaji("漢字"))
        self.assertIsNone(s.romaji("かaな"))
        self.assertIsNone(s.romaji(""))

    def test_joined_at_kana_level(self):
        # 語末の ん の後に母音・や行・な行が来ると nn が要るので、助詞はかなの段階でつなぐ
        self.assertEqual(s.romaji("ほん" + "の"), "honnno")
        self.assertEqual(s.romaji("ほん" + "で"), "honde")
        self.assertEqual(s.romaji("ほん" + "に"), "honnni")
        self.assertNotEqual(s.romaji("ほん") + s.romaji("の"), s.romaji("ほんの"))

    def test_all_readings_in_repo_are_mostly_readable(self):
        words = s.load_readings(os.path.join(REPO, "dictionaries", "readings.txt"))
        self.assertGreater(len(words), 20000)
        unreadable = [w for w in words if s.romaji(w) is None]
        self.assertLess(len(unreadable), 20, unreadable[:5])


class NeighborsTests(unittest.TestCase):
    def test_same_as_romaji_typo_corrector(self):
        source = read(os.path.join(REPO, "src", "Meltype.Core", "Composition", "RomajiTypoCorrector.cs"))
        block = source[source.index("Neighbors = new()"):]
        block = block[:block.index("};")]
        table = dict(re.findall(r"\['([a-z])'\] = \"([a-z]+)\"", block))
        self.assertEqual(len(table), 26)
        self.assertEqual(table, s.NEIGHBORS)


class TypoTests(unittest.TestCase):
    def test_each_kind(self):
        forms = set(s.typos("kore"))
        self.assertIn("kre", forms)  # 1 文字落とし
        self.assertIn("kroe", forms)  # 隣どうしの入れ替え
        self.assertIn("koore", forms)  # 1 文字の重ね打ち
        self.assertTrue(any(len(f) == 5 and f[0] == "k" and f[1] != "k" and f[1] in s.NEIGHBORS["k"] for f in forms))  # 隣のキーの足し
        self.assertNotIn("kore", forms)  # 元の語は含めない

    def test_neighbor_insert_uses_neighbors_and_is_stable(self):
        forms = s.typos("de")
        extra = [f for f in forms if len(f) == 3 and f.startswith("d") and f.endswith("e") and f[1] not in ("d", "e")]
        for f in extra:
            self.assertIn(f[1], s.NEIGHBORS["d"] + s.NEIGHBORS["e"])
        self.assertEqual(s.typos("de"), s.typos("de"))
        self.assertEqual(len(forms), len(set(forms)))

    def test_short_words(self):
        self.assertEqual(s.typos(""), [])
        self.assertNotIn("", s.typos("a"))


class BuildTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.dir = tempfile.mkdtemp()
        with open(os.path.join(cls.dir, "readings.txt"), "w", encoding="utf-8") as f:
            f.write("# コメント\nじぶん\nほん\nたべる\tv1\nあっ\n漢字\nのむ\nかう\n")
        with open(os.path.join(cls.dir, "japanese.txt"), "w", encoding="utf-8") as f:
            f.write("# c\nkonnichiha arigatou # 後ろもコメント\nkonnichiha\n")
        with open(os.path.join(cls.dir, "english.txt"), "w", encoding="utf-8") as f:
            f.write("# c\nhello world\nnode_modules  hello\nbase64\n")

    def test_groups_and_counts(self):
        g, excluded = s.build(self.dir, stride=1)
        self.assertEqual(set(g), {"j1", "j2", "j3", "e"})
        self.assertEqual(excluded, 1)  # 漢字
        singles = set(g["j1"])
        self.assertIn("jibun", singles)
        self.assertIn("konnichiha", singles)
        self.assertIn("jibunde", singles)
        self.assertIn("honnno", singles)
        self.assertIn("honde", singles)
        self.assertEqual(len(g["j1"]), len(set(g["j1"])))  # 重複なし
        self.assertEqual(sorted(g["e"]).count("hello"), 1)
        self.assertIn("hellowo", g["e"])
        self.assertIn("hellodesu", g["e"])
        self.assertIn("node_modulesga", g["e"])
        self.assertIn("base64no", g["e"])

    def test_j3_shape(self):
        g, _ = s.build(self.dir, stride=1)
        j3 = set(g["j3"])
        # じぶん + で + 余計な 1 キー + 続く語 (jibundeftotemo と同じ型。f は t の隣)
        joined = [r for r in s.J3_RIGHTS if "jibunde" + r in j3]  # 余計なキーが無いつなぎ
        self.assertEqual(len(joined), s.J3_N_RIGHTS)
        for right in joined:
            # 余計なキーは右の語の頭の隣のキー (totemo なら t の隣の f)
            self.assertTrue(any("jibunde" + k + right in j3 for k in s.NEIGHBORS[right[0]]), right)
        for t in j3:
            self.assertTrue(all(" " <= ch <= "~" for ch in t))

    def test_deterministic(self):
        a, _ = s.build(self.dir, stride=1)
        b, _ = s.build(self.dir, stride=1)
        self.assertEqual(a, b)

    def test_stride_thins_out(self):
        full, _ = s.build(self.dir, stride=1)
        light, _ = s.build(self.dir, stride=2)
        self.assertLess(len(light["j1"]), len(full["j1"]))
        self.assertTrue(set(light["j1"]) <= set(full["j1"]))

    def test_write_chunks(self):
        g, _ = s.build(self.dir, stride=1)
        out = tempfile.mkdtemp()
        chunks = s.write_chunks(g, out, chunk_size=50)
        self.assertGreater(len(chunks), 1)
        total = 0
        for d in chunks:
            for name in os.listdir(d):
                self.assertRegex(name, r"^(j1|j2|j3|e)-\d+\.tsv$")
                for line in read(os.path.join(d, name)).splitlines():
                    parts = line.split("\t")
                    self.assertEqual(len(parts), 4)
                    self.assertEqual(parts[0], parts[1])
                    self.assertEqual(parts[2], "full")
                    self.assertEqual(parts[3], name.split("-")[0])
                    total += 1
        self.assertEqual(total, sum(len(v) for v in g.values()))


class CompareTests(unittest.TestCase):
    def test_has_latin_and_japanese(self):
        self.assertTrue(c.has_latin("じぶんでfとても"))
        self.assertFalse(c.has_latin("じぶんでとても。"))
        self.assertTrue(c.has_japanese("soreの"))
        self.assertTrue(c.has_japanese("ｱ"))  # 半角カナ (U+FF00〜FFEF)
        self.assertTrue(c.has_japanese("　"))  # 全角空白 (U+3000)
        self.assertFalse(c.has_japanese("hello world"))

    def test_classify(self):
        self.assertEqual(c.classify("j3", "じぶんでとても", "じぶんでfとても"), "jp_loss")
        self.assertEqual(c.classify("j1", "ほん", "ほんn"), "jp_loss")
        self.assertEqual(c.classify("j1", "ほんでf", "ほんで"), "other")  # 英字が減る方は損失ではない
        self.assertEqual(c.classify("j2", "abc", "あbc"), "other")  # base に英字がある
        self.assertEqual(c.classify("e", "undef", "うんでf"), "en_loss")
        self.assertEqual(c.classify("e", "hello", "hello"), None)
        self.assertEqual(c.classify("e", "あhello", "あhelloo"), "other")  # base に日本語がある
        self.assertEqual(c.classify("e", "hello", "helloo"), "other")

    def test_group_from_file(self):
        self.assertEqual(c.group_of("j3-0002.tsv"), "j3")
        self.assertEqual(c.group_of("e-0001.tsv"), "e")

    def _write(self, d, name, rows):
        with open(os.path.join(d, name), "w", encoding="utf-8") as f:
            json.dump([{"file": r[0], "typed": r[1], "actual": r[2], "ok": True} for r in rows], f, ensure_ascii=False)

    def test_run_exit_codes(self):
        base, new, out = tempfile.mkdtemp(), tempfile.mkdtemp(), tempfile.mkdtemp()
        rows = [("j1-0001.tsv", "a", "あ"), ("j3-0001.tsv", "jibundeftotemo", "じぶんでとても"), ("e-0001.tsv", "hello", "hello")]
        self._write(base, "chunk-001.json", rows)
        self._write(new, "chunk-001.json", rows)
        md, tsv = os.path.join(out, "c.md"), os.path.join(out, "d.tsv")
        self.assertEqual(c.run(base, new, md, tsv, None, "b", "n"), 0)
        self.assertEqual(read(tsv).count("\n"), 1)  # 見出しだけ
        rows2 = [rows[0], ("j3-0001.tsv", "jibundeftotemo", "じぶんでfとても"), ("e-0001.tsv", "hello", "ハロー")]
        self._write(new, "chunk-001.json", rows2)
        self.assertEqual(c.run(base, new, md, tsv, None, "b", "n"), 1)
        text = read(md)
        self.assertIn("jibundeftotemo", text)
        self.assertIn("日本語の損失", text)
        self.assertIn("英語の損失", text)
        self.assertIn("jibundeftotemo", read(tsv))

    def test_other_only_is_zero(self):
        base, new, out = tempfile.mkdtemp(), tempfile.mkdtemp(), tempfile.mkdtemp()
        self._write(base, "chunk-001.json", [("j1-0001.tsv", "a", "あ")])
        self._write(new, "chunk-001.json", [("j1-0001.tsv", "a", "ア")])
        self.assertEqual(c.run(base, new, os.path.join(out, "c.md"), os.path.join(out, "d.tsv"), None, "b", "n"), 0)

    def test_mismatch_is_error(self):
        base, new, out = tempfile.mkdtemp(), tempfile.mkdtemp(), tempfile.mkdtemp()
        self._write(base, "chunk-001.json", [("j1-0001.tsv", "a", "あ"), ("j1-0001.tsv", "b", "び")])
        self._write(new, "chunk-001.json", [("j1-0001.tsv", "a", "あ")])
        self.assertEqual(c.run(base, new, os.path.join(out, "c.md"), os.path.join(out, "d.tsv"), None, "b", "n"), 2)
        self.assertEqual(c.run(base, tempfile.mkdtemp(), os.path.join(out, "c.md"), os.path.join(out, "d.tsv"), None, "b", "n"), 2)


if __name__ == "__main__":
    unittest.main()
