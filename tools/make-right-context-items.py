#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""要件 049: 既存の文を「左・真ん中・右」に機械的に分け、右文脈つきの正解表 (AJIMEE と同じ形の JSON) を作る。

    python3 tools/make-right-context-items.py [--out eval/kanji/right-context-items.json]

新しい文は書かない (作り手の癖を新しく入れないため)。元の表:
- `eval/kanji/kanji-cases.tsv` (6 列: category・reading・expected・left_context・note・accepted)。
  `note` が「前回の70文」で始まる行は人が読みを付けた文 (`docs/ZENZAI_DESIGN_1-1.md`)、それ以外は LLM が作った文として `source` を分ける。
- `eval/kanji/bench-homophones.tsv` (4 列: id・reading・accepted・left_context。Serugu/Meltype-Zenzai-Vulkan 由来、GPL-3.0。出典は表の先頭の注記)。

分け方 (決まった手順。人の判断を入れない):
1. 期待の文を「かなの連なり」と「かな以外 (漢字・数字・記号) の連なり」に切る。かなの連なりは、読みと同じ文字 (カタカナはひらがなに寄せて比べる)、
   かな以外の連なりは、読みの 1 文字以上に当たるとして、読みとの対応をすべて数える。対応がちょうど 1 通りの文だけ使う (2 通り以上・0 通りは飛ばす)。
2. 「文節」は、かな以外の連なり 1 つと、その後ろのかなの連なり (送り仮名・助詞) を 1 つにしたもの。先頭がかなで始まる文は、その部分を 1 つ目の文節にする
   (ただし先頭が「お」「ご」だけなら、次の文節の頭にする)。
3. 真ん中は「かな以外を含む最初の文節で、後ろにまだ文節があるもの」。左はその前のすべて (元の左文脈があれば、その後ろにつなぐ)、右はその後ろのすべて。
4. 許容解: 期待の文の真ん中。元の表の別の許容 (`|` 区切り) は、左と右が同じ文字のときだけ、その真ん中を許容に足す。
"""

import argparse
import csv
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
KANJI_CASES = os.path.join(ROOT, "eval", "kanji", "kanji-cases.tsv")
HOMOPHONES = os.path.join(ROOT, "eval", "kanji", "bench-homophones.tsv")
DEFAULT_OUT = os.path.join(ROOT, "eval", "kanji", "right-context-items.json")


def to_hiragana(text: str) -> str:
    """カタカナ (ァ〜ヶ) をひらがなに寄せる。比べるときだけ使う (長音符 ー・中点 ・ はそのまま)。"""
    return "".join(chr(ord(ch) - 0x60) if 0x30A1 <= ord(ch) <= 0x30F6 else ch for ch in text)


def is_kana(ch: str) -> bool:
    return "ぁ" <= ch <= "ゖ" or "ァ" <= ch <= "ヶ" or ch == "ー"


def runs(text: str) -> list:
    """(かなか, 文字列) の連なりに切る。句読点などかな以外の記号は、読みにそのまま出ることが多いので、かなと同じく「そのまま比べる」側にする。"""
    out = []
    for ch in text:
        literal = is_kana(ch) or ch in "、。！？!?・「」"
        if out and out[-1][0] == literal:
            out[-1] = (literal, out[-1][1] + ch)
        else:
            out.append((literal, ch))
    return out


def alignments(parts: list, reading: str, limit: int = 2) -> list:
    """連なりごとの読みの長さの組を、最大 limit 通りまで返す (ちょうど 1 通りかを見るため)。"""
    target = to_hiragana(reading)
    found = []

    def walk(i: int, pos: int, lengths: list):
        if len(found) >= limit:
            return
        if i == len(parts):
            if pos == len(target):
                found.append(list(lengths))
            return
        literal, text = parts[i]
        if literal:
            want = to_hiragana(text)
            if target.startswith(want, pos):
                walk(i + 1, pos + len(want), lengths + [len(want)])
            return
        for length in range(1, len(target) - pos + 1):
            walk(i + 1, pos + length, lengths + [length])

    walk(0, 0, [])
    return found


def bunsetsu(parts: list, lengths: list) -> list:
    """(期待の文字列, 読みの長さ, かな以外を含むか) の文節の並び。"""
    out = []
    for (literal, text), length in zip(parts, lengths):
        if literal and out:
            out[-1][0] += text   # かな・句読点は前の文節につける (送り仮名・助詞)
            out[-1][1] += length
        else:
            out.append([text, length, not literal])   # かな以外は新しい文節を始める。文の先頭のかなも 1 つの文節
    return [tuple(b) for b in out]


def split(reading: str, expected: str):
    """(左の期待, 真ん中の読み, 真ん中の期待, 右の期待) か、分けられない理由の文字列を返す。"""
    parts = runs(expected)
    found = alignments(parts, reading)
    if len(found) != 1:
        return "読みとの対応が 1 通りでない" if found else "読みと対応しない"
    units = bunsetsu(parts, found[0])
    # 接頭の「お」「ご」だけの文節 (お茶・ご飯) は、次の文節の頭にする (「ちゃが」だけを変換させないため)
    if len(units) > 1 and not units[0][2] and units[0][0] in ("お", "ご"):
        units = [(units[0][0] + units[1][0], units[0][1] + units[1][1], units[1][2])] + units[2:]
    for index, (text, length, has_kanji) in enumerate(units):
        if has_kanji and index < len(units) - 1:
            left = "".join(u[0] for u in units[:index])
            start = sum(u[1] for u in units[:index])
            right = "".join(u[0] for u in units[index + 1:])
            return left, reading[start:start + length], text, right
    return "後ろに文節が残る、かな以外を含む文節が無い"


def load_kanji_cases(path: str) -> list:
    rows = []
    with open(path, encoding="utf-8", newline="") as handle:
        reader = csv.reader(handle, delimiter="\t", quoting=csv.QUOTE_NONE)
        header = next(reader)
        assert header[:6] == ["category", "reading", "expected", "left_context", "note", "accepted"], header
        for number, columns in enumerate(reader, start=2):
            columns += [""] * (6 - len(columns))
            source = "kanji-cases (人)" if columns[4].startswith("前回の70文") else "kanji-cases (LLM)"
            alternatives = [a for a in columns[5].split("|") if a]
            rows.append({"id": f"K{number:03d}", "reading": columns[1], "expected": columns[2],
                         "alternatives": alternatives, "left": columns[3], "source": source})
    return rows


def load_homophones(path: str) -> list:
    rows = []
    with open(path, encoding="utf-8") as handle:
        for line in handle:
            if line.startswith("#") or not line.strip():
                continue
            columns = line.rstrip("\n").split("\t")
            columns += [""] * (4 - len(columns))
            accepted = [a for a in columns[2].split("|") if a]
            rows.append({"id": columns[0], "reading": columns[1], "expected": accepted[0],
                         "alternatives": accepted[1:], "left": columns[3], "source": "bench-homophones (第三者)"})
    return rows


def build(rows: list) -> tuple:
    items, skipped = [], []
    for row in rows:
        result = split(row["reading"], row["expected"])
        if isinstance(result, str):
            skipped.append((row["id"], result))
            continue
        left, middle_reading, middle, right = result
        accepted = [middle]
        for alternative in row["alternatives"]:
            if alternative.startswith(left) and alternative.endswith(right) and len(alternative) > len(left) + len(right):
                candidate = alternative[len(left):len(alternative) - len(right)]
                if candidate not in accepted:
                    accepted.append(candidate)
        items.append({
            "index": row["id"],
            "input": middle_reading,   # ひらがな (ajimee-eval.py の katakana_to_hiragana はひらがなを変えない)
            "expected_output": accepted,
            "context_text": row["left"] + left,
            "right_context": right,
            "source": row["source"],
            "original": row["expected"],
        })
    return items, skipped


def main() -> int:
    parser = argparse.ArgumentParser(description="右文脈つきの正解表を、既存の文から機械的に作る (要件 049)")
    parser.add_argument("--out", default=DEFAULT_OUT)
    args = parser.parse_args()
    rows = load_kanji_cases(KANJI_CASES) + load_homophones(HOMOPHONES)
    items, skipped = build(rows)
    with open(args.out, "w", encoding="utf-8") as handle:
        json.dump(items, handle, ensure_ascii=False, indent=1)
        handle.write("\n")
    by_source = {}
    for item in items:
        by_source.setdefault(item["source"], 0)
        by_source[item["source"]] += 1
    print(f"元の文 {len(rows)} / 表に入れた {len(items)} (右文脈が空でない行 {sum(1 for i in items if i['right_context'])}) / 飛ばした {len(skipped)}")
    for source, count in sorted(by_source.items()):
        print(f"  {source}: {count}")
    reasons = {}
    for _, reason in skipped:
        reasons[reason] = reasons.get(reason, 0) + 1
    for reason, count in sorted(reasons.items()):
        print(f"  飛ばした理由「{reason}」: {count}")
    print(f"書きました: {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
