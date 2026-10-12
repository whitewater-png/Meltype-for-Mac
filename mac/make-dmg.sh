#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
#
# pkg を dmg に包む。dmg を開くと、Finder のウィンドウに pkg と開き方の図解 (背景) が出る。
#   cd mac && ./make-pkg.sh && ./make-dmg.sh             # dist/Meltype-mac-<version>.dmg
#   ./make-dmg.sh <pkg のパス>                            # その pkg を包んで、同じフォルダーに .dmg を作る
# pkg は署名していないので、開くときに許可が必要。その手順を背景と「開けないときは.txt」で見せる
# (macOS 15 以降は システム設定 → プライバシーとセキュリティ →「このまま開く」。macOS 14 以前は右クリック →「開く」)。
# Finder の配置 (.DS_Store) は dmgbuild で直接書く (Finder を操作しない。macOS 26 の Finder は
# AppleScript での背景の設定を保存しない)。dmgbuild は初回に .build/dmg-venv へ pip で入れる
# (版とハッシュは pkg/dmg/requirements.txt で固定。ネットワークが要るのは初回だけ)。
# 既にある dmg は上書きしない (公開済みのファイルと SHA-256 が変わるのを防ぐ)。
set -euo pipefail
# 引数の pkg は、実行したフォルダーから見たパスとして先に絶対パスにする (下で cd するため)
ARG_PKG=""
if [[ $# -ge 1 ]]; then
    [[ -f "$1" ]] || { echo "pkg がありません: $1" >&2; exit 1; }
    ARG_PKG="$(cd "$(dirname "$1")" && pwd)/$(basename "$1")"
fi
cd "$(dirname "$0")"

if [[ -n "$ARG_PKG" ]]; then
    PKG="$ARG_PKG"
else
    APP=build/Meltype.app
    [[ -d "$APP" ]] || { echo "build/Meltype.app がありません。先に ./build.sh と ./make-pkg.sh を実行してください。" >&2; exit 1; }
    VERSION="$(plutil -extract CFBundleShortVersionString raw "$APP/Contents/Info.plist")"
    PKG="dist/Meltype-mac-$VERSION.pkg"
fi
[[ -f "$PKG" ]] || { echo "pkg がありません: $PKG (先に ./make-pkg.sh を実行してください)" >&2; exit 1; }
PKG_NAME="$(basename "$PKG")"
[[ "$PKG_NAME" == *.pkg ]] || { echo "pkg ではありません: $PKG" >&2; exit 1; }
OUT="$(dirname "$PKG")/${PKG_NAME%.pkg}.dmg"
[[ ! -e "$OUT" ]] || { echo "既にあります (上書きしません): $OUT" >&2; exit 1; }

# dmgbuild (固定した版だけを入れる)
# requirements.txt が変わったとき・venv の Python が消えたとき (brew の更新など) は作り直す
VENV=.build/dmg-venv
REQ_SHA="$(shasum -a 256 pkg/dmg/requirements.txt | cut -d" " -f1)"
if [[ ! -x "$VENV/bin/dmgbuild" ]] || ! "$VENV/bin/python" -c 1 2>/dev/null \
   || [[ "$(cat "$VENV/.requirements.sha256" 2>/dev/null)" != "$REQ_SHA" ]]; then
    PY=""
    for c in python3.14 python3.13 python3.12 python3.11 python3.10 python3; do
        if command -v "$c" >/dev/null && "$c" -c 'import sys; sys.exit(0 if sys.version_info >= (3, 10) else 1)'; then
            PY="$(command -v "$c")"; break
        fi
    done
    [[ -n "$PY" ]] || { echo "dmgbuild には Python 3.10 以降が必要です (例: brew install python)。" >&2; exit 1; }
    rm -rf "$VENV"
    "$PY" -m venv "$VENV"
    "$VENV/bin/pip" install -q --disable-pip-version-check --require-hashes --only-binary :all: -r pkg/dmg/requirements.txt
    echo "$REQ_SHA" > "$VENV/.requirements.sha256"
fi

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# 背景 (1x と 2x を 1 つの tiff に) と、同じ内容の文字版
swift pkg/dmg/make-background.swift "$PKG_NAME" "$WORK/bg.png" "$WORK/bg@2x.png"
tiffutil -cathidpicheck "$WORK/bg.png" "$WORK/bg@2x.png" -out "$WORK/background.tiff" >/dev/null 2>&1
README="$WORK/開けないときは.txt"
TXT="$(cat pkg/dmg/README.txt)"
printf '%s\n' "${TXT//@PKG@/$PKG_NAME}" > "$README"

"$VENV/bin/dmgbuild" -s pkg/dmg/settings.py \
    -D pkg="$PKG" -D readme="$README" -D background="$WORK/background.tiff" \
    "${PKG_NAME%.pkg}" "$WORK/out.dmg" >/dev/null
# 途中で失敗したときに壊れた dmg を残さないよう、作業フォルダーで作って確かめてから移す
hdiutil verify -quiet "$WORK/out.dmg"
mv -n "$WORK/out.dmg" "$OUT"
[[ ! -e "$WORK/out.dmg" ]] || { echo "既にあります (上書きしません): $OUT" >&2; exit 1; }
echo "作成しました: $OUT"
shasum -a 256 "$OUT"
