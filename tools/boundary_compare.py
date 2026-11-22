#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""境界例の一括比較: 2 つの作業ツリーの結果 (--expect-all の JSON) を、同じ入力どうしで比べて分類する。

    python3 tools/boundary_compare.py <base-dir> <new-dir> --md out.md --tsv out.tsv [--manifest manifest.json]

base-dir・new-dir には、同じ名前の *.json (chunk ごとの --expect-all の結果) が並ぶ。比べるのは actual (打って Enter した結果)。
組は結果の file 列 (j1-0001.tsv の j1) から取る。
  日本語の損失 … j1・j2・j3 の入力で、base の結果は英字を含まず、new の結果は英字を含む
  英語の損失   … e の入力で、base の結果は日本語を含まず、new の結果は日本語を含む
  その他の変化 … 上のどちらでもない違い
終了コード: 損失が 1 件でもあれば 1、無ければ 0 (その他の変化だけなら 0)、入力の誤り (JSON が読めない・base と new で入力が合わない) は 2。
標準ライブラリだけで動く。メモリは chunk 1 つ分 + 違いの先頭だけ。
"""

import argparse
import json
import os
import sys

MAX_LISTED = 20
KINDS = [("jp_loss", "日本語の損失"), ("en_loss", "英語の損失"), ("other", "その他の変化")]


def has_latin(text):
    return any("A" <= ch <= "Z" or "a" <= ch <= "z" for ch in text)


def _japanese(ch):
    # Repro.cs の IsJapanese と同じ範囲: U+3000〜30FF、U+3400〜9FFF、U+FF00〜FFEF
    return "　" <= ch <= "ヿ" or "㐀" <= ch <= "鿿" or "＀" <= ch <= "￯"


def has_japanese(text):
    return any(_japanese(ch) for ch in text)


def group_of(file):
    return os.path.basename(file or "").split("-")[0]


def classify(group, base, new):
    """違いの種類 (jp_loss / en_loss / other)。同じなら None。"""
    if base == new:
        return None
    if group.startswith("j") and not has_latin(base) and has_latin(new):
        return "jp_loss"
    if group == "e" and not has_japanese(base) and has_japanese(new):
        return "en_loss"
    return "other"


def _load(path):
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    if not isinstance(data, list):
        raise ValueError("配列ではありません")
    table = {}
    for r in data:
        key = (group_of(r["file"]), r["typed"])
        table[key] = r["actual"]
    return table, len(data)


def run(base_dir, new_dir, md_path, tsv_path, manifest_path, base_label, new_label):
    try:
        names = sorted(n for n in os.listdir(base_dir) if n.endswith(".json"))
        new_names = sorted(n for n in os.listdir(new_dir) if n.endswith(".json"))
    except OSError as e:
        print(f"読めません ({e})", file=sys.stderr)
        return 2
    if not names or names != new_names:
        print(f"base と new で結果のファイルが合いません (base: {names}、new: {new_names})", file=sys.stderr)
        return 2

    stats = {}  # 組 -> {total, same, jp_loss, en_loss, other}
    listed = {}  # (種類, 組) -> [(typed, base, new)] 先頭だけ
    with open(tsv_path, "w", encoding="utf-8", newline="\n") as tsv:
        tsv.write("kind\tgroup\ttyped\tbase\tnew\n")
        for name in names:
            try:
                b, nb = _load(os.path.join(base_dir, name))
                n, nn = _load(os.path.join(new_dir, name))
            except (OSError, ValueError, KeyError, TypeError) as e:
                print(f"{name}: 読めません ({e})", file=sys.stderr)
                return 2
            if nb != len(b) or nn != len(n) or b.keys() != n.keys():
                print(f"{name}: base と new で入力が合いません (base {nb} 行・new {nn} 行、重複や欠けがあります)", file=sys.stderr)
                return 2
            for (group, typed), base_actual in b.items():
                s = stats.setdefault(group, {"total": 0, "same": 0, "jp_loss": 0, "en_loss": 0, "other": 0})
                s["total"] += 1
                new_actual = n[(group, typed)]
                kind = classify(group, base_actual, new_actual)
                if kind is None:
                    s["same"] += 1
                    continue
                s[kind] += 1
                tsv.write(f"{kind}\t{group}\t{typed}\t{base_actual}\t{new_actual}\n".replace("\r", ""))
                rows = listed.setdefault((kind, group), [])
                if len(rows) < MAX_LISTED:
                    rows.append((typed, base_actual, new_actual))

    manifest = {}
    if manifest_path:
        try:
            with open(manifest_path, encoding="utf-8") as f:
                manifest = json.load(f)
        except (OSError, ValueError):
            manifest = {}
    totals = {k: sum(s[k] for s in stats.values()) for k in ("total", "same", "jp_loss", "en_loss", "other")}
    lines = [f"# 境界例の一括比較: {base_label} → {new_label}", ""]
    if manifest:
        lines += [f"- 規模: {manifest.get('scale')} / 除いた読み (ローマ字にできない): {manifest.get('excluded_readings')} 件", ""]
    lines += ["| 組 | 入力 | 同じ | 日本語の損失 | 英語の損失 | その他の変化 |", "|---|---:|---:|---:|---:|---:|"]
    for group in sorted(stats):
        s = stats[group]
        lines.append(f"| {group} | {s['total']} | {s['same']} | {s['jp_loss']} | {s['en_loss']} | {s['other']} |")
    lines.append(f"| 合計 | {totals['total']} | {totals['same']} | {totals['jp_loss']} | {totals['en_loss']} | {totals['other']} |")
    lines.append("")
    for kind, label in KINDS:
        lines.append(f"## {label} ({totals[kind]} 件)")
        lines.append("")
        if not totals[kind]:
            lines += ["なし", ""]
            continue
        for group in sorted(stats):
            rows = listed.get((kind, group))
            if not rows:
                continue
            lines += [f"### {group} ({stats[group][kind]} 件。先頭 {len(rows)} 件)", "", "| 打鍵 | base | new |", "|---|---|---|"]
            for typed, base_actual, new_actual in rows:
                esc = lambda x: x.replace("|", "\\|").replace("\n", "\\n")  # noqa: E731
                lines.append(f"| `{esc(typed)}` | `{esc(base_actual)}` | `{esc(new_actual)}` |")
            lines.append("")
    lines.append(f"全件は {os.path.basename(tsv_path)} (kind・group・typed・base・new)。")
    with open(md_path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    print(f"入力 {totals['total']} 件: 日本語の損失 {totals['jp_loss']} / 英語の損失 {totals['en_loss']} / その他の変化 {totals['other']}")
    return 1 if totals["jp_loss"] + totals["en_loss"] else 0


def main(argv=None):
    ap = argparse.ArgumentParser(description="境界例の一括比較の分類")
    ap.add_argument("base_dir")
    ap.add_argument("new_dir")
    ap.add_argument("--md", required=True)
    ap.add_argument("--tsv", required=True)
    ap.add_argument("--manifest")
    ap.add_argument("--base-label", default="base")
    ap.add_argument("--new-label", default="new")
    a = ap.parse_args(argv)
    return run(a.base_dir, a.new_dir, a.md, a.tsv, a.manifest, a.base_label, a.new_label)


if __name__ == "__main__":
    sys.exit(main())
