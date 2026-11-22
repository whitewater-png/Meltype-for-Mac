#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""境界例の一括比較で打つ入力の一覧を、リポジトリの辞書から決まった手順で作る。

    python3 tools/boundary_sweep.py --out <out-dir> [--dict-dir dictionaries] [--scale full|light] [--chunk 200000]

組 (group):
  j1  日本語の語 (readings.txt と japanese.txt の語のローマ字) の単独と、助詞・語尾 (を が に で の と は も です) を付けた形
  j2  j1 の単独の形の打ち間違い (1 文字落とし・隣どうしの入れ替え・1 文字の重ね打ち・隣のキーの 1 文字足し)
  j3  日本語の語の連結 (左の語 + 助詞 + 続く語) と、そのつなぎ目に余計な 1 キー (隣のキー) を足した形
  e   英単語 (english.txt) の単独と、助詞 (wo ga no desu) を付けた形
<out-dir>/chunk-NNN/<組>-NNNN.tsv (打鍵 = 期待、比べ方 full、出典 = 組の名前の 4 列) と manifest.json を書く。
chunk ごとに --expect-all を回し (1 回の結果が大きくなりすぎないように)、結果は組の名前を先頭に持つファイル名で結び直す。
同じ辞書・同じ指定なら、何度作っても同じ一覧になる (乱数は使わず、選び方は語の並びと CRC32 で決める)。標準ライブラリだけで動く。

かな → ローマ字の表は src/Meltype.Core.Tests/Jht.cs の表 (ヘボン式が先頭) と、隣のキーの表は
src/Meltype.Core/Composition/RomajiTypoCorrector.cs の Neighbors と同じ (test_boundary_sweep.py が後者を機械で比べる)。
"""

import argparse
import json
import os
import re
import sys
import zlib

# 規模: 辞書の語を何語に 1 語取るか。full は全語、light は手元・査読の確認用 (数分)
SCALES = {"full": 1, "light": 20}

PARTICLES = ["を", "が", "に", "で", "の", "と", "は", "も", "です"]
ENGLISH_PARTICLES = ["wo", "ga", "no", "desu"]
# j3: つなぐ助詞と、よく続く語 (固定の一覧。つなぎ目に足す余計なキーは、右の語の頭の隣のキーにも当たるように頭の文字をばらけさせてある)
J3_PARTICLES = ["で", "の", "と", "は", "が", "を", "に", "も"]
J3_RIGHTS = [
    "totemo", "suru", "shita", "mou", "kore", "sore", "dekiru", "toki", "tokoro", "demo", "tsukau", "iru",
    "aru", "naru", "kimeru", "tabeta", "gurai", "kara", "yori", "garu", "ryouri", "yasui", "cha", "bakari",
]
# 左の語: この語尾で終わる語は全部 (jibun のように ん で終わる語は、で との連結で 要件 012 の undef の損失が出た)
J3_LEFT_ENDINGS = "んうぶむぬ"
J3_OTHER_STRIDE = 25  # それ以外の語は、決まった間隔で取る
J3_N_RIGHTS = 6  # ん で終わる語に で でつなぐ右の語の数
J2_STRIDE = 2  # j2 は j1 の単独の形のうち 2 語に 1 語 (形が多く、1 入力あたりの時間も長いため)

# QWERTY の隣のキー (RomajiTypoCorrector.cs の Neighbors と同じ)
NEIGHBORS = {
    "q": "wa", "w": "qeas", "e": "wrsd", "r": "etdf", "t": "ryfg", "y": "tugh", "u": "yihj", "i": "uojk", "o": "ipkl", "p": "ol",
    "a": "qwsz", "s": "weadzx", "d": "erfsxc", "f": "rtdgcv", "g": "tyfhvb", "h": "yugjbn", "j": "uihknm", "k": "iojlm", "l": "opk",
    "z": "asx", "x": "zsdc", "c": "xdfv", "v": "cfgb", "b": "vghn", "n": "bhjm", "m": "njk",
}


def _build_table():
    table = {}
    for row, consonant in [("あいうえお", ""), ("かきくけこ", "k"), ("なにぬねの", "n"), ("まみむめも", "m"), ("らりるれろ", "r"),
                           ("がぎぐげご", "g"), ("ばびぶべぼ", "b"), ("ぱぴぷぺぽ", "p")]:
        for i, kana in enumerate(row):
            table[kana] = consonant + "aiueo"[i]
    table.update({
        "さ": "sa", "し": "shi", "す": "su", "せ": "se", "そ": "so", "た": "ta", "ち": "chi", "つ": "tsu", "て": "te", "と": "to",
        "は": "ha", "ひ": "hi", "ふ": "fu", "へ": "he", "ほ": "ho", "や": "ya", "ゆ": "yu", "よ": "yo", "わ": "wa", "を": "wo", "ゔ": "vu",
        "ざ": "za", "じ": "ji", "ず": "zu", "ぜ": "ze", "ぞ": "zo", "だ": "da", "ぢ": "di", "づ": "du", "で": "de", "ど": "do",
    })
    for kana, consonant in [("き", "ky"), ("に", "ny"), ("ひ", "hy"), ("み", "my"), ("り", "ry"), ("ぎ", "gy"), ("び", "by"), ("ぴ", "py"), ("ぢ", "dy")]:
        table[kana + "ゃ"] = consonant + "a"
        table[kana + "ゅ"] = consonant + "u"
        table[kana + "ょ"] = consonant + "o"
    table.update({
        "しゃ": "sha", "しゅ": "shu", "しょ": "sho", "しぇ": "she", "ちゃ": "cha", "ちゅ": "chu", "ちょ": "cho", "ちぇ": "che",
        "じゃ": "ja", "じゅ": "ju", "じょ": "jo", "じぇ": "je", "ふぁ": "fa", "ふぃ": "fi", "ふぇ": "fe", "ふぉ": "fo", "てぃ": "thi", "でぃ": "dhi",
        "うぃ": "wi", "うぇ": "we", "ゔぁ": "va", "ゔぃ": "vi", "ゔぇ": "ve", "ゔぉ": "vo",
        "ぁ": "xa", "ぃ": "xi", "ぅ": "xu", "ぇ": "xe", "ぉ": "xo", "ゃ": "xya", "ゅ": "xyu", "ょ": "xyo", "ー": "-",
    })
    return table


ROMAJI = _build_table()


def romaji(kana):
    """ひらがなをヘボン式で打つキーにする (Jht.cs の最初の打ち方と同じ)。打てない文字があれば None。"""
    if not kana:
        return None
    sounds = []  # (かな, ローマ字。っ・ん は None)
    i = 0
    while i < len(kana):
        ch = kana[i]
        if ch in "っん":
            sounds.append((ch, None))
            i += 1
        elif kana[i:i + 2] in ROMAJI and len(kana[i:i + 2]) == 2:
            sounds.append((kana[i:i + 2], ROMAJI[kana[i:i + 2]]))
            i += 2
        elif ch in ROMAJI:
            sounds.append((ch, ROMAJI[ch]))
            i += 1
        else:
            return None
    parts = [""] * len(sounds)
    for i in range(len(sounds) - 1, -1, -1):
        nxt = parts[i + 1] if i + 1 < len(sounds) else ""
        kana_i, value = sounds[i]
        if kana_i == "っ":
            # 次の子音を重ねる (ch は tch)。次が子音でない (母音・n・記号・語末) なら xtu
            if not nxt or not ("a" <= nxt[0] <= "z") or nxt[0] in "aiueon":
                parts[i] = "xtu"
            elif nxt.startswith("ch"):
                parts[i] = "t"
            else:
                parts[i] = nxt[0]
        elif kana_i == "ん":
            # 次が母音・や行・な行なら nn、それ以外と語末は n
            parts[i] = "nn" if nxt and nxt[0] in "aiueoyn" else "n"
        else:
            parts[i] = value
    return "".join(parts)


def _lines(path):
    with open(path, encoding="utf-8") as f:
        for line in f:
            yield line.split("#", 1)[0].strip()


def load_readings(path):
    """readings.txt の読みの列 (活用の種類は使わない)。重複は除く。"""
    seen, words = set(), []
    for line in _lines(path):
        if not line:
            continue
        word = line.split("\t")[0].strip()
        if word and word not in seen:
            seen.add(word)
            words.append(word)
    return words


def load_wordlist(path):
    """japanese.txt・english.txt の語 (空白区切り、# 以降はコメント)。重複は除く。"""
    seen, words = set(), []
    for line in _lines(path):
        for word in line.split():
            if word not in seen:
                seen.add(word)
                words.append(word)
    return words


def _printable(text):
    return bool(text) and all(" " <= ch <= "~" for ch in text) and not text.startswith("#") and len(text) <= 300


def join_romaji(word, particle):
    """ローマ字の語に助詞をつなぐ (語末の n の後に母音・や行・な行が来ると nn が要る)。"""
    if word.endswith("n") and particle[0] in "aiueoyn":
        word += "n"
    return word + particle


def typos(word):
    """打ち間違いの形 (元の語・空は含めない。重複なし)。隣のキーの足しは、位置ごとに CRC32 で決めた 1 つ。"""
    out = []
    for i in range(len(word)):
        out.append(word[:i] + word[i + 1:])  # 1 文字落とし
        if i + 1 < len(word) and word[i] != word[i + 1]:
            out.append(word[:i] + word[i + 1] + word[i] + word[i + 2:])  # 隣どうしの入れ替え
        out.append(word[:i + 1] + word[i] + word[i + 1:])  # 1 文字の重ね打ち
        near = NEIGHBORS.get(word[i])
        if near:
            pick = near[zlib.crc32((word + str(i)).encode()) % len(near)]
            out.append(word[:i + 1] + pick + word[i + 1:])  # 隣のキーの 1 文字足し
    seen, result = {word}, []
    for form in out:
        if form and form not in seen:
            seen.add(form)
            result.append(form)
    return result


def _dedup(items):
    seen, out = set(), []
    for x in items:
        if x not in seen and _printable(x):
            seen.add(x)
            out.append(x)
    return out


def _seam_extras(whole, pos, after):
    """つなぎ目 (whole の pos の後ろ) に足す余計なキー: 前の文字と後ろの文字の、隣のキー。"""
    keys = []
    for ch in (whole[pos - 1], after):
        for k in NEIGHBORS.get(ch, ""):
            if k not in keys:
                keys.append(k)
    return keys


def build(dict_dir, stride=1):
    """組ごとの入力の一覧 ({組: [打鍵]}) と、ローマ字にできず除いた読みの数を返す。"""
    readings = load_readings(os.path.join(dict_dir, "readings.txt"))[::stride]
    japanese = load_wordlist(os.path.join(dict_dir, "japanese.txt"))[::stride]
    english = [w for w in load_wordlist(os.path.join(dict_dir, "english.txt"))[::stride] if _printable(w)]

    excluded = 0
    kana_words = []  # (かな, ローマ字)
    for kana in readings:
        r = romaji(kana)
        if r is None:
            excluded += 1
        else:
            kana_words.append((kana, r))

    j1, singles = [], []
    for kana, r in kana_words:
        singles.append(r)
        j1.append(r)
        j1.extend(romaji(kana + p) for p in PARTICLES)
    for w in japanese:
        singles.append(w)
        j1.append(w)
        j1.extend(join_romaji(w, romaji(p)) for p in PARTICLES)
    singles = _dedup(singles)

    j2 = []
    for w in singles[::J2_STRIDE]:
        j2.extend(typos(w))

    j3, others = [], 0
    for kana, r in kana_words:
        if kana[-1] not in J3_LEFT_ENDINGS:
            others += 1
            if others % J3_OTHER_STRIDE:
                continue
        base = zlib.crc32(kana.encode())
        if kana[-1] == "ん":
            # ん + で (jibun + de)。右の語は J3_N_RIGHTS 語 (頭の文字がばらけた 24 語の輪から、語ごとに決めた位置から連続で取る)。
            # 余計な 1 キーは、右の語の頭と で の末尾の隣のキー (jibundeftotemo の f は t の隣)
            whole = romaji(kana + "で")
            for k in range(J3_N_RIGHTS):
                right = J3_RIGHTS[(base + k) % len(J3_RIGHTS)]
                j3.append(whole + right)
                j3.extend(whole + x + right for x in _seam_extras(whole, len(whole), right[0]))
        else:
            # それ以外の語は、助詞 1 つ・右の語 1 つを語ごとに決め、つなぎ目の両方に余計な 1 キーを足す
            particle = J3_PARTICLES[base % len(J3_PARTICLES)]
            right = J3_RIGHTS[(base // 7) % len(J3_RIGHTS)]
            whole = romaji(kana + particle)
            pos1 = len(whole) - len(romaji(particle))
            j3.append(whole + right)
            j3.extend(whole[:pos1] + x + whole[pos1:] + right for x in _seam_extras(whole, pos1, whole[pos1]))
            j3.extend(whole + x + right for x in _seam_extras(whole, len(whole), right[0]))

    e = []
    for w in english:
        e.append(w)
        e.extend(w + p for p in ENGLISH_PARTICLES)

    return {"j1": _dedup(j1), "j2": _dedup(j2), "j3": _dedup(j3), "e": _dedup(e)}, excluded


def write_chunks(groups, out_dir, chunk_size):
    """入力の一覧を、chunk-NNN/<組>-NNNN.tsv に書く (1 つの chunk は chunk_size 行まで)。chunk のフォルダーの一覧を返す。"""
    os.makedirs(out_dir, exist_ok=True)
    dirs, count, index, files = [], 0, 0, {}
    handle = None
    current = None

    def new_chunk():
        nonlocal current, index, count
        index += 1
        current = os.path.join(out_dir, f"chunk-{index:03d}")
        os.makedirs(current)
        dirs.append(current)
        count = 0

    new_chunk()
    for group in ("j1", "j2", "j3", "e"):
        number = 0
        handle = None
        for typed in groups[group]:
            if count >= chunk_size:
                if handle:
                    handle.close()
                    handle = None
                new_chunk()
            if handle is None:
                number += 1
                handle = open(os.path.join(current, f"{group}-{number:04d}.tsv"), "w", encoding="utf-8", newline="\n")
            handle.write(f"{typed}\t{typed}\tfull\t{group}\n")
            count += 1
        if handle:
            handle.close()
    return dirs


def main(argv=None):
    ap = argparse.ArgumentParser(description="境界例の一括比較の入力の一覧を作る")
    ap.add_argument("--out", required=True, help="出力先 (chunk-NNN と manifest.json ができる。無いか空のフォルダー)")
    ap.add_argument("--dict-dir", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "dictionaries"))
    ap.add_argument("--scale", choices=sorted(SCALES), default="full")
    ap.add_argument("--chunk", type=int, default=200000, help="1 つの chunk の最大行数 (既定 200000)")
    args = ap.parse_args(argv)
    if os.path.isdir(args.out) and os.listdir(args.out):
        print(f"{args.out}: 空ではありません", file=sys.stderr)
        return 2
    try:
        groups, excluded = build(args.dict_dir, SCALES[args.scale])
    except OSError as e:
        print(f"辞書を読めません ({e})", file=sys.stderr)
        return 2
    dirs = write_chunks(groups, args.out, args.chunk)
    manifest = {"scale": args.scale, "counts": {g: len(v) for g, v in groups.items()}, "excluded_readings": excluded,
                "total": sum(len(v) for v in groups.values()), "chunks": [os.path.basename(d) for d in dirs]}
    with open(os.path.join(args.out, "manifest.json"), "w", encoding="utf-8") as f:
        json.dump(manifest, f, ensure_ascii=False, indent=1)
    print(f"scale={args.scale} 組ごと: " + " ".join(f"{g}={len(v)}" for g, v in groups.items()) +
          f" 合計={manifest['total']} 除いた読み={excluded} chunk={len(dirs)}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
