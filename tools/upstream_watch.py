#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""本家 Meltype の新しいコミットを PR 単位で拾い、Mac 版への取り込み状況と新しい正解ケースを候補表にまとめる。

    MELTYPE_UPSTREAM=<本家の clone> python3 tools/upstream_watch.py [--since <hash>] [--no-fetch] [--dry-run] [--no-eval]

やること (docs/UPSTREAM_WATCH.md の先頭の `<!-- seen: <hash> -->` から origin/main までを見る):
  1. 本家の clone を fetch する (--no-fetch で省く。通信はこれだけ)
  2. first-parent の PR 単位でコミットを並べ、変更ファイルから分類する (判定・変換 / Windows / 本家 Mac / Linux / 文書)
  3. Mac 版に取り込み済みか (Mac 版のコミットの件名に `本家 <7桁>` があるか、追跡文書に記録があるか) を調べる
  4. 判定・変換のコミットは、Mac 版にそのまま当たるか (git apply --check) を調べる
  5. 本家の QualityTests.cs に足された new("カテゴリ", "打鍵", "期待") を eval/candidates/upstream-<日付>-<since 短hash>.tsv に書き、
     Mac 版で当たるか数える (--no-eval で省く)
  6. docs/UPSTREAM_WATCH.md の先頭に節を足す (--dry-run なら書かず、標準出力だけ)

候補ファイルは正解表 (eval/cases) に自動では足さない。人が確かめてから移す。
終了コード: 0 = 正常 (新コミット 0 件も 0)、2 = 引数・環境の誤り。標準ライブラリ + git + dotnet だけで動く。
"""

import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
from datetime import date

WATCH_DOC = "docs/UPSTREAM_WATCH.md"
RECORD_DOCS = ["docs/UPSTREAM_PR_TRACKER.md", "docs/UPSTREAM_PR_SURVEY.md"]
QUALITY_TESTS = "src/Meltype.Core.Tests/QualityTests.cs"
CORE_PATHS = ["src/Meltype.Core", "dictionaries", "src/Meltype.Core.Tests"]
MAX_MISSES = 20

SEEN_RE = re.compile(r"\A<!--\s*seen:\s*([0-9a-fA-F]+)\s*-->")

# 分類。上から順に見て、当たったものを名前の順 (判定・変換 → Windows → 本家 Mac → Linux → 文書) で並べる。
CATEGORY_ORDER = ["判定・変換", "Windows", "本家 Mac", "Linux", "文書"]


class WatchError(Exception):
    """引数・環境の誤り (終了コード 2)。"""


# ---------------------------------------------------------------- 純粋関数 (git を使わない)

def category_of(path):
    """1 ファイルのパスから分類を返す。どれにも当たらなければ None。"""
    if path.startswith(("src/Meltype.Core/", "dictionaries/", "src/Meltype.Core.Tests/")):
        return "判定・変換"
    if path.startswith(("src/Meltype.Mac.Native/", "mac/")):
        return "本家 Mac"
    if path.startswith("linux/"):
        return "Linux"
    if path.startswith(("src/Meltype/", "native/", ".github/")) or path.endswith(".ps1"):
        return "Windows"
    if path.startswith("docs/") or path.endswith(".md"):
        return "文書"
    return None


def classify(paths):
    """変更ファイルの一覧から分類を返す。(主, [併記...])。判定・変換があれば必ず主。当たるものが無ければ ("その他", [])。"""
    found = {c for c in map(category_of, paths) if c}
    ordered = [c for c in CATEGORY_ORDER if c in found]
    if not ordered:
        return "その他", []
    return ordered[0], ordered[1:]


def label_of(primary, others):
    return primary + ("".join(f" (+{o})" for o in others) if others else "")


def pr_number(subject):
    """PR 番号を返す (無ければ None)。Merge pull request #N → 末尾の (#N) → 最初の #N の順に探す。"""
    for pattern in (r"^Merge pull request #(\d+)", r"\(#(\d+)\)\s*$", r"#(\d+)"):
        m = re.search(pattern, subject)
        if m:
            return m.group(1)
    return None


def csharp_unescape(body):
    """C# の通常の文字列リテラルの中身 (引用符の内側) を戻す。扱えない記法があれば None。"""
    out = []
    i = 0
    simple = {"\\": "\\", '"': '"', "'": "'", "n": "\n", "t": "\t", "r": "\r", "0": "\0"}
    while i < len(body):
        c = body[i]
        if c != "\\":
            out.append(c)
            i += 1
            continue
        if i + 1 >= len(body):
            return None
        n = body[i + 1]
        if n in simple:
            out.append(simple[n])
            i += 2
        elif n == "u" and re.fullmatch(r"[0-9a-fA-F]{4}", body[i + 2:i + 6]):
            out.append(chr(int(body[i + 2:i + 6], 16)))
            i += 6
        else:
            return None
    return "".join(out)


_STR = r'"((?:[^"\\]|\\.)*)"'
CASE_RE = re.compile(r"(?<![\w.])new\(\s*" + _STR + r"\s*,\s*" + _STR + r"\s*,\s*" + _STR + r"\s*\)")
NEW_RE = re.compile(r"(?<![\w.])new\(")
CASE_LINE_RE = re.compile(r"^\s*[\[{(]?\s*new\(")


def strip_comments(text, in_block):
    """1 行から // と /* */ のコメントを除く (文字列の中は触らない)。(コメントを除いた行, 行末で /* の中か)。"""
    out = []
    i = 0
    while i < len(text):
        if in_block:
            j = text.find("*/", i)
            if j < 0:
                return "".join(out), True
            i, in_block = j + 2, False
        elif text[i] == '"':
            j = i + 1
            while j < len(text) and text[j] != '"':
                j += 2 if text[j] == "\\" else 1
            out.append(text[i:j + 1])
            i = j + 1
        elif text.startswith("//", i):
            break
        elif text.startswith("/*", i):
            i, in_block = i + 2, True
        else:
            out.append(text[i])
            i += 1
    return "".join(out), in_block


def representable(cat, typed, expected):
    """4 列 (TSV) で表せ、評価表で比べられる値か。"""
    if not typed or not expected or typed != typed.strip() or expected != expected.strip():
        return False
    if any(c < " " or c == "\x7f" for c in cat + typed + expected):
        return False
    if typed.startswith("#"):
        return False  # TSV の # 始まりはコメント行になる
    return all(" " <= c <= "~" for c in typed)  # かなで打つ行は評価表で比べ方を付けられない


def extract_cases(diff_text):
    """diff の追加行から new("カテゴリ", "打鍵", "期待") を取り出す。(cases, skipped)。

    cases = [(カテゴリ, 打鍵, 期待)]。削除行・コメントは無視する。1 行に複数あれば全部拾う。
    skipped = 4 列で表せない new(...) の数 (Before: などの名前付き引数、扱えないエスケープ、
    # 始まり・非 ASCII・Tab・改行を含む打鍵、前後に空白がある値、空の打鍵・期待)。
    """
    cases, skipped = [], 0
    in_block = False
    for line in diff_text.splitlines():
        if line.startswith(("@@", "diff ", "+++", "---", "-")) or line[:1] not in ("+", " "):
            in_block = in_block and not line.startswith(("@@", "diff "))
            continue
        code, in_block = strip_comments(line[1:], in_block)  # 文脈行も通して /* の状態を追う
        if not line.startswith("+") or not CASE_LINE_RE.match(code):
            continue
        found = 0
        for m in CASE_RE.finditer(code):
            found += 1
            cat, typed, expected = (csharp_unescape(g) for g in m.groups())
            if None in (cat, typed, expected) or not representable(cat, typed, expected):
                skipped += 1
            else:
                cases.append((cat, typed, expected))
        skipped += len(NEW_RE.findall(code)) - found
    return cases, skipped


def parse_seen(doc):
    m = SEEN_RE.search(doc or "")
    return m.group(1) if m else None


def set_seen(doc, new_hash):
    """先頭の seen 印を書き換える (無ければ先頭に足す)。"""
    mark = f"<!-- seen: {new_hash} -->"
    if SEEN_RE.search(doc):
        return SEEN_RE.sub(lambda _: mark, doc, count=1)
    return mark + "\n" + doc


def insert_section(doc, section):
    """最初の `## ` の節の前に section を差し込む (新しい順)。節が 1 つも無ければ末尾に足す。"""
    lines = doc.split("\n")
    block = section.rstrip("\n").split("\n") + [""]
    for i, line in enumerate(lines):
        if line.startswith("## "):
            return "\n".join(lines[:i] + block + lines[i:])
    base = doc if doc.endswith("\n") or doc == "" else doc + "\n"
    return base + ("\n" if base and not base.endswith("\n\n") else "") + "\n".join(block)


def cell(text):
    """表の 1 セルに入れられる形にする (本家由来の文字列用)。"""
    text = re.sub(r"[\r\n\t]", " ", str(text))
    return text.replace("|", "\\|").replace("<", "&lt;")


def code_span(text):
    return "`" + cell(text).replace("`", "ˋ") + "`"


def newcases_labels(counts, skips, results, evaluated):
    """行ごとの「当たり/件数」。counts[i] 件の候補が results に順に並んでいる。"""
    labels, pos = [], 0
    for n, skipped in zip(counts, skips):
        label = "-"
        if n or skipped:
            hit = str(sum(1 for x in results[pos:pos + n] if x["ok"])) if evaluated else "-"
            label = f"{hit}/{n}" if n else "0/0"
            if skipped:
                label += f" (+4 列で表せない {skipped})"
        labels.append(label)
        pos += n
    return labels


def render_section(heading, rows, candidate_note):
    """rows = 表の 1 行ずつの dict。candidate_note = 表の後の文 (無ければ None)。"""
    out = [heading, "", "| hash | 日付 | PR | 件名 | 分類 | 取り込み | 当たるか | 新ケース (当たり/件数) |", "|---|---|---|---|---|---|---|---|"]
    for r in rows:
        out.append("| " + " | ".join(cell(r[k]) for k in ("hash", "date", "pr", "subject", "category", "imported", "applies", "newcases")) + " |")
    out.append("")
    if candidate_note:
        out.append(candidate_note)
        out.append("")
    return "\n".join(out)


# ---------------------------------------------------------------- git・dotnet

def run(cmd, cwd=None, input_text=None, check=True):
    p = subprocess.run(cmd, cwd=cwd, input=input_text, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if check and p.returncode != 0:
        raise WatchError(f"失敗しました: {' '.join(cmd)}\n{p.stderr.strip()}")
    return p


def git(tree, *args, input_text=None, check=True):
    return run(["git", "-C", tree, *args], input_text=input_text, check=check)


def list_commits(upstream, since, until):
    """first-parent の PR 単位で、古い順に [{hash, short, date, subject, parents}]。"""
    sep = "\x1f"
    out = git(upstream, "log", "--first-parent", "--reverse", f"--format=%H{sep}%cs{sep}%P{sep}%s", f"{since}..{until}").stdout
    commits = []
    for line in out.splitlines():
        h, d, parents, subject = line.split(sep, 3)
        commits.append({"hash": h, "short": h[:7], "date": d, "parents": parents.split(), "subject": subject})
    return commits


def changed_files(upstream, h):
    out = git(upstream, "diff-tree", "-m", "--first-parent", "--no-commit-id", "--name-only", "-r", h).stdout
    return [p for p in out.splitlines() if p]


def mac_subjects(mac):
    """Mac 版 (HEAD) のコミットの [(7 桁 hash, 件名)]。"""
    out = git(mac, "log", "HEAD", "--abbrev=7", "--format=%h\x1f%s").stdout
    return [tuple(line.split("\x1f", 1)) for line in out.splitlines() if "\x1f" in line]


def import_state(subjects, short, pr, doc_texts):
    """Mac 版の件名に `本家 <7桁>` があれば 取り込み済み、追跡文書に hash か #NNN があれば 記録あり、どちらも無ければ 未。"""
    pattern = re.compile(r"本家 " + re.escape(short) + r"(?![0-9a-fA-F])")
    for h, subject in subjects:
        if pattern.search(subject):
            return f"取り込み済み ({h})"
    for name, text in doc_texts.items():
        if short in text or (pr and re.search(r"#" + pr + r"(?!\d)", text)):
            return f"記録あり ({os.path.basename(name)})"
    return "未"


def fits_as_is(upstream, mac, h):
    """判定・変換の差分が Mac 版にそのまま当たるか。そのまま / 手移植 / 対象ファイルなし。"""
    # merge コミットは format-patch だと空になるので、first-parent との差分を取る。
    # パッチはバイトのまま Mac 版に渡す (UTF-8 でない行を置き換えて壊さない)。
    d = subprocess.run(["git", "-C", upstream, "diff", "--binary", f"{h}^1", h, "--", *CORE_PATHS], capture_output=True)
    if d.returncode != 0:
        raise WatchError(f"失敗しました: git diff {h}\n{d.stderr.decode('utf-8', 'replace').strip()}")
    if not d.stdout.strip():
        return "対象ファイルなし"
    ok = subprocess.run(["git", "-C", mac, "apply", "--check", "-"], input=d.stdout, capture_output=True).returncode == 0
    return "そのまま" if ok else "手移植"


def quality_cases(upstream, h):
    diff = git(upstream, "diff", f"{h}^1", h, "--", QUALITY_TESTS).stdout
    return extract_cases(diff)


def run_eval(mac, tsv, out_json):
    env = dict(os.environ)
    dotnet_home = os.path.expanduser("~/.dotnet")
    if os.path.isdir(dotnet_home):
        env["PATH"] = dotnet_home + os.pathsep + env.get("PATH", "")
    cmd = ["dotnet", "run", "-c", "Release", "--project", "src/Meltype.Core.Tests", "--", "--expect", tsv, out_json]
    try:
        p = subprocess.run(cmd, cwd=mac, env=env, capture_output=True, text=True, encoding="utf-8")
    except OSError as e:
        raise WatchError(f"dotnet を起動できません ({e})。--no-eval で省けます")
    if p.returncode != 0:
        raise WatchError("dotnet の評価が失敗しました:\n" + (p.stdout + p.stderr)[-1500:])
    with open(out_json, encoding="utf-8") as f:
        return json.load(f)


# ---------------------------------------------------------------- 本体

def parse_args(argv):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--upstream", default=os.environ.get("MELTYPE_UPSTREAM"), help="本家の clone (既定: 環境変数 MELTYPE_UPSTREAM)")
    p.add_argument("--mac", default=os.path.dirname(os.path.dirname(os.path.abspath(__file__))), help="Mac 版の作業ツリー (既定: このスクリプトのあるリポジトリ)")
    p.add_argument("--since", help=f"ここより後を見る (既定: {WATCH_DOC} 先頭の seen)")
    p.add_argument("--no-fetch", action="store_true")
    p.add_argument("--dry-run", action="store_true", help="文書・候補ファイルを書かず、標準出力だけ")
    p.add_argument("--no-eval", action="store_true", help="dotnet を回さない")
    return p.parse_args(argv)


def watch(a):
    if not a.upstream:
        raise WatchError("本家の clone が指定されていません。--upstream <clone> か環境変数 MELTYPE_UPSTREAM を使ってください")
    upstream, mac = os.path.abspath(a.upstream), os.path.abspath(a.mac)
    for label, path in (("--upstream", upstream), ("--mac", mac)):
        if not os.path.isdir(path):
            raise WatchError(f"{label} {path} がフォルダーではありません")
        if run(["git", "-C", path, "rev-parse", "--git-dir"], check=False).returncode != 0:
            raise WatchError(f"{label} {path} は git の作業ツリーではありません")

    doc_path = os.path.join(mac, WATCH_DOC)
    doc = open(doc_path, encoding="utf-8").read() if os.path.exists(doc_path) else None
    since = a.since or (parse_seen(doc) if doc else None)
    if not since:
        raise WatchError(f"どこから見るか分かりません。--since <hash> を付けてください ({WATCH_DOC} 先頭の seen もありません)")

    if not a.no_fetch:
        git(upstream, "fetch", "-q", "origin", "main")
    p = git(upstream, "rev-parse", "--verify", "-q", "--end-of-options", f"{since}^{{commit}}", check=False)
    if p.returncode != 0 or not p.stdout.strip():
        raise WatchError(f"--since {since} が本家の clone にありません")
    since = p.stdout.strip()  # 以降は完全な hash だけを使う
    new_hash = git(upstream, "rev-parse", "origin/main").stdout.strip()  # fetch の直後に 1 回だけ取る
    since_short = since[:7]

    today = date.today().isoformat()
    commits = list_commits(upstream, since, new_hash)
    subjects = mac_subjects(mac)
    doc_texts = {}
    for name in RECORD_DOCS:
        path = os.path.join(mac, name)
        if os.path.exists(path):
            doc_texts[name] = open(path, encoding="utf-8").read()

    rows, all_cases, n_core, n_unimported = [], [], 0, 0
    for c in commits:
        pr = pr_number(c["subject"])
        primary, others = classify(changed_files(upstream, c["hash"]))
        imported = import_state(subjects, c["short"], pr, doc_texts)
        row = {"hash": c["short"], "date": c["date"], "pr": f"#{pr}" if pr else "-", "subject": c["subject"],
               "category": label_of(primary, others), "imported": imported, "applies": "-", "newcases": "-", "_cases": []}
        if imported == "未":
            n_unimported += 1
        if primary == "判定・変換":
            n_core += 1
            row["applies"] = fits_as_is(upstream, mac, c["hash"])
            cases, skipped = quality_cases(upstream, c["hash"])
            src = f"本家 {c['short']}" + (f" #{pr}" if pr else "")
            row["_cases"], row["_skipped"] = [(typed, exp, src) for _, typed, exp in cases], skipped
            all_cases += row["_cases"]
        rows.append(row)

    # 候補ファイルと評価 (一時フォルダーで評価し、成功してから作業ツリーに置く)
    rel_candidate = f"eval/candidates/upstream-{today}-{since_short}.tsv"
    results, evaluated = [], False
    candidate_src = None
    work = tempfile.TemporaryDirectory()
    try:
        if all_cases:
            tsv_lines = [f"# 本家 {since_short}..{new_hash[:7]} の QualityTests.cs に足された正解ケース (tools/upstream_watch.py が {today} に書いた)。",
                         "# 正解表 (eval/cases) には自動で足さない。人が確かめてから upstream-quality.tsv の末尾に移す。",
                         ] + [f"{t}\t{e}\tfull\t{s}" for t, e, s in all_cases]
            candidate_src = os.path.join(work.name, os.path.basename(rel_candidate))
            with open(candidate_src, "w", encoding="utf-8") as f:
                f.write("\n".join(tsv_lines) + "\n")
            if not a.no_eval:
                results = run_eval(mac, candidate_src, os.path.join(work.name, "result.json"))
                if len(results) != len(all_cases):
                    raise WatchError(f"評価の件数が合いません ({len(results)} 件 / 候補 {len(all_cases)} 件)")
                evaluated = True
            if not a.dry_run:
                dest = os.path.join(mac, rel_candidate)
                os.makedirs(os.path.dirname(dest), exist_ok=True)
                shutil.copy(candidate_src, dest)
    finally:
        work.cleanup()

    # 行ごとの当たり/件数
    labels = newcases_labels([len(r["_cases"]) for r in rows], [r.get("_skipped", 0) for r in rows], results, evaluated)
    for r, label in zip(rows, labels):
        r["newcases"] = label
    total_hit = sum(1 for x in results if x["ok"])
    misses = [x for x in results if not x["ok"]]

    note = None
    if all_cases:
        if evaluated:
            note = f"候補ファイル: `{rel_candidate}` (Mac 版で {total_hit}/{len(all_cases)} 当たり)"
            if misses:
                note += "。外れ:\n\n" + "\n".join(f"- {code_span(x['typed'])} → {code_span(x['expected'])} (Mac 版の実際: {code_span(x['actual'])})" for x in misses[:MAX_MISSES])
                if len(misses) > MAX_MISSES:
                    note += f"\n- …ほか {len(misses) - MAX_MISSES} 件"
        else:
            note = f"候補ファイル: `{rel_candidate}` ({len(all_cases)} 件。--no-eval のため、当たるかは未確認)"

    elif commits:
        note = "新しい正解ケース (QualityTests.cs の追加行) はありません。"

    heading = f"## {today} ({since_short}..{new_hash[:7]}、{len(commits)} 件)"
    section = render_section(heading, rows, note) if commits else None

    # 文書の更新
    new_doc = doc if doc is not None else (
        "# 本家追従の候補表\n\n"
        "`tools/upstream_watch.py` が更新します (使い方は docs/DEVELOPMENT.md の「本家追従」)。新しい節が上です。\n"
        "候補ファイル (`eval/candidates/`) の新しい正解ケースは、人が確かめてから `eval/cases/upstream-quality.tsv` に移します。\n")
    new_doc = set_seen(new_doc, new_hash)
    if section:
        new_doc = insert_section(new_doc, section)
    if not a.dry_run:
        with open(doc_path, "w", encoding="utf-8") as f:
            f.write(new_doc)

    # 標準出力の要約
    lines = [f"本家の新コミット {len(commits)} 件 ({since_short}..{new_hash[:7]})" + (" [dry-run: 何も書いていません]" if a.dry_run else ""),
             f"判定・変換 {n_core} 件、未取り込み {n_unimported} 件"]
    if all_cases:
        lines.append(f"新ケース " + (f"{total_hit}/{len(all_cases)} 当たり" if evaluated else f"{len(all_cases)} 件 (評価なし)"))
        lines.append(f"候補ファイル: {rel_candidate}")
    else:
        lines.append("新ケース 0 件")
    if commits and a.dry_run:
        lines.append("--- 節 ---")
        lines.append(section.rstrip("\n"))
    print("\n".join(lines))


def main(argv):
    try:
        a = parse_args(argv)
        watch(a)
    except WatchError as e:
        print(f"エラー: {e}", file=sys.stderr)
        return 2
    except Exception as e:  # 想定外の失敗も文書は書かずに終わる
        print(f"エラー: 想定外の失敗です ({type(e).__name__}: {e})", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
