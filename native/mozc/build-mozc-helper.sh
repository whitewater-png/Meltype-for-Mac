#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
#
# Mozc の変換ヘルパー (meltype_mozc_helper) を Linux 用にビルドする (Windows 版は Build-MozcHelper.ps1)。
#   native/mozc/build-mozc-helper.sh [Mozc を置く場所 (既定: ~/mozc)] [Bazel のディスクキャッシュ]
# 必要なもの: git、Bazelisk (bazel、環境変数 BAZEL で場所を指定できる)、clang、Mozc の Linux のビルドに要るライブラリ (README.md)。
# できたものは native/mozc/bin/linux/ に置く。
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
mozc="${1:-$HOME/mozc}"
cache="${2:-}"
commit="$(tr -d '[:space:]' < "$here/MOZC_COMMIT")"
out="$here/bin/linux"

# src の有無ではなく .git で見る (GitHub Actions のキャッシュが src/third_party_cache だけを先に戻すため)。
if [[ ! -d "$mozc/.git" ]]; then
    mkdir -p "$mozc"
    git -C "$mozc" init -q
    git -C "$mozc" remote add origin https://github.com/google/mozc.git
    git -C "$mozc" fetch -q --depth 1 origin "$commit"
    git -C "$mozc" checkout -q FETCH_HEAD
    git -C "$mozc" submodule update -q --init --recursive --depth 1
fi
src="$mozc/src"

# ヘルパーのソースと BUILD の定義を Mozc の converter パッケージに入れる (エンジンを使えるのがこのパッケージのため)。
cp "$here/meltype_mozc_helper.cc" "$src/converter/"
if ! grep -q 'name = "meltype_mozc_helper"' "$src/converter/BUILD.bazel"; then
    printf '\n%s\n' "$(cat "$here/BUILD.fragment")" >> "$src/converter/BUILD.bazel"
fi

cd "$src"
options=(build //converter:meltype_mozc_helper --config oss_linux --config release_build)
[[ -n "$cache" ]] && options+=("--disk_cache=$cache")
"${BAZEL:-bazel}" "${options[@]}"

mkdir -p "$out"
cp -f bazel-bin/converter/meltype_mozc_helper "$out/"
# Bazel の出力は読み取り専用なので、上書きできるようにする。
chmod u+w,a+x "$out/meltype_mozc_helper"
cp -f data/installer/credits_en.html "$out/MOZC-CREDITS.html"
cp -f "$mozc/LICENSE" "$out/MOZC-LICENSE.txt"
echo "作成しました: $out/meltype_mozc_helper"
