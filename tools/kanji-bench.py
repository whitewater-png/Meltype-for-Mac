#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""かな → 漢字の比較ベンチ表 (要件 034) を、変更ごとに Zenzai OFF / ON で測り、前回の記録との差を出す。

    python3 tools/kanji-bench.py --binary mac/.build/release/MeltypeIME [--weights <gguf>] \\
        [--tables eval/kanji/bench-mixed.tsv eval/kanji/bench-homophones.tsv] \\
        [--baseline eval/kanji/bench-baseline.json] [--save <記録.json>] [--out <dir>] [--fail-on-regression]

- 表は `MeltypeIME --convert-eval` が読む 4 列 (id / 読み / 正解 (`|` 区切りで複数許容) / 左文脈)。既定は
  `eval/kanji/bench-mixed.tsv` (日英混在 60 文の日本語の部分 73 行) と `eval/kanji/bench-homophones.tsv` (かな漢字 120 文)。
- OFF (Zenzai なし = 製品の既定) は必ず測る。`--weights` を渡したときだけ ON も測る (モデルは `--convert-eval` が検査する。通信しない)。
- 数え方は 2 通り: 厳密一致 = 第一候補が正解の 1 つ目 (原文の期待) と同じ / 許容あり = 第一候補が `|` で足した許容のどれか。
  文の単位も出す (日英混在の行は `M01_0`・`M01_1` のように同じ文の部分なので、文の当たり = 部分がすべて当たり)。
- `--baseline` を渡すと、前回の記録 (`--save` で書いたもの) と行ごとに突き合わせ、当たり → 外れ・外れ → 当たりの行を出す。
  突き合わせは「記録した第一候補」を **今の表** で採点し直して行う (表の許容を足した分は、エンジンの変化に数えない)。
- `--fail-on-regression` を付けると、当たり → 外れが 1 行でもあれば終了コード 1 (OFF・ON どちらも見る)。
- 速さ (p50 / p95) は測った機械の値。記録に機械の名前を残す。別の機械の記録とは比べない。

注意 (過学習): `bench-homophones.tsv` は Zenzai の評価用の表で、同音異義語に偏っている。この表の外れを直すために辞書や規則を足すと、
表だけ良くなる。表の外の文 (eval/kanji/kanji-cases.tsv、tools/ajimee-eval.py) でも悪化していないことを必ず併せて確かめる。

標準ライブラリだけで動く。
"""

import argparse
import json
import math
import os
import platform
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_TABLES = [os.path.join(ROOT, "eval", "kanji", "bench-mixed.tsv"),
                  os.path.join(ROOT, "eval", "kanji", "bench-homophones.tsv")]
DICT_BUNDLE = "AzooKeyKanaKanjiConverter_KanaKanjiConverterModuleWithDefaultDictionary.bundle"
MODES = ("off", "on")
MODE_LABEL = {"off": "OFF (Zenzai なし)", "on": "ON (Zenzai)"}


def load_table(path: str) -> list:
    """4 列の表を読む。`#` の行と空行は読まない (`ConvertEval.parse` と同じ)。正解の 1 つ目 = 原文の期待。"""
    rows = []
    with open(path, encoding="utf-8") as handle:
        for line in handle.read().split("\n"):
            if not line or line.startswith("#"):
                continue
            columns = line.split("\t")
            if len(columns) < 3 or not columns[1]:
                continue
            accepted = [text for text in columns[2].split("|") if text]
            if not accepted:
                continue
            rows.append({"id": columns[0], "reading": columns[1], "accepted": accepted,
                         "context": columns[3] if len(columns) >= 4 else ""})
    return rows


def sentence_of(row_id: str) -> str:
    """`M01_1` → `M01` (日英混在の行は 1 文を英単語で切った部分)。`Z001` はそのまま。"""
    return row_id.split("_", 1)[0]


def parse_engine_output(stdout: str) -> list:
    """`--convert-eval` の標準出力から `id<TAB>hit<TAB>top<TAB>ms` の行を取る (`#` の行は読まない)。"""
    results = []
    for line in stdout.split("\n"):   # splitlines() は U+001C〜U+001E などでも切るので使わない
        if not line or line.startswith("#"):
            continue
        columns = line.split("\t")
        if len(columns) < 4:
            continue
        try:
            results.append({"id": columns[0], "top": columns[2], "ms": float(columns[3])})
        except ValueError:
            continue
    return results


def run_engine(binary: str, table: str, weights=None) -> list:
    if not os.path.exists(binary):
        raise SystemExit(f"バイナリがありません: {binary}")
    bundle = os.path.join(os.path.dirname(os.path.abspath(binary)), DICT_BUNDLE)
    if not os.path.isdir(bundle):
        raise SystemExit(f"辞書のバンドルがバイナリと同じフォルダーにありません: {bundle}\n  (swift build の出力フォルダーのバイナリを渡してください)")
    command = [binary, "--convert-eval", table]
    if weights:
        if not os.path.exists(weights):
            raise SystemExit(f"Zenzai のモデルがありません: {weights}")
        command += ["--weights", weights]
    done = subprocess.run(command, capture_output=True, text=True)
    if done.returncode != 0:
        print(done.stdout[-2000:], file=sys.stderr)
        print(done.stderr[-2000:], file=sys.stderr)
        raise SystemExit(f"IME が終了コード {done.returncode} で終わりました ({'ON' if weights else 'OFF'}、{table})")
    return parse_engine_output(done.stdout)


def percentile(values: list, ratio: float) -> float:
    """nearest-rank (⌈q·n⌉ 番目)。tools/ajimee-eval.py と同じ。"""
    values = sorted(values)
    return values[max(0, math.ceil(round(len(values) * ratio, 9)) - 1)]


def judge(row: dict, top: str) -> tuple:
    """(許容ありの当たり, 厳密一致の当たり)。"""
    return top in row["accepted"], top == row["accepted"][0]


def score(rows: list, tops: dict, ms: "dict | None" = None) -> dict:
    """表の行と、行ごとの第一候補 `tops` から、行の単位と文の単位の件数を数える。tops に無い行は外れとして数える。"""
    sentences = {}
    row_lenient = row_strict = 0
    for row in rows:
        lenient, strict = judge(row, tops.get(row["id"], ""))
        row_lenient += lenient
        row_strict += strict
        s = sentences.setdefault(sentence_of(row["id"]), [True, True])
        s[0] = s[0] and lenient
        s[1] = s[1] and strict
    out = {"rows": len(rows), "rows_lenient": row_lenient, "rows_strict": row_strict,
           "sentences": len(sentences),
           "sentences_lenient": sum(1 for s in sentences.values() if s[0]),
           "sentences_strict": sum(1 for s in sentences.values() if s[1])}
    times = [ms[row["id"]] for row in rows if ms and row["id"] in ms]
    if times:
        out["p50_ms"] = percentile(times, 0.5)
        out["p95_ms"] = percentile(times, 0.95)
    return out


def diff_records(rows: list, old_tops: dict, new_tops: dict) -> dict:
    """同じ表で、前回と今回の第一候補を **今の表** で採点し直して突き合わせる。共通の行だけ。"""
    broken, fixed, changed_miss = [], [], []
    strict_lost = strict_gained = 0
    for row in rows:
        if row["id"] not in old_tops or row["id"] not in new_tops:
            continue
        old_l, old_s = judge(row, old_tops[row["id"]])
        new_l, new_s = judge(row, new_tops[row["id"]])
        entry = {"id": row["id"], "reading": row["reading"], "accepted": row["accepted"],
                 "old": old_tops[row["id"]], "new": new_tops[row["id"]]}
        if old_l and not new_l:
            broken.append(entry)
        elif new_l and not old_l:
            fixed.append(entry)
        elif not old_l and not new_l and old_tops[row["id"]] != new_tops[row["id"]]:
            changed_miss.append(entry)   # 外れのまま、別の外れに変わった行 (数は変えないが、見ておく)
        strict_lost += old_s and not new_s
        strict_gained += new_s and not old_s
    common = [row["id"] for row in rows if row["id"] in old_tops and row["id"] in new_tops]
    missing = [row["id"] for row in rows if row["id"] not in old_tops]
    return {"broken": broken, "fixed": fixed, "changed_miss": changed_miss,
            "strict_lost": strict_lost, "strict_gained": strict_gained,
            "common": len(common), "not_in_baseline": missing}


def machine_name() -> str:
    try:
        done = subprocess.run(["sysctl", "-n", "machdep.cpu.brand_string"], capture_output=True, text=True, timeout=5)
        if done.returncode == 0 and done.stdout.strip():
            return done.stdout.strip()
    except (OSError, subprocess.SubprocessError):
        pass
    return platform.processor() or platform.machine()


def pct(n: int, total: int) -> str:
    return f"{n}/{total} ({n * 100 / total:.1f}%)" if total else "0 件"


def table_name(path: str) -> str:
    return os.path.splitext(os.path.basename(path))[0]


def render(record: dict, tables: dict, baseline=None) -> tuple:
    """Markdown と、当たり → 外れの件数を返す。"""
    meta = record["meta"]
    md = ["# かな漢字の比較ベンチ (要件 034)", "",
          f"- 対象: `{meta.get('binary', '')}`" + (f" / 木の版: `{meta['revision']}`" if meta.get("revision") else ""),
          f"- 機械: {meta.get('machine', '?')} (速さはこの機械の値。別の機械・M1 級では未確認)",
          f"- Zenzai ON: {'あり (--weights)' if 'on' in meta.get('modes', []) else 'なし (OFF だけ測った)'}", ""]
    total_broken = 0
    for name, data in record["tables"].items():
        rows = tables[name]
        md += [f"## {name}", "",
               "| 設定 | 行 厳密一致 | 行 許容あり | 文 厳密一致 | 文 許容あり | p50 / p95 |", "|---|---|---|---|---|---|"]
        scores = {}
        for mode in MODES:
            if mode not in data:
                continue
            tops = {k: v["top"] for k, v in data[mode].items()}
            ms = {k: v["ms"] for k, v in data[mode].items()}
            s = scores[mode] = score(rows, tops, ms)
            speed = f"{s['p50_ms']:.1f} / {s['p95_ms']:.1f} ms" if "p50_ms" in s else "-"
            md.append(f"| {MODE_LABEL[mode]} | {pct(s['rows_strict'], s['rows'])} | {pct(s['rows_lenient'], s['rows'])} | "
                      f"{pct(s['sentences_strict'], s['sentences'])} | {pct(s['sentences_lenient'], s['sentences'])} | {speed} |")
        md.append("")
        if "off" in data and "on" in data:
            off_tops = {k: v["top"] for k, v in data["off"].items()}
            on_tops = {k: v["top"] for k, v in data["on"].items()}
            d = diff_records(rows, off_tops, on_tops)
            md += [f"OFF → ON (許容あり、行): 外れ → 当たり {len(d['fixed'])} 行 / 当たり → 外れ {len(d['broken'])} 行", ""]
        if baseline and name in baseline["tables"]:
            for mode in MODES:
                if mode not in data or mode not in baseline["tables"][name]:
                    continue
                old_tops = {k: v["top"] for k, v in baseline["tables"][name][mode].items()}
                new_tops = {k: v["top"] for k, v in data[mode].items()}
                d = diff_records(rows, old_tops, new_tops)
                total_broken += len(d["broken"])
                md += [f"### 前回の記録との差 — {MODE_LABEL[mode]} ({d['common']} 行を突き合わせ)", "",
                       f"- 当たり → 外れ: **{len(d['broken'])} 行** / 外れ → 当たり: {len(d['fixed'])} 行 / 外れのまま別の第一候補: {len(d['changed_miss'])} 行"
                       f" (厳密一致の増減: +{d['strict_gained']} / -{d['strict_lost']})"]
                if d["not_in_baseline"]:
                    md.append(f"- 前回の記録に無い行: {len(d['not_in_baseline'])} 行 (突き合わせに入れていない): {', '.join(d['not_in_baseline'][:10])}")
                for title, key in (("当たり → 外れ", "broken"), ("外れ → 当たり", "fixed")):
                    if d[key]:
                        md += ["", f"{title}:", "", "| id | 読み | 前回 | 今回 | 正解 |", "|---|---|---|---|---|"]
                        for e in d[key]:
                            md.append(f"| {e['id']} | `{e['reading'][:24]}` | `{e['old'][:24]}` | `{e['new'][:24]}` | `{'｜'.join(e['accepted'])[:40]}` |")
                md.append("")
        if "off" in scores:
            misses = [row for row in rows if not judge(row, {k: v["top"] for k, v in data["off"].items()}.get(row["id"], ""))[0]]
            md += [f"OFF で外れの行 ({len(misses)} 行): " + (", ".join(row["id"] for row in misses) or "なし"), ""]
        if "on" in scores:
            misses = [row for row in rows if not judge(row, {k: v["top"] for k, v in data["on"].items()}.get(row["id"], ""))[0]]
            md += [f"ON で外れの行 ({len(misses)} 行): " + (", ".join(row["id"] for row in misses) or "なし"), ""]
    md += ["---", "注意: `bench-homophones` は Zenzai の評価用で同音異義語に偏る。この表に合わせた辞書・規則の追加は過学習になる。"
           "表の外の文 (eval/kanji/kanji-cases.tsv・tools/ajimee-eval.py) でも悪化していないことを併せて確かめる。"]
    return "\n".join(md) + "\n", total_broken


def revision() -> str:
    try:
        done = subprocess.run(["git", "-C", ROOT, "rev-parse", "--short", "HEAD"], capture_output=True, text=True, timeout=10)
        return done.stdout.strip() if done.returncode == 0 else ""
    except (OSError, subprocess.SubprocessError):
        return ""


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description="かな漢字の比較ベンチ表を Zenzai OFF/ON で測り、前回の記録との差を出す")
    parser.add_argument("--binary", required=True, help="MeltypeIME のバイナリ (swift build の出力。辞書バンドルが同じフォルダーに必要)")
    parser.add_argument("--tables", nargs="+", default=DEFAULT_TABLES, help="測る表 (既定: eval/kanji/bench-mixed.tsv と bench-homophones.tsv)")
    parser.add_argument("--weights", default=None, help="Zenzai の GGUF。指定すると ON も測る (指定しない = OFF だけ)")
    parser.add_argument("--baseline", default=None, help="前回の記録 (--save で書いた JSON)。指定すると行ごとの差を出す")
    parser.add_argument("--save", default=None, help="今回の記録を JSON で書く (次回の --baseline に使う)")
    parser.add_argument("--out", default=None, help="Markdown の報告 (report.md) の出力フォルダー")
    parser.add_argument("--fail-on-regression", action="store_true", help="前回の記録より当たり → 外れが 1 行でもあれば終了コード 1")
    args = parser.parse_args(argv)

    tables = {table_name(path): load_table(path) for path in args.tables}
    if any(not rows for rows in tables.values()):
        raise SystemExit("行が 1 つも無い表があります")
    modes = ["off"] + (["on"] if args.weights else [])
    record = {"version": 1,
              "meta": {"binary": args.binary, "machine": machine_name(), "revision": revision(), "modes": modes},
              "tables": {}}
    for path in args.tables:
        name = table_name(path)
        record["tables"][name] = {}
        for mode in modes:
            results = run_engine(args.binary, path, args.weights if mode == "on" else None)
            returned = {r["id"] for r in results}
            absent = [row["id"] for row in tables[name] if row["id"] not in returned]
            if absent or len(results) != len(tables[name]):
                raise SystemExit(f"{name} ({mode}): 返ってきた行 {len(results)} / 表 {len(tables[name])}。足りない id: {absent[:5]}")
            record["tables"][name][mode] = {r["id"]: {"top": r["top"], "ms": r["ms"]} for r in results}
    baseline = None
    if args.baseline:
        with open(args.baseline, encoding="utf-8") as handle:
            baseline = json.load(handle)
    markdown, broken = render(record, tables, baseline)
    print(markdown)
    if args.save:
        os.makedirs(os.path.dirname(os.path.abspath(args.save)), exist_ok=True)
        with open(args.save, "w", encoding="utf-8") as handle:
            json.dump(record, handle, ensure_ascii=False, indent=1, sort_keys=True)
            handle.write("\n")
        print(f"記録を書きました: {args.save}", file=sys.stderr)
    if args.out:
        os.makedirs(args.out, exist_ok=True)
        with open(os.path.join(args.out, "report.md"), "w", encoding="utf-8") as handle:
            handle.write(markdown)
    if args.fail_on_regression and baseline and broken:
        print(f"当たり → 外れが {broken} 行あります", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
