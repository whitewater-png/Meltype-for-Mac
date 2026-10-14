#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
#
# 2 つの作業ツリーで同じ正解表 (eval/cases/*.tsv) を回し、判定の差 (broken / fixed など) を出す。
#
#   tools/eval-compare.sh <base-tree> <new-tree> <cases-dir> <out-dir>
#
#   base-tree  比べる元 (例: 本家 Meltype の clone、Mac 版の前回のコミットの作業ツリー)
#   new-tree   比べる先 (例: Mac 版の今の作業ツリー)
#   cases-dir  正解表のフォルダー (例: eval/cases)
#   out-dir    出力先。base.json・new.json・compare.md ができる
#
# 終了コード: broken (base で当たり new で外れた) が 1 件でもあれば 1。問題なしは 0。dotnet や入力の失敗は 2。
# --expect-all が無い作業ツリー (本家など) は、tools/eval_legacy_base.py が --expect を表ごとに回して同じ形の JSON にする。
# dotnet は作業ツリーの中に bin/obj を作る (git の対象外)。読み取り専用にしたい clone は、コピーを渡す。
# macOS には timeout コマンドが無いので使わない。bash --noprofile --norc でも動く。

set -euo pipefail

if [ "$#" -ne 4 ]; then
  sed -n '2,15p' "$0" >&2
  exit 2
fi

[ -d "$HOME/.dotnet" ] && export PATH="$HOME/.dotnet:$PATH"

script_dir="$(cd "$(dirname "$0")" && pwd)"
abs() { (cd "$1" && pwd); }

base_tree="$(abs "$1")"
new_tree="$(abs "$2")"
cases_dir="$(abs "$3")"
mkdir -p "$4"
out_dir="$(abs "$4")"

run_tree() {
  local tree="$1" out="$2"
  if grep -q -- '"--expect-all"' "$tree/src/Meltype.Core.Tests/Program.cs"; then
    # global.json・nuget.config は実行したフォルダーから探されるので、作業ツリーの中で動かす
    (cd "$tree" && dotnet run -c Release --project src/Meltype.Core.Tests -- --expect-all "$cases_dir" "$out" >/dev/null)
  else
    echo "(--expect-all が無い作業ツリー: ${tree} 。--expect を表ごとに回して連結します)" >&2
    python3 "$script_dir/eval_legacy_base.py" "$tree" "$cases_dir" "$out"
  fi
}

# 実行の失敗 (dotnet のビルド・表の誤りなど) は、回帰 (1) と区別して 2 にする
run_tree "$base_tree" "$out_dir/base.json" || { echo "base の実行に失敗しました" >&2; exit 2; }
run_tree "$new_tree" "$out_dir/new.json" || { echo "new の実行に失敗しました" >&2; exit 2; }

status=0
python3 "$script_dir/eval_compare.py" "$out_dir/base.json" "$out_dir/new.json" \
  --md "$out_dir/compare.md" --base-label "$(basename "$base_tree")" --new-label "$(basename "$new_tree")" || status=$?
exit "$status"
