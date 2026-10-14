#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
#
# Meltype の Mac 版をビルドして ~/Library/Input Methods にインストールする。
#   ./build.sh            ビルドしてインストール
#   ./build.sh --no-install  ビルドだけ (build/Meltype.app)
# 必要なもの: macOS 13.3 以降、Xcode (またはコマンドライン ツール: xcode-select --install)、.NET 10 SDK
set -euo pipefail
cd "$(dirname "$0")"

INSTALL=1
ID=io.github.yksr-melt.inputmethod.Meltype
# 入力ソースの「+」の一覧に Meltype が出ない Mac がある (自分で署名した版、macOS 26、#21)。
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
[[ "${1:-}" == "--no-install" ]] && INSTALL=0

case "$(uname -m)" in
    arm64) RID=osx-arm64 ;;
    x86_64) RID=osx-x64 ;;
    *) echo "対応していない CPU です: $(uname -m)" >&2; exit 1 ;;
esac

BUILD=build
APP="$BUILD/Meltype.app"
rm -rf "$BUILD/native" "$APP"
mkdir -p "$BUILD"

echo "== 1/3 本体 (C#, NativeAOT) をビルド"
# リポジトリの nuget.config は NuGet を使わない設定 (Windows の開発環境用) なので、NativeAOT のコンパイラを取るために nuget.org を指定する。
dotnet publish ../src/Meltype.Mac.Native/Meltype.Mac.Native.csproj -c Release -r "$RID" \
    -p:PublishAot=true -p:NativeLib=Shared -p:StripSymbols=true \
    --source https://api.nuget.org/v3/index.json -o "$BUILD/native"

echo "== 2/3 IME (Swift) をビルド (初回は azooKey の変換エンジンと辞書のダウンロードに時間がかかります)"
# ビルドしたマシンのフォルダーの絶対パス (/Users/<名前>/…/mac/.build/checkouts/…) が、実行ファイルの文字列 (assert・エラーの __FILE__ や #filePath) に残る。
# Zenzai を付けて (C++ の marisa・azooKey の Zenzai のコードが入って) から、この文字列が 75 件に増え、strip -S (下) では消えなかった。
# 配布先の Mac でユーザー名と作業フォルダーが読めてしまうので、コンパイラに「このフォルダーは . と書く」と頼む (C/C++ と Swift の両方)。
SOURCE_ROOT="$(pwd -P)"
swift build -c release --force-resolved-versions \
    -Xcc "-ffile-prefix-map=$SOURCE_ROOT=." -Xswiftc -file-prefix-map -Xswiftc "$SOURCE_ROOT=."

echo "== 3/3 Meltype.app を組み立て"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources" "$APP/Contents/Frameworks"
BIN="$(swift build -c release --force-resolved-versions --show-bin-path)"
cp "$BIN/MeltypeIME" "$APP/Contents/MacOS/Meltype"
# 辞書の管理画面「Meltype 辞書」(ふつうのアプリ)。IME は背面専用 (LSBackgroundOnly) で自分のウインドウがキーボード入力を受けられないので、
# 別のアプリにして Meltype.app の中 (Contents/Helpers。codesign が入れ子のコードとして扱う場所) に入れ、入力メニューの「設定・辞書…」から開く。
# Meltype.app を消すと一緒に消える (アンインストールで別に消すものは無い)。
HELPER="$APP/Contents/Helpers/MeltypeDictionary.app"
mkdir -p "$HELPER/Contents/MacOS" "$HELPER/Contents/Resources"
cp "$BIN/MeltypeDictionary" "$HELPER/Contents/MacOS/MeltypeDictionary"
cp Resources/Dictionary/Info.plist "$HELPER/Contents/Info.plist"
cp -R Resources/Dictionary/ja.lproj "$HELPER/Contents/Resources/"
# アイコン (氷の M。入力メニューの icon.tiff と同じ絵柄の 1024px 版。元絵と作り方は docs/IMPROVEMENT_PLAN.md の 0-6)
cp Resources/Meltype.icns "$APP/Contents/Resources/Meltype.icns"
cp Resources/Meltype.icns "$HELPER/Contents/Resources/Meltype.icns"
# 版は Meltype.app と同じにする (Info.plist は 1 か所で管理する)
plutil -replace CFBundleShortVersionString -string "$(plutil -extract CFBundleShortVersionString raw Resources/Info.plist)" "$HELPER/Contents/Info.plist"
plutil -replace CFBundleVersion -string "$(plutil -extract CFBundleVersion raw Resources/Info.plist)" "$HELPER/Contents/Info.plist"
# azooKey が使う llama.framework などの動的なフレームワークも同梱する。
# 入れていなかったため、1.0.0 は起動できなかった (dyld: Library not loaded: @rpath/llama.framework、#13)。
# $BIN にあるものを全部入れると、実行ファイルが使わないものまで配布物に入る (サイズが増え、検査の対象も増える)。
# なので、実行ファイルを otool -L して @rpath/<名前>.framework/... で参照されているものだけを入れる。
while read -r name; do
    [[ -n "$name" ]] || continue
    if [[ ! -d "$BIN/$name.framework" ]]; then echo "実行ファイルが参照するフレームワークが見つかりません: $BIN/$name.framework" >&2; exit 1; fi
    cp -R "$BIN/$name.framework" "$APP/Contents/Frameworks/"
    # azooKey の出来合いの llama.framework は arm64 と x86_64 の 2 つ入り (5.6 MB)。配布物は 1 つの CPU 用なので、使う方だけにする (約半分)。
    # 署名は後ろの codesign が付け直す (薄くすると、元の署名は無効になる)。
    framework_binary="$APP/Contents/Frameworks/$name.framework/Versions/A/$name"
    if [[ -f "$framework_binary" && "$(lipo -archs "$framework_binary")" == *" "* ]]; then
        # ビルドする CPU ($RID。別の CPU 向けにビルドしても、その CPU の分を残す) に合わせて薄くする
        lipo -thin "$([[ "$RID" == osx-arm64 ]] && echo arm64 || echo x86_64)" "$framework_binary" -output "$framework_binary.thin"
        mv "$framework_binary.thin" "$framework_binary"
    fi
done < <( (otool -L "$BIN/MeltypeIME"; otool -L "$BIN/MeltypeDictionary") | sed -n 's|^[[:space:]]*@rpath/\([^/]*\)\.framework/.*|\1|p' | sort -u)
# Swift 6.2 以降は、古い macOS 向けの互換ライブラリ (libswiftCompatibilitySpan.dylib など) を @rpath で読む。
# macOS 26 は OS に入っているが、13〜15 では無いので、ツールチェーンから同梱する (#20)。辞書の管理画面も同じものを使う (二重に入れない)。
TOOLCHAIN_SWIFT_LIBS="$(dirname "$(xcrun --find swift)")/../lib"
while read -r lib; do
    name="${lib#@rpath/}"
    [[ "$name" == libswift*.dylib && ! -e "$APP/Contents/Frameworks/$name" ]] || continue
    for dylib in "$TOOLCHAIN_SWIFT_LIBS"/swift-*/macosx/"$name" "$TOOLCHAIN_SWIFT_LIBS"/swift/macosx/"$name"; do
        [[ -e "$dylib" ]] && { cp "$dylib" "$APP/Contents/Frameworks/"; break; }
    done
done < <( (otool -L "$BIN/MeltypeIME"; otool -L "$BIN/MeltypeDictionary") | awk '/@rpath\//{print $1}')
# 実行ファイルの隣 (@loader_path) だけでなく、Contents/Frameworks も探すようにする
install_name_tool -add_rpath "@executable_path/../Frameworks" "$APP/Contents/MacOS/Meltype" 2>/dev/null || true
# 辞書の管理画面は、外側の Meltype.app/Contents/Frameworks を探す (MacOS → Contents → .app → Helpers → Meltype.app/Contents)。
# libMeltypeNative.dylib も同じ場所のものを dlopen する (NativeDictionary.defaultLibraryPath)。
install_name_tool -add_rpath "@executable_path/../../../../Frameworks" "$HELPER/Contents/MacOS/MeltypeDictionary" 2>/dev/null || true
# @rpath で読み込むライブラリが全部 Contents/Frameworks にあるか確かめる (無ければ配布しない)
missing=0
while read -r lib; do
    name="${lib#@rpath/}"
    if [[ ! -e "$APP/Contents/Frameworks/$name" ]]; then echo "同梱されていないライブラリ: $lib" >&2; missing=1; fi
done < <( (otool -L "$APP/Contents/MacOS/Meltype"; otool -L "$HELPER/Contents/MacOS/MeltypeDictionary") | awk '/@rpath\//{print $1}')
[[ $missing -eq 0 ]] || { echo "Meltype.app に必要なライブラリが足りません" >&2; exit 1; }
cp "$BUILD/native/MeltypeNative.dylib" "$APP/Contents/Frameworks/libMeltypeNative.dylib"
cp Resources/Info.plist "$APP/Contents/Info.plist"
cp Resources/icon.tiff "$APP/Contents/Resources/icon.tiff"
cp Resources/icon-roman.tiff "$APP/Contents/Resources/icon-roman.tiff"
# システム設定の入力ソースの一覧に出す名前
cp -R Resources/ja.lproj Resources/en.lproj "$APP/Contents/Resources/"
# azooKey の辞書などのリソース (Swift Package のリソースバンドル)
for bundle in "$BIN"/*.bundle; do
    [[ -e "$bundle" ]] && cp -R "$bundle" "$APP/Contents/Resources/"
done
# ライセンス表示: Meltype.app を配布するとき、GPL-3.0・MIT・Apache-2.0 の部品の著作権表示とライセンス全文を一緒に渡す義務がある。
# 署名すると中身を変えられなくなるので、署名の前に入れる。部品のライセンスは取得済みのソース (.build/checkouts) から写す。
LICENSES="$APP/Contents/Resources/Licenses"
mkdir -p "$LICENSES"
cp ../LICENSE "$LICENSES/LICENSE-Meltype-GPL-3.0.txt"
cp ../THIRD-PARTY-NOTICES.md "$LICENSES/THIRD-PARTY-NOTICES.md"
for dep in AzooKeyKanaKanjiConverter Jinja swift-algorithms swift-collections swift-numerics swift-tokenizers SwiftyMarisa; do
    found=
    # SwiftyMarisa (と、その中の marisa-trie) のライセンスは LICENSE.md (BSD 2-clause と LGPL のデュアル。Meltype は BSD 2-clause で使う)
    for name in LICENSE LICENSE.txt LICENSE.md NOTICE NOTICE.txt; do
        if [[ -f ".build/checkouts/$dep/$name" ]]; then cp ".build/checkouts/$dep/$name" "$LICENSES/$dep-$name"; found=1; fi
    done
    [[ -n "$found" ]] || { echo "$dep のライセンスファイルが見つかりません (.build/checkouts/$dep)。ライセンス表示なしでは配布できません。" >&2; exit 1; }
done
# azooKey の辞書 (Apache-2.0)
AZ_DICT_LICENSE=".build/checkouts/AzooKeyKanaKanjiConverter/Sources/KanaKanjiConverterModuleWithDefaultDictionary/azooKey_dictionary_storage/LICENSE"
[[ -f "$AZ_DICT_LICENSE" ]] || { echo "azooKey 辞書のライセンスファイルが見つかりません。" >&2; exit 1; }
cp "$AZ_DICT_LICENSE" "$LICENSES/azooKey_dictionary_storage-LICENSE"
# azooKey の絵文字辞書は Mozc の emoji_data.tsv (BSD-3-Clause) を元に作られている。Mozc の LICENSE の原文
# (native/mozc/MOZC_COMMIT の版から取ったもの) を mac/Resources/Licenses に置いてある
[[ -f Resources/Licenses/Mozc-LICENSE.txt ]] || { echo "Mozc のライセンスファイル (mac/Resources/Licenses/Mozc-LICENSE.txt) が見つかりません。" >&2; exit 1; }
cp Resources/Licenses/Mozc-LICENSE.txt "$LICENSES/Mozc-LICENSE.txt"
# llama.cpp (MIT)。azooKey が配る出来合いの llama.framework (Contents/Frameworks) をそのまま同梱するので、MIT の条件どおり著作権表示と許諾文の全文を一緒に渡す
# (xcframework には LICENSE が入っていないので、mac/Resources/Licenses に置いてある)
[[ -f Resources/Licenses/llama.cpp-LICENSE.txt ]] || { echo "llama.cpp のライセンスファイル (mac/Resources/Licenses/llama.cpp-LICENSE.txt) が見つかりません。" >&2; exit 1; }
cp Resources/Licenses/llama.cpp-LICENSE.txt "$LICENSES/llama.cpp-LICENSE.txt"
# 実行ファイルの RPATH に、ビルドしたマシンの絶対パス (/Library/Developer/CommandLineTools/... など) が残っていると、
# 配布先の Mac でそのパスを探しに行く (ビルドした人の環境が漏れ、同じパスに置かれたライブラリを読み込まされる余地も残る)。
# 署名すると中身を変えられなくなるので、署名の前に外す。無いときに失敗しないよう || true。
# 外さないのは、OS の標準の場所 (/usr/lib/swift・/System/...) と @ で始まる同梱側のパス (@loader_path・@executable_path/../Frameworks)。
# /usr/lib/swift は OS 同梱の Swift の置き場所で、これを外すと起動できなくなるので残す。
# (外したあとに動くかは、組み立て後の Meltype.app で otool -l を見て確かめる)
for executable in "$APP/Contents/MacOS/Meltype" "$HELPER/Contents/MacOS/MeltypeDictionary"; do
    while read -r rpath; do
        case "$rpath" in
            /usr/lib/*|/System/*|@*) ;;
            /*) install_name_tool -delete_rpath "$rpath" "$executable" 2>/dev/null || true ;;
        esac
    done < <(otool -l "$executable" | awk '$1=="cmd"&&$2=="LC_RPATH"{r=1;next} r&&$1=="path"{print $2;r=0}')
done
# 実行ファイルには、ビルドしたマシンのホームフォルダー配下の絶対パス (/Users/<名前>/…/mac/.build/ など) が、デバッグ用の記録 (stabs) として残る。
# 配布先の Mac では使わず、ユーザー名と作業フォルダーの場所が配布物から読めてしまうので、署名の前に取り除く
# (C# 側の libMeltypeNative.dylib は、上の StripSymbols=true で同じことをしている)。
# -S が取り除くのはデバッグ用の記録だけで、関数名などの通常のシンボルは残る。strip するのは .app の中のコピーで、.build の下の元の実行ファイルは変わらない。
# .app の中のコピーはリンカーが付けた署名 (linker-signed) の状態なので、strip は警告を出さない。署名は直後の codesign で付け直す。
# (署名したあとに strip すると「署名が無効になる」と警告が出て、codesign --verify が失敗する。必ず署名より前に行うこと。)
for executable in "$APP/Contents/MacOS/Meltype" "$HELPER/Contents/MacOS/MeltypeDictionary"; do
    strip -S "$executable"
done
# 署名: 環境変数 MELTYPE_MAC_IDENTITY (Developer ID Application の証明書の名前) があれば配布用に署名する
# (Hardened Runtime・タイムスタンプ付き。公証 (notarization) はまだ行っていない (CI 無し))。無ければ自分の Mac で使うための署名。
# 中から外へ: Frameworks の中身 → 辞書の管理画面 (Contents/Helpers) → Meltype.app。
# 辞書の管理画面は外側の Frameworks の libMeltypeNative.dylib を読むので、配布用の署名ではどちらも同じ証明書 (同じ Team ID) で署名する
# (Hardened Runtime のライブラリの検証は、同じ Team ID のものだけを読み込ませる)。
if [[ -n "${MELTYPE_MAC_IDENTITY:-}" ]]; then
    for item in "$APP/Contents/Frameworks/"*; do
        codesign --force --sign "$MELTYPE_MAC_IDENTITY" --options runtime --timestamp "$item"
    done
    codesign --force --sign "$MELTYPE_MAC_IDENTITY" --options runtime --timestamp "$HELPER"
    codesign --force --deep --sign "$MELTYPE_MAC_IDENTITY" --options runtime --timestamp "$APP"
    echo "配布用に署名しました: $MELTYPE_MAC_IDENTITY"
else
    codesign --force --sign - "$HELPER"
    codesign --force --deep --sign - "$APP"
fi
# 入れ子の辞書の管理画面まで含めて、署名の封印が壊れていないか (install.sh・自動更新と同じ検査)
codesign --verify --deep --strict "$APP" || { echo "Meltype.app の署名の検査に失敗しました" >&2; exit 1; }
echo "作成しました: $APP"

if [[ $INSTALL -eq 1 ]]; then
    TARGET="$HOME/Library/Input Methods"
    mkdir -p "$TARGET"
    if [[ -d "$TARGET/Meltype.app" ]]; then
        # 入れ直し: Meltype.app のフォルダーは消さずに中身だけを入れ替える。
        # フォルダーごと消して入れ直すと、macOS が入力ソースの一覧から Meltype を外して有効な入力ソースの設定からも消し、
        # 入力メニューに出ない・選んでも切り替わらない状態になる (ログアウトするまで直らない)。
        rsync -a --delete "$APP/" "$TARGET/Meltype.app/"
        # 入れ替えてから、動いていた Meltype を止める (次にキーを打ったときに macOS が新しい Meltype を起動する)。
        # 先に止めると、入れ替えの途中でキーを打ったときに、新旧が混ざった Meltype が起動されてしまう。
        pkill -x Meltype 2>/dev/null || true
        # 開いていた辞書の管理画面も閉じる (古い版のまま残さない。変更はその都度保存済み)。
        # 実行ファイル名が 16 文字を超え pkill -x では合わないので、入れ替えた Meltype.app の中のパスで探す。
        pkill -f "$TARGET/Meltype.app/Contents/Helpers/MeltypeDictionary.app/" 2>/dev/null || true
        FIRST_INSTALL=0
    else
        cp -R "$APP" "$TARGET/"
        FIRST_INSTALL=1
    fi
    # Meltype.app が無かったのに、もう入力ソースの一覧にある: 前の Meltype.app を消したあと、まだログアウトしていない。
    # その一覧はそのうち消えるので、ここで登録しても使えない (登録が二重になることもある)。ログアウトしてもらう。
    STALE=0
    [[ $FIRST_INSTALL -eq 1 ]] && is_registered && STALE=1
    # 同じバンドル ID の Meltype.app が複数あると、macOS が入れた方ではない Meltype.app (build/ や、消したフォルダーの
    # コピーの古い登録) を起動しようとして入力ソースを選べないことがある。入れた方以外は登録から外しておく。
    LSREGISTER=/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister
    "$LSREGISTER" -u "$(pwd -P)/$APP" 2>/dev/null || true
    while IFS= read -r other; do
        [[ "$other" == "$TARGET/Meltype.app" ]] || "$LSREGISTER" -u "$other" 2>/dev/null || true
    done < <(mdfind "kMDItemCFBundleIdentifier == '$ID'" 2>/dev/null || true)
    "$LSREGISTER" -f "$TARGET/Meltype.app" 2>/dev/null || true
    echo "インストールしました: $TARGET/Meltype.app"
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
        echo "メニューバーの入力メニューで Meltype を選んでください (出ていなければ、ログアウトしてログインし直してください)。"
    else
        # 登録できなかったときだけ、有効な入力ソースの一覧に直接書き込んでおく (#21)。
        enable_input_source
        echo "初めてのときは、いったんログアウトしてログインし直してから、"
        echo "システム設定 → キーボード → 入力ソース →「編集…」→「+」→ 日本語 → Meltype を追加してください。"
    fi
fi
