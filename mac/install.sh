#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
#
# テスト版の Meltype.app を ~/Library/Input Methods に入れる。zip を展開したフォルダーで実行する:
#   bash install.sh          途中で、確認が要る操作 (隔離属性を外す) だけ y/N を聞く
#   bash install.sh --yes    確認を聞かずに進める (自動実行用。内容を確かめたうえで使う)
#
# このスクリプトがやること (影響範囲):
#   - Meltype.app の署名が壊れていないか確かめる (壊れていたら何も入れずに中止)
#   - ~/Library/Input Methods/Meltype.app に入れる (cp / 入れ直しのときは、今のものを一時の場所に退避してから rsync --delete で中身だけ入れ替え、署名の検査に失敗したら退避から戻す)
#   - 入れ直しのとき、動いている Meltype を止める (pkill -x Meltype)
#   - 隔離属性 (com.apple.quarantine) を外す (確認してから。下を読むこと)
#   - 入力ソースとして登録し (Meltype --register)、入力メニューと IME の起動役を起動し直す (killall imklaunchagent TextInputMenuAgent)
#   - 登録できなかったときだけ、入力ソースの一覧に書き込む (defaults write com.apple.HIToolbox AppleEnabledInputSources)
#   - 同じバンドル ID の別の Meltype.app を LaunchServices の登録から外す (lsregister -u。ファイルは消さない)
# それ以外 (管理者権限・ネットワーク通信・ほかのアプリの設定) には触れない。
set -euo pipefail
cd "$(dirname "$0")"

ASSUME_YES=0
for arg in "$@"; do
    case "$arg" in
        --yes|-y) ASSUME_YES=1 ;;
        *) echo "使えない引数です: $arg (使えるのは --yes だけです)" >&2; exit 1 ;;
    esac
done

# リポジトリを直接開いたとき (mac/ には Meltype.app が無く、./build.sh が build/ に作る) は、build/Meltype.app を入れる。
if [[ ! -d Meltype.app && -d build/Meltype.app ]]; then
    echo "ソースからビルドした build/Meltype.app を入れます。"
    cd build
fi
if [[ ! -d Meltype.app ]]; then
    echo "Meltype.app が見つかりません。" >&2
    echo "  ・配布の zip を使うとき: zip を展開したフォルダーで実行してください。" >&2
    echo "  ・ソースから入れるとき: 先に mac フォルダーで ./build.sh を実行してください (build.sh は ビルドして、そのまま入れ替えまで行います)。" >&2
    exit 1
fi

# 展開した Meltype.app が壊れていないか (ダウンロードの途中切れ・展開の失敗・書き換え) を、入れる前に確かめる。
# 配布用の署名 (公証) が無い版でも、署名の封印 (全ファイルの検査値) は付いているので、1 バイトでも違えば失敗する。
echo "Meltype.app の署名を確かめています…"
if ! codesign --verify --deep --strict Meltype.app 2>&1; then
    echo "Meltype.app が壊れているか、書き換えられています。何も入れずに中止します。zip をダウンロードし直してください。" >&2
    exit 1
fi
echo "署名の検査: 問題ありません"

# 主なファイルの SHA-256。リリースページ (または配布した人) が載せた値と見比べられる。違うときは Ctrl+C で止めること。
# (署名の検査は「zip の中で整合している」ことしか分からないので、入手元が本物かは、この値の一致で確かめる)
echo
echo "SHA-256 (リリースページに載っている値と同じか、見比べてください。違うときは Ctrl+C で中止):"
for file in Meltype.app/Contents/MacOS/Meltype Meltype.app/Contents/Frameworks/libMeltypeNative.dylib; do
    if [[ -f "$file" ]]; then
        shasum -a 256 "$file"
    else
        echo "見つかりません: $file" >&2
        exit 1
    fi
done
echo

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
    # ただし入れ替えの途中で失敗すると、新旧が混ざった壊れた Meltype.app が残る。入れ替える前に今のものを一時の場所へ退避し、
    # 入れ替えと、入れ替えたあとの署名の検査のどちらかが失敗したら、退避から書き戻す (この区間は set -e に任せず、明示的に処理する)。
    BACKUP_DIR="$(mktemp -d "${TMPDIR:-/tmp}/meltype-backup.XXXXXX")" || { echo "退避用のフォルダーを作れませんでした。何も入れずに中止します。" >&2; exit 1; }
    if ! cp -Rp "$TARGET/Meltype.app" "$BACKUP_DIR/Meltype.app"; then
        echo "今の Meltype.app を退避できませんでした。何も入れずに中止します。" >&2
        rm -rf "$BACKUP_DIR"
        exit 1
    fi
    SWAP_OK=1
    rsync -a --delete Meltype.app/ "$TARGET/Meltype.app/" || SWAP_OK=0
    if [[ $SWAP_OK -eq 1 ]] && ! codesign --verify --deep --strict "$TARGET/Meltype.app" 2>&1; then
        SWAP_OK=0
    fi
    if [[ $SWAP_OK -eq 0 ]]; then
        echo "入れ替えに失敗したか、入れ替えたあとの署名の検査に失敗しました。元の Meltype.app に戻します…" >&2
        if rsync -a --delete "$BACKUP_DIR/Meltype.app/" "$TARGET/Meltype.app/"; then
            rm -rf "$BACKUP_DIR"
            echo "元に戻しました。今の Meltype はそのまま使えます。" >&2
        else
            echo "元に戻すことにも失敗しました。元の Meltype.app は次の場所に残してあります。手で入れ直してください:" >&2
            echo "  $BACKUP_DIR/Meltype.app" >&2
        fi
        exit 1
    fi
    rm -rf "$BACKUP_DIR"
    # 入れ替えてから、動いていた Meltype を止める (次にキーを打ったときに macOS が新しい Meltype を起動する)。
    # 先に止めると、入れ替えの途中でキーを打ったときに、新旧が混ざった Meltype が起動されてしまう。
    pkill -x Meltype 2>/dev/null || true
    FIRST_INSTALL=0
else
    cp -R Meltype.app "$TARGET/"
    FIRST_INSTALL=1
fi
# インターネットから取ってきた印 (隔離属性 com.apple.quarantine) を外す。
# これは macOS の Gatekeeper (ダウンロードしたものを初回に検査する仕組み) を、この Meltype.app については回避することになる。
# テスト版は Apple の公証を受けていない (署名が自分用) ので、外さないと macOS が起動させない。
# 入手元と SHA-256 を確かめたものだけに行う。対話できる端末では y/N を聞き、--yes のときは聞かない。
# 端末でなく --yes も無いとき (自動実行) は、黙って検査を回避しないよう外さない。
remove_quarantine=0
if [[ $ASSUME_YES -eq 1 ]]; then
    remove_quarantine=1
elif [[ -t 0 ]]; then
    echo "隔離属性 (com.apple.quarantine) を外します。これは macOS の Gatekeeper の検査を、この Meltype.app について回避することです。"
    echo "上の SHA-256 がリリースページの値と一致していて、入手元が信頼できるときだけ「y」を押してください。"
    read -r -p "外しますか? [y/N] " answer
    [[ "$answer" == [yY] || "$answer" == [yY][eE][sS] ]] && remove_quarantine=1
fi
if [[ $remove_quarantine -eq 1 ]]; then
    echo "隔離属性を外しています (Gatekeeper の検査を回避します)…"
    xattr -dr com.apple.quarantine "$TARGET/Meltype.app" 2>/dev/null || true
else
    echo "隔離属性は外しませんでした。このままだと macOS が Meltype を起動させないことがあります。"
    echo "内容を確かめたうえで外すなら、次を実行してください (または bash install.sh --yes):"
    echo "  xattr -dr com.apple.quarantine \"$HOME/Library/Input Methods/Meltype.app\""
fi
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
