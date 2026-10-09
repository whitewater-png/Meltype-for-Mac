#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
#
# Meltype をアンインストールする。
#   bash uninstall.sh                  対話式 (アプリを消すか、設定・学習データも消すかを聞く)
#   bash uninstall.sh --yes            アプリの削除を確認なしで行う (設定・学習データを消すかは、端末なら聞く。端末が無ければ残す)
#   bash uninstall.sh --yes --remove-data   設定・学習データ・ユーザー辞書・自作の専門用語集も消す
#   bash uninstall.sh --yes --keep-data     設定・学習データなどは聞かずに残す
# 設定・学習データ・ユーザー辞書・自作の専門用語集は、聞かれて「y」と答えたときか --remove-data のときだけ消す。
# 消す前に、利用者が作ったデータ (ユーザー辞書・自作の専門用語集・専門用語集の除外) をデスクトップの
# Meltype-backup-<日時> フォルダーへ写す。写せなければ (set -e で) 消さずに止まる。
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
    # 辞書の管理画面「Meltype 辞書」(Meltype.app の中の Contents/Helpers/MeltypeDictionary.app) も閉じる。
    # アプリ自体は Meltype.app と一緒に消える。実行ファイル名が 16 文字を超え pkill -x では合わないので、パスで探す。
    pkill -f "$APP/Contents/Helpers/MeltypeDictionary.app/" 2>/dev/null || true
    # 消す対象は ~/Library/Input Methods/Meltype.app だけ (パスを確かめてから)
    if [[ "$APP" == "$HOME/Library/Input Methods/Meltype.app" ]]; then
        rm -rf "$APP"
        echo "Meltype.app を削除しました。"
    fi
fi

if [[ -d "$DATA" ]]; then
    if [[ "$REMOVE_DATA" == ask ]]; then
        if ask "設定・学習データ・ユーザー辞書・自作の専門用語集 ($DATA) も削除しますか? (ユーザー辞書と自作の専門用語集は、消す前にデスクトップへ写します。残すと、入れ直したときにそのまま使えます)"; then
            REMOVE_DATA=yes
        else
            REMOVE_DATA=no
        fi
    fi
    if [[ "$REMOVE_DATA" == yes ]]; then
        # 利用者が作ったデータは、消す前にデスクトップへ写しておく:
        #   userdict.txt (ユーザー辞書)、terms/ (自作の専門用語集。「専門用語集へ移す」で移した語はユーザー辞書には残っていない)、
        #   terms-excluded.txt (同梱の専門用語集から除外した語)
        # 写すのに失敗したら set -e で止まり、下の rm -rf には進まない。
        BACKUP="$HOME/Desktop/Meltype-backup-$(date +%Y%m%d-%H%M%S)"
        # 同じ名前のフォルダーが既にあれば番号を付ける (前のバックアップに混ぜない)
        n=1; base="$BACKUP"
        while [[ -e "$BACKUP" ]]; do BACKUP="$base-$n"; n=$((n + 1)); done
        mkdir -p "$HOME/Desktop"
        mkdir "$BACKUP"
        [[ -f "$DATA/userdict.txt" ]] && cp -p "$DATA/userdict.txt" "$BACKUP/"
        [[ -f "$DATA/terms-excluded.txt" ]] && cp -p "$DATA/terms-excluded.txt" "$BACKUP/"
        # terms/ の *.txt と、消した分野の控え terms/.trash/*.txt (ロック用の空ファイル .〜.lock は写さない)。
        # find -exec は cp が失敗しても 0 を返し set -e が効かないので、1 件ずつ cp して失敗で止める。
        for sub in "" ".trash"; do
            src="$DATA/terms${sub:+/$sub}"
            [[ -d "$src" ]] || continue
            dest="$BACKUP/terms${sub:+/$sub}"
            for f in "$src"/*.txt; do
                if [[ -f "$f" ]]; then
                    mkdir -p "$dest"
                    cp -p "$f" "$dest/"
                fi
            done
        done
        if rmdir "$BACKUP" 2>/dev/null; then
            echo "退避するユーザー辞書・専門用語集はありませんでした。"
        else
            echo "ユーザー辞書・自作の専門用語集をバックアップしました: $BACKUP"
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
