#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
#
# Meltype (Linux 版テスト版) を入れる。zip を展開したフォルダーで実行する:
#   bash install.sh
# プログラムは /opt/meltype に、IBus のコンポーネントは /usr/share/ibus/component に入れる (管理者のパスワードを聞かれる)。
set -euo pipefail
cd "$(dirname "$0")"
target=/opt/meltype

if [[ ! -f libMeltypeNative.so || ! -f ibus-engine-meltype ]]; then
    echo "libMeltypeNative.so が見つかりません。zip を展開したフォルダーで実行してください。" >&2
    exit 1
fi

# IBus と、Python から IBus を使う部品 (Ubuntu の標準の画面なら、ふつうは入っている)
missing=()
command -v ibus > /dev/null || missing+=(ibus)
python3 -c 'import gi; gi.require_version("IBus", "1.0"); from gi.repository import IBus' 2> /dev/null || missing+=(python3-gi gir1.2-ibus-1.0)
if [[ ${#missing[@]} -gt 0 ]]; then
    echo "必要な部品を入れます: ${missing[*]}"
    sudo apt-get install -y "${missing[@]}"
fi

echo "$target に入れます (管理者のパスワードを聞かれます)"
sudo rm -rf "$target"
sudo mkdir -p "$target"
sudo cp -R libMeltypeNative.so ibus-engine-meltype icon.png mozc LICENSE THIRD-PARTY-NOTICES.md "$target/"
sudo chmod 755 "$target/ibus-engine-meltype" "$target/mozc/meltype_mozc_helper"
sed "s|@DIR@|$target|g" meltype.xml | sudo tee /usr/share/ibus/component/meltype.xml > /dev/null

# IBus に読み込み直させる (動いている Meltype も止まり、次に使うときに新しいものが起動する)
ibus write-cache 2> /dev/null || true
ibus restart 2> /dev/null || true

echo
echo "インストールしました。"
echo "初めてのときは:"
echo "  1. いったんログアウトしてログインし直す"
echo "  2. 設定 → キーボード → 入力ソース →「+ 入力ソースを追加」→ 日本語 → Meltype を追加"
echo "  3. 画面右上の入力ソースのメニュー (または Super + Space) で Meltype を選ぶ"
