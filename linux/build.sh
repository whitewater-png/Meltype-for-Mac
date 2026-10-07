#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
#
# Meltype の Linux 版 (IBus) を組み立てる。できたものは linux/build/Meltype-linux/ に置く。
#   linux/build.sh
# 先に Mozc の変換ヘルパーを native/mozc/build-mozc-helper.sh でビルドしておく (native/mozc/bin/linux/)。
# 必要なもの: .NET 10 SDK、clang (NativeAOT のリンクに使う)、zlib (zlib1g-dev)
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(dirname "$here")"
out="$here/build/Meltype-linux"
mozc="$root/native/mozc/bin/linux"

case "$(uname -m)" in
    x86_64) rid=linux-x64 ;;
    aarch64) rid=linux-arm64 ;;
    *) echo "対応していない CPU です: $(uname -m)" >&2; exit 1 ;;
esac
if [[ ! -x "$mozc/meltype_mozc_helper" ]]; then
    echo "Mozc の変換ヘルパーがありません。先に native/mozc/build-mozc-helper.sh を実行してください。" >&2
    exit 1
fi

rm -rf "$here/build"
mkdir -p "$out/mozc"

echo "== 1/2 本体 (C#, NativeAOT) をビルド"
# リポジトリの nuget.config は NuGet を使わない設定 (Windows の開発環境用) なので、NativeAOT のコンパイラを取るために nuget.org を指定する。
dotnet publish "$root/src/Meltype.Mac.Native/Meltype.Mac.Native.csproj" -c Release -r "$rid" \
    -p:PublishAot=true -p:NativeLib=Shared -p:StripSymbols=true \
    --source https://api.nuget.org/v3/index.json -o "$here/build/native"
cp "$here/build/native/MeltypeNative.so" "$out/libMeltypeNative.so"

echo "== 2/2 組み立て"
cp "$here/ibus-engine-meltype" "$here/meltype.xml" "$here/icon.png" "$here/install.sh" "$here/uninstall.sh" "$out/"
cp "$here/TESTER-README.txt" "$out/はじめにお読みください.txt"
cp "$root/LICENSE" "$root/THIRD-PARTY-NOTICES.md" "$out/"
cp "$mozc/meltype_mozc_helper" "$mozc/MOZC-LICENSE.txt" "$mozc/MOZC-CREDITS.html" "$out/mozc/"
chmod +x "$out/ibus-engine-meltype" "$out/install.sh" "$out/uninstall.sh" "$out/mozc/meltype_mozc_helper"
echo "作成しました: $out"
