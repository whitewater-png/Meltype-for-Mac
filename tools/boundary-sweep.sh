#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
#
# 判定や辞書の変更の悪化を、大量の境界例 (辞書の語・助詞・打ち間違い・語の連結) で測る。
# 同じ入力の一覧を 2 つの作業ツリーで打って Enter した結果を比べ、日本語の損失・英語の損失・その他の変化に分ける。
#
#   tools/boundary-sweep.sh <base-tree> <new-tree> <out-dir> [full|light]
#
#   base-tree  比べる元 (例: 変更前のコミットの作業ツリー)
#   new-tree   比べる先 (例: 変更後の作業ツリー)
#   out-dir    出力先 (無いか空のフォルダー)。inputs/ (入力の一覧)・base/ new/ (結果の JSON)・compare.md・diff.tsv ができる
#   full|light 規模 (既定 full。入力の数と所要時間は docs/DEVELOPMENT.md)。light は辞書の語を 20 語に 1 語だけ取る (手元・査読の確認用)
#
# 入力の一覧は、このスクリプトのあるリポジトリの辞書から 1 回だけ作り、両方の木で同じものを使う。base と new は別々のコアで同時に回す。
# 終了コード: 損失 (日本語の損失・英語の損失) が 1 件でもあれば 1。無ければ 0。dotnet や入力の失敗は 2。
# --expect-all が無い作業ツリーは、tools/eval_legacy_base.py が --expect を表ごとに回して同じ形の JSON にする。
# dotnet は作業ツリーの中に bin/obj を作る (git の対象外)。macOS には timeout コマンドが無いので使わない。bash --noprofile --norc でも動く。

set -euo pipefail

if [ "$#" -lt 3 ] || [ "$#" -gt 4 ]; then
  sed -n '2,16p' "$0" >&2
  exit 2
fi

[ -d "$HOME/.dotnet" ] && export PATH="$HOME/.dotnet:$PATH"

script_dir="$(cd "$(dirname "$0")" && pwd)"
abs() { (cd "$1" && pwd); }

base_tree="$(abs "$1")"
new_tree="$(abs "$2")"
mkdir -p "$3"
out_dir="$(abs "$3")"
scale="${4:-full}"

# 入力の一覧を作る (空でない out-dir は拒む。前の結果に混ぜない)
if [ -n "$(ls -A "$out_dir")" ]; then
  echo "$out_dir は空ではありません" >&2
  exit 2
fi
python3 "$script_dir/boundary_sweep.py" --out "$out_dir/inputs" --scale "$scale" || { echo "入力の一覧の作成に失敗しました" >&2; exit 2; }

run_chunk() {
  local tree="$1" chunk="$2" out="$3"
  if grep -q -- '"--expect-all"' "$tree/src/Meltype.Core.Tests/Program.cs"; then
    # global.json・nuget.config は実行したフォルダーから探されるので、作業ツリーの中で動かす
    (cd "$tree" && dotnet run -c Release --project src/Meltype.Core.Tests -- --expect-all "$chunk" "$out" >/dev/null)
  else
    python3 "$script_dir/eval_legacy_base.py" "$tree" "$chunk" "$out"
  fi
}

run_tree() {
  local tree="$1" label="$2"
  mkdir -p "$out_dir/$label"
  for chunk in "$out_dir"/inputs/chunk-*; do
    echo "[$label] $(basename "$chunk")" >&2
    run_chunk "$tree" "$chunk" "$out_dir/$label/$(basename "$chunk").json" || return 1
  done
}

# base と new は別々のコア・別々の作業ツリーで同時に回す (同じ作業ツリーを渡されたときは、ビルドが衝突するので順に回す)。
# 実行の失敗は、損失 (1) と区別して 2 にする
if [ "$base_tree" = "$new_tree" ]; then
  run_tree "$base_tree" base || { echo "base の実行に失敗しました" >&2; exit 2; }
  run_tree "$new_tree" new || { echo "new の実行に失敗しました" >&2; exit 2; }
else
  run_tree "$base_tree" base & base_pid=$!
  new_failed=0
  run_tree "$new_tree" new || new_failed=1
  base_failed=0
  wait "$base_pid" || base_failed=1
  [ "$base_failed" -eq 0 ] || { echo "base の実行に失敗しました" >&2; exit 2; }
  [ "$new_failed" -eq 0 ] || { echo "new の実行に失敗しました" >&2; exit 2; }
fi

status=0
python3 "$script_dir/boundary_compare.py" "$out_dir/base" "$out_dir/new" \
  --md "$out_dir/compare.md" --tsv "$out_dir/diff.tsv" --manifest "$out_dir/inputs/manifest.json" \
  --base-label "$(basename "$base_tree")" --new-label "$(basename "$new_tree")" || status=$?
exit "$status"
