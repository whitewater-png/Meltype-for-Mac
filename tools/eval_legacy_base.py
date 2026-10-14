#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""--expect-all を持たない古い作業ツリー (本家 Meltype の main など) で正解表を回し、--expect-all と同じ形の JSON を作る。

    python3 tools/eval_legacy_base.py <作業ツリー> <正解表のフォルダー> <出力 JSON>
    python3 tools/eval_legacy_base.py --rejudge <--expect-all の JSON>

本家の Core.Tests には --expect (2 列の表だけ) があり、--expect-all と full/ascii/nojp の比べ方は無い。
そこで、表ごとに「打鍵<Tab>期待」の 2 列の表を作って --expect で回し、返ってきた actual (打って Enter で確定した結果) を、
ここで full/ascii/nojp の規則で判定し直す。actual の取り方は本家も Mac 版も同じ (Keyboard で打って Enter、Host.Document)。
判定の規則は src/Meltype.Core.Tests/Repro.cs の Checks.Judge と同じにしてある。
--rejudge は、Mac 版の --expect-all の JSON をこちらの判定で付け直し、C# の ok と食い違う行が無いことを確かめる (規則の食い違いを見つける)。
標準ライブラリだけで動く。
"""

import json
import os
import re
import subprocess
import sys
import tempfile

MODES = ("full", "ascii", "nojp")


def hiragana(s):
    return "".join(chr(ord(c) - 0x60) if "ァ" <= c <= "ヶ" else c for c in s)


def ascii_lower(s):
    return "".join(chr(ord(c) + 32) if "A" <= c <= "Z" else c for c in s)


def old_words(s):
    """2 列の古い表の語 (変更前の Expect と同じ)。"""
    return " ".join(m.lower() for m in re.findall(r"[A-Za-z][A-Za-z'’-]*", s))


def ascii_words(s):
    """印字可能 ASCII の連続を小文字にして空白でつなぐ (Repro.cs の AsciiWords と同じ)。"""
    return " ".join(m.lower() for m in re.findall(r"[!-~]+", s))


def is_japanese(c):
    return "　" <= c <= "ヿ" or "㐀" <= c <= "鿿" or "＀" <= c <= "￯"


def has_kanji(s):
    return any("㐀" <= c <= "鿿" for c in s)


def has_katakana(s):
    return any("ァ" <= c <= "ヶ" for c in s)


def judge(mode, expected, actual):
    if mode == "":
        # 2 列の古い表: 変更前の Expect と同じ
        return old_words(actual) == old_words(expected) if has_kanji(expected) else actual == expected
    if mode == "ascii":
        return ascii_words(actual) == ascii_words(expected)
    if mode == "nojp":
        return not any(is_japanese(c) for c in actual)
    if has_katakana(expected):
        return ascii_lower(hiragana(actual)) == ascii_lower(hiragana(expected))
    return hiragana(actual) == hiragana(expected)


def parse_cases(path):
    """Repro.cs の ParseCases と同じ読み方。# で始まらない空でない行に 4 列の行が 1 つも無ければ変更前と同じ読み方 (# も読み、2 列でない行は捨てる)。"""
    with open(path, encoding="utf-8-sig") as f:
        lines = [l.rstrip("\r") for l in f.read().split("\n")]
    legacy = not any(l.strip() and not l.lstrip().startswith("#") and len(l.split("\t")) == 4 for l in lines)
    cases = []
    for number, line in enumerate(lines, 1):
        if legacy:
            two = line.split("\t")
            if len(two) != 2:
                continue
            typed, expected = two[0].strip(), two[1].strip()
            if typed and expected:
                cases.append({"typed": typed, "expected": expected, "mode": "", "source": ""})
            continue
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        parts = line.split("\t")
        if len(parts) != 4:
            raise SystemExit(f"{path}:{number} 列が {len(parts)} 個です (打鍵・期待・比べ方・出典の 4 列)")
        typed, expected = parts[0].strip(), parts[1].strip()
        if not typed or not expected:
            continue
        mode, source = parts[2].strip(), parts[3].strip()
        if mode not in ("",) + MODES:
            raise SystemExit(f"{path}:{number} 比べ方 '{mode}' が分かりません")
        if mode and any(c > "~" for c in typed):
            raise SystemExit(f"{path}:{number} かなで打つ行に比べ方 '{mode}' は使えません: {typed}")
        if mode == "ascii" and not ascii_words(expected):
            raise SystemExit(f"{path}:{number} ascii の期待に比べる英字・記号の語がありません: {expected}")
        cases.append({"typed": typed, "expected": expected, "mode": mode, "source": source})
    return cases


def run_legacy(tree, cases_dir, out):
    project = os.path.join(tree, "src", "Meltype.Core.Tests")
    if not os.path.isdir(project):
        raise SystemExit(f"{project} がありません")
    results = []
    with tempfile.TemporaryDirectory(prefix="meltype-eval-legacy-") as tmp:
        for name in sorted(n for n in os.listdir(cases_dir) if n.endswith(".tsv")):
            cases = parse_cases(os.path.join(cases_dir, name))
            table = os.path.join(tmp, "in.txt")
            result = os.path.join(tmp, "out.json")
            with open(table, "w", encoding="utf-8", newline="\n") as f:
                for c in cases:
                    f.write(f"{c['typed']}\t{c['expected']}\n")
            subprocess.run(
                ["dotnet", "run", "-c", "Release", "--project", os.path.join("src", "Meltype.Core.Tests"), "--", "--expect", table, result],
                # global.json・nuget.config は実行したフォルダーから探されるので、作業ツリーの中で動かす。
                # --project は相対パスで渡す (/tmp のようにシンボリックリンクを通る絶対パスだと、参照アセンブリが見つからずビルドが失敗した)
                check=True, stdout=subprocess.DEVNULL, cwd=tree,
            )
            with open(result, encoding="utf-8") as f:
                got = json.load(f)
            if len(got) != len(cases):
                raise SystemExit(f"{name}: 件数が合いません (表 {len(cases)} 件、結果 {len(got)} 件)")
            for c, g in zip(cases, got):
                if g["typed"] != c["typed"]:
                    raise SystemExit(f"{name}: 並びが合いません: {c['typed']!r} / {g['typed']!r}")
                mode = c["mode"]
                results.append({
                    "file": name, "typed": c["typed"], "expected": c["expected"], "mode": mode,
                    "source": c["source"], "actual": g["actual"],
                    # かなで打つ行 (候補を見る行) は本家が判定した ok をそのまま使う
                    "ok": g["ok"] if g.get("kind") == "候補" else judge(mode, c["expected"], g["actual"]),
                })
    with open(out, "w", encoding="utf-8") as f:
        json.dump(results, f, ensure_ascii=False, indent=2)


def rejudge(path):
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    # 候補の行 (かなで打つ) は actual が候補の並びなので、判定し直さない
    bad = [r for r in data if r.get("kind") != "候補" and judge(r["mode"], r["expected"], r["actual"]) != r["ok"]]
    for r in bad:
        print("食い違い:", r["file"], repr(r["typed"]), r["mode"], repr(r["expected"]), repr(r["actual"]), r["ok"])
    print(f"{len(data)} 件を判定し直し、食い違い {len(bad)} 件")
    return 1 if bad else 0


if __name__ == "__main__":
    if len(sys.argv) == 3 and sys.argv[1] == "--rejudge":
        sys.exit(rejudge(sys.argv[2]))
    if len(sys.argv) != 4:
        raise SystemExit(__doc__)
    run_legacy(sys.argv[1], sys.argv[2], sys.argv[3])
