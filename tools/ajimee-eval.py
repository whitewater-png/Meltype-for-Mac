#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""AJIMEE-Bench (外部の中立の正解表) で、Mac 版の **かな → 漢字変換** を測る。

    python3 tools/ajimee-eval.py --binary <MeltypeIME のバイナリ> [--items <json>] [--out <dir>] [--limit N] [--weights <gguf>] [--top N]
    python3 tools/ajimee-eval.py --compare <rows.tsv> <rows.tsv> --out <dir>   # 2 回の結果を行ごとに突き合わせる (McNemar)

右文脈 (要件 049): 正解表の項目に `right_context` (カーソルの後ろの文字) があれば、`--convert-eval` の表に 5 列目として書く
(Zenzai ON のときだけ効く)。項目に `source` があれば、結果を出どころごとにも出す。`--ignore-right-context` を付けると、
同じ表のまま右文脈を使わずに測る (「なし」の基準)。右文脈つきの表は `tools/make-right-context-items.py` が作る。
AJIMEE の表には右文脈が無いので、IME に渡す表と数字は今までと同じ (rows.tsv の後ろに 2 列、results.json の行に欄が増えるだけ)。

AJIMEE-Bench (Advanced Japanese IME Evaluation Benchmark / 味見ベンチ)
  https://github.com/azooKey/AJIMEE-Bench
  - `JWTD_v2/v1/evaluation_items.json` … 200 件。うち 100 件が `context_text` (左文脈) 付き
  - `input` はカタカナ表記の読み、`expected_output` は許容解を複数列挙 (平均 2.1 個)
  - 評価式は本リポジトリの `utils.py` (CC0 1.0) と同じ: acc@1 = 第一候補が許容解に含まれるか、MinCER = 許容解のうちの最小の文字誤り率

`eval/cases` が測る **日英判定** とは軸が違う。この表は **かな → 漢字の変換だけ**を測るので、
`eval/cases` (839 件) と `--eval` の代わりにではなく **並べて**使う。

データの扱い: 正解表のデータは **CC-BY-SA 3.0** (元データの日本語 Wikipedia 入力誤りデータセット v2 に準拠)。
なのでリポジトリには同梱せず、**実行時に取ってキャッシュフォルダーに置く** (既定 `~/.cache/meltype/ajimee`)。
評価の式は CC0 なので、このファイルに書き写してよい。

標準ライブラリだけで動く (Levenshtein は自前で計算する。`python-Levenshtein` は要らない)。
"""

import argparse
import collections
import json
import math
import os
import sys
import urllib.request

ITEMS_URL = "https://raw.githubusercontent.com/azooKey/AJIMEE-Bench/main/JWTD_v2/v1/evaluation_items.json"
DEFAULT_CACHE = os.path.expanduser("~/.cache/meltype/ajimee")
DICT_BUNDLE = "AzooKeyKanaKanjiConverter_KanaKanjiConverterModuleWithDefaultDictionary.bundle"


def fetch_items(path: str, no_download: bool) -> str:
    """正解表の JSON を取ってパスを返す。既にあれば取り直さない。"""
    if os.path.exists(path):
        return path
    if no_download:
        raise SystemExit(f"--no-download で、正解表がありません: {path}\n  (先に手動で置いてください。URL: {ITEMS_URL})")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    print(f"正解表を取得します: {ITEMS_URL}", file=sys.stderr)
    with urllib.request.urlopen(ITEMS_URL, timeout=60) as response:
        data = response.read()
    with open(path, "wb") as handle:
        handle.write(data)
    print(f"  保存: {path} ({len(data)} バイト)。CC-BY-SA 3.0 のデータなので、リポジトリには入れないでください。", file=sys.stderr)
    return path


def katakana_to_hiragana(text: str) -> str:
    """AJIMEE の `input` はカタカナ表記。変換エンジンに渡す読みはひらがなに直す。
    カタカナ (U+30A1..U+30FF) はひらがな (U+3041..) と **0x60 離れている**ので -0x60 で寄せる
    (0x20 ではない。0x20 は半角カタカナとの差で、この表は 0x60。セ U+30BB → せ U+305B)。濁音・半濁音も同じ差で対応する。
    長音符 `ー` (U+30FC) はひらがなの文でも使うのでそのまま (`-0x60` すると U+309C = 半濁音記号 `゜` になってしまう)。
    中音符 `・` (U+30FB) も同じで、そのままにする (`-0x60` すると U+309B = 濁音記号 `゛` になり、期待値の `・` と食い違う。実測で Zenzai ON で 7 件 / なしで 3 件がこれで落ちていた)。
    `ヴ` (U+30F4) はひらがな `ゔ` (U+3094) が辞書に無いので `う゛` に寄せる。
    """
    out = []
    for ch in text:
        code = ord(ch)
        if ch == "ヴ":
            out.append("う゛")
        elif ch == "ー" or ch == "・":
            out.append(ch)
        elif 0x30A1 <= code <= 0x30FF:
            out.append(chr(code - 0x60))
        else:
            out.append(ch)
    return "".join(out)


def levenshtein(a: str, b: str) -> int:
    """文字編集距離 (Levenshtein)。`utils.py` が使っている python-Levenshtein と同じ定義。標準ライブラリだけで計算する。"""
    if a == b:
        return 0
    if not a:
        return len(b)
    if not b:
        return len(a)
    previous = list(range(len(b) + 1))
    for i, ca in enumerate(a, start=1):
        current = [i]
        for j, cb in enumerate(b, start=1):
            current.append(min(previous[j] + 1, current[j - 1] + 1, previous[j - 1] + (ca != cb)))
        previous = current
    return previous[-1]


def min_cer(references: list, hypothesis: str) -> float:
    """`utils.py` の calculate_MinCER と同じ (許容解のうち最小の CER。CER = 編集距離 / 正解の長さ)。"""
    return min(levenshtein(reference, hypothesis) / len(reference) for reference in references if reference)


def build_table(items: list, tsv_path: str, limit: int) -> int:
    """`MeltypeIME --convert-eval` が読む表を書く: id / 読み / 許容解 (`|` 区切り) / 左文脈 (/ 右文脈)。
    右文脈の列は、項目のどれかに `right_context` があるときだけ足す (無い表は今までと同じ 4 列)。"""
    rows = items[:limit] if limit else items
    with_right = any(item.get("right_context") for item in rows)
    with open(tsv_path, "w", encoding="utf-8") as handle:
        handle.write("# id\treading\taccepted\tleft_context" + ("\tright_context" if with_right else "") + "\n")
        for item in rows:
            reading = katakana_to_hiragana(item["input"])
            accepted = "|".join(item["expected_output"])
            context = item.get("context_text") or ""
            line = f"{item['index']}\t{reading}\t{accepted}\t{context}"
            if with_right:
                line += "\t" + (item.get("right_context") or "")
            handle.write(line + "\n")
    return len(rows)


def run_engine(binary: str, tsv_path: str, limit: int, weights=None, top: int = 0, ignore_right_context: bool = False) -> list:
    """IME のバイナリを `--convert-eval` で走らせ、行ごとの `id hit top ms` を取り出す。"""
    if not os.path.exists(binary):
        raise SystemExit(f"バイナリがありません: {binary}")
    # 変換の辞書は、SwiftPM がバイナリと同じフォルダーに置くバンドルから読む (mac/Sources/MeltypeIME/Converter.swift の resource())
    bundle = os.path.join(os.path.dirname(os.path.abspath(binary)), DICT_BUNDLE)
    if not os.path.isdir(bundle):
        raise SystemExit(f"辞書のバンドルがバイナリと同じフォルダーにありません: {bundle}\n  (swift build の出力フォルダーのバイナリを渡してください)")
    command = [binary, "--convert-eval", tsv_path]
    if limit:
        command += ["--limit", str(limit)]
    if top:
        command += ["--top", str(top)]
    if weights:
        if not os.path.exists(weights):
            raise SystemExit(f"Zenzai のモデルがありません: {weights}")
        command += ["--weights", weights]
    if ignore_right_context:
        command += ["--ignore-right-context"]
    import subprocess
    done = subprocess.run(command, capture_output=True, text=True)
    if done.returncode != 0:
        print(done.stdout[-2000:], file=sys.stderr)
        print(done.stderr[-2000:], file=sys.stderr)
        raise SystemExit(f"IME が終了コード {done.returncode} で終わりました")
    results = []
    for line in done.stdout.split("\n"):   # splitlines() は U+001C〜U+001E や U+2028 でも切ってしまうので使わない
        if line.startswith("#"):
            print("  " + line, file=sys.stderr)
            continue
        columns = line.split("\t")
        if len(columns) not in (4, 5):
            continue
        # 5 列目 (--top のとき) は、同じ読みの候補を U+001F 区切りで上位から並べたもの
        candidates = columns[4].split("\x1f") if len(columns) == 5 and columns[4] else None
        results.append({"id": columns[0], "hit": columns[1] == "1", "top": columns[2], "ms": float(columns[3]), "candidates": candidates})
    return results


def report(items: list, results: list, out_dir: str, binary: str, zenzai_label: str, dataset=None) -> dict:
    by_index = {str(item["index"]): item for item in items}
    rows = []
    for result in results:
        item = by_index.get(result["id"])
        if item is None:
            continue
        references = item["expected_output"]
        context = item.get("context_text") or ""
        right = item.get("right_context") or ""
        rows.append({
            "id": result["id"],
            "input": item["input"],
            "reading": katakana_to_hiragana(item["input"]),
            "context": context,
            "has_context": bool(context),
            "right_context": right,
            "has_right_context": bool(right),
            "source": item.get("source") or "",
            "top": result["top"],
            "accepted": references,
            "hit": result["top"] in references,
            "candidates": result.get("candidates"),
            # 許容解のどれかが候補の何番目に出るか (1 始まり)。--top 無しなら None、上位 N に無ければ 0
            "rank": (next((i for i, c in enumerate(result["candidates"], start=1) if c in references), 0)
                     if result.get("candidates") is not None else None),
            "cer": min_cer(references, result["top"]),
            "ms": result["ms"],
        })
    total = len(rows)
    hits = sum(1 for row in rows if row["hit"])
    with_context = [row for row in rows if row["has_context"]]
    without_context = [row for row in rows if not row["has_context"]]

    def block(group: list) -> dict:
        n = len(group)
        h = sum(1 for row in group if row["hit"])
        return {"n": n, "hits": h, "acc@1": (h / n) if n else None,
                "mean_MinCER": (sum(row["cer"] for row in group) / n) if n else None}

    summary = {
        "binary": binary,
        "dataset": dataset or "AJIMEE-Bench JWTD_v2/v1 (CC-BY-SA 3.0、実行時に取得。リポジトリには同梱しない)",
        "engine": f"Mac 版の製品の経路 (MeltypeConverter.clauses の第一候補)。{zenzai_label}",
        "total": block(rows),
        "with_context": block(with_context),
        "without_context": block(without_context),
    }
    if any(row["has_right_context"] for row in rows):
        summary["with_right_context"] = block([row for row in rows if row["has_right_context"]])
    sources = sorted({row["source"] for row in rows if row["source"]})
    if sources:
        summary["by_source"] = {source: block([row for row in rows if row["source"] == source]) for source in sources}
    misses = [row for row in rows if not row["hit"]]
    # --top のとき、外れを「候補に正解がある (順位の問題)」と「上位 N に無い (語彙・組み合わせの問題)」に分ける
    ranked = [row for row in misses if row["rank"] is not None]
    in_list = [row for row in ranked if row["rank"] > 0]
    not_in_list = [row for row in ranked if row["rank"] == 0]
    summary["miss_breakdown"] = ({"misses": len(misses), "in_candidates": len(in_list), "not_in_candidates": len(not_in_list),
                                  "rank_of_in_candidates": sorted(row["rank"] for row in in_list)} if ranked else None)

    os.makedirs(out_dir, exist_ok=True)
    with open(os.path.join(out_dir, "rows.tsv"), "w", encoding="utf-8") as handle:
        # 10 列目以降 (右文脈の有無・出どころ) は後ろに足した列。--compare は 9 列までの表も読める
        handle.write("id\thit\tcer\tms\thas_context\ttop\taccepted\treading\trank\thas_right_context\tsource\n")
        for row in rows:
            handle.write(f"{row['id']}\t{1 if row['hit'] else 0}\t{row['cer']:.3f}\t{row['ms']:.1f}\t"
                         f"{1 if row['has_context'] else 0}\t{row['top']}\t{'|'.join(row['accepted'])}\t{row['reading']}\t"
                         f"{'' if row['rank'] is None else row['rank']}\t{1 if row['has_right_context'] else 0}\t{row['source']}\n")
    with open(os.path.join(out_dir, "results.json"), "w", encoding="utf-8") as handle:
        json.dump({"summary": summary, "rows": rows}, handle, ensure_ascii=False, indent=1)

    def line(block: dict) -> str:
        return f"{block['hits']}/{block['n']} ({block['acc@1'] * 100:.1f}%)" if block["n"] else "0 件"

    def pct(block: dict) -> str:
        """MinCER の平均。群が空 (例: 全件が左文脈付きの表) のとき None になるので文字列に直す。"""
        return f"{block['mean_MinCER'] * 100:.1f}%" if block["n"] else "-"

    md = [
        "# AJIMEE-Bench での測定 (かな → 漢字変換)",
        "",
        f"- 対象: `{binary}`",
        f"- エンジン: {summary['engine']}",
        f"- データ: {summary['dataset']}",
        "",
        "| 区分 | 第一候補の完全一致 (acc@1) | MinCER の平均 |",
        "|---|---|---|",
        f"| 全体 | {line(summary['total'])} | {pct(summary['total'])} |",
        f"| 左文脈あり ({summary['with_context']['n']} 件) | {line(summary['with_context'])} | {pct(summary['with_context'])} |",
        f"| 左文脈なし ({summary['without_context']['n']} 件) | {line(summary['without_context'])} | {pct(summary['without_context'])} |",
    ]
    if "with_right_context" in summary:
        md.append(f"| 右文脈あり ({summary['with_right_context']['n']} 件) | {line(summary['with_right_context'])} | {pct(summary['with_right_context'])} |")
    for source, group in summary.get("by_source", {}).items():
        md.append(f"| 出どころ: {source} ({group['n']} 件) | {line(group)} | {pct(group)} |")
    md += [
        "",
        f"外れ {len(misses)} 件 (CER の大きい順に 25 件。`許容解` は `|` 区切り):",
        "",
        "| id | 読み | 第一候補 | 許容解 | CER | 左文脈 | 許容解の順位 |",
        "|---|---|---|---|---|---|---|",
    ]
    if ranked:
        breakdown = [
            f"外れ {len(misses)} 件を、候補の一覧 (一覧の個数は最大 {max(len(r['candidates']) for r in ranked)} 個だった) で切り分けた:",
            "",
            f"- 候補に許容解が**ある** (順位の問題。1 番目に上げれば当たる): {len(in_list)} 件"
            + (f" (順位の内訳 {dict(sorted(collections.Counter(row['rank'] for row in in_list).items()))})" if in_list else ""),
            f"- 候補に許容解が**無い** (語彙・語の組み合わせが候補に出てこない): {len(not_in_list)} 件",
            "- 注: 候補は入力の全体を使った変換結果だけ (長い読みでは、語は辞書にあっても全体の組み合わせが上位に出ないことがある)。"
            "「無い」は語彙不足の証拠ではなく、語彙不足か、組み合わせが出ないかの区別まではつかない。",
            "",
        ]
        at = next(i for i, m in enumerate(md) if m.startswith("外れ "))
        md[at:at] = breakdown
    for row in sorted(misses, key=lambda r: -r["cer"])[:25]:
        md.append(f"| {row['id']} | `{row['reading'][:28]}` | `{row['top'][:28]}` | `{row['accepted'][0][:28]}` | {row['cer'] * 100:.0f}% | {'あり' if row['has_context'] else 'なし'} | {'-' if row['rank'] is None else (row['rank'] or '圏外')} |")
    with open(os.path.join(out_dir, "report.md"), "w", encoding="utf-8") as handle:
        handle.write("\n".join(md) + "\n")

    print(f"全体 acc@1 {summary['total']['hits']}/{summary['total']['n']} ({summary['total']['acc@1'] * 100:.1f}%) / "
          f"左文脈あり {line(summary['with_context'])} / 左文脈なし {line(summary['without_context'])}")
    print(f"MinCER の平均: 全体 {pct(summary['total'])} / "
          f"あり {pct(summary['with_context'])} / なし {pct(summary['without_context'])}")
    if summary["miss_breakdown"]:
        info = summary["miss_breakdown"]
        print(f"外れ {info['misses']} 件の内訳: 候補に許容解あり {info['in_candidates']} 件 (順位の問題) / 候補に無し {info['not_in_candidates']} 件")
    print(f"外れ {len(misses)} 件。詳細: {os.path.join(out_dir, 'report.md')} / {os.path.join(out_dir, 'rows.tsv')}")
    return summary


def compare(rows_a: str, rows_b: str, out_dir: str, labels=("A", "B")) -> int:
    """2 回の測定の結果 (`rows.tsv`) を行ごとに突き合わせ、片方だけ正解だった数を McNemar の検定で出す。

    同一ビルドで ON/OFF を比べたときに使う (`--weights` の有無で 2 回走らせる)。
    """
    def load(path: str) -> dict:
        out = {}
        with open(path, encoding="utf-8") as handle:
            for line in handle:
                if line.startswith("#"):
                    continue
                columns = line.rstrip("\n").split("\t")
                if len(columns) < 7 or columns[0] == "id":
                    continue
                out[columns[0]] = {"hit": columns[1] == "1", "top": columns[5], "accepted": columns[6], "cer": float(columns[2]), "ms": float(columns[3]),
                                   "source": columns[10] if len(columns) > 10 else ""}
        return out

    def mcnemar(keys: list) -> tuple:
        """(A のみ正解の id, B のみ正解の id, 両側正確検定の p)"""
        only_a = [key for key in keys if a[key]["hit"] and not b[key]["hit"]]
        only_b = [key for key in keys if not a[key]["hit"] and b[key]["hit"]]
        m = len(only_a) + len(only_b)
        if not m:
            return only_a, only_b, 1.0
        tail = sum(math.comb(m, k) for k in range(0, min(len(only_a), len(only_b)) + 1)) / (2 ** m)
        return only_a, only_b, min(1.0, 2 * tail)

    a, b = load(rows_a), load(rows_b)
    common = sorted(set(a) & set(b), key=lambda key: int(key) if key.isdigit() else key)
    if not common:
        raise SystemExit("2 つの結果に行が共通ありません")
    hits_a = sum(1 for key in common if a[key]["hit"])
    hits_b = sum(1 for key in common if b[key]["hit"])
    # McNemar: b = A 正解 → B 外れ、c = A 外れ → B 正解。外れを 2 回引いた数が統計量の差
    b_only, c_only, p = mcnemar(common)

    def q(values: list, ratio: float) -> float:
        values = sorted(values)
        return values[max(0, math.ceil(round(len(values) * ratio, 9)) - 1)]   # nearest-rank (⌈q·n⌉ 番目)

    md = [
        f"# {labels[0]} と {labels[1]} の突き合わせ ({len(common)} 件共通)",
        "",
        f"- acc@1: {labels[0]} {hits_a}/{len(common)} ({hits_a * 100 / len(common):.1f}%) / "
        f"{labels[1]} {hits_b}/{len(common)} ({hits_b * 100 / len(common):.1f}%) / 差 {(hits_b - hits_a) * 100 / len(common):+.1f}pt",
        f"- McNemar: {labels[0]} のみ正解 {len(b_only)} 件、{labels[1]} のみ正解 {len(c_only)} 件、両側正確検定 p = {p:.2e}",
        f"- 変換 1 行の中央値: {labels[0]} {q([a[k]['ms'] for k in common], 0.5):.1f} ms / "
        f"{labels[1]} {q([b[k]['ms'] for k in common], 0.5):.1f} ms",
        "",
    ]
    # rows.tsv に出どころの列 (要件 049) があれば、出どころごとにも突き合わせる (A の表の出どころで分ける)
    sources = sorted({a[key]["source"] for key in common if a[key]["source"]})
    if sources:
        md += [f"| 出どころ | 件数 | {labels[0]} | {labels[1]} | {labels[0]} のみ | {labels[1]} のみ | p |", "|---|---|---|---|---|---|---|"]
        for source in sources:
            keys = [key for key in common if a[key]["source"] == source]
            only_a, only_b, p_source = mcnemar(keys)
            md.append(f"| {source} | {len(keys)} | {sum(1 for k in keys if a[k]['hit'])} | {sum(1 for k in keys if b[k]['hit'])} | "
                      f"{len(only_a)} | {len(only_b)} | {p_source:.2e} |")
        md.append("")
    md += [
        f"### {labels[0]} だけ正解 ({labels[1]} で悪化した行、{len(b_only)} 件)",
        "",
        "| id | {0} の第一候補 | {1} の第一候補 | 許容解 |".format(labels[0], labels[1]),
        "|---|---|---|---|",
    ]
    for key in b_only[:40]:
        md.append(f"| {key} | `{a[key]['top'][:24]}` | `{b[key]['top'][:24]}` | `{a[key]['accepted'][:40]}` |")
    md += ["", f"### {labels[1]} だけ正解 ({len(c_only)} 件" + (f"、うち 15 件を表示" if len(c_only) > 15 else "") + ")", "",
           "| id | {0} の第一候補 | {1} の第一候補 | 許容解 |".format(labels[0], labels[1]), "|---|---|---|---|"]
    for key in c_only[:15]:
        md.append(f"| {key} | `{a[key]['top'][:24]}` | `{b[key]['top'][:24]}` | `{a[key]['accepted'][:40]}` |")
    os.makedirs(out_dir, exist_ok=True)
    path = os.path.join(out_dir, "compare.md")
    with open(path, "w", encoding="utf-8") as handle:
        handle.write("\n".join(md) + "\n")
    print(f"{labels[0]} {hits_a}/{len(common)} ({hits_a * 100 / len(common):.1f}%) / {labels[1]} {hits_b}/{len(common)} "
          f"({hits_b * 100 / len(common):.1f}%) / 差 {(hits_b - hits_a) * 100 / len(common):+.1f}pt")
    print(f"McNemar: {labels[0]} のみ {len(b_only)} 件 / {labels[1]} のみ {len(c_only)} 件 / p = {p:.2e}")
    print(f"詳細: {path}")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description="AJIMEE-Bench で Mac 版のかな → 漢字変換を測る")
    parser.add_argument("--binary", default=None, help="MeltypeIME のバイナリ (swift build の出力。辞書バンドルが同じフォルダーに必要)。--compare を使うなら要らない")
    parser.add_argument("--items", default=os.path.join(DEFAULT_CACHE, "evaluation_items.json"), help="正解表の JSON (無ければ取得する)")
    parser.add_argument("--out", default=os.path.join(DEFAULT_CACHE, "run"), help="結果の出力フォルダー (リポジトリの外を推奨)")
    parser.add_argument("--limit", type=int, default=0, help="測る件数 (0 = 全件 200)")
    parser.add_argument("--weights", default=None, help="Zenzai の GGUF。指定すると Zenzai ON で測る (指定しない = 製品の既定と同じ OFF)。取得はせず、既にあるファイルを検査して使う")
    parser.add_argument("--top", type=int, default=0, help="候補を上位 N 個まで出して、外れを「候補に正解あり (順位)」と「候補に無し」に切り分ける (0 = 出さない。最大 100)")
    parser.add_argument("--no-download", action="store_true", help="取得しない (正解表が既にあるときだけ)")
    parser.add_argument("--compare", nargs=2, default=None, metavar=("rows_a.tsv", "rows_b.tsv"),
                        help="2 回の測定の結果 (`rows.tsv`) を行ごとに突き合わせる (McNemar)。Zenzai ON/OFF の判定に使う")
    parser.add_argument("--labels", default="A,B", help="--compare のラベル 2 つ (例: --labels OFF,ON)")
    parser.add_argument("--ignore-right-context", action="store_true",
                        help="表に右文脈 (right_context) があっても使わずに測る (要件 049 の「なし」の基準。表は同じものを書く)")
    args = parser.parse_args()
    if not 0 <= args.top <= 100:
        raise SystemExit("--top は 0〜100 で指定してください (0 = 候補を出さない)")

    if args.compare:
        labels = tuple(args.labels.split(","))
        if len(labels) != 2:
            raise SystemExit("--labels は 2 つ (例: --labels OFF,ON)")
        return compare(args.compare[0], args.compare[1], args.out, labels)
    if not args.binary:
        raise SystemExit("--binary を指定してください (--compare のときは要りません)")
    items_path = fetch_items(args.items, args.no_download)
    with open(items_path, encoding="utf-8") as handle:
        items = json.load(handle)
    os.makedirs(args.out, exist_ok=True)
    tsv_path = os.path.join(args.out, "table.tsv")
    count = build_table(items, tsv_path, args.limit)
    print(f"正解表 {count} 件を作りました: {tsv_path}", file=sys.stderr)
    results = run_engine(args.binary, tsv_path, args.limit, args.weights, args.top, args.ignore_right_context)
    if len(results) != count:
        print(f"警告: 返ってきた行が {len(results)} 件 / 正解表 {count} 件", file=sys.stderr)
    label = "Zenzai ON (--weights)" if args.weights else "Zenzai なし (製品の既定)"
    if any(item.get("right_context") for item in items):
        label += "。右文脈は" + ("使わない (--ignore-right-context)" if args.ignore_right_context else ("使う" if args.weights else "Zenzai なしなので効かない"))
    # 右文脈つきの表 (AJIMEE ではない) のときだけ、データの欄を表のパスにする。AJIMEE・Flash 40 件の表は今までどおり
    dataset = f"{items_path} (右文脈つき。tools/make-right-context-items.py が既存の文から作った表)" if any(item.get("right_context") for item in items) else None
    report(items, results, args.out, args.binary, label, dataset)
    return 0


if __name__ == "__main__":
    sys.exit(main())
