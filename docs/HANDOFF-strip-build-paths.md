# 引き継ぎ: 配布物に残るビルド時の絶対パスを消す (Swift 側の実行ファイルに `strip -S`)

- 作成: 2026-10-12 (査読担当: ヘンリエッタ。実装・コミットはしていない)
- 対象: `mac/build.sh` (行番号は HEAD `0ac6381` のもの。着手前に `grep` で取り直す)
- 出どころ: [PKG_REVIEW_2026-10.md](PKG_REVIEW_2026-10.md) の §2 (ビルド時のパス) と §4 (リョウさんの判断)
- 状態: **未着手**。この文書だけで着手できるように書いている

## 1. 何をするか (一言で)

配布する実行ファイル 2 本 (`Meltype`、`MeltypeDictionary`) に、ビルドしたマシンのホームフォルダー配下の絶対パス (`/Users/<ユーザー名>/Meltype/mac/.build/…` など) が、デバッグ用の記録として 1,020 件残っている。ダウンロードした人は取り出せる。
`mac/build.sh` の署名の前に `strip -S` を足して、次のリリースの配布物から消す。公開済みの 1.1.5 は変わらない (新しい版から効く)。

## 2. リョウさんが決めたこと (2026-10-12)

| 項目 | 決定 | この文書での扱い |
|---|---|---|
| ビルド時のパスを消すか | **次のリリースのビルドから消す** | §4 のとおり実装する (メインのセッション) |
| pkg の配布 | **今のまま続ける。** Release 本文の「動作確認中」と「うまくいかないときは zip を使うか…」の表記は保つ | 触らない (§7) |
| 次のリリースのノート | **開き方は最初から併記で書く** | §8 |
| 管理表の 1 行の崩れ | メインのセッションに任せる | §9 |
| Release の公開・push・タグ | 変わらず、リョウさんの OK が出るまでしない | §7 |

## 3. 今の状態 (実測)

1.1.5 の pkg の中身のコピー (`pkgutil --expand-full` で展開したもの) で測った。**新しくビルドした実行ファイルではない**ので、実装のあとで §6 のとおり測り直すこと。

| ファイル | `/Users/<名前>/` の件数 | stab (デバッグ用の記録) | 通常のシンボル | 大きさ (バイト) |
|---|---|---|---|---|
| `Meltype` | 987 → 0 | 88,000 → 0 | 32,135 → 32,134 | 8,668,816 → 7,120,240 |
| `MeltypeDictionary` | 33 → 0 | 8,214 → 0 | 3,840 → 3,839 | 1,081,920 → 946,560 |

- 各列は「`strip -S` の前 → 後」。stab と通常のシンボルの数は、`nm -pa <実行ファイル> | awk '$2=="-"{s++} $2!="-"{r++} END{print s+0, r+0}'` が出す「stab の数, 通常の数」。
- 件数の内訳は、`Meltype` が `mac/.build/` の下 976 件と `mac/Sources/` の下 11 件、`MeltypeDictionary` が 17 件と 16 件で、それ以外のパスは無い。ユーザー名は、リョウさんの Mac のログイン名と同じ。`libMeltypeNative.dylib` と `libswiftCompatibilitySpan.dylib` は 0 件。
- C# 側の `libMeltypeNative.dylib` は、`build.sh` 57 行目の `-p:StripSymbols=true` で既に消えている。Swift 側の 2 本だけ抜けている。
- 前例: `build.sh` 136〜149 行目は、RPATH に残るビルドマシンの絶対パスを署名の前に外している (コミット `b778d26`)。同じ考え方で、実行ファイルのデバッグ用の記録も外す。
- 数え方の注意: `strings` は、既定でも `-a` でも、`Meltype` に 987 件あるのに 0 件を返す (全バイトを見る `strings -` なら 987 件)。`grep -a -o -E` を正とする (§6)。

## 4. 直し方 (提案)

`mac/build.sh` の、RPATH を外すループ (142〜149 行目) の直後、`# 署名:` のコメント (150 行目) の直前に、次の 1 ブロックを足す。
**署名より前でなければならない**: 署名の後に `strip` すると署名が壊れる (コピーで確認。strip の直後の `codesign --verify --deep --strict` は rc=1)。

```diff
--- a/mac/build.sh
+++ b/mac/build.sh
@@ -147,6 +147,14 @@ for executable in "$APP/Contents/MacOS/Meltype" "$HELPER/Contents/MacOS/MeltypeD
         esac
     done < <(otool -l "$executable" | awk '$1=="cmd"&&$2=="LC_RPATH"{r=1;next} r&&$1=="path"{print $2;r=0}')
 done
+# 実行ファイルには、ビルドしたマシンのホームフォルダー配下の絶対パス (/Users/<名前>/…/mac/.build/ など) が、デバッグ用の記録 (stabs) として残る。
+# 配布先の Mac では使わず、ユーザー名と作業フォルダーの場所が配布物から読めてしまうので、署名の前に取り除く
+# (C# 側の libMeltypeNative.dylib は、上の StripSymbols=true で同じことをしている)。
+# -S が取り除くのはデバッグ用の記録だけで、関数名などの通常のシンボルは残る。strip するのは .app の中のコピーで、.build の下の元の実行ファイルは変わらない。
+# strip は「署名が無効になる」と警告を出すが、直後の codesign で署名し直す。
+for executable in "$APP/Contents/MacOS/Meltype" "$HELPER/Contents/MacOS/MeltypeDictionary"; do
+    strip -S "$executable"
+done
 # 署名: 環境変数 MELTYPE_MAC_IDENTITY (Developer ID Application の証明書の名前) があれば配布用に署名する
 # (Hardened Runtime・タイムスタンプ付き。公証 (notarization) はまだ行っていない (CI 無し))。無ければ自分の Mac で使うための署名。
 # 中から外へ: Frameworks の中身 → 辞書の管理画面 (Contents/Helpers) → Meltype.app。
```

- `set -euo pipefail` (9 行目) の下で動く。`strip` は警告を出しても rc=0 で終わる。警告は「changes being made to the file will invalidate the code signature」で、ビルドのログに 2 回出る (署名は、すぐ後ろの `codesign --force` で付け直す)。
- この差分は、HEAD `0ac6381` の `mac/build.sh` に `git apply --cached --check` が通り、`bash -n` も通る (どちらも何も変更しない確認)。追加するブロックは、そのままの文面でコピーに対して実行し、`build.sh` の署名の部分と同じ ad-hoc 署名をし直すと、`codesign --verify --deep --strict` が通った (rc=0)。
- コードの変更は `mac/build.sh` だけ (テストと `tools/` は `build.sh` の中身を読まない)。版番号・Info.plist・ほかのスクリプトは変えない。文書は §10。

## 5. 得と損

得:

- 配布物 (zip・pkg・自動更新で配る zip) から、ビルドしたマシンのユーザー名と作業フォルダーの場所が消える。
- 実行ファイルが小さくなる (`Meltype` で約 1.5 MB。圧縮後は未測定)。

損:

- ビルド手順が 1 か所増え、「署名より前」という順序の制約が付く (後ろに回すと署名が壊れる)。
- デバッグ用の記録 (stab) が配布する実行ファイルから無くなる。関数名などの通常のシンボルはほぼ残る (上の表)。クラッシュログの読み取りへの影響は未検証。strip するのは `.app` の中のコピーで、`.build` の下の元の実行ファイル (`$BIN/MeltypeIME`、`$BIN/MeltypeDictionary`) は、次の `swift build` で作り直されるまで残る。
- strip した実行ファイルが普通に起動するかは未確認 (コピーに対して strip・署名し直し・検証までで、起動はしていない)。実機で確かめる (§6 の 4)。

## 6. 確かめ方

1. ビルド (インストールはしない): `bash --noprofile --norc -c 'cd mac && ./build.sh --no-install'`。あわせて `bash -n mac/build.sh`。
2. 件数が 0 になったこと (パスの文字列は出さず、件数だけを見る):

   ```bash
   for f in mac/build/Meltype.app/Contents/MacOS/Meltype \
            mac/build/Meltype.app/Contents/Helpers/MeltypeDictionary.app/Contents/MacOS/MeltypeDictionary; do
     printf '%s: ' "${f##*/}"; grep -a -o -E '/Users/[A-Za-z0-9._-]+/' "$f" | wc -l
   done
   ```

   どちらも 0 になる。1 以上なら、署名の前に strip が効いていない。
3. 署名: `build.sh` 最後の `codesign --verify --deep --strict` (167 行目) が通ること。`codesign -dv mac/build/Meltype.app 2>&1 | grep -E 'Signature|TeamIdentifier'` が `Signature=adhoc` と `TeamIdentifier=not set` のままであること。
4. 実機: これまでの作業と同じ進め方で入れ替え (`./build.sh`)、TextEdit でいつもの入力を数文字。入力メニューの「設定・辞書…」から辞書の管理画面が開くこと (`MeltypeDictionary` も strip したため)。
5. 独立した査読を受けてから完了とする (実装者と査読者を分ける)。

## 7. やらないこと (リョウさんの方針・ガードレール)

- push・タグ・`gh release create|upload|edit`・X への投稿は、リョウさんの明示の OK が出るまでしない。コミットはローカルのみで、明示したファイルだけ (`git add -A` を使わない。未追跡の `docs/HANDOFF-*.md` が混ざる)。
- 版番号を上げない。リリースの準備は別の作業。
- `mac/dist` の既存の pkg・zip を上書きしない・消さない (`mac/dist` は `.gitignore` の対象で、消えると git では戻せない。`Meltype-mac-1.1.5.zip` の SHA-256 は Release の `digest` と一致している)。`make-pkg.sh`・`make-zip.sh` は、確認のためだけに回さない (どちらも出力先の既存ファイルを確認なしで置き換える。`make-zip.sh` は先に `rm -rf` で消す。[PKG_REVIEW_2026-10.md](PKG_REVIEW_2026-10.md) の §6)。
- pkg の実インストール (ダブルクリック、`installer -pkg … -target …`) をしない (リョウさんは「インストールはしません」と判断した)。行うなら、リョウさんの OK が出てから。実機の確認で `./build.sh` による開発ビルドの入れ替えをするのは、これまでどおり。
- 署名の基盤 (Developer ID・公証・作者署名) と CI を提案しない (運用コストのため見送り。`docs/RELEASE.md` 冒頭)。
- pkg の配布をやめない。Release 本文の「動作確認中」の表記を消さない (リョウさんの判断)。
- v1.1.5 の Release 本文を直さない (リョウさんの判断)。
- 別のセッションが同じ作業ツリーを使っている。着手前に `git status --short` と `git log --oneline -3` を見る。

## 8. 次のリリースのノートを書くとき

開き方は、最初から併記で書く (リョウさんの判断)。前回の本文の文面をそのまま引き継がない (Apple の案内では、macOS 15 以降は右クリックでは開けない)。`README.md` の 50〜51 行目と 62 行目の書き方に合わせる。62 行目の括弧の中は次のとおり:

> 「開発元が未確認」と出たら、右クリック →「開く」→「開く」。macOS 15 以降は、一度ダブルクリックしてから システム設定 → プライバシーとセキュリティ → 下のほうの「このまま開く」

- 根拠: [Apple Developer News 2024-08-06](https://developer.apple.com/news/?id=saqachfa) (macOS 15 以降は右クリックでは開けない)、[Apple サポート 102445](https://support.apple.com/ja-jp/102445) (システム設定 → プライバシーとセキュリティ → 「このまま開く」)、[Mac ユーザガイド mh40616](https://support.apple.com/ja-jp/guide/mac-help/mh40616/mac) (試みてから約 1 時間。ログインパスワードを入力する)。
- 右クリックの案内は消さない (minos が 13.0 で、macOS 13/14 の利用者がいる。13/14 の実機は未確認)。
- 最後のボタン名は、Apple の 2 ページで違う (102445 は「開く」、mh40616 は「OK」)。書かない。
- pkg の節は、「動作確認中」と「うまくいかないときは zip を使うか…」を保つ。
- 公開 (`gh release create`) は、リョウさんの OK が出てから。既存の本文を編集するときは、先に今の本文をファイルに保存する (`gh release view v<版>-mac --json body --jq .body > <ファイル>`。API に編集履歴がなく、上書きすると戻せない)。

## 9. ついでに (任意。リョウさんの指示で、メインのセッションに任せる)

`docs/UPSTREAM_PR_TRACKER.md` の 22 行目 (S1 の記録。`セキュリティレビュー (4 領域` で始まる) が、「1. 取り込みの項目」の表 (7 列) の中に 2 列の行として入っている。本来は「4. 更新の記録」の表 (2 列)。中身は正しく、見た目が崩れるだけ。更新の記録の表へ、時刻の順に置く (この表は「作成」の行のあと新しい順。S1 のコミット `9084282` は 19:09 で、「P35 を実装し…」の行 (コミット `aa65978`、20:34) より古く、「Q2・Q3 を実機…」の行 (コミット `6c005b0`、18:51) より新しい。その間)。着手前に `git diff --stat -- docs/UPSTREAM_PR_TRACKER.md` が空であることを確かめる。

## 10. 終わったら

- 次のリリースの実行ファイルで、§6 の 2 の件数が 0 になったことが確認できたら、P38 の検査項目 ([PKG_REVIEW_2026-10.md](PKG_REVIEW_2026-10.md) の §7 の 6) の期待値を 0 にできる (1.1.5 は 1,020 件のまま)。
- 文書 (前例の `b778d26` は、同じコミットで `mac/README.md` と `docs/IMPROVEMENT_PLAN.md` も更新している): `mac/README.md` の RPATH の説明 (HEAD `0ac6381` で 83〜84 行目) に、strip の一文を足す。`docs/IMPROVEMENT_PLAN.md` の末尾に、S1・P36 と同じ形で「## 追加項目 …」の節を足し、実測したことと、まだ確かめていないことを分けて書く (ID は実装するセッションが決める)。
- この文書は一時的なもの。**未追跡なので、削除すると git で戻せない。削除はリョウさんに確認してから行う。**
