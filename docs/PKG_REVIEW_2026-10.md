# pkg の査読: Meltype-mac-1.1.5.pkg (2026-10-11)

GitHub Release の `Meltype-mac-1.1.5.pkg` (タグ `v1.1.5-mac`) が「インストールできるか」を、**実インストールをせずに**、pkg・リポジトリ・導入済みの Meltype を変更しない形で調べた記録。結論、確認できたこと、見つかったことと対応、判断と見送り、未確認、`make-pkg.sh`・`make-zip.sh` の注意、P38 (配布前の自動チェック。[UPSTREAM_PR_TRACKER.md](UPSTREAM_PR_TRACKER.md)) の検査項目の案をまとめる。

**確かめ方**: この文書の事実の多くは、書くときに手元で再確認したか、独立した査読 (別のエージェント) が再現できたもの。再現できていないものは、本文で「推測」「推定」「記録による」と書き分けた。

## 1. 結論

静的検査の範囲では、構造・出所・スクリプトに、インストールを妨げる欠陥は見つからなかった。ただし未署名なので Gatekeeper の手動許可が要り、macOS 26 での実インストールは未確認。**「インストール可能」とは言い切らない**。

## 2. 確認できたこと (pkg の実行もインストールもしていない。展開と写しはリポジトリの外のパスに作った)

- SHA-256 (`eceb709e…`) が Release の `digest` と一致する。`mac/dist/Meltype-mac-1.1.5.pkg` ともバイト一致。zip も Release の `digest` と一致し、zip 内の `Meltype` 実行ファイルは pkg 内のものと同一 (SHA-256 `e5a46b4d…`)。
- 未署名: `pkgutil --check-signature` は no signature、`spctl --assess --type install -vv` は rejected (source=no usable signature)。
- 展開 (`pkgutil --expand-full`) は成功する。Distribution は `hostArchitectures="arm64"`・`os-version min="13.0"`・`enable_currentUserHome="true"` (`enable_localSystem` と `enable_anywhere` は false)。PackageInfo は `install-location="/Library/Input Methods"`・`relocatable="false"`・`auth="root"`・`upgrade-bundle`・script の `timeout="600"`。`strict-identifier` は `io.github.yksr-melt.inputmethod.Meltype` と `io.github.yksr-melt.Meltype.Dictionary` の 2 つ。ホームフォルダー限定の pkg なのに `auth="root"` で、Installer が管理者の認可を求めるかは未確認 (§5)。
- postinstall はタグ `v1.1.5-mac` のものと差分 0 行で一致する。Distribution は、`VERSION` の置換と、productbuild による書き換え (XML 宣言の `standalone="yes"`、`pkg-ref` の `installKBytes`・`updateKBytes` と参照先の先頭の `#`、`bundle-version` ブロックの追加、末尾の改行の消失) を除いて一致する。
- S1 ([IMPROVEMENT_PLAN.md](IMPROVEMENT_PLAN.md) の追加項目。セキュリティレビューの対応) の署名検査 (コミット `9084282`) は `v1.1.5-mac` の祖先ではなく、タグ時点の postinstall に `codesign` は無い。つまり **1.1.5 の pkg には入っていない** (次のリリースから)。
- ペイロード: symlink・setuid/setgid・他者書き込み・`.DS_Store` が 0。Mach-O は 4 本。arch はすべて arm64 を含む (`libswiftCompatibilitySpan.dylib` だけ arm64e との fat)。macOS 向け (platform 1) の minos は最大 13.0 (`Meltype` と `MeltypeDictionary` が 13.0、`libMeltypeNative.dylib` が 12.0、`libswiftCompatibilitySpan.dylib` が 11.0)。`libswiftCompatibilitySpan.dylib` だけは `LC_BUILD_VERSION` を 2 つ持ち、2 つ目は Mac Catalyst (platform 6) の minos 14.0。RPATH は、`Meltype` が `/usr/lib/swift`・`@loader_path`・`@executable_path/../Frameworks`、`MeltypeDictionary` が `/usr/lib/swift`・`@loader_path`・`@executable_path/../../../../Frameworks`、dylib 2 本は無し。ad-hoc 署名 (TeamIdentifier なし、エンタイトルメントなし) は `codesign --verify --deep --strict` を通る。リンクされた依存 (`otool -L`) のうちシステム外のものは、`Meltype` の `@rpath/libswiftCompatibilitySpan.dylib` (同梱) だけ。`libMeltypeNative.dylib` (同梱) は、`Meltype` と `MeltypeDictionary` が実行時に `dlopen` で `Contents/Frameworks` から読むので、`otool -L` の一覧には出ない。Info.plist・Distribution・PackageInfo の版は 1.1.5 (21) で一致する。
- **ビルド時のパスが残っている** (重さは低): `Meltype.app` の中の Mach-O のうち、`Meltype` に 987 件、`MeltypeDictionary` に 33 件、合計 1,020 件の `/Users/<ユーザー名>/…` (ビルドしたときのホームフォルダー配下の絶対パス) がある。内訳は、リポジトリの `mac/.build/` の下が `Meltype` 976 件・`MeltypeDictionary` 17 件、`mac/Sources/` の下が 11 件と 16 件で、それ以外は 0 件。dylib 2 本は 0 件。zip の `Meltype` も同じ (987 件)。`nm -pa` で見ると、デバッグ用のシンボル (stabs) の SO と OSO のうち `/Users/` を含むエントリが、`Meltype` は各 493 件、`MeltypeDictionary` は各 16 件ある。写しに `strip -S` をかけると 987 件と 33 件がどちらも 0 件になるので、出どころはこのデバッグ用シンボルと確認できた。ビルドで strip をかけているのは C# 側の `libMeltypeNative.dylib` だけ (`mac/build.sh` 57 行目の `StripSymbols=true`)。Swift 側の `Meltype` と `MeltypeDictionary` には、2026-10-12 時点で strip の処理が無い (`strip -S` は署名を無効にするので、実際には署名の前に行う必要がある)。秘密情報ではなく、ユーザー名とディレクトリ構成が読める程度。次のリリースのビルドから消すことに決まった (§4)。
- 開発用の Mac に入っている Meltype (この文書を書いた時点の測定。並行する作業で変わる): 1.1.5 (21) で、版番号は pkg と同じ。実行ファイルの SHA-256 が pkg 内のものと違い、タグより新しい開発ビルドと見られる (SHA の違いと更新時刻からの推定)。この Mac に pkg を入れると、同じ版番号のまま古い中身に置き換わると見込まれる (推測。実インストールは未実施)。

## 3. 見つかったことと対応 (`135d21d`)

- **(A) 開き方の案内**: pkg の案内 (`mac/README.md`、`mac/make-pkg.sh` のコメント) と、zip のアンインストールの案内 (`README.md`、`mac/INSTALL.txt`)、`mac/Install Meltype.command`・`mac/Uninstall Meltype.command` のコメントが「右クリック → 開く」だけだった。macOS 15 以降は、この方法では開けない ([Apple Developer News 2024-08-06](https://developer.apple.com/news/?id=saqachfa))。Apple の案内は、一度開こうとしてから システム設定 → プライバシーとセキュリティ →「このまま開く」 ([Apple サポート 102445](https://support.apple.com/ja-jp/102445)、[Mac ユーザガイド](https://support.apple.com/ja-jp/guide/mac-help/mh40616/mac))。zip のインストールの案内 (`README.md`、`mac/INSTALL.txt`) には併記があったので、同じ書き方にそろえた。右クリックは macOS 14 以前向けに残した (minos が 13.0 のため)。
- **(C) 完了画面**: pkg の `mac/pkg/resources/conclusion.html` が案内するアンインストーラーは、zip にしか入っていない。zip の入手先 (リリースページ) を足した。pkg だけで入れた人は zip を持っていない。
- **(B) 記録の訂正**: [SECURITY_REVIEW_2026-10.md](SECURITY_REVIEW_2026-10.md) の pkg 経路の中-2 (見出しは「pkg 経路はインストール時の完全性検査が無く、Gatekeeper の評価もされない」。同じ文書に zip の `install.sh` の中-2 もある) が、Release 本文に「右クリック→開く」「このまま開く」の説明があると書いていたが、全 16 リリース (v1.0.1〜v1.1.5) の本文を取得して数えると、「このまま開く」は 0 件だった (「右クリック」は全版にある)。記録文書なので、元の記述は `<del>` で残して日付つきで訂正した。
- **配布物への反映**: `conclusion.html` は pkg の中身、`mac/INSTALL.txt` と `.command` 2 本は zip の中身なので、次のリリースから反映される (配布中の 1.1.5 は変わらない)。`README.md` と `mac/README.md` は、push すると GitHub のページに出る。
- **この修正の査読**: 差分は独立した査読を 1 回受け、指摘 2 件を反映した (コミットメッセージによる)。

## 4. 判断と見送り

**リョウさんの判断**

- v1.1.5 の Release 本文 (22・28 行目) には併記しない (2026-10-11)。公開物のため。査読担当の整理では、得は、公開済みの記録を変えないこと。損は、Release のページだけを見た人に、macOS 15 以降で通じない「右クリック → 開く」だけが案内されること (README には併記がある)。
- pkg の配布は、今のまま続ける (2026-10-12)。[SECURITY_REVIEW_2026-10.md](SECURITY_REVIEW_2026-10.md) の pkg 経路の中-2 の修正案 (b)「pkg が「試験中」である旨を Release 本文にも残し、推奨経路を zip + install.sh に揃える」は、v1.1.5 の Release 本文がすでに部分的に満たしている。zip を先に置いて「zip (動作確認済み)」、pkg の節を「pkg (ダブルクリックのインストーラー・動作確認中)」とし (20・27 行目の見出し)、29 行目は、太字で「まだ実機での確認が十分ではありません。」と書き、続けて「うまくいかないときは zip を使うか、Issue で教えてください。」と書いている。更新の手順も zip 前提 (17 行目)。「推奨」とまでは書いていない。この表記を保つことで足りるとする。(c)「中-1 の署名が実現するまでは pkg の配布をやめる選択も検討」は採らない。署名の基盤 (Developer ID・公証・作者署名) と CI は、運用コストのため見送っている ([RELEASE.md](RELEASE.md) 冒頭、[IMPROVEMENT_PLAN.md](IMPROVEMENT_PLAN.md) の S1)。査読担当の整理では、得は、署名の基盤や CI の運用コストを足さずに、pkg を配布し続けられること。損は、未署名で、実機でも確かめていない pkg の配布が続くこと (利用者に警告を押して開く習慣がつきうる。postinstall の `codesign --verify` は、破損は見つけるが、署名し直した改ざんは見つけられない。しかも 1.1.5 の postinstall には入っておらず、`9084282` で入った HEAD の版から効く)。査読担当の見立て: 「動作確認中」を外すかどうかは、pkg を実機で確かめられたときに、改めて決めればよい。
- 次のリリースのノートは、開き方を最初から併記で書く (2026-10-12)。macOS 15 以降は「このまま開く」、14 以前は右クリック → 開く。`README.md` の書き方に合わせる。公開は別途リョウさんの OK が出てから。査読担当の整理では、得は、macOS 15 以降の利用者に通じる手順が最初から載ること。損は、macOS 13/14 の右クリックが実機では未確認なことと、最後のボタン名が Apple の 2 ページで違うため (102445 は「開く」、mh40616 は「OK」)、ボタン名は書けないこと。
- 配布物に残るビルド時のパス (§2) は、次のリリースのビルドから消す (2026-10-12)。実装はメインのセッションが行う。実施の有無は `git log --oneline -S'strip -S' -- mac/build.sh` が空でないかで確かめる (`mac/build.sh` を触る別のコミットも `git log -- mac/build.sh` には出るため)。公開済みの 1.1.5 は変わらない。方法 (`mac/build.sh` で、Swift 側の実行ファイル 2 本に、署名より前に `strip -S`) は査読担当の提案。査読担当の整理では、得は、配布物にビルド元のユーザー名とディレクトリ構成が残らないこと (実行ファイルも小さくなる)。損は、ビルド手順の変更と確認が要ること、デバッグ用の記録が配布する実行ファイルから無くなること (`.build` の下の元の実行ファイルには残る)。1.1.5 の写しでは、`strip -S` で `/Users/` の件数が 0 になり、通常のシンボルはほぼ残り (`Meltype` は 32,135 → 32,134 件)、`build.sh` と同じ ad-hoc 署名をし直すと `codesign --verify --deep --strict` が通った。strip した実行ファイルが普通に起動するかは未確認 (実機で確かめる)。

**査読担当の判断 (対応しない)**

- AppleDouble `._*` の除去: `make-pkg.sh` の `ditto` に `--noextattr --norsrc` を足す案は、**試したが効かなかった** (`._*` は 3,470 件のまま。`xattr -cr` を足しても同じ。コピー先のファイルに macOS が `com.apple.provenance` を付けるため、と推測)。`._*` は 1 件 163 バイトで、全件で非圧縮 565,610 バイト (約 0.57 MB)。無害と見て放置する。Installer での挙動は未確認 (§5)。
- postinstall の `chown -R`: `if [[ "$(id -u)" == 0 ]]` の中、つまり root で動くときだけの分岐。ホームフォルダーのみの pkg では通らない見込み (Installer の挙動からの見込みで、実機では未確認)。[SECURITY_REVIEW_2026-10.md](SECURITY_REVIEW_2026-10.md) でも「`-R` のみでシンボリックリンクを追わないので安全」と評価済み。残る懸念はハードリンク経由の理論上のもの (推測)。対応は不要。
- `mac/pkg/resources/welcome.html` の「管理者パスワードは不要です」: 変更しない。Installer の段階の話で、「このまま開く」の段階で聞かれるログインパスワードとは別なので、矛盾しない (`mac/README.md` の pkg の説明にも同じ言い方がある)。ただし macOS 26 の Installer が本当に聞かないかは未確認 (§5)。

## 5. 未確認

- 実インストール (macOS 26 の Installer が通るか、`auth="root"` の pkg でも Installer の段階で管理者パスワードを聞かれないか)。リョウさんは「インストールはしません」と判断した (2026-10-11)。行うなら、リョウさんの OK が出てから。
- 初回登録 (`--register`) の経路。別の Mac か新規ユーザーが要る。
- macOS 13/14 での「右クリック → 開く」(Sequoia での変更から導いた推定)。
- ペイロードの `._*` が Installer で問題を起こさないか (bsdtar と pkgutil での展開は無事)。
- postinstall の署名検査が失敗したとき (`exit 1`、S1)、Installer の画面にどう見えるか。
- 案内文の「ログインパスワードを聞かれることがあります」(Apple の日本語ガイドによる。macOS 26 の実機では未確認)。

## 6. `make-pkg.sh`・`make-zip.sh` の注意

出力先の `mac/dist/Meltype-mac-<版>.pkg` に既存のファイルがあっても、確認せずに上書きする (小さなダミーの payload で `pkgbuild` と `productbuild` を同じ出力先に 2 回かけて再現した)。build が公開済みの版番号のままだと、実行すると `mac/dist/Meltype-mac-1.1.5.pkg` (Release の `digest` と一致している) が、公開版と `digest` の違う開発ビルドの pkg に置き換わる。公開版は GitHub から取り直せるが、`gh release create` で dist のファイルを添付する作業のときに取り違える恐れがある。HTML や README の確認のためだけに実行せず、実行するなら先に `cp -p` で退避して、`shasum -a 256` が Release の `digest` と一致することを確かめる (または版を上げてから)。

`mac/make-zip.sh` も同じ向きの危険がある。作り始めに `rm -rf "$OUT" "dist/Meltype-mac-$VERSION.zip"` (15 行目) で、同じ版の zip と作業用のフォルダー (`dist/Meltype-mac`) を先に消してから作り直す。build が公開済みの版番号のままだと、Release の `digest` と一致する `mac/dist` の zip が消え、開発ビルドの zip に置き換わる (確認は出ない)。手元の `mac/dist` には `Meltype-mac-1.1.5.zip` があり、SHA-256 が Release の `digest` と一致することを確かめた (2026-10-12)。`mac/dist` は `.gitignore` の対象なので、消えると git では戻せない。公開版は GitHub から取り直せるが、`gh release create` で dist のファイルを添付する作業のときに取り違える恐れがある。実行するなら先に `cp -p` で退避し、`shasum -a 256` が Release の `digest` と一致することを確かめる (または版を上げてから)。

## 7. P38 の検査項目の案

P38 として実装するなら使える項目の案 (P38 は未着手。実装するかはリョウさんの判断)。**pkg・`mac/dist`・リポジトリ・導入済みの Meltype を変更しない**形にする: pkg・postinstall・`make-pkg.sh` を実行しない、`installer` に `-target` を付けない (`-pkg` と `-target` を並べる書式はインストールそのもの)、`mac/dist` を上書きしない。書き込むのは、項目 3 と 6 の展開先 (リポジトリの外の新しいパス) だけ。`xattr -l` と `mdls` は署名つきのダウンロード URL (`kMDItemWhereFroms`) を出しうるので使わず、属性の名前だけを見る。

1. 出所: pkg の SHA-256 を、`mac/dist` の同名ファイルと、公開後は Release の `digest` (`gh release view v<版>-mac --json assets`) に突き合わせる。
2. 署名の状態: `pkgutil --check-signature` (署名しない方針のあいだは no signature。署名ありに変わったら気づく)、`spctl --assess --type install -vv`、`installer -pkginfo -pkg <pkg>` (または `-dominfo`・`-volinfo`)。
3. 展開: `pkgutil --expand-full <pkg> <存在しないパス>` が rc 0。展開先は存在しないパスにする (空のフォルダーでも rc 1 で失敗する)。リポジトリの外に作る。
4. 構造: Distribution の `hostArchitectures`・`os-version min`・`domains`、PackageInfo の `install-location`・`relocatable`・`auth`・`upgrade-bundle`・`strict-identifier`・script の `timeout` が想定どおり (1.1.5 の実測値は §2)。
5. 出所の整合: postinstall がバイト一致、Distribution が `VERSION` の置換後に一致。比べる相手は、公開後はタグ、公開前はタグを付ける予定のコミット (HEAD)。ただし productbuild が Distribution を書き換えるので (`standalone="yes"`、`installKBytes`・`updateKBytes`、参照先の先頭の `#`、`bundle-version` ブロック、末尾の改行。§2)、完全一致ではなく、その部分を除いて比べる。
6. ペイロード: symlink・setuid/setgid・他者書き込み・`.DS_Store` が 0。Mach-O は arch に arm64 を含み、macOS 向け (platform 1) の `LC_BUILD_VERSION` の minos が Distribution の `os-version min` (13.0) 以下で (`libswiftCompatibilitySpan.dylib` は Mac Catalyst の platform 6 の行も持つので、platform で見分ける)、RPATH と、リンクされた非システム依存 (`otool -L`) が想定どおり。`libMeltypeNative.dylib` が `Contents/Frameworks` にある (実行時に `dlopen` で読まれるので `otool -L` には出ない)。1.1.5 の実測は §2。`/Users/<名前>/` の埋め込みは件数を記録する (`Meltype.app` の中の Mach-O が対象で、postinstall の `/Users/$CONSOLE_USER` は対象外。1.1.5 は 1,020 件。ビルド側で消すことに決まった (§4) ので、消す処理が入った版からは期待値を 0 にできる)。数え方は `grep -a -o -E '/Users/[A-Za-z0-9._-]+/' <ファイル> | wc -l` で、`strings` は、既定でも `-a` でも、987 件ある `Meltype` で 0 件を返す (全バイトを見る `strings -` なら 987 件)。`grep -a -o -E` を正とする。`._*` は、項目 3 の展開後のツリーでは数えられない (xattr に戻って見えない)。`pkgutil --expand <pkg> <リポジトリの外の存在しないパス>` のあと、`lsbom <パス>/Meltype-component.pkg/Bom | grep -c '/\._'` か、`gunzip -c <パス>/Meltype-component.pkg/Payload | tar -tf - | grep -c '/\._'` で数える (1.1.5 は 3,470 件。参考値で、無害と見ている)。
7. 署名: `codesign --verify --deep --strict` が通る、ad-hoc (TeamIdentifier なし)、エンタイトルメントなし。
8. 版の一致: Info.plist の `CFBundleShortVersionString`・`CFBundleVersion`、Distribution、PackageInfo、そして公開後はタグ、公開前は HEAD の版。
9. 案内文の整合: `git grep -n -E "右クリック[^→]*→ *「開く」" -- README.md mac` の各行の前後 3 行に「このまま開く」がある。`135d21d` 以降の作業ツリーでは 8 行にヒットし、すべて満たしている。
10. (開発用の Mac 向けの参考) 導入済みの Meltype と pkg の版・実行ファイルの SHA-256 を比べる (同じ版番号で中身が違う状態に気づく)。

検査の雛形は、作者の環境のスキル (`meltype-mac-maintenance` の `scripts/audit_payload.py`) にあり、リポジトリには無い。扱うのは symlink・特殊権限・`.DS_Store`・`._*`・xattr の名前・Mach-O の列挙で、項目 6 の arch・minos・RPATH・依存・`/Users/` は含まない。P38 で実装するなら、リポジトリに収録する必要がある。
