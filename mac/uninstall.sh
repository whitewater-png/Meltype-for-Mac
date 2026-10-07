#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
#
# Meltype をアンインストールする。
#   bash uninstall.sh                  対話式 (アプリを消すか、設定・学習データも消すかを聞く)
#   bash uninstall.sh --yes            アプリの削除を確認なしで行う (設定・学習データは残す)
#   bash uninstall.sh --yes --remove-data   設定・学習データ・ユーザー辞書も消す
# 設定・学習データ・ユーザー辞書は、聞かれて「y」と答えたときか --remove-data のときだけ消す。
# 消す前に、ユーザー辞書をデスクトップにバックアップする。
set -euo pipefail

APP="$HOME/Library/Input Methods/Meltype.app"
DATA="$HOME/Library/Application Support/Meltype"
ASSUME_YES=0
REMOVE_DATA=ask
for arg in "$@"; do
    case "$arg" in
        --yes|-y) ASSUME_YES=1 ;;
        --remove-data) REMOVE_DATA=yes ;;
        --keep-data) REMOVE_DATA=no ;;
        *) echo "使い方: bash uninstall.sh [--yes] [--remove-data | --keep-data]" >&2; exit 2 ;;
    esac
done

# y/N を聞く (端末が無いときは「いいえ」)。第 1 引数: 質問
ask() {
    [[ -t 0 ]] || return 1
    local answer
    read -r -p "$1 [y/N] " answer
    [[ "$answer" == [yY] || "$answer" == [yY][eE][sS] ]]
}

if [[ ! -d "$APP" ]]; then
    echo "Meltype.app は入っていません ($APP)。"
else
    if [[ $ASSUME_YES -ne 1 ]] && ! ask "Meltype をアンインストールします。よろしいですか?"; then
        echo "中止しました。何も変更していません。"
        exit 0
    fi
    # 先に入力ソースを外す (アプリを消したあとも入力メニューに残るのを防ぐ)。
    # 古い版など --unregister が無い版では失敗するので、そのときは手動で外してもらう。
    if ! "$APP/Contents/MacOS/Meltype" --unregister 2>/dev/null; then
        echo "入力ソースを自動では外せませんでした。システム設定 → キーボード → 入力ソース →「編集…」で、Meltype を「−」で外してください。"
    fi
    # 動いている Meltype を止めてから消す (次にキーを打っても起動し直されない)
    pkill -x Meltype 2>/dev/null || true
    # 消す対象は ~/Library/Input Methods/Meltype.app だけ (パスを確かめてから)
    if [[ "$APP" == "$HOME/Library/Input Methods/Meltype.app" ]]; then
        rm -rf "$APP"
        echo "Meltype.app を削除しました。"
    fi
fi

if [[ -d "$DATA" ]]; then
    if [[ "$REMOVE_DATA" == ask ]]; then
        if ask "設定・学習データ・ユーザー辞書 ($DATA) も削除しますか? (残すと、入れ直したときにそのまま使えます)"; then
            REMOVE_DATA=yes
        else
            REMOVE_DATA=no
        fi
    fi
    if [[ "$REMOVE_DATA" == yes ]]; then
        # ユーザー辞書は手で登録した大事なデータなので、消す前にデスクトップへ写しておく
        if [[ -f "$DATA/userdict.txt" ]]; then
            BACKUP="$HOME/Desktop/Meltype-userdict-backup-$(date +%Y%m%d-%H%M%S).txt"
            cp "$DATA/userdict.txt" "$BACKUP"
            echo "ユーザー辞書をバックアップしました: $BACKUP"
        fi
        if [[ "$DATA" == "$HOME/Library/Application Support/Meltype" ]]; then
            rm -rf "$DATA"
            echo "設定・学習データを削除しました。"
        fi
    else
        echo "設定・学習データ・ユーザー辞書は残しました ($DATA)。"
    fi
fi

echo
echo "完了しました。入力メニューから Meltype を完全に消すには、いったんログアウトしてログインし直してください。"
echo "(すぐに入れ直すときも、先にログアウトしてログインし直してください)"
