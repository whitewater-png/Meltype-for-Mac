#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
#
# テスト版の Meltype.app を ~/Library/Input Methods に入れる。zip を展開したフォルダーで実行する:
#   bash install.sh
set -euo pipefail
cd "$(dirname "$0")"

if [[ ! -d Meltype.app ]]; then
    echo "Meltype.app が見つかりません。zip を展開したフォルダーで実行してください。" >&2
    exit 1
fi

ID=io.github.yksr-melt.inputmethod.Meltype

# 入力ソースの「+」の一覧に Meltype が出ない Mac がある (macOS 26、#21)。
# 登録 (Meltype --register) できなかったときなどに、ことえりと同じ形で、有効な入力ソースの一覧 (AppleEnabledInputSources) に入れておく。
# macOS 26 では他社の IME は com.apple.inputsources の AppleEnabledThirdPartyInputSources に入るので、
# どちらかに入っていれば追加はしない (両方に入ると、入力ソースの一覧に同じ Meltype が 2 つ出る)。
enable_input_source() {
    local enabled
    enabled="$(defaults read com.apple.HIToolbox AppleEnabledInputSources 2>/dev/null || true)"
    enabled+="$(defaults read com.apple.inputsources AppleEnabledThirdPartyInputSources 2>/dev/null || true)"
    grep -q "$ID" <<<"$enabled" && return 0
    defaults write com.apple.HIToolbox AppleEnabledInputSources -array-add \
        "<dict><key>Bundle ID</key><string>$ID</string><key>InputSourceKind</key><string>Keyboard Input Method</string></dict>" \
        "<dict><key>Bundle ID</key><string>$ID</string><key>Input Mode</key><string>$ID.Japanese</string><key>InputSourceKind</key><string>Input Mode</string></dict>" \
        "<dict><key>Bundle ID</key><string>$ID</string><key>Input Mode</key><string>com.apple.inputmethod.Roman</string><key>InputSourceKind</key><string>Input Mode</string></dict>"
    echo "入力ソースに Meltype を追加しました"
}

# 初めて入れたとき: 入力ソースとして登録し (Meltype --register)、入力メニュー (TextInputMenuAgent) と
# IME を起動する imklaunchagent を起動し直して一覧を読み直させる。うまくいけば、ログアウトしなくても使える。
# (この 2 つは止めてもすぐに起動し直される。止めた瞬間だけ、入力メニューが消えたり入力ソースの切り替えが遅れたりする)。
register_input_source() {
    echo "入力ソースに登録しています (1 分ほどかかることがあります)…"
    "$TARGET/Meltype.app/Contents/MacOS/Meltype" --register || return 1
    killall imklaunchagent TextInputMenuAgent 2>/dev/null || true
}

# 入力ソースの一覧に Meltype が出ているか
is_registered() {
    "$TARGET/Meltype.app/Contents/MacOS/Meltype" --register-check
}

TARGET="$HOME/Library/Input Methods"
mkdir -p "$TARGET"
if [[ -d "$TARGET/Meltype.app" ]]; then
    # 入れ直し: Meltype.app のフォルダーは消さずに中身だけを入れ替える。
    # フォルダーごと消して入れ直すと、macOS が入力ソースの一覧から Meltype を外して有効な入力ソースの設定からも消し、
    # 入力メニューに出ない・選んでも切り替わらない状態になる (ログアウトするまで直らない)。
    rsync -a --delete Meltype.app/ "$TARGET/Meltype.app/"
    # 入れ替えてから、動いていた Meltype を止める (次にキーを打ったときに macOS が新しい Meltype を起動する)。
    # 先に止めると、入れ替えの途中でキーを打ったときに、新旧が混ざった Meltype が起動されてしまう。
    pkill -x Meltype 2>/dev/null || true
    FIRST_INSTALL=0
else
    cp -R Meltype.app "$TARGET/"
    FIRST_INSTALL=1
fi
# インターネットから取ってきた印 (隔離属性) を外す。署名が自分用なので、外さないと macOS が起動させない。
xattr -dr com.apple.quarantine "$TARGET/Meltype.app" 2>/dev/null || true
# Meltype.app が無かったのに、もう入力ソースの一覧にある: 前の Meltype.app を消したあと、まだログアウトしていない。
# その一覧はそのうち消えるので、ここで登録しても使えない (登録が二重になることもある)。ログアウトしてもらう。
STALE=0
[[ $FIRST_INSTALL -eq 1 ]] && is_registered && STALE=1

# 同じバンドル ID の Meltype.app が複数あると、macOS が入れた方ではない Meltype.app を起動しようとして
# 入力ソースを選べないことがある。入れた方以外の Meltype.app (展開したフォルダーのものなど) は登録から外しておく。
LSREGISTER=/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister
"$LSREGISTER" -u "$(pwd -P)/Meltype.app" 2>/dev/null || true
while IFS= read -r other; do
    [[ "$other" == "$TARGET/Meltype.app" ]] || "$LSREGISTER" -u "$other" 2>/dev/null || true
done < <(mdfind "kMDItemCFBundleIdentifier == '$ID'" 2>/dev/null || true)
"$LSREGISTER" -f "$TARGET/Meltype.app" 2>/dev/null || true

echo "インストールしました: $TARGET/Meltype.app"
echo
if [[ $FIRST_INSTALL -eq 0 ]]; then
    if is_registered; then
        echo "入れ替えました。次にキーを打ったときから新しい Meltype になります。"
    else
        echo "入れ替えましたが、入力ソースの一覧に Meltype がありません (前に Meltype.app を消して入れ直したときなど)。"
        echo "いったんログアウトしてログインし直してから、メニューバーの入力メニューで Meltype を選んでください。"
    fi
elif [[ $STALE -eq 1 ]]; then
    enable_input_source
    echo "前の Meltype を消したあと、まだログアウトしていないようです。"
    echo "いったんログアウトしてログインし直してから、メニューバーの入力メニューで Meltype を選んでください。"
elif register_input_source; then
    echo "メニューバーの入力メニューで Meltype を選んでください。"
    echo "(出ていなければ、いったんログアウトしてログインし直してください)"
else
    # 登録できなかったときだけ、有効な入力ソースの一覧に直接書き込んでおく (#21)。
    enable_input_source
    echo "初めてのときは:"
    echo "  1. いったんログアウトしてログインし直す"
    echo "  2. メニューバーの入力メニューで Meltype を選ぶ"
    echo "     (出ていなければ、システム設定 → キーボード → 入力ソース →「編集…」→「+」→ 日本語 → Meltype を追加)"
fi
