#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""2 つの評価結果 (--expect-all の JSON) を比べ、回帰 (broken) があれば終了コード 1 にする。

    python3 tools/eval_compare.py base.json new.json [--md out.md]

考え方は本家 Meltype の .github/bot/pr-checks.mjs の compare() (80-107 行、コミット a4b6f1a) と同じ。
key = file + typed ごとに次の 4 つに分ける。
  broken  … base で ok、new で外れ (回帰)
  fixed   … base で外れ、new で ok
  added   … new だけにある
  removed … base だけにある
終了コード: 0 = 問題なし、1 = broken あり (回帰ゲート)、2 = 入力の誤り (ファイル無し・形式違い)。標準ライブラリだけで動く。

本家のゲートは broken + addedFailing (新しく増えて外れた例も止める)。こちらは broken だけで止める
(同じフォルダーの表を両方で回すなら added は 0 になる)。
key は (file, typed, 何回目か)。同じ打鍵の行を表の前の方に挿入すると、後ろの同じ打鍵の番号がずれて、別の行と比べてしまう。
"""

import argparse
import json
import sys
from collections import Counter, OrderedDict

MAX_LISTED = 30


def load(path):
    try:
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
    except (OSError, ValueError) as e:
        print(f"{path}: 読めません ({e})", file=sys.stderr)
        sys.exit(2)
    if not isinstance(data, list) or not all(isinstance(r, dict) and "typed" in r and "ok" in r for r in data):
        print(f"{path}: --expect-all の出力 (typed・ok を持つ配列) ではありません", file=sys.stderr)
        sys.exit(2)
    return data


def keyed(results):
    """key = (file, typed)。同じ打鍵が同じファイルに複数あれば、2 つ目以降に番号を付けて区別する。"""
    seen = Counter()
    out = OrderedDict()
    for r in results:
        base = (r.get("file", ""), r["typed"])
        seen[base] += 1
        out[(base[0], base[1], seen[base])] = r
    return out


def compare(base, new):
    b, n = keyed(base), keyed(new)
    broken = [(k, n[k], b[k]) for k in n if k in b and b[k]["ok"] and not n[k]["ok"]]
    fixed = [(k, n[k], b[k]) for k in n if k in b and not b[k]["ok"] and n[k]["ok"]]
    added = [(k, n[k], None) for k in n if k not in b]
    removed = [(k, None, b[k]) for k in b if k not in n]
    return b, n, broken, fixed, added, removed


def per_file(b, n):
    files = sorted({k[0] for k in b} | {k[0] for k in n})
    rows = []
    for f in files:
        bt = [r for k, r in b.items() if k[0] == f]
        nt = [r for k, r in n.items() if k[0] == f]
        rows.append((f, sum(r["ok"] for r in bt), len(bt), sum(r["ok"] for r in nt), len(nt)))
    return rows


def pct(ok, total):
    return "-" if total == 0 else f"{ok}/{total} ({ok / total * 100:.1f}%)"


def cell(text):
    return str(text).replace("|", "\\|").replace("\n", " ")


def code(text):
    """打鍵をコード表記にする。バッククォートは ˋ (U+02CB) に置き換えて、コード表記を保つ (本家 pr-checks.mjs と同じ)。"""
    return f"`{cell(text).replace('`', 'ˋ')}`"


def render(b, n, broken, fixed, added, removed, base_label, new_label):
    lines = ["## 正解表の比較", ""]
    lines.append(f"| ファイル | {cell(base_label)} | {cell(new_label)} |")
    lines.append("|---|---|---|")
    rows = per_file(b, n)
    for f, bo, bt, no, nt in rows:
        lines.append(f"| {cell(f)} | {pct(bo, bt)} | {pct(no, nt)} |")
    total = tuple(sum(x) for x in zip(*[r[1:] for r in rows])) if rows else (0, 0, 0, 0)
    lines.append(f"| **合計** | {pct(total[0], total[1])} | {pct(total[2], total[3])} |")
    lines.append("")
    lines.append(f"broken {len(broken)} / fixed {len(fixed)} / added {len(added)} / removed {len(removed)}")
    lines.append("")

    def section(title, items, show_base):
        if not items:
            return
        lines.append(f"### {title} ({len(items)})")
        lines.append("")
        lines.append("| ファイル | 打鍵 | 期待 | 比べ方 | " + ("base の結果 | " if show_base else "") + "new の結果 |")
        lines.append("|---|---|---|---|" + ("---|" if show_base else "") + "---|")
        for k, nr, br in items[:MAX_LISTED]:
            r = nr or br
            row = f"| {cell(k[0])} | {code(k[1])} | {cell(r['expected'])} | {cell(r.get('mode', ''))} | "
            if show_base:
                row += f"{cell(br['actual']) if br else ''} | "
            row += f"{cell(nr['actual']) if nr else '(なし)'} |"
            lines.append(row)
        if len(items) > MAX_LISTED:
            lines.append(f"\n…ほか {len(items) - MAX_LISTED} 件")
        lines.append("")

    section("❌ broken (base で当たり、new で外れた)", broken, True)
    section("✅ fixed (base で外れ、new で当たった)", fixed, True)
    section("➕ added (new だけにある)", added, False)
    section("➖ removed (base だけにある)", removed, False)
    lines.append(f"回帰ゲート: {'❌ broken があります' if broken else '✅ broken 0 件'}")
    return "\n".join(lines) + "\n"


def main(argv):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("base")
    p.add_argument("new")
    p.add_argument("--md", help="Markdown の表を書き出すファイル")
    p.add_argument("--base-label", default="base")
    p.add_argument("--new-label", default="new")
    a = p.parse_args(argv)

    try:
        b, n, broken, fixed, added, removed = compare(load(a.base), load(a.new))
    except KeyError as e:
        print(f"入力の形が違います (キー {e} がありません)", file=sys.stderr)
        return 2
    md = render(b, n, broken, fixed, added, removed, a.base_label, a.new_label)
    if a.md:
        with open(a.md, "w", encoding="utf-8") as f:
            f.write(md)
    sys.stdout.write(md)
    if broken:
        print(f"broken が {len(broken)} 件あります", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
