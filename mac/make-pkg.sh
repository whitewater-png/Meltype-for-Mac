#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
#
# ダブルクリックでインストールできる Meltype-mac-<version>.pkg を作る (先に ./build.sh で build/Meltype.app を作っておく)。
#   cd mac && ./make-pkg.sh
# 署名していない (ad-hoc 署名の Meltype.app をそのまま入れる) ので、開くときに許可が必要 (macOS 15 以降は システム設定 → プライバシーとセキュリティ →「このまま開く」。macOS 14 以前は右クリック →「開く」)。
set -euo pipefail
cd "$(dirname "$0")"

APP=build/Meltype.app
[[ -d "$APP" ]] || { echo "build/Meltype.app がありません。先に ./build.sh を実行してください。" >&2; exit 1; }
VERSION="$(plutil -extract CFBundleShortVersionString raw "$APP/Contents/Info.plist")"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
mkdir -p dist

# 入れる中身。インストール先は「このユーザーのホーム」の /Library/Input Methods (= ~/Library/Input Methods)
mkdir -p "$WORK/root"
ditto "$APP" "$WORK/root/Meltype.app"

# 入れる場所を勝手に動かされない・上書き更新にする
pkgbuild --analyze --root "$WORK/root" "$WORK/component.plist" >/dev/null
# macOS 27 の pkgbuild --analyze は BundleIsRelocatable のキーを出さない (古い macOS は出す)。
# Set は「キーが無い」で失敗するので、無ければ Add する (どちらの macOS でも false になる)。
plist_set() {
    /usr/libexec/PlistBuddy -c "Set :0:$1 $3" "$WORK/component.plist" 2>/dev/null ||
        /usr/libexec/PlistBuddy -c "Add :0:$1 $2 $3" "$WORK/component.plist"
}
plist_set BundleIsRelocatable bool false
plist_set BundleOverwriteAction string upgrade

pkgbuild --root "$WORK/root" --component-plist "$WORK/component.plist" \
    --identifier io.github.yksr-melt.inputmethod.Meltype.pkg --version "$VERSION" \
    --install-location "/Library/Input Methods" --scripts pkg/scripts \
    "$WORK/Meltype-component.pkg" >/dev/null

sed "s/VERSION/$VERSION/" pkg/Distribution.xml > "$WORK/Distribution.xml"
productbuild --distribution "$WORK/Distribution.xml" --resources pkg/resources --package-path "$WORK" \
    "dist/Meltype-mac-$VERSION.pkg" >/dev/null

echo "作成しました: dist/Meltype-mac-$VERSION.pkg"
shasum -a 256 "dist/Meltype-mac-$VERSION.pkg"
