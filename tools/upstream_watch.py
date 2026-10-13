#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""本家 Meltype の新しいコミットを PR 単位で拾い、Mac 版への取り込み状況と新しい正解ケースを候補表にまとめる。

    MELTYPE_UPSTREAM=<本家の clone> python3 tools/upstream_watch.py [--since <hash>] [--no-fetch] [--dry-run] [--no-eval]

やること (docs/UPSTREAM_WATCH.md の先頭の `<!-- seen: <hash> -->` から origin/main までを見る):
  1. 本家の clone を fetch する (--no-fetch で省く。通信はこれだけ)
  2. first-parent の PR 単位でコミットを並べ、変更ファイルから分類する (判定・変換 / Windows / 本家 Mac / Linux / 文書)
  3. Mac 版に取り込み済みか (hash が Mac 版のコミットにあるか、追跡文書に記録があるか) を調べる
  4. 判定・変換のコミットは、Mac 版にそのまま当たるか (git apply --check) を調べる
  5. 本家の QualityTests.cs に足された new("カテゴリ", "打鍵", "期待") を eval/candidates/upstream-<日付>.tsv に書き、
     Mac 版で当たるか数える (--no-eval で省く)
  6. docs/UPSTREAM_WATCH.md の先頭に節を足す (--dry-run なら書かず、標準出力だけ)

候補ファイルは正解表 (eval/cases) に自動では足さない。人が確かめてから移す。
終了コード: 0 = 正常 (新コミット 0 件も 0)、2 = 引数・環境の誤り。標準ライブラリ + git + dotnet だけで動く。
"""

import argparse
import json
import os
import re
import subprocess
import sys
import tempfile
from datetime import date

WATCH_DOC = "docs/UPSTREAM_WATCH.md"
RECORD_DOCS = ["docs/UPSTREAM_PR_TRACKER.md", "docs/UPSTREAM_PR_SURVEY.md", "docs/IMPROVEMENT_PLAN.md"]
QUALITY_TESTS = "src/Meltype.Core.Tests/QualityTests.cs"
CORE_PATHS = ["src/Meltype.Core", "dictionaries", "src/Meltype.Core.Tests"]
MAX_MISSES = 20

SEEN_RE = re.compile(r"<!--\s*seen:\s*([0-9a-fA-F]+)\s*-->")

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
    """subject の最初の #NNN を返す (無ければ None)。"""
    m = re.search(r"#(\d+)", subject)
    return m.group(1) if m else None


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
CASE_RE = re.compile(r"^\s*new\(\s*" + _STR + r"\s*,\s*" + _STR + r"\s*,\s*" + _STR + r"\s*\)\s*,?\s*(?://.*)?$")
CASE_START_RE = re.compile(r"^\s*new\(")


def extract_cases(diff_text):
    """diff の追加行から new("カテゴリ", "打鍵", "期待") を取り出す。(cases, skipped)。

    cases = [(カテゴリ, 打鍵, 期待)]。削除行・コメント行は無視する。
    skipped = 4 列で表せない行の数 (Before: などの名前付き引数、扱えないエスケープ、Tab・改行・非 ASCII を含む打鍵、空の打鍵・期待)。
    """
    cases, skipped = [], 0
    for line in diff_text.splitlines():
        if not line.startswith("+") or line.startswith("+++"):
            continue
        text = line[1:]
        if not CASE_START_RE.match(text):
            continue
        m = CASE_RE.match(text)
        if not m:
            skipped += 1
            continue
        cat, typed, expected = (csharp_unescape(g) for g in m.groups())
        if None in (cat, typed, expected):
            skipped += 1
            continue
        typed, expected = typed.strip(), expected.strip()
        if not typed or not expected or any(c in "\t\r\n" for c in typed + expected + cat):
            skipped += 1
            continue
        if any(c > "~" or c < " " for c in typed):
            skipped += 1  # かなで打つ行は評価表で比べ方を付けられない
            continue
        cases.append((cat, typed, expected))
    return cases, skipped


def parse_seen(doc):
    m = SEEN_RE.search(doc or "")
    return m.group(1) if m else None


def set_seen(doc, new_hash):
    """先頭の seen 印を書き換える (無ければ先頭に足す)。"""
    mark = f"<!-- seen: {new_hash} -->"
    if SEEN_RE.search(doc):
        return SEEN_RE.sub(mark, doc, count=1)
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
    return str(text).replace("|", "\\|").replace("\n", " ")


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
    p = subprocess.run(cmd, cwd=cwd, input=input_text, capture_output=True, text=True, encoding="utf-8")
    if check and p.returncode != 0:
        raise WatchError(f"失敗しました: {' '.join(cmd)}\n{p.stderr.strip()}")
    return p


def git(tree, *args, input_text=None, check=True):
    return run(["git", "-C", tree, *args], input_text=input_text, check=check)


def list_commits(upstream, since):
    """first-parent の PR 単位で、古い順に [{hash, short, date, subject, parents}]。"""
    sep = "\x1f"
    out = git(upstream, "log", "--first-parent", "--reverse", f"--format=%H{sep}%cs{sep}%P{sep}%s", f"{since}..origin/main").stdout
    commits = []
    for line in out.splitlines():
        h, d, parents, subject = line.split(sep, 3)
        commits.append({"hash": h, "short": h[:7], "date": d, "parents": parents.split(), "subject": subject})
    return commits


def changed_files(upstream, h):
    out = git(upstream, "diff-tree", "-m", "--first-parent", "--no-commit-id", "--name-only", "-r", h).stdout
    return [p for p in out.splitlines() if p]


def import_state(mac, short, pr, doc_texts):
    """Mac 版のコミットに hash があれば 取り込み済み、追跡文書に hash か #NNN があれば 記録あり、どちらも無ければ 未。"""
    hits = git(mac, "log", "--all", "--abbrev=7", "--format=%h", f"--grep={short}", check=False).stdout.split()
    if hits:
        return f"取り込み済み ({hits[0]})"
    for name, text in doc_texts.items():
        if short in text or (pr and re.search(r"#" + pr + r"(?!\d)", text)):
            return f"記録あり ({os.path.basename(name)})"
    return "未"


def fits_as_is(upstream, mac, h):
    """判定・変換の差分が Mac 版にそのまま当たるか。そのまま / 手移植 / 対象ファイルなし。"""
    # merge コミットは format-patch だと空になるので、first-parent との差分を取る。
    patch = git(upstream, "diff", "--binary", f"{h}^1", h, "--", *CORE_PATHS).stdout
    if not patch.strip():
        return "対象ファイルなし"
    return "そのまま" if git(mac, "apply", "--check", "-", input_text=patch, check=False).returncode == 0 else "手移植"


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
    if git(upstream, "rev-parse", "--verify", "-q", f"{since}^{{commit}}", check=False).returncode != 0:
        raise WatchError(f"--since {since} が本家の clone にありません")
    new_hash = git(upstream, "rev-parse", "origin/main").stdout.strip()
    since_short = git(upstream, "rev-parse", "--short=7", f"{since}^{{commit}}").stdout.strip()

    today = date.today().isoformat()
    commits = list_commits(upstream, since)
    doc_texts = {}
    for name in RECORD_DOCS:
        path = os.path.join(mac, name)
        if os.path.exists(path):
            doc_texts[name] = open(path, encoding="utf-8").read()

    rows, all_cases, n_core, n_unimported = [], [], 0, 0
    for c in commits:
        pr = pr_number(c["subject"])
        primary, others = classify(changed_files(upstream, c["hash"]))
        imported = import_state(mac, c["short"], pr, doc_texts)
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

    # 候補ファイルと評価
    rel_candidate = f"eval/candidates/upstream-{today}.tsv"
    results, evaluated = [], False
    tmp = None
    if all_cases:
        tsv_lines = [f"# 本家 {since_short}..{new_hash[:7]} の QualityTests.cs に足された正解ケース (tools/upstream_watch.py が {today} に書いた)。",
                     "# 正解表 (eval/cases) には自動で足さない。人が確かめてから upstream-quality.tsv の末尾に移す。",
                     ] + [f"{t}\t{e}\tfull\t{s}" for t, e, s in all_cases]
        tsv_text = "\n".join(tsv_lines) + "\n"
        if a.dry_run:
            tmp = tempfile.TemporaryDirectory()
            tsv_path = os.path.join(tmp.name, os.path.basename(rel_candidate))
        else:
            tsv_path = os.path.join(mac, rel_candidate)
            os.makedirs(os.path.dirname(tsv_path), exist_ok=True)
        with open(tsv_path, "w", encoding="utf-8") as f:
            f.write(tsv_text)
        if not a.no_eval:
            work = tmp or tempfile.TemporaryDirectory()
            try:
                results = run_eval(mac, os.path.abspath(tsv_path), os.path.join(work.name, "result.json"))
            finally:
                if work is not tmp:
                    work.cleanup()
            if len(results) != len(all_cases):
                raise WatchError(f"評価の件数が合いません ({len(results)} 件 / 候補 {len(all_cases)} 件)")
            evaluated = True

    # 行ごとの当たり/件数
    pos = 0
    for r in rows:
        n = len(r["_cases"])
        skipped = r.get("_skipped", 0)
        if n or skipped:
            hit = str(sum(1 for x in results[pos:pos + n] if x["ok"])) if evaluated else "-"
            text = f"{hit}/{n}" if n else "0/0"
            if skipped:
                text += f" (+4 列で表せない {skipped})"
            r["newcases"] = text
        pos += n
    total_hit = sum(1 for x in results if x["ok"])
    misses = [x for x in results if not x["ok"]]

    note = None
    if all_cases:
        if evaluated:
            note = f"候補ファイル: `{rel_candidate}` (Mac 版で {total_hit}/{len(all_cases)} 当たり)"
            if misses:
                note += "。外れ:\n\n" + "\n".join(f"- `{x['typed']}` → `{x['expected']}` (Mac 版の実際: `{x['actual']}`)" for x in misses[:MAX_MISSES])
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
    if tmp:
        tmp.cleanup()

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
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
