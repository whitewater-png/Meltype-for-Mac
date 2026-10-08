#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
#
# 配布用の zip (dist/Meltype-mac-<version>.zip) を作る。先に ./build.sh で build/Meltype.app を作っておく。
#   cd mac && ./make-zip.sh
set -euo pipefail
cd "$(dirname "$0")"

APP=build/Meltype.app
[[ -d "$APP" ]] || { echo "build/Meltype.app がありません。先に ./build.sh を実行してください。" >&2; exit 1; }
VERSION="$(plutil -extract CFBundleShortVersionString raw "$APP/Contents/Info.plist")"
OUT="dist/Meltype-mac"

rm -rf "$OUT" "dist/Meltype-mac-$VERSION.zip"
mkdir -p "$OUT"
ditto "$APP" "$OUT/Meltype.app"
cp install.sh uninstall.sh "Install Meltype.command" "Uninstall Meltype.command" INSTALL.txt "$OUT/"
# ライセンス表示 (GPL-3.0・サードパーティー通知)。アプリの中 (Contents/Resources/Licenses) にも入っているが、zip を開いたところで読めるように
cp ../LICENSE "$OUT/LICENSE.txt"
cp ../THIRD-PARTY-NOTICES.md "$OUT/"

chmod +x "$OUT"/*.sh "$OUT"/*.command
# --norsrc: 拡張属性 (._ファイル) を zip に入れない
(cd dist && ditto -c -k --norsrc --keepParent Meltype-mac "Meltype-mac-$VERSION.zip")

echo "作成しました: dist/Meltype-mac-$VERSION.zip"
shasum -a 256 "dist/Meltype-mac-$VERSION.zip"
