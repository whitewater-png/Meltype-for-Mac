# Meltype for Mac セキュリティレビュー（2026-10-11、HEAD 3b32db8）

対象: このリポジトリ の Mac 版 (Swift IME / NativeAOT dylib / 配布スクリプト / 自動更新)。Windows/Linux は対象外。
方法: 4 領域 (A 自動更新・B 入力データ・C FFI 境界・D ビルド/配布/サプライチェーン) を独立にコードレビューし、「中」以上の指摘はヘンリエッタが実物で再確認、さらに統合版を別エージェントが 3 回査読（3 回目は必須修正なし）。ビルド・実機操作はしていません。実機側は `ls -la` と `codesign -d` のみで、ログの中身は読んでいません。

## 結論（先に）

- **重大度「高」の問題はありません。** パスワード欄など秘匿入力中 (`IsSecureEventInputEnabled`) は Core を呼ばずキーを素通し、打鍵内容のログは既定 OFF、学習データは 0600/0700 + 原子的置換、自動更新は URL 固定・SHA-256 照合・codesign 検証・ロールバック付き。IME として押さえるべき基本は押さえられています。
- 「中」は 10 件。半分 (M1/M2/M6/M8/M9) は **ad-hoc 署名で作者の手元ビルドを配る、という配布モデルの限界** で、コードの欠陥ではなく「GitHub アカウントと TLS を信頼する」前提を明文化するか、帯域外の署名を足すかの判断事項です。残り半分 (M3/M4/M5/M7/M10) はコードと運用の問題です。
- 今すぐ手を動かすべきは **実機に残っているトレースログ 2 本の削除 (M4)** と **ログに VK が出る箇所の修正 (M3)**、**pkg の postinstall に codesign 検証を足す (M8)** の 3 点です。いずれも小さい変更です。

## 指摘一覧（重大度順）

### 中

| # | 領域 | 場所 | 問題 | 影響 | 修正案 |
|---|---|---|---|---|---|
| M1 | A/D | `Updater.swift:434-450`, `install.sh`, `SECURITY.md:20-22` | 更新物の真正性が「GitHub + TLS」のみ。SHA-256 は URL と同じ `releases/latest` JSON の `digest` 由来（同一ソース）。codesign は ad-hoc なので改ざん検知のみで発行者は証明しない（SECURITY.md はこの限界を明記済） | GitHub アカウント乗っ取り or リリース権限の悪用で、全利用者に任意コードを配れる | 埋め込み公開鍵で zip を Ed25519 署名し Updater で検証（Swift は CryptoKit `Curve25519.Signing` で可。minisign 形式は BLAKE2b プレハッシュが既定で CryptoKit に無いため、minisign に合わせず生の Ed25519 署名を独自形式で配る方が確実）。Developer ID にする場合は Updater 側で Team ID 要件 (`codesign -R`) を照合しないと発行者検証にならない。最低限、信頼前提を README/リリースノートにも明記 |
| M2 | A | `Updater.swift:460, 600-` | zip 内の `install.sh`（codesign の封印外）を `--yes` で起動し、隔離属性の除去 (Gatekeeper 回避) が利用者の目に触れずに行われる | digest で担保されるので現状の実害は小さいが、M1 が破られた場合に最初に動くコードが未検証スクリプトになる | (a) install.sh のハッシュを Updater に埋め込んで照合、または install.sh 相当を Swift 側に取り込む。(b) 更新ダイアログに隔離解除を行う旨を表示 |
| M3 | B | `CompositionController.cs:501` (確認済) | `FileLog` ON・`LogTypedText` OFF でも、英数判定を打ち切ったキーの VK を 16 進で記録する。打ち切りを起こすのは英字以外のキーすべて（数字・Enter・Space・記号）で、「英字→数字→英字→数字」の列では非英字キーが全部残る | `Log.Text` を通さない情報がログに残る（`Log.cs:35-36` の方針では「判定語の先頭数文字」は残る設計だが、VK はその範囲外）。英字+数字はパスワードに多い | VK を出さず「英字以外 / 数字 / Enter」の分類だけ記録する。`SecurityTests.cs` にガードテストを追加 |
| M4 | B | 実機 `~/Library/Application Support/Meltype/imk-trace.log`, `imk-trace.log.round1` (201KB / 246KB, 0600。サイズと権限のみ確認済) | 過去のデバッグで作ったトレースログが残存。実装上は確定文字と前後 20 文字を書くため、打鍵内容を含むと推定される（中身は未読）。`MeltypeTraceIMK` は現在未設定 | 同一ユーザー権限なら読める。Time Machine 等のバックアップにも乗る | **削除を推奨**（リョウさんの OK 後に私が実行可）。併せてトレースに有効期限 or 起動時の警告表示を追加 |
| M5 | B | `InputController.swift:130`, `CompositionController.cs:1840-1859 LearnLanguage()` (主), `1963-2000 Learn()` (従) (確認済) | 秘匿判定が `IsSecureEventInputEnabled()`（グローバル状態）のみ。Secure Input を使わない独自パスワード UI や Secure Keyboard Entry OFF のターミナルでは IME が通常動作する | 変換していない確定でも `LearnLanguage()` が走り、打った英字列 (`_text.Raw`、小文字化される) が `languages.json` に保存され、`FileLog` ON なら `Log.Text` 経由でログにも出る (`LanguageMemory.cs:147,156`)。条件は「自動判定が日本語なのに英数モードで確定」「自動判定が英語なのにかなモードで確定」「Tab で提案どおり英字に確定」。パスワードがローマ字に化けて英数に戻して確定する操作がまさにこの経路。緩和要因は、英字のみ 2 文字以上の語に限られる (`LearnLanguage()` の `IsAsciiLetter` 判定、数字・記号を含む語は落ちる) ことと、利用者の明示操作を要すること | `IMKTextInput` に secure かを問う API は無い（補助判定は不確実）。現実的な対策は、コード種別アプリ（ターミナル・エディタ）で英字語の学習を既定 OFF にする設定、`AppRules` での per-app OFF の案内を README に明記 |
| M6 | C | `NativeCore.swift:205-207` dlopen、導入済アプリ `flags=0x2(adhoc)`, Hardened Runtime なし (確認済) | `DYLD_LIBRARY_PATH` が設定された環境では、絶対パス指定でも葉名で先に探索され dylib を差し替えられる | 同一ユーザー権限が必要で権限昇格にはならない | Hardened Runtime (`--options runtime`) で閉じられる見込みだが、ad-hoc（Team ID なし）でライブラリ検証が同梱 dylib を通すか拒否するかは領域レビューと見立てが割れており未検証（『推測』、要実機確認。拒否されるなら `disable-library-validation` entitlement の併用）。ad-hoc のままなら受容して文書化 |
| M7 | C | `NativeCore.swift` dlopen 失敗時, `Exports.cs:184-196` Destroy 系 | (a) dlopen 失敗や ABI 不一致時に fail-open（全キー素通し）で NSLog のみ、利用者への通知なし。(b) `GCHandle.FromIntPtr` は解放済/不正ハンドルで未定義動作。コメントの「例外で止まる」はゼロ値のみ有効 | (a) 利用者が「IME が壊れている」と気づけない。(b) 現状 Swift 側は単一所有で実害なしだが、将来のリファクタで二重解放が起きても防波堤がない | (a) 失敗時に 1 回だけ通知（辞書アプリは既に表示している）。(b) ハンドルを ID 間接化するか、コメントを実態に合わせる |
| M8 | D | `mac/pkg/scripts/postinstall:61` (早期 exit 含め常に `exit 0`, 確認済), `mac/README.md:92` | pkg 経路は SHA 照合も codesign 検証もなく、Installer 配置では Gatekeeper 評価も起きない。unsigned である旨は README に 1 文のみ | 改ざんされた pkg でも成功したように見える | postinstall 冒頭で `codesign --verify --deep --strict` を実行し失敗なら `exit 1`（数行で済む）。README の注意を強める |
| M9 | D | `.github/workflows` なし、`build.sh:151` が存在しない `mac.yml` を参照 | CI もビルド来歴 (provenance) もなく、配布物は作者の手元ビルド。ビルドマシンの侵害を検知する手段がない | M1 と同根。利用者はソースと配布物の対応を検証できない | GitHub Actions で再現ビルド + attestation、または最低限 SBOM/ビルド環境の記録を Release に添付。再現性の前提として `global.json` + `packages.lock.json` (`--locked-mode`) を追加（D 領域では「中」、ここでは M9 に併合）。`build.sh:151` のコメント修正 |
| M10 (訂正: 対応時に誤りと確認。context は azooKey に渡していない → 低) | B | `InputController.swift:183, 697-705`, `Converter.swift:181` | 変換中でない各キーで前後 20 文字を読み（秘匿入力中・`AppRules` で OFF のアプリ・未確定文字がある間は読まない）、確定時に azooKey へ context (直前テキストが日本語と判定された場合のみ、末尾 10 文字以内。`CompositionController.cs:1573-1579`) を渡す | 周辺テキスト（IME が打っていない文字も含む）が変換エンジンに流れる。azooKey 側が context を永続化するかは未確認（『推測』） | azooKey の学習/永続化の有無を確認。永続化があるなら context を渡す範囲を絞るか、学習 OFF オプションを明記 |

### 低（抜粋。詳細は各領域レポート）

- A: 表示用 `tag_name` を無制限にダイアログへ / 既定ボタンが「更新する」 / `update.log` に `O_NOFOLLOW` なし / 版解釈が C#・Swift の二重実装
- B: azooKey のファイルが 0644（親 0700 で保護）/ 候補・ヒントウィンドウは AX から読める（IME 共通の性質）/ NSSpellChecker に語を送る（OS 内）
- C: `privateFrameworksPath` nil 時に `/libMeltypeNative.dylib` を開く / 埋め込み NUL で文字列が切れる / `MELTYPE_DATA_DIR` が本番でも有効
- D: `swift build` に `--force-resolved-versions` 推奨 / git author に個人 gmail（公開リポジトリで可視）

### 秘密情報

追跡ファイル・全履歴ともトークン/鍵のパターンは 0 件。`src/Meltype/AppInfo.cs:26` に本家作者の公開連絡先があるのみ（本家と同一）。`.gitignore` は build/dist/bin/obj/鍵類を除外。GitHub の Private vulnerability reporting は有効（API で確認）。

## 良い点

- 秘匿入力の扱いが一貫している（Core 未呼出・周辺テキスト未読取・トレース停止の 3 点が同じ条件で揃う）。
- 自動更新の手順が「退避 → rsync → 再検証 → ロールバック → pkill」の順で、シェル補間ゼロ・絶対パス・固定 PATH。URL 検証は接頭辞完全一致でテスト固定済。
- `SafeFile` / `Log.Text` に保護ロジックが集約され `SecurityTests.cs` がある。シェルスクリプトは `set -euo pipefail`・mktemp・rm 前のパス等価確認が徹底。
- FFI は 63 エクスポート + 6 コールバックで所有権規約が一貫し、アロケータも一致。

## 推奨する順番

1. **M4** トレースログ 2 本の削除（リョウさんの OK 後、私が実行可）
2. **M3** VK ログの修正 + ガードテスト（小さく、Core 共通なので Windows にも届く。挙動変化はログ出力のみ）
3. **M8** postinstall に codesign 検証（数行）
4. **M1/M2/M9** 配布モデルの判断：(a) 現状維持 + 信頼前提を README に明記、(b) Ed25519 で Release 署名 + Updater 検証 + CI ビルド。(b) は鍵管理と CI 運用が増えるトレードオフ
5. **M10** azooKey の context 永続化の確認、**M5** 学習 OFF 設定、**M7** 失敗通知、低の順次対応

各領域の詳細は以下に付録として収録。対応の記録は `IMPROVEMENT_PLAN.md` の S1。


---

# 付録 A: 領域レビュー原文 (updater)

## Meltype for Mac 自動更新機能 セキュリティレビュー (A: updater)

- 対象リポジトリ: このリポジトリ (HEAD 3b32db8)
- 読み取り専用で実施。ファイル変更・git 操作・ビルド・インストールは行っていない。
- 「事実」= コードで確認したもの。「推測」= 実行・実機で確かめていないもの (明記)。

### 対象ファイルと行数

| ファイル | 行数 | 読んだ範囲 |
|---|---|---|
| mac/Sources/MeltypeIME/Updater.swift | 728 | 全行 |
| mac/Sources/MeltypeIME/UpdateSettings.swift | 39 | 全行 |
| src/Meltype.Core/Update/UpdateCheck.cs | 162 | 全行 |
| mac/Resources/Info.plist | 123 | 全行 (ATS 例外の有無) |
| mac/Sources/MeltypeIME/Converter.swift | 257 | 79 行目 (versionString) |
| mac/install.sh | 199 | 全行 (更新時に zip から実行されるスクリプト) |
| mac/make-zip.sh | 28 | 全行 (zip の構造確認) |
| mac/Sources/MeltypeIME/NativeCore.swift | 472 | 160–263, 430–447 (evaluateUpdate / dataDirectory) |
| src/Meltype.Mac.Native/Exports.cs | 1060 | 879–896 (meltype_update_evaluate) |
| src/Meltype.Core/Config/AppPaths.cs | — | 1–17 (DataDirectory の決め方) |
| mac/Sources/MeltypeIME/InputController.swift | 706 | 318–377 (メニューからの呼び出し) |
| mac/Sources/MeltypeIME/main.swift | 37 | 全行 |
| src/Meltype.Core.Tests/UpdateCheckTests.cs | — | grep のみ (URL 検証のテスト有無) |

### 更新経路の全体像 (事実)

1. 起動 5 分後、以降 1 時間ごとのタイマーと入力メニューを開いたときに `checkIfDue` → 24 時間に 1 回 `GET https://api.github.com/repos/whitewater-png/Meltype-for-Mac/releases/latest` (Updater.swift:44, 157–196)。
2. JSON は C# 側 `UpdateCheck.Evaluate` (UpdateCheck.cs:39–82) が検証し、「版・URL・SHA-256・サイズ・リリースページ」の 5 行文字列を Swift に返す (Exports.cs:884–896, NativeCore.swift:430–440)。
3. 利用者が「更新する」を押すと (Updater.swift:305–338 の osascript ダイアログ)、裏スレッドで `Updater.run` (416–465): URL 再検証 → ダウンロード (リダイレクト先ホスト制限、サイズ上限) → SHA-256 を GitHub API の asset `digest` と比較 → `zipinfo` で事前検査 → `ditto -x -k` で展開 → `codesign --verify --deep --strict` → Info.plist の版・バンドル ID → Mach-O の CPU 種別 → 切り離した `bash -c` から zip 内の `install.sh --yes` を起動 (604–658)。
4. `install.sh` (mac/install.sh:100–134) が `~/Library/Input Methods/Meltype.app` を `cp -Rp` で退避 → `rsync -a --delete` で中身を入れ替え → `codesign --verify` 失敗なら退避から戻す → `pkill -x Meltype` → `xattr -dr com.apple.quarantine` (146–156) → 登録処理。

### 指摘事項

#### 重大度 高

該当なし。リモートの第三者が GitHub / TLS を破らずに任意コードを入れられる経路はコード上見つからなかった。

#### 重大度 中

##### 中-1. 真正性の根拠が「GitHub アカウント + TLS」だけで、公開鍵署名が無い (設計上の最大のリスク)
- 場所: Updater.swift:434–436 (SHA-256 比較), 450 (codesign), UpdateCheck.cs:68–69 (digest の取り出し); mac/build.sh:163–164 (ad-hoc 署名 `--sign -`)
- 問題 (事実): 比較する SHA-256 は、ダウンロード URL と同じ `releases/latest` JSON の asset `digest` から来る。つまり**ハッシュと配布物が同一ソース**で、JSON を差し替えられる立場 (メンテナーの GitHub アカウント/トークンの奪取、GitHub 側の侵害、信頼済み CA を持つ MITM) は両方を整合させられる。`codesign --verify --deep --strict` は ad-hoc 署名では「zip 内で封印が壊れていない」ことしか保証せず、発行者を判別しない (攻撃者も `codesign -s -` できる)。リリースノートに載せる SHA-256 はアプリからは一切参照していない (人手で見比べる用途のみ: install.sh:52–63)。
- 影響: 上記のいずれかが侵害されると、利用者が「更新する」を 1 クリックするだけで任意コードがユーザー権限で実行される (zip 内 `install.sh` も実行される)。ダウングレードは `Compare(latest, current) <= 0 → null` (UpdateCheck.cs:55) で防げているが、「番号だけ新しい悪意ある版」は防げない。
- 修正案: Sparkle と同じ方式で、リリース zip に対する Ed25519 署名を別 asset (または release body の固定書式) として配布し、公開鍵をアプリに埋め込んで `Updater.run` の SHA-256 比較の直後に検証する。秘密鍵はビルド環境外 (ローカル/ハードウェアキー) に置き、GitHub 側の侵害と独立にする。Developer ID で署名できる場合は、`codesign --verify -R='anchor apple generic and certificate leaf[subject.OU] = "<TeamID>"'` を併用する (build.sh:157–160 はすでに `MELTYPE_MAC_IDENTITY` に対応している)。
- 補足: ダイアログ文「SHA-256 を確かめてから入れ替えます」(Updater.swift:313) は事実だが、利用者が「リリースノートの SHA と照合している」と誤解しうる。README/ノートに「API の digest と比較。発行者の検証ではない」と明記するのが望ましい。

##### 中-2. zip 内の `install.sh` が署名の封印の外にあり、`--yes` で隔離属性の除去まで無確認で行われる
- 場所: Updater.swift:461, 616 (`/bin/bash install.sh --yes`), install.sh:146–156 (`xattr -dr com.apple.quarantine`), make-zip.sh:18 (install.sh は Meltype.app の外に置かれる)
- 問題 (事実): アプリ側の検証 (codesign・版・バンドル ID・CPU) は `Meltype.app` にしか及ばず、実際に最初に実行されるのは zip 直下の `install.sh` (任意の bash)。現状は zip 全体の SHA-256 で間接的に守られているが、中-1 の修正で「アプリの署名」を信頼の根にした場合、install.sh は検証外のまま残る。また `--yes` は Gatekeeper の検査回避 (install.sh:140–144 のコメントどおり) を利用者に見せずに実施する。
- 影響: 中-1 と同じ前提が崩れたときの実行経路。また Gatekeeper 回避は利用者が意識できない。
- 修正案: (a) 入れ替えロジックを IME 本体 (署名済みバイナリ) 側に持つか、zip 内 install.sh の SHA-256 を本体に埋め込んで `isInstallFolder` 段階で照合する。(b) 更新ダイアログ (Updater.swift:313) に「隔離属性を外す (Gatekeeper の検査を回避する)」旨を一言入れる。
- 推測: 配布 zip が公証されていない以上、隔離を外さないと起動できないため、挙動自体は妥当。問題は「見せていない」点。

#### 重大度 低

##### 低-1. JSON 由来の `tag_name` を長さ・内容の制限なしにダイアログに埋め込む
- 場所: Updater.swift:200–214 (`whyNoOffer`), 235 (`showMessage(problem)`)
- 問題 (事実): `Evaluate` が null を返した場合、Swift 側で独自に `tag_name` を取り出し、`"新しい版 (\(tag)) があります..."` として osascript の argv に渡す。長さ制限 (JSON 全体で 1 MB) 以外のサニタイズが無い。argv 渡しなので AppleScript インジェクションにはならない (事実)。
- 影響: JSON を差し替えられる立場なら、ダイアログ本文を使ったソーシャルエンジニアリング (「以下のコマンドを実行してください」等) や巨大ダイアログ。中-1 の前提を要する。
- 修正案: 表示用の `tag` も `TryParseTag` 相当の形式 (`^v?\d+(\.\d+){1,3}(-mac)?$`, ≤40 文字) に限り、外れたら定型文にする。

##### 低-2. `releaseUrl` (html_url) の検証が接頭辞一致のみ
- 場所: UpdateCheck.cs:71–73, Updater.swift:334 (`NSWorkspace.shared.open`)
- 問題 (事実): `https://github.com/whitewater-png/Meltype-for-Mac/releases` で始まれば何でも通る。ホストは github.com に固定されるので別ホストへは飛ばない (`@` や `..` を付けてもパスのまま)。改行を含む場合は 5 行チェック (NativeCore.swift:438) で全体が無効になる (fail-closed)。
- 影響: 「詳細」で github.com 内の別ページ (別リポジトリの releases 配下など) を開かされる程度。
- 修正案: `html_url` を使わず、`ReleasePagePrefix + "/tag/" + tag` を組み立てて固定する。

##### 低-3. 確認ダイアログの既定ボタンが「更新する」で、5 分間フォーカスを待つ
- 場所: Updater.swift:314–315, 323
- 問題 (事実): `default button "更新する"`。osascript のダイアログは別プロセスで、IME 利用中の Return 入力で意図せず確定しうる。時間切れ時は button returned が空で無視される (事実、330–335)。
- 影響: 誤操作で更新開始 (悪意ある経路ではない)。
- 修正案: 既定を「あとで」にするか、`display dialog` を使わず本体の NSAlert 相当で前面化する。

##### 低-4. 切り離した install.sh の環境に `TMPDIR` を渡していないため、退避先が `/tmp` になる
- 場所: Updater.swift:644 (`PATH`, `HOME`, `LANG` のみ), install.sh:106 (`mktemp -d "${TMPDIR:-/tmp}/meltype-backup.XXXXXX"`)
- 問題 (事実): 作業フォルダー自体は per-user の `FileManager.temporaryDirectory` (0700) だが、旧 Meltype.app の退避は `/tmp` 直下の mktemp ディレクトリ (0700、原子的作成) になる。シンボリックリンク攻撃は mktemp で防げており、中身も公開物なので実害は小さい。
- 修正案: 環境に `TMPDIR=\(FileManager.default.temporaryDirectory.path)` を加える。

##### 低-5. zip 事前検査が central directory の申告値に依存 (zip bomb 耐性は SHA-256 前提)
- 場所: Updater.swift:522–537 (`zipinfo -t` の「bytes uncompressed」を信用), 441 (`ditto -x -k`)
- 問題 (事実): 申告サイズと実際の伸長サイズが食い違う zip は検出できない。ただし SHA-256 一致後にしか展開しないので、中-1 の前提が無ければ到達しない。
- 修正案: 展開先のディスク使用量を監視するか、展開を `ditto` ではなく上限付きで自前展開する (優先度は低)。

##### 低-6. 版の解釈が C# と Swift で二重実装
- 場所: UpdateCheck.cs:113–143 と Updater.swift:205–209 / 28–33
- 問題 (事実): Swift 側 (`whyNoOffer`) は `-` 以降を切り捨てるため `v1.2.3-beta` も「新しい版」と表示するが、C# 側は拒否する。ダウンロード可否は C# のみが決めるので安全性には影響しない。
- 影響: 表示の食い違いと将来の保守リスク。
- 修正案: 表示用のタグ解析も C# の `TryParseTag` を FFI で公開して一本化する。

##### 低-7. TLS はシステムの信頼ストア任せ (ピン留め無し)、鮮度の保証無し
- 場所: Updater.swift:166–172, 553–559 (`URLSessionConfiguration.ephemeral`, delegate に challenge 処理なし)
- 問題 (事実): Info.plist に `NSAppTransportSecurity` は無く ATS 既定 (https 必須) が効く。http・ATS 例外・外部ホストへのリダイレクトは無い。ただし利用者が入れた信頼済み CA (企業プロキシ等) による MITM は阻止できず、その場合は中-1 の経路になる。また「最新」の JSON を古いまま再生し続ける凍結攻撃も検出できない。
- 修正案: 中-1 の署名を入れれば MITM による差し替えは無効化できる。凍結は署名付き日付が無いと防げないので、許容リスクとして文書化。

##### 低-8. `update.log` を `O_TRUNC` で開く際に `O_NOFOLLOW` が無い
- 場所: Updater.swift:638
- 問題 (事実): 0700 のデータフォルダー内なので同一ユーザー以外は置けない。`MELTYPE_DATA_DIR` (AppPaths.cs:13–16) で共有場所に向けた場合のみ意味を持つ。
- 修正案: `O_NOFOLLOW` を追加 (1 語)。

### 観点ごとの確認結果 (事実)

1. **取得先 URL と TLS**: 取得先は定数 1 か所 (Updater.swift:44)。https 固定、ATS 例外無し、Cookie/キャッシュ無効、リダイレクトは JSON が `api.github.com` のみ、zip が `github.com` / `*.githubusercontent.com` のみ (662–693)。
2. **JSON 検証**: draft/prerelease 拒否、タグ形式 `v?\d(.\d){1,3}(-mac)?` ≤40 文字、数値比較 (1.0.10 > 1.0.9)、asset 名 `Meltype-mac-<ver>.zip` 完全一致、URL は接頭辞+タグ+ファイル名の**完全一致**かつ `%` `\` `#` `?` `@` 空白・制御文字・非 ASCII・`..`・ポート・userinfo を拒否、size は 0 < n ≤ 100 MB、digest は `sha256:` + 64 hex のみ (UpdateCheck.cs:89–110)。Swift 側でも同等の再検証 (Updater.swift:19–34, 430)。別ホスト・別リポジトリ・http はテストで拒否を確認済み (UpdateCheckTests.cs:73–101)。
3. **整合性**: SHA-256 比較あり (出所は API digest = 中-1)。codesign は ad-hoc の封印検査のみ。バンドル ID・版・CPU 種別の照合あり。
4. **展開・置換**: 絶対パス・`~`・`\`・`..` 成分を持つ名前を拒否、エントリ数 ≤5000・伸長 ≤300 MB (522–537)。作業ディレクトリは per-user TMPDIR 下に `UUID` 名で 0700 (418–420)。`isInstallFolder` は install.sh / Meltype.app のシンボリックリンクを拒否 (540–547)。`rsync -a --delete` の対象は `$TARGET/Meltype.app/` 配下のみ (install.sh:113)。退避 → 入れ替え → 再 codesign → 失敗時ロールバック (install.sh:106–128) で、途中失敗時に新旧混在が残りにくい。TOCTOU は同一ユーザー内に閉じる。ロックは `mkdir` による原子的取得 (609–615)。
5. **外部コマンド**: すべて絶対パス + 引数配列 (`/usr/bin/osascript`, `/usr/bin/ditto`, `/usr/bin/codesign`, `/usr/bin/zipinfo`, `/bin/bash`)。AppleScript の文字列は argv 経由で、ソースに埋め込んでいない (290, 321–326, 375–379, 607)。`bash -c` のスクリプトは定数で、ユーザー/JSON 由来の値は位置引数 `$1`〜`$5` のみ (641)。`PATH` は `/usr/bin:/bin:/usr/sbin:/sbin` に固定 (644) で、利用者のシェル設定による PATH 乗っ取りを受けない。版文字列は数字と `.` のみに制限済み。
6. **頻度・プライバシー**: 24 h 間隔、失敗後 1 h 抑制、起動 5 分後開始、手動は OFF でも可。送信ヘッダは `User-Agent: Meltype/<版>` と `Accept` のみ、識別子・打鍵内容は送らない (161–171)。既定 ON (オプトアウト) はメニューで明示。
7. **UpdateSettings**: `~/Library/Application Support/Meltype/update.json`、フォルダー 0700・ファイル 0600、一時ファイル + `rename` で原子的更新 (84–95)。壊れた JSON は `enabled = false` にする安全側の既定 (UpdateSettings.swift:20–25)。改変耐性は不要な設計 (同一ユーザーが書ける場所で、書ける者は既に全権限を持つ)。

### 良い点

- URL 検証が C# と Swift の二層で、かつ「接頭辞一致」ではなく完全一致 + 文字種の拒否リストになっており、テストで別ホスト・別リポジトリ・`%2e%2e`・ポート・userinfo の拒否が固定されている。
- 入れ替えが「退避 → rsync → 再検証 → 失敗時に戻す → 成功してから pkill」の順で、途中失敗で IME が壊れたまま残る窓が小さい。シェルへの文字列補間が一切無く、位置引数と絶対パスで統一されている。

### 総括

現状の更新経路は、実装品質 (入力検証・展開・置換・コマンド実行・プライバシー) としては丁寧で、リモートの第三者が GitHub と TLS を破らずに介入できる穴は見つからなかった。
ただし信頼の根が「メンテナーの GitHub アカウント + システム TLS」だけで、SHA-256 も codesign (ad-hoc) も発行者を証明しないため、サプライチェーン侵害や信頼済み CA 経由の MITM に対しては無防備である (中-1)。
「GitHub と TLS を信頼する」という前提を文書化したうえで使うなら現状で許容できるが、公開配布の更新経路として「安全」と言い切るには、Ed25519 などアプリ埋め込み公開鍵による zip 署名の検証 (と install.sh の検証範囲への取り込み) が必要。


---

# 付録 B: 領域レビュー原文 (data)

## Meltype for Mac セキュリティ/プライバシーレビュー (B: 入力データの扱い)

対象リポジトリ: このリポジトリ (HEAD 3b32db8, 2026-10-11)。読み取り専用で実施 (ビルド・変更・git 操作なし)。
実機の `~/Library/Application Support/Meltype/` は `ls -la` による存在・権限の確認のみ (中身は読んでいない)。

### 対象ファイルと行数

| ファイル | 行数 |
|---|---|
| mac/Sources/MeltypeIME/InputController.swift | 706 |
| mac/Sources/MeltypeIME/Bench.swift | 285 |
| mac/Sources/MeltypeIME/main.swift | 37 |
| mac/Sources/MeltypeIME/SuggestionHint.swift | 60 |
| src/Meltype.Core/Diagnostics/Log.cs | 136 |
| src/Meltype.Core/Learning/UserModel.cs | 206 |
| src/Meltype.Core/Config/AppPaths.cs | 65 |
| src/Meltype.Core/Config/Backup.cs | 129 |
| src/Meltype.Core/Config/SafeFile.cs | 181 |
| src/Meltype.Core/Config/FileLock.cs | 84 |
| src/Meltype.Core/Config/LearningJson.cs | 28 |
| src/Meltype.Core/Config/Settings.cs | 687 |
| src/Meltype.Core/Config/SharedSettings.cs | 102 |
| src/Meltype.Mac.Native/Exports.cs | 1060 |

参照のみ (ログ/学習の実体を確認するため): CompositionController.cs, LanguageMemory.cs, ConversionPreferences.cs, DictionarySuggestions.cs, LearningData.cs, MeltypeSession.cs, DetectionTypes.cs, Converter.swift, NativeCore.swift, Updater.swift, mac/Resources/Info.plist。

---

### 指摘事項

#### 重大度: 高

該当なし。パスワード欄 (秘匿入力) での素通し・学習/ログ停止、打鍵内容のデフォルト非記録、学習データ/ログの 0600/0700 化、原子的書き込みはいずれもコードで確認できた。

#### 重大度: 中

##### M-1. 秘匿入力の判定が `IsSecureEventInputEnabled()` (システム全体の状態) のみに依存している
- 場所: `mac/Sources/MeltypeIME/InputController.swift:130-133`, `:64`
- 問題: パスワード欄かどうかを、Carbon の `IsSecureEventInputEnabled()` だけで判定している。これは「どこかのアプリが EnableSecureEventInput を呼んでいるか」というグローバル状態で、(a) AppKit の `NSSecureTextField`、Safari/Chromium の `<input type=password>`、Terminal.app の echo-off 時は自動で ON になる (推測: 主要アプリの実装としては一般的だが、Meltype 側で検証したものではない) が、(b) 独自実装のパスワード欄 (Java/Qt/一部クロスプラットフォーム UI、`echo off` 以外の方法で隠す CLI、iTerm2 の「Secure Keyboard Entry」OFF 時の `ssh`/`sudo` プロンプト等) では ON にならない。その場合、英数モードでも `DirectModeAutoDetect` (既定 ON) により打った英字が Core の `_heldLetters` に保持され、判定結果が `LanguageMemory`・`DictionarySuggestions`・`ConversionHistory` などに学習され得る (ファイル: languages.json 等。該当コードは CompositionController.cs:1968-1998 の `Remember`)。
- 影響: 秘匿フラグを立てないアプリで打った秘密情報の一部 (英字のみの語、小文字化されたもの) が学習ファイルに残り得る。同一ユーザー権限で読める (他ユーザーからは 0700 で不可)。ログ (meltype.log) には既定では文字数しか出ないのでログ経由の漏えいは限定的。
- 確認できた事実: `IMKTextInput` には「このクライアントは secure か」を直接問う API が無く、IMK ベースの IME としては現行実装が標準的。コードのコメント (:129) もグローバル状態である点を明記している。
- 修正案 (いずれも軽量):
  1. `client.attributes(forCharacterIndex:lineHeightRectangle:)` や `client.validAttributesForMarkedText()` から取れる情報で secure 欄を補助判定できないか調査 (推測: 確実な手段ではない)。
  2. 「コード」種別 (ターミナル・エディタ) では `LanguageMemory`/`DictionarySuggestions` への英字語の学習を既定 OFF にする設定を用意する (ターミナルで打たれる英字列はパスワード・トークン・ホスト名などの比率が高い)。
  3. 既存の「ログに入力した文字を残す」の説明文 (Settings.cs:480) と同様の注意書きを、学習データについても README/設定画面に明記する。

##### M-2. トレース (`MeltypeTraceIMK`) に有効期限・画面上の表示が無く、打鍵内容を含むログが残り続ける
- 場所: `InputController.swift:53-60` (起動時に 1 回だけ `UserDefaults` を読む), `:69-97` (追記のみ・5MB で `.old` に 1 世代ローテーション), `AppPaths.cs:58`, `LearningData.cs:57` (消去対象は `imk-trace.log` と `.old` のみ)
- 問題: (a) 有効化は `defaults write` 1 回で、IME が再起動されるたびに再び ON のまま動く。OFF にする操作 (`defaults delete` + 再起動) を忘れると、確定文字・前後 20 文字の surroundingText・bundle ID・keyCode が最大 5MB + 5MB 分残り続ける。(b) ON であることを示す UI 表示 (入力メニューの項目や候補ウィンドウ注釈) が無い。(c) `.old` 以外に手でリネームしたコピーは「学習データをすべて消去」の対象外。実機では `imk-trace.log` (約 200KB) と `imk-trace.log.round1` (約 240KB) が残存していた (権限は 0600。中身は未確認)。
- 影響: 本人がデバッグ後に消し忘れると、同一ユーザー権限のプロセス (およびバックアップ・Time Machine 経由) から打鍵内容が読める状態が続く。他ユーザーからは 0700/0600 で読めない。
- 修正案: (1) トレース ON 時は入力メニューに「原因調査用トレース記録中 (OFF にする)」項目を出す。(2) `defaults` の値を bool ではなく「有効化した時刻」にして、例えば 24 時間で自動的に無効化する。(3) クリーンアップ時に `imk-trace.log*` をグロブで消す。(4) 可能なら trace の各行で surroundingText を記録しない (keyCode と結果だけで大半の調査は足りる)。

##### M-3. `FileLog` ON・`LogTypedText` OFF でも、英数モードの判定打ち切り時に押したキーの仮想キーコードが残る
- 場所: `src/Meltype.Core/Composition/CompositionController.cs:501` (`英数状態の判定を打ち切り: キー 0x{vk:X2}`), `Log.cs:35-41` (RecordText OFF は「文字数だけ残す」と宣言)
- 問題: 英数モードで英字を保留中に英字以外のキー (数字・記号・Enter 等) が来ると、そのキーの VK が 16 進で Info ログに出る。`Log.Text` を通していないため、「ログに入力した文字を残す」OFF の約束 (文字数のみ) から外れる。英字 + 数字/記号という形はパスワードに多い。
- 影響: `FileLog` を ON にしているユーザー (既定 OFF) の meltype.log に、英字列の直後に打った 1 キー分の情報が残る。単独では限定的だが、M-1 と組み合わさると秘匿されない欄での打鍵の一部が復元できる。
- 修正案: `RecordText` が false のときは「英字以外のキー」「Enter」「数字」程度の分類だけ記録する (VK そのものは出さない)。`DetectionTypes.Describe` と同じく `RecordText` で出し分ける。

##### M-4. 20 文字の surroundingText を全アプリ・全キーで読み取り、azooKey の学習コンテキストに渡している
- 場所: `InputController.swift:183-185, 697-705` (`attributedSubstring` でキャレット前後 20 文字), `CompositionController.cs:707` (`_precedingText = before`), `:1573-1579` (`ContextOf`: 文末以降の最大 10 文字), `:2030`/`Exports.cs:949-963` (`Learn(context, clauses)` → Swift `learnCallback` → azooKey)
- 問題: 変換中でないときの各キーで、入力欄のキャレット前後 20 文字を読む。前後文字列は判定文脈として妥当な用途だが、(a) 確定時に azooKey へ `context` (最大 10 文字、日本語のときのみ) が渡る。azooKey の学習 (`updateLearningData`) が context 自体を永続化するかはコードで確認できていない (推測: 候補データのみ保存で context は保存されない可能性が高いが、未確認)。(b) トレース ON 時はこの前後文字列が 40 文字までファイルに残る (M-2)。(c) 秘匿入力 ON のときは読まない (:128, :130) ことは確認済み。
- 影響: 他アプリの既存テキスト (ユーザーが打っていない内容、例えばメールの引用部分) が学習データに混入する可能性 (未確認)。
- 修正案: azooKey (AzooKeyKanaKanjiConverter) の LearningManager が context を保存しないことを一度確認し、結果を docs に 1 行残す。保存するなら `learn` に context を渡さない選択肢を検討。

#### 重大度: 低

##### L-1. azooKey 学習ファイルが 0644 (親ディレクトリ 0700 で保護)
- 場所: `mac/Sources/MeltypeIME/Converter.swift:68-69` (ディレクトリのみ 0700)。実機 `azooKey/` 配下は `-rw-r--r--`。
- 問題: azooKey 側が書くファイル (memory*.louds* 等。確定した語が入る) は umask 022 のまま 0644。ディレクトリが 0700 なので他ユーザーは到達できないが、ディレクトリ権限が何かの理由で緩むと (手動 chmod・移行ツール等) そのまま読める。
- 修正案: `learn` 後 (`commitUpdateLearningData` 直後) に `azooKey/` 配下のファイルを 0600 に `setAttributes` する (数ファイル、コストは無視できる)。

##### L-2. トレースファイル作成のフォールバックで権限が一瞬 0644 になり得る
- 場所: `InputController.swift:85-96`
- 問題: `createFile(... 0o600)` が失敗し、`FileHandle(forWritingTo:)` も開けない場合、`try? data.write(to: file)` (非 atomic・既定権限) で書く。umask 022 では 0644 で作られ、次回の `setAttributes(0o600)` まで残る。既存ファイルがある場合は `data.write` が内容を切り詰める (ローテーション中の競合など稀なケース)。
- 修正案: フォールバックを削除するか、`Data.WritingOptions` を使わず `open(2)` with `O_CREAT|O_APPEND, 0600` で統一する。

##### L-3. `Backup.Restore` は `File.WriteAllBytes` → `Restrict` の順で、一瞬 0644 (umask 依存) になる。`.before-restore` は無期限に残る
- 場所: `src/Meltype.Core/Config/Backup.cs:105-112`
- 事実: Mac 版のコード (Exports.cs / mac/Sources) から `Backup` は呼ばれていない (Windows UI 用)。Mac では非該当。
- 修正案 (Windows 向け): `SafeFile.WriteAllBytes` を使えば 0600 で作成・原子的置換になる。`.before-restore` は `LearningData.ClearLeftovers` の対象に加える。

##### L-4. `Settings.SwitchProfile` / `ImportProfile` は Mac では未使用だが、`SwitchProfile` は `Deserialize` の例外を握らない
- 場所: `Settings.cs:341-343`
- 問題: プロファイルの `Values` (JsonObject、ユーザーが手で編集し得る) に型の合わない値があると `JsonException` が呼び出し側へ伝播する。`ImportProfile` (:421) は握っている。Mac では `MacSettingsCatalog` 経由のみで `SwitchProfile` を呼ぶ経路は無い (grep で確認)。
- 修正案: `SwitchProfile` も `ImportProfile` と同じ例外フィルタで `current` を返す。

##### L-5. `SafeFile.EnsureDirectory` はデータフォルダ直下だけ権限を矯正し、サブフォルダ (terms/, dictionaries/) は既存のままにする
- 場所: `SafeFile.cs:23-33`
- 事実: 新規作成時は 0700。既存サブフォルダが 0755 でも直さない (コメントどおりの意図)。実機の `terms/` は 0700 だった。
- 修正案: データフォルダ配下の自分が作るサブフォルダは同様に矯正してよい (他人のフォルダを触らないという懸念はパス前方一致で担保できる)。

##### L-6. 登録ヒントのパネルと候補ウィンドウはアクセシビリティ API で読める
- 場所: `SuggestionHint.swift:25-49` (NSPanel + NSTextField, `canJoinAllSpaces`), `main.swift:29` (IMKCandidates)
- 事実: ヒントは『語』を含む文字列 (MeltypeSession.cs:253)。Accessibility 権限 (TCC) を持つ同一ユーザーのプロセスなら読める。これは IME の候補ウィンドウ一般と同じで、IMKCandidates (システム実装) も同条件。他プロセスから読める独自の経路 (XPC/Distributed Notification/ソケット/名前付きパイプ) は IME 側に無い (grep で確認: NSPasteboard 利用は「Meltype 辞書」アプリのユーザー操作によるコピーのみ)。
- 修正案: 対応不要。ヒントを「辞書に登録できる語があります」にして語を出さない選択肢はある。

##### L-7. 英数モードで打った英字列が macOS の `NSSpellChecker` (AppleSpell, 別プロセス) へ渡る
- 場所: `NativeCore.swift:90-93`, `Exports.cs:1001-1028` (語ごとにキャッシュ、2 文字以上)
- 事実: 英単語判定のため小文字化した語をシステムのスペルチェッカへ問い合わせる。AppleSpell は XPC の別プロセス (推測: 問い合わせ語を永続化・送信はしない)。秘匿入力中は呼ばれない。
- 修正案: 対応不要。M-1 の「コード種別では学習を控える」と同じ方針で、問い合わせ自体を控える設定があってもよい。

##### L-8. 同一ユーザーの任意プロセスが `defaults write` でトレースを有効化できる
- 場所: `InputController.swift:54`
- 事実: `UserDefaults.standard` の bool は TCC で保護されない。ただしその出力先 (0600) を読める時点で同じ攻撃者は学習データも読めるので、追加の脅威は小さい。M-2 の UI 表示で緩和される。

---

### 確認できた事実 (観点ごと)

1. **秘匿入力**: `handle()` の先頭で `IsSecureEventInputEnabled()` を見て、未確定文字があれば確定したうえで `return false` (キー素通し、Core 未呼出、surroundingText 未読取)。トレースも同関数内で止める (:64)。候補ウィンドウ・学習・ログは Core を経由しないので止まる。
2. **ログ**: 既定で `FileLog=false`、`LogTypedText=false` (Settings.cs:478-481)。`Log.Write` は `_filePath` が null ならキューにも入れない (Log.cs:58)。ファイルは `UnixCreateMode=0600` で作成し、旧版の 0644 も一度だけ矯正 (Log.cs:111, 118)。5MB で `.old` 1 世代。NSLog は定型文と文字数のみで本文を含まない (全 NSLog 箇所を確認)。`Console.WriteLine` は Core/Native に無い。トレースは `defaults` で明示 ON のときだけ (:53-60)。
3. **学習データ**: 保存するのは conversions.json (文節の読み→確定語・回数・時刻)、languages.json (ユーザーが英字/かなに直した小文字英字語)、suggest.json (読み・語・回数)、translations.json、azooKey メモリ (確定語)。surroundingText 自体は Core のファイルには保存されない。model.json (`UserModel`, 先頭 3〜6 文字の統計) は Mac では `MeltypeSession.CreateDefault` が生成せず未使用 (LearningData.cs の注記どおり Windows 用)。すべて `SafeFile` 経由で 0600。`.broken` は `File.Copy` で元のモードを引き継ぐ (実機で 0600 を確認)。
4. **SafeFile/FileLock**: 同一ディレクトリに GUID 付き一時ファイルを `CreateNew`+0600 で作り、`Flush(true)` 後に `File.Move(overwrite)` (= rename) で置換。rename はシンボリックリンク自体を置き換えるので、リンク先を上書きしない。失敗時は一時ファイルを削除。読み込みは 20MB 上限、超過は `.oversize` に退避して保存をブロック。ロックは `.名前.lock` (0600) を `FileShare.None` (flock) で保持、3 秒でタイムアウト例外。ロックファイルを消さない理由も妥当。
5. **設定パース**: ソース生成の System.Text.Json (MaxDepth 既定 64)、例外は `.broken` 退避 + 既定値。`Normalize` で null 要素・範囲外値を矯正。`AppRules` の `Process` は Mac では bundle ID、文字列比較のみで正規表現等は使わない。
6. **デバッグ入口**: `--bench` (main.swift:16) はリリースにも残るが、IMKServer を立てず一時フォルダで動くローカル計測で、外部からの入力注入や読み出しの経路にはならない。`--repro`/`--eval`/`--segments` はテストプロジェクト (Meltype.Core.Tests) のみ、`--self-test` は「Meltype 辞書」アプリで `MELTYPE_DATA_DIR` が一時フォルダのときのみ本体を通す。IME に IPC (XPC/Distributed Notification/ソケット/FIFO) は無い。
7. **候補ウィンドウ**: IMKCandidates (システム) と SuggestionHint の NSPanel。L-6 参照。

### 良い点

- 秘匿入力の扱いが「Core に渡さない・周辺文字も読まない・トレースも書かない」まで一貫しており、グローバル状態であることの副作用 (他アプリが付けっぱなしだと全体が素通し) もコードに明記されている。
- ログの既定が「文字数のみ」で、本文を出す箇所が `Log.Text` / `RecordText` に集約されている。ファイル権限 (0600/0700)、原子的置換、サイズ上限、`.oversize` 退避による上書き防止など、学習データの保護が Core の `SafeFile` 1 箇所に集約されていて漏れにくい。`SecurityTests.cs` にこれらのテストがある。

### 総括

重大 (高) の問題は無く、パスワード欄・ログ既定・ファイル権限・原子的書き込みは適切に実装されている。
残る主なリスクは、秘匿フラグを立てないアプリ (独自パスワード UI・一部ターミナル) で打った英字列が学習ファイルに残り得ること (M-1) と、トレースの消し忘れ・表示なし (M-2、実機に残存ファイルあり)。
短期の対策は M-2 の UI 表示/自動失効と M-3 の VK 非記録、中期はコード種別アプリでの学習抑制設定と azooKey 側の context 保存有無の確認。


---

# 付録 C: 領域レビュー原文 (ffi)

## Meltype for Mac — Swift ⇄ C# (NativeAOT) FFI 境界レビュー (メモリ安全性・堅牢性)

対象リポジトリ: このリポジトリ (HEAD `3b32db8`)。読み取り専用で実施 (ビルド・変更なし)。
補助的に、インストール済み `~/Library/Input Methods/Meltype.app` (2026-10-11 15:30 ビルド) を `otool` / `codesign -d` で観察した。

### 対象ファイルと行数

| ファイル | 行数 | 役割 |
|---|---|---|
| `src/Meltype.Mac.Native/Exports.cs` | 1060 | `UnmanagedCallersOnly` エクスポート 63 本、Swift コールバックの呼び出し、UTF-8 変換 |
| `mac/Sources/MeltypeIME/NativeCore.swift` | 472 | IME 側の dlopen/dlsym・ラッパー・Swift 側コールバック 6 本 |
| `mac/Sources/MeltypeDictionaryKit/NativeDictionary.swift` | 423 | 辞書アプリ側の dlopen/dlsym・ラッパー (36 関数) |
| `mac/Sources/MeltypeIME/InputController.swift` | 706 | セッションの生成・破棄、`handleKey` の引数の組み立て (FFI に直結する部分のみ参照) |
| `src/Meltype.Mac.Native/Meltype.Mac.Native.csproj` | 29 | AOT 設定 |
| `tools/check-mac-aot-settings.py` | 907 | AOT dylib の機能・プロセス間整合の検査 |
| `mac/build.sh` | 221 | dylib の配置・RPATH 整理・署名 |

### エクスポート関数一覧

所有権の記法: **返値→呼出側 free** = C# が `NativeMemory.Alloc` した NUL 終端 UTF-8 を返し、Swift が `meltype_free` で解放する。**out\*** = `byte**` / `int*` 出力引数 (文字列は同様に呼出側 free)。「入力文字列」はすべて呼出側が所有する NUL 終端 UTF-8 で、C# は呼び出し中に `Marshal.PtrToStringUTF8` でコピーするだけ。

try/catch 列: ○ = 関数本体全体 (または `Run`/`Text`/`Reason` ラッパー) が包む、△ = 一部 (`FromUtf8`・`ParseWords` 等) が try の外、× = 無し。

| # | 関数 (Exports.cs 行) | 引数 | 返値の所有権 | try/catch |
|---|---|---|---|---|
| 1 | `meltype_set_reader` (43) | fnptr | void | × (代入のみ) |
| 2 | `meltype_abi_version` (56) | – | int | × (定数) |
| 3 | `meltype_get_continue_after_conversion` (60) | – | int | × (内部 `ConfigFlag.IsOn` が lock+try) |
| 4 | `meltype_set_continue_after_conversion` (64) | int | int | × (同上 `Set`) |
| 5 | `meltype_get_shift_enter_newline` (68) | – | int | × (同上) |
| 6 | `meltype_set_shift_enter_newline` (72) | int | int | × (同上) |
| 7 | `meltype_term_domains` (79) | – | 返値→呼出側 free / NULL | ○ |
| 8 | `meltype_set_term_domain` (95) | str,int | int | ○ |
| 9 | `meltype_clear_learning` (113) | – | int | ○ |
| 10 | `meltype_init` (128) | fnptr×5 | int | × (代入のみ) |
| 11 | `meltype_init_mozc` (143) | str,str | int | ○ (Linux 用) |
| 12 | `meltype_create` (167) | – | GCHandle (IntPtr) / 0 | ○ |
| 13 | `meltype_destroy` (184) | handle | void | ○ (ただし後述 M-1) |
| 14 | `meltype_handle_key` (203) | handle,int×3,str,str | 返値→呼出側 free (JSON) / NULL | ○ (`Run`) |
| 15 | `meltype_commit` (211) | handle | 同上 | ○ |
| 16 | `meltype_select_candidate` (215) | handle,int | 同上 | ○ |
| 17 | `meltype_select_prediction` (219) | handle,int | 同上 | ○ |
| 18 | `meltype_forget_last_commit` (223) | handle | void | ○ |
| 19 | `meltype_reconvert` (240) | handle,str | 同上 | △ (`FromUtf8` が外) |
| 20 | `meltype_set_direct` (248) | handle,int | void | ○ |
| 21 | `meltype_set_app` (265) | handle,str | int | ○ |
| 22 | `meltype_add_user_word` (282) | handle,str,str | 返値→呼出側 free / NULL | ○ |
| 23 | `meltype_suggestions` (299) | handle | 同上 | ○ |
| 24 | `meltype_suggest_accept` (316) | handle,str,str | 同上 | ○ |
| 25 | `meltype_suggest_reject` (333) | handle,str,str | void | ○ |
| 26 | `meltype_suggest_clear` (347) | handle | void | ○ |
| 27 | `meltype_suggest_hint` (361) | handle | 返値→呼出側 free / NULL | ○ |
| 28 | `meltype_data_directory` (377) | – | 返値→呼出側 free | × |
| 29 | `meltype_userdict_words` (385) | – | 同上 / NULL | ○ (`Text`) |
| 30 | `meltype_userdict_version` (394) | – | int | ○ |
| 31 | `meltype_userdict_check` (411) | str×4 | 同上 | △ |
| 32 | `meltype_userdict_add` (421) | str,str | 同上 | △ |
| 33 | `meltype_userdict_add_many` (433) | str, out\* | 同上 + out | ○ |
| 34 | `meltype_userdict_problem` (453) | – | 同上 | ○ |
| 35 | `meltype_userdict_update` (462) | str×4 | 同上 | △ |
| 36 | `meltype_userdict_remove` (475) | str, out\* | 同上 + out | ○ |
| 37 | `meltype_userdict_restore` (493) | str | 同上 | △ |
| 38 | `meltype_userdict_import` (504) | str, out\*×2 | 同上 + out | ○ |
| 39 | `meltype_userdict_export` (524) | str, int\* | 同上 + out | ○ |
| 40 | `meltype_to_reading` (541) | str | 同上 | △ |
| 41 | `meltype_term_words` (551) | str | 同上 | △ |
| 42 | `meltype_term_excluded` (559) | – | 同上 | ○ |
| 43 | `meltype_term_set_excluded` (564) | str,int | 同上 | △ (`ParseWords`+LINQ が外) |
| 44 | `meltype_term_edit` (575) | str×4, int\* | 同上 + out | ○ |
| 45 | `meltype_term_revision` (593) | – | int | ○ |
| 46 | `meltype_settings_get` (615) | – | 同上 | ○ |
| 47 | `meltype_settings_set` (622) | str,str | 同上 | △ |
| 48 | `meltype_settings_reset` (631) | – | 同上 | ○ |
| 49 | `meltype_term_create` (638) | str, out\* | 同上 + out | ○ |
| 50 | `meltype_term_rename` (656) | str,str | 同上 | △ |
| 51 | `meltype_term_delete` (665) | str, out\*, int\* | 同上 + out | ○ |
| 52 | `meltype_term_restore_domain` (685) | str,str,int | 同上 | △ |
| 53 | `meltype_term_user_check` (694) | str×5 | 同上 | △ |
| 54 | `meltype_term_user_add` (705) | str,str, out\*×2 | 同上 + out | ○ |
| 55 | `meltype_term_user_remove` (725) | str,str, out\* | 同上 + out | ○ |
| 56 | `meltype_term_user_restore` (744) | str,str | 同上 | △ |
| 57 | `meltype_term_user_update` (753) | str×5 | 同上 | △ |
| 58 | `meltype_term_move` (769) | str,str, out\*×3 | 同上 + out | ○ |
| 59 | `meltype_term_export` (790) | str,str, int\* | 同上 + out | ○ |
| 60 | `meltype_term_import` (807) | str, out\*×2 | 同上 + out | ○ |
| 61 | `meltype_report_url` (862) | str | 返値→呼出側 free | ○ (catch 内でも `ToUtf8`) |
| 62 | `meltype_update_evaluate` (884) | str,str | 同上 / NULL | ○ |
| 63 | `meltype_free` (898) | ptr | void | × (`NativeMemory.Free`。NULL 可) |

△ の 13 本について: try の外にあるのは `Marshal.PtrToStringUTF8` (不正 UTF-8 は U+FFFD に置換し、例外は投げない。投げ得るのは OOM のみ) と `string.Split` 系で、実質的に例外経路は無い。ただし無効ポインタによる SIGSEGV は try/catch では捕れない (NativeAOT は null 以外のアクセス違反を例外に変換しない) ので、「△でも実害なし」は「Swift 側が有効な NUL 終端ポインタを渡す」前提の上に成り立つ。

Swift → C# コールバック (C# が呼ぶ Swift 関数、NativeCore.swift 44–111):

| コールバック | 入力 | 返値 | 備考 |
|---|---|---|---|
| `clausesCallback` | C# 所有 (呼出し中のみ有効) | `strdup` → C# が `TakeUtf8` で `NativeMemory.Free` | `String(cString:)` で即コピー |
| `candidatesCallback` / `predictionsCallback` | 同上 | 同上 | 空配列でも `strdup("")` を返す (NULL でなく空文字) |
| `readingCallback` | 同上 | 同上 / nil | |
| `isWordCallback` | 同上 | Int32 | `NSSpellChecker.shared` (メインスレッド) |
| `learnCallback` | 同上 (ThreadPool スレッドから) | void | `DispatchQueue.main.async` の前に String 化 (寿命問題を正しく回避) |

---

### 指摘事項

#### 重大度: 高

該当なし。メモリ破壊・UAF・二重解放に直結するコードパスは、通常の呼び出し順 (Swift 側が単一所有者として `createSession` → 使用 → `deinit` で `destroySession`) では見つからなかった。

#### 重大度: 中

**M-1. `meltype_destroy` / 各 `Run` の「不正・解放済みハンドルでも例外で止まる」は保証されない**
- 場所: `Exports.cs:184-196` (Destroy のコメント「二重解放・不正なハンドルで InvalidOperationException になっても…落とさない」)、`:905`, `:228`, `:254`, `:270` ほかの `GCHandle.FromIntPtr(handle).Target`。
- 問題: `GCHandle.FromIntPtr` は値がゼロかしか検証しない。解放済み・でたらめな値を渡したときの `Target` / `Free` は .NET の仕様上 **未定義動作** (スロットが再利用されていれば別オブジェクトが返る、またはハンドルテーブルの破損・クラッシュ)。`InvalidOperationException` が出るのはゼロの場合だけで、ゼロは既に `if (handle != IntPtr.Zero)` で弾いている。つまりこの try/catch が守る範囲はコメントが示唆するより狭い。『推測』: NativeAOT のハンドルテーブルは Debug ビルドでのみ解放済み検出のアサートがあり、Release では検出しない。
- 影響: 現状の Swift 側は `session` を `private var` に持ち、`deinit` で 1 回だけ `destroySession` を呼び、その後は使わない (InputController.swift:13, 42, 46)。よって**現時点で UAF は起きない**。ただし今後 Swift 側で `session = nil` にせず再利用・二重破棄する変更が入ると、C# 側に防波堤が無いまま UB になる。
- 修正案: (a) Swift 側 `destroySession` 後に `session = nil` を代入する習慣を入れる (deinit なので必須ではないが意図を明示)。(b) C# 側で `GCHandle` の生値を直接返す代わりに、`ConcurrentDictionary<nint, MeltypeSession>` と単調増加 ID (または生成時刻を混ぜたトークン) で間接化し、未知の ID は `null` を返す。これで解放後使用・二重解放を「検出できる失敗」にできる。(c) 少なくともコメントを「ゼロのみ検出。解放済みハンドルは未定義」と正確に直す。

**M-2. dlopen の失敗・ABI 不一致は fail-open (全キー素通し) で、ユーザーに見える通知が無い**
- 場所: `NativeCore.swift:204-263` (dlopen 失敗は `NSLog` のみ、`isCompatible=false`)、`InputController.swift:185-190` (結果 nil → `return false`)。
- 問題: dylib が無い・壊れている・版が合わない場合、Meltype を選んでいるのに入力がすべてアプリへ素通しになる。IME としては「落ちるより素通し」が正しい設計 (コメント通り) だが、ユーザーには「Meltype が壊れている」と「日本語モードになっていない」の区別がつかず、設定ファイルやログを見ない限り原因に辿り着けない。セキュリティ上の境界ではない (攻撃者が dylib を消せる時点で同一ユーザー権限) が、堅牢性としては「静かな機能停止」。
- 修正案: `initialize()` 失敗時に `NSUserNotification`/`UNUserNotificationCenter` か、入力メニューに「本体を読み込めません (再インストールしてください)」の項目を 1 つ出す。辞書アプリ側 (`NativeDictionary.LoadError`) は画面に理由を出す設計になっており、IME 側だけ無言なのは不揃い。

**M-3. ローカル (ad-hoc) 署名ビルドでは `DYLD_LIBRARY_PATH` による差し替えが理論上可能**
- 場所: `NativeCore.swift:205-207` (`dlopen(絶対パス, RTLD_NOW)`)、`build.sh:162-165` (ad-hoc 署名は `--options runtime` 無し)。確認結果: インストール済みアプリは `flags=0x2(adhoc)`、Hardened Runtime 無し。
- 問題: macOS の `dlopen(3)` は、パスにスラッシュが含まれていても **まず `$DYLD_LIBRARY_PATH` の各ディレクトリを葉の名前 (`libMeltypeNative.dylib`) で探す**。Hardened Runtime か restricted プロセスでなければ DYLD_* は無効化されないため、IME プロセスの環境変数に `DYLD_LIBRARY_PATH` を注入できれば (`launchctl setenv` 等)、別の dylib を読ませられる。
- 影響: 攻撃に同一ユーザー権限が必要で、その権限があれば Frameworks 内の dylib 自体を書き換えて再 ad-hoc 署名もできるため、**権限昇格にはならない**。配布ビルド (`MELTYPE_MAC_IDENTITY` あり) は Hardened Runtime + 同一 Team ID のライブラリ検証が効き、この経路は閉じる (build.sh:150-160 のコメント通り)。開発者自身のローカルビルドに限った話。
- 修正案: ローカル署名でも `--options runtime` を付けられるか検討 (ad-hoc + runtime は可能。ただし library validation で同梱の Swift 互換 dylib もまとめて ad-hoc 署名されている必要がある。`--deep` で署名しているので満たせる見込み。『推測』、要実機確認)。付けない判断なら、README/build.sh に「ローカルビルドは DYLD 環境変数の影響を受ける」と一行残す。

#### 重大度: 低

**L-1. `Bundle.main.privateFrameworksPath` が nil のとき `"/libMeltypeNative.dylib"` (ルート直下) を dlopen する**
- 場所: `NativeCore.swift:205`。
- 問題: `?? ""` で空文字にフォールバックするため、固定パス `/libMeltypeNative.dylib` を開きに行く。ルートは非 root には書けないので実害は小さいが、意図しないパスを探す。
- 修正案: `guard let frameworks = Bundle.main.privateFrameworksPath else { library = nil; …; return }` として読み込みを諦める。

**L-2. `NativeDictionary.defaultLibraryPath` の第 1 候補はバンドル位置からの相対で、Helpers 外に置かれると任意の `../../Frameworks/` を見る**
- 場所: `NativeDictionary.swift:78-83`。
- 問題: `MeltypeDictionary.app` を単体で `~/Downloads/X/` 等にコピーして起動すると `~/Downloads/Frameworks/libMeltypeNative.dylib` を探す。配布ビルドではライブラリ検証で同一 Team ID の署名が無いものは拒否されるので、やはり同一ユーザー権限の範囲内。環境変数で差し替えない方針 (コメント) は良い。
- 修正案: `outer` 候補を使う条件に「自分が `*/Contents/Helpers/` の下にいる」ことを加える。

**L-3. 例外ラップの網羅性: 13 本で `FromUtf8`/`ParseWords` が try の外、2 本 (`meltype_data_directory`, `meltype_free`) は無包装**
- 場所: 上表の △・× (特に `Exports.cs:567` `TermSetExcluded` は `ParseWords(...).Select(...).ToList()` が外、`:378` `DataDirectory` は `ToUtf8` の `NativeMemory.Alloc` が OOM で投げ得る)。
- 問題: 現実に投げ得るのは OOM だけで、OOM 時は何をしても助からないため実害はほぼ無い。ただし「全エクスポートが包まれている」と言い切るには、形が統一されていない。#9 (例外復旧) は「実質網羅」であって「機械的網羅」ではない。
- 修正案: 入力の `FromUtf8`/Parse も `Reason`/`Text` ラムダの中へ移すか、`UnmanagedCallersOnly` 直下を 1 つの汎用ラッパー (`Guard(Func<byte*>)`) に統一し、Roslyn アナライザーや簡単なスクリプトで「EntryPoint 直下の式が Guard/Run/Text/Reason 呼び出しであること」を検査する。`meltype_free` と `meltype_abi_version` は例外扱いでよい。

**L-4. `isCompatible` を見ない呼び出しが 2 つ (`dataDirectory`, `reportUrl`)**
- 場所: `NativeCore.swift:443-454`。
- 問題: ABI 不一致の dylib でもこの 2 つは呼ばれる。現状の引数形は版 1 から変わっていないので壊れないが、「不一致なら一切呼ばない」という設計 (`:165` コメント) と矛盾。
- 修正案: 他と同じく `guard isCompatible` を足す。

**L-5. 埋め込み NUL (U+0000) で文字列が途中で切れる**
- 場所: 双方向。Swift `withCString` → C# `PtrToStringUTF8` (strlen ベース)、C# `ToUtf8` → Swift `String(cString:)` / `strlen(pointer)` (NativeCore.swift:459)。
- 問題: 周辺テキスト (`before`/`after`) や再変換の選択文字列に NUL が含まれると、そこで切れる。JSON 結果に NUL が入れば Swift のデコードが失敗し nil → キー素通し。メモリ安全性の問題ではなく、データの欠落のみ。発生頻度は極めて低い。
- 修正案: 長さ付き (`ptr, len`) に変える価値はない。Core 側で `before/after` から `\0` を除去するか、仕様として「NUL は末尾扱い」と `Exports.cs:17` のコメントに追記する。

**L-6. `surroundingText` の `selection.location + selection.length` は Swift の整数オーバーフロー trap になり得る**
- 場所: `InputController.swift:698-704`。
- 問題: `location != NSNotFound` は確認するが `length` は確認しない。IMK クライアントが壊れた `NSRange` (`length == NSNotFound` 等) を返した場合、`Int` 加算で trap → IME プロセスごと落ちる。『推測』: 実アプリでそういう値を返す例は確認できていない (Google ドキュメント等で `selectedRange` が不正確という記録はあるが、長さが巨大だった記録は無い)。再変換経路 (`:175`) は `length <= 200` で守られている。
- 修正案: `guard selection.length != NSNotFound, selection.length >= 0, selection.location <= Int.max - selection.length else { return (nil, nil) }` を足す。FFI に渡す前の Swift 側の防御なので C# 側の変更は不要。

**L-7. `Int32(index)` の変換 trap (理論上)**
- 場所: `NativeCore.swift:285, 290` (`Int32(index)`)。
- 問題: `Int` → `Int32` は範囲外で trap。index は候補ウィンドウの行番号で候補数に縛られるため実質到達不能。C# 側は `index < 0 || index >= Count` で境界検査済み (CompositionController.cs:354, 363)。
- 修正案: `Int32(clamping: index)` にすれば trap の可能性をゼロにできる (1 語の変更)。

**L-8. `CallbackWordChecker` の `_cache.Count` はキャッシュミスごとに O(n)**
- 場所: `Exports.cs:1012`。
- 問題: `ConcurrentDictionary.Count` は全バケットのロックを取って数える。メモリ安全性ではなく性能のみ。20000 語に達するまでは小さな n なので体感には出ない。
- 修正案: `Interlocked.Increment` した自前カウンタか、`_cache.Count` の確認を 256 回に 1 回にする。

**L-9. `MELTYPE_DATA_DIR` 環境変数が本番でも保存先を変える**
- 場所: `AppPaths.cs:13-16`。
- 問題: 検証用 (check-mac-aot-settings.py) の口が本番ビルドにも残る。IME プロセスの環境変数を変えられるのは同一ユーザーなので境界越えではないが、「ネットワークに何も送らない・保存先は固定」という設計書 §21 の主張との整合は一言あると良い。
- 修正案: 現状維持で可。README かコメントに「同一ユーザーが環境変数で変えられる (検証用)」と明記する程度。

---

### 観点別の確認結果 (コードで確認できた事実)

**(1) dlopen のパス解決**
- IME: `Bundle.main.privateFrameworksPath + "/libMeltypeNative.dylib"` の絶対パス、`RTLD_NOW`。環境変数による差し替え口は無い (M-3 の dyld 仕様を除く)。
- 辞書アプリ: 外側 `Meltype.app/Contents/Frameworks` → 自分の Frameworks の順。環境変数は見ない (意図的、コメントあり)。
- dylib 自身の依存はすべて `/usr/lib` `/System` の絶対パスで `LC_RPATH` 無し (インストール済み dylib を `otool -L`/`-l` で確認)。build.sh:142-149 は実行ファイルのビルドマシン絶対 RPATH を署名前に除去しており、残る RPATH は `/usr/lib/swift`, `@loader_path`, `@executable_path/../Frameworks` のみ (確認済)。
- 失敗時: fail-open (M-2)。ABI 版数 (8) の一致を `meltype_abi_version` で先に確かめ、不一致なら他の関数を呼ばない。

**(2) 文字列・配列の受け渡し**
- 形式: 全方向で NUL 終端 UTF-8、長さは渡さない。配列は「改行区切り・タブ区切り」の 1 文字列。
- 所有権: C#→Swift は `NativeMemory.Alloc` (= C ランタイム malloc の薄いラッパー) で確保し `meltype_free` (= `NativeMemory.Free` = free) で解放。Swift→C# は `strdup` (malloc) で確保し C# `TakeUtf8` が `NativeMemory.Free` で解放。アロケータは双方 libSystem の malloc で一致。
- 寿命: Swift は `withCString` のクロージャ内で呼び出しを完結させ、C# は呼び出し中に `PtrToStringUTF8` でコピーする。C#→Swift のコールバック引数は Swift が `String(cString:)` で即コピー。`learnCallback` は `DispatchQueue.main.async` の前にコピーしている (正しい)。
- 二重解放・リーク: `Exports.cs` の各コールバック呼び出しは `try/finally` で入力バッファを解放 (`:927-935, :954-962, :973-991, :1019-1027`)。`MoreCandidates`/`Predictions` (`:919, :941`) は finally を使っていないが、間に例外が出る式は無い。Swift 側は返値を `defer { freeFunction?(pointer) }` で解放 (`freeFunction` が nil になるのは同じ dylib に `meltype_free` が無い場合のみ = 到達不能)。
- 非 NUL 終端バッファを `String(cString:)` に渡す箇所: 無し。
- 不正 UTF-8: `PtrToStringUTF8` も `String(cString:)` も置換して受け入れる (例外・trap なし)。

**(3) 整数境界**
- 長さ・インデックスは `int` (C#) ⇄ `Int32` (Swift) で一致。`vk`/`ch`/`modifiers` は `Int32`。`ch` は Swift 側で UTF-16 単位 1 つのみ (`utf16.count == 1`) かつ私用領域を除外して渡し、C# は `ch > 0 ? (char)ch : null` (`:206`) — 範囲内。
- `SelectCandidate`/`SelectPrediction` は C# で `index < 0 || index >= Count` を検査 (CompositionController.cs:354, 363)。
- 再変換: Swift `selection.length <= 200` (UTF-16) と C# `text.Length > MaxReconvertLength(200)` (UTF-16) で二重に制限。
- `deleteBefore` (JSON → Swift `Int`) は `min(edit.deleteBefore, caret)` でクランプ (InputController.swift:525)。
- 残る懸念は L-6, L-7。

**(4) 例外**
- 63 本中 48 本は本体全体が try/catch またはラッパー経由。13 本は入力変換のみ外 (OOM 以外投げない)。2 本 (`data_directory`, `free`) は無包装 (L-3)。
- Swift コールバック内の trap 源 (`try!`, `as!`, `fatalError`, 強制 unwrap) は `Converter.swift` に無し (grep で確認)。`@convention(c)` クロージャは throw できないため、Swift 側の失敗は nil/0 で表現されている。
- `Learn` を呼ぶ ThreadPool スレッドは `CompositionController.cs:2042-2048` で try/catch 済み (裏スレッドの未処理例外 = プロセス終了、を正しく避けている)。

**(5) スレッド安全性**
- IME 本体: IMK がメインスレッドから呼ぶ前提 (`Exports.cs:19`)。例外は (a) `Learn` → ThreadPool → Swift `learnCallback` → `DispatchQueue.main.async` で azooKey へ (正しい)、(b) `meltype_update_evaluate` を Updater が裏スレッドで呼ぶ (状態を持たない関数、コメントあり)。
- 静的関数ポインタ `s_*` は `meltype_init` で 1 回だけ書かれ、その後は読み取りのみ (ワード単位の読み書きなので tearing 無し)。
- `CallbackWordChecker` はセッションごとの `ConcurrentDictionary`。`ConfigFlag` は `lock`、`UserDictionary` は `lock(_gate)` + プロセス間 `FileLock`、`TermDomains.User` は `lock(Lock)` + `FileLock` (grep で確認。全パスを追ってはいない—『推測』: `MacSettingsCatalog` も同様と見なす)。
- 再入: C# → Swift コールバック (`clauses`/`isWord`) は同期で、`MeltypeConverter` から `NativeCore` へ戻る呼び出しは無い (grep で確認)。

**(6) ハンドル/ライフサイクル**
- `meltype_create` が `GCHandle.Alloc(session)` (強参照) の生値を返し、`meltype_destroy` が `Free`。Swift は `init` で 1 回作り `deinit` で 1 回壊す単一所有。破棄後の使用経路は無い。
- 検出不能な UB の余地は M-1 の通り。

**(7) ビルド設定**
- `InvariantGlobalization=false`: macOS では `libicucore` を使う (dylib の `otool -L` に `/usr/lib/libicucore.A.dylib` を確認)。ICU 無しで落ちる経路は無い。
- `StackTraceSupport` は未設定 (既定 true) なので `ex.ToString()` にマネージドのスタックが残る。`StripSymbols=true` はネイティブシンボルのみで、ログの例外メッセージには影響しない。
- `IlcDisableReflection` 未設定 + `JsonSerializerIsReflectionEnabledByDefault=true` は、ソース生成し忘れの JSON を落とさないための保険 (コメント通り)。
- `check-mac-aot-settings.py` が担保するもの: AOT 固有の JSON メタデータ欠落、設定・辞書・専門用語集のプロセス間保存/読み直し、同時書き込みで語が消えないこと、IME 側セッションが別プロセスの変更に追随すること。**担保しないもの**: 不正ハンドル・二重 destroy・NULL 必須引数・巨大入力・不正 UTF-8・埋め込み NUL など、本レビューの観点 (2)(3)(6) に当たる境界値。これらは `ctypes` で同じ枠組みを使えば数十行で追加できる。

---

### 良い点

1. **所有権の規約が単純で一貫している**: 「C# が返す文字列は `meltype_free`、Swift が返す文字列は `strdup` して C# が free」の 2 文で全 63 関数 + 6 コールバックが説明でき、コードもその通りに書かれている。Swift 側の `take()`/`decode()` と C# 側の `TakeUtf8()`/`ToUtf8()` に集約されており、個別関数で手で free している箇所が無い。
2. **ABI 版数ゲートと RPATH 掃除**: 版の違う dylib が混ざったときに「引数の食い違いで落ちる」代わりに初期化を止める設計、ビルドマシンの絶対 RPATH を署名前に除去する処理、dylib 自身に `@rpath` 依存を残していない点は、dylib 読み込み周りの定番の落とし穴を先回りしている。

### 総括

メモリ安全性に直結する欠陥 (UAF・二重解放・バッファ overread) は見つからず、所有権と寿命の扱いは一貫している。
残る課題は「保証されていない安全性をコメントが約束している」(M-1 GCHandle) と「失敗が静かすぎる」(M-2 fail-open 無通知)、およびローカルビルド限定の DYLD 環境変数の影響 (M-3) で、いずれも同一ユーザー権限の範囲内。
`check-mac-aot-settings.py` に境界値 (不正ハンドル・二重破棄・巨大/不正入力) の数ケースを足せば、本レビューの前提の多くを機械的に守り続けられる。


---

# 付録 D: 領域レビュー原文 (supply)

## Meltype for Mac セキュリティレビュー D: ビルド・インストール・配布スクリプト / サプライチェーン / 秘密情報

- 対象リポジトリ: このリポジトリ (HEAD = 1.1.5 相当、`git ls-files` 258 件、コミット 56 件)
- 方法: 読み取りのみ (ファイル閲覧、`git ls-files` / `git grep` / `git log -p` の走査、GitHub API の GET)。ビルド・インストール・変更は行っていない。shellcheck は未インストールのため目視。
- ローカルに残っていた `mac/build/Meltype.app` (gitignore 対象) と `~/Library/Input Methods/Meltype.app` の `codesign -dvv` 結果、`mac/.build/checkouts` の依存ソースも参照した (これらは「コードで確認した事実」として扱う)。

### 対象ファイルと行数

| ファイル | 行数 |
|---|---|
| mac/build.sh | 221 |
| mac/install.sh | 199 |
| mac/uninstall.sh | 110 |
| mac/Install Meltype.command | 10 |
| mac/Uninstall Meltype.command | 10 |
| mac/make-pkg.sh | 37 |
| mac/make-zip.sh | 28 |
| mac/pkg/Distribution.xml | 21 |
| mac/pkg/scripts/postinstall | 61 |
| mac/pkg/resources/welcome.html / conclusion.html | 10 / 7 |
| mac/INSTALL.txt | 105 |
| mac/README.md | 268 |
| mac/Resources/Info.plist | 123 |
| mac/Resources/Dictionary/Info.plist | 40 |
| entitlements | リポジトリに無し (0 件) |
| mac/Package.swift | 47 |
| mac/Package.resolved | 59 |
| nuget.config | 1 |
| src/Meltype.Core/Meltype.Core.csproj | 24 |
| src/Meltype.Mac.Native/Meltype.Mac.Native.csproj | 29 |
| src/Meltype/Meltype.csproj | 23 |
| src/Meltype.Core.Tests/*.csproj / src/Meltype.Tests/*.csproj | 15 / 15 |
| tools/*.mjs (8 本) | 105 / 262 / 38 / 42 / 117 / 84 / 48 / 72 |
| tools/check-mac-aot-settings.py | 907 |
| tools/report-form/Code.gs / README.md | 94 / 78 |
| .github/ISSUE_TEMPLATE/1-bug.yml / 2-idea.yml / config.yml | 49 / 11 / 1 |
| .github/workflows | 無し (ローカル・リモートとも 404) |
| SECURITY.md | 55 |
| .gitignore | 32 |
| README.md | 195 |
| THIRD-PARTY-NOTICES.md | 399 |

参考として `mac/Sources/MeltypeIME/Updater.swift` (728 行) は、install.sh を呼ぶ側として関連部分のみ確認した (アプリ本体のレビューは対象外)。

---

### 指摘事項

#### 重大度: 高

該当なし。利用者のデータや Mac を壊す・漏らす経路になる明確なスクリプト上の欠陥は見つからなかった。ただし下の「中」の 1・2 は配布モデル (ad-hoc 署名 + 無署名 pkg) 由来の構造的な限界で、スクリプトの修正では消えない。

#### 重大度: 中

##### 中-1. ad-hoc 署名のため「同じ作者の物か」をどの段階でも検証できない
- 場所: `mac/build.sh:163-164` (`codesign --force --sign -`)、`mac/install.sh:46,114` と `Updater.swift:450` (`codesign --verify --deep --strict` のみ)、`SECURITY.md:20-22`
- 事実: ローカルの `build/Meltype.app` と導入済みアプリは `Signature=adhoc`、`TeamIdentifier=not set`、entitlements 無し、Hardened Runtime 無し (`--options runtime` は `MELTYPE_MAC_IDENTITY` が有るときだけ: build.sh:155-161)。`codesign --verify` が確認するのは「zip 内で整合している (改変されていない)」ことだけで、作者の同一性は見ない。SHA-256 の照合値 (`digest`) も同じ GitHub Release から取る。
- 影響: GitHub アカウント/リポジトリの乗っ取り、または Release 資産の差し替えに対して、install.sh・アプリ内更新のどちらも検知できない。IME は全キー入力を扱うため、差し替えられた場合の被害はキーロガーと同等。SECURITY.md:20-22 は正直にこの限界を書いている (良い)。
- 推測: ad-hoc 署名の Designated Requirement は cdhash ベースなので、TCC (もし将来アクセシビリティ等の許可を要する機能を足した場合) の許可が版ごとに失われる。現状の IMK 入力ソースは TCC 許可を要しないので、今は実害なし。
- 修正案 (費用の無い順):
  1. Release 単位の **帯域外の署名** を導入する: 作者が保持する鍵 (minisign / ssh-keygen -Y sign / Sigstore など) で zip・pkg の SHA-256 に署名し、公開鍵をアプリに埋め込んで Updater が検証する。これで「GitHub 乗っ取りでは防げない」という限界を潰せる。公開鍵は README/SECURITY.md にも載せる。
  2. Developer ID + 公証 (年 99 USD)。build.sh は既に `MELTYPE_MAC_IDENTITY` 経路と Hardened Runtime を用意してあるので、証明書だけで切り替わる。更新時に `codesign -dr -` の Team ID 比較も可能になる。
  3. いずれも難しい間は、SECURITY.md/INSTALL.txt の「限界」節を保ち、利用者が zip の SHA-256 を**別経路 (X の告知など)** でも確認できるようにする。

##### 中-2. pkg 経路はインストール時の完全性検査が無く、Gatekeeper の評価もされない
- 場所: `mac/make-pkg.sh:27-34` (無署名 `pkgbuild`/`productbuild`)、`mac/pkg/scripts/postinstall` 全体、`mac/pkg/Distribution.xml:7-8`
- 事実: zip 経路の install.sh は `codesign --verify` と SHA-256 表示を行うが、pkg 経路では postinstall が `--register-check`/`--register`/`pkill` を行うだけで、配置された Meltype.app の署名検査・SHA-256 照合は無い。Installer が置いたファイルには quarantine 属性が付かないので、アプリ自体に対する Gatekeeper の評価も起きない。pkg は無署名なので利用者は Installer 自体の警告を無視して開く必要がある (Release 本文に「右クリック→開く」<del>「このまま開く」</del>の説明あり: GitHub API で確認)。[訂正 2026-10-11: 公開済みの全 16 リリースの本文を取得して数え直したところ、「このまま開く」は 0 件で、書かれているのは「右クリック→開く」だけだった。API には編集履歴がないので、レビュー時点の本文との差は確認できない]
- 影響: pkg は「Gatekeeper 警告を無視して開く」ことを利用者に習慣づける経路になる。pkg 自体が差し替えられた場合、zip 経路より検知の機会が少ない (Release 本文の SHA-256 を手で比べるしかない)。`mac/README.md:93` に「動作確認中です (別の Mac で未確認)」と明記されており、まだ試験的な配布物。
- 推測: macOS 15 以降は pkg も「右クリック→開く」では開けず、システム設定→プライバシーとセキュリティ→「このまま開く」が必要。<del>Release 本文はそれも書いているが、</del>INSTALL.txt の pkg 言及は無い (INSTALL.txt は zip 向け)。[訂正 2026-10-11: Release 本文 (全 16 版) と `mac/README.md` の pkg の説明は「右クリック→開く」だけで、「このまま開く」を書いていなかった。zip 向けの `README.md` と `INSTALL.txt` は併記済み。pkg 向けの併記 (`mac/README.md`、`make-pkg.sh` のコメント) は同日に対応した。Release 本文は未修正 (見送り)]
- 修正案: (a) postinstall の冒頭で `codesign --verify --deep --strict "$APP"` を行い、失敗したら `exit 1` して Installer に失敗を報告させる (現在は常に `exit 0`: postinstall:60)。(b) pkg が「試験中」である旨を Release 本文にも残し、推奨経路を zip + install.sh に揃える。(c) 中-1 の署名が実現するまでは pkg の配布をやめる選択も検討 (配布面を一つ減らす)。(d) [追記 2026-10-11] pkg の案内 (`mac/README.md`、`make-pkg.sh` のコメント) に macOS 15 以降の「このまま開く」を併記する (対応済み。Release 本文は未修正)。 [追記 2026-10-12] (b)(c) は決定した: pkg の配布は今のまま続ける (Release 本文の「動作確認中」の表記で足りるとし、配布をやめる案は採らない)。経緯は [PKG_REVIEW_2026-10.md](PKG_REVIEW_2026-10.md) の §4。

##### 中-3. .NET 側のビルド再現性: SDK/ILCompiler の版が固定されていない
- 場所: `nuget.config:1` (`<clear />`)、`mac/build.sh:55-58` (`--source https://api.nuget.org/v3/index.json`)、`src/Meltype.Mac.Native/Meltype.Mac.Native.csproj` (PackageReference 無し)、`global.json` 無し、`packages.lock.json` 無し
- 事実: PackageReference はどのプロジェクトにも無い (良い: 直接依存ゼロ)。しかし NativeAOT に必要な `Microsoft.DotNet.ILCompiler` と `runtime.osx-*.Microsoft.DotNet.ILCompiler`、ランタイムパックは SDK が暗黙に決める版で nuget.org から取得される。SDK の版を固定する `global.json` が無いため、ビルドマシンの SDK が変わると取得物も変わる。ロックファイルも無いので、取得した版の記録が残らない。
- 推測: macOS の `dotnet restore` は既定で NuGet パッケージ署名の検証をしない (Windows のみ既定で有効)。HTTPS で nuget.org 公式のみに接続している点は良い。
- 影響: 配布物の SHA-256 が「どの SDK・どの ILCompiler で作ったか」と結び付かず、第三者が同じバイナリを再現して照合できない。乗っ取り検知 (中-1) の代替手段としての再現ビルドが成り立たない。
- 修正案: `global.json` で SDK 版 (`rollForward: latestPatch` 程度) を固定し、`Meltype.Mac.Native.csproj` に `<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>` を付けて `packages.lock.json` をコミット、build.sh の publish に `--locked-mode` を足す。Release 本文に SDK/Swift の版を 1 行書く。

##### 中-4. CI/ビルド来歴が無く、配布物は作者の手元ビルド
- 場所: `.github/workflows` 無し (ローカル・リモート両方で確認)。`mac/build.sh:151` のコメントは「公証 (notarization) は mac.yml で行う」と述べるが、そのファイルは存在しない (古い記述)。
- 事実: Release の zip/pkg と SHA-256 は作者のマシンで `make-zip.sh`/`make-pkg.sh` が出力したものを手で本文に貼る運用 (skill 文書と README から)。GitHub 側の `digest` は GitHub がアップロード時に計算する。
- 影響: ビルドマシンが侵害された場合に検知手段が無い。SHA-256 は「作者が貼った値 = アップロードした物」の整合性しか示さない (中-1 と同根)。
- 修正案: GitHub Actions (macOS runner) で `build.sh --no-install` → `make-zip.sh` を回し、Release 資産を Actions から上げる (`actions/attest-build-provenance` で来歴を付けられる)。費用ゼロ。少なくとも build.sh:151 のコメントを現状に合わせる。

#### 重大度: 低

##### 低-1. アプリ内更新は、ダウンロードした zip の中の `install.sh` をそのまま実行する
- 場所: `Updater.swift:443-445, 460, 600-616, 641`、`mac/INSTALL.txt:56`
- 事実: 更新時に実行される install.sh は zip に同梱の物で、`codesign` の封印 (Meltype.app 内) の外にある。整合性は GitHub の `digest` (SHA-256) で担保される。PATH は `/usr/bin:/bin:/usr/sbin:/sbin` に固定 (Updater.swift:644、良い)。
- 影響: 中-1 と同じ信頼モデル (GitHub を信頼) なので追加のリスクはほぼ無いが、「アプリ本体の署名検査を通ったから安全」という利用者の認識とはズレる。
- 修正案: 更新の入れ替え手順をアプリ側 (署名の封印の中) に持つか、install.sh の SHA-256 を Release 本文に併記する。または INSTALL.txt:56 に「install.sh は zip の SHA-256 で確かめている」と一言足す。

##### 低-2. install.sh が表示する実行ファイル単位の SHA-256 は、Release 本文に無い
- 場所: `mac/install.sh:52-63`、`mac/INSTALL.txt:37-38`、Release 本文 (ハッシュは zip と pkg の 2 つのみ: GitHub API で確認)
- 影響: 「リリースページの値と見比べて」と言われた利用者が比べる相手が無い。確認手段としては zip の SHA-256 で十分なので、説明と出力を揃える。
- 修正案: Release 本文に 3 つのハッシュを足す (make-zip.sh で出力させる) か、install.sh の文言を「zip の SHA-256 を確かめていれば省略可」に直す。

##### 低-3. Swift 依存の固定は正しいが、再解決を禁止していない
- 場所: `mac/Package.swift:16` と `mac/Package.resolved:9` (revision `d59a28e4…` が一致。`.build/checkouts` の HEAD も一致を確認)、`mac/build.sh:61,65` (`swift build -c release`)
- 事実: ルート依存はコミット固定。推移依存 (Jinja、swift-algorithms、swift-collections、swift-numerics、swift-tokenizers) は Package.resolved で固定。azooKey の Package.swift 内の `swift-argument-parser`・`SwiftyMarisa` は条件付きで、現在の解決には入っていない。
- 影響: Package.resolved が古くなっても `swift build` は黙って再解決するので、推移依存が気付かず動く余地がある。
- 修正案: build.sh の `swift build` に `--force-resolved-versions` を付け、resolved との不一致でビルドを失敗させる。

##### 低-4. azooKey 経由でビルド時にプリビルドのバイナリ (llama.cpp xcframework) を取得する
- 場所: `mac/.build/checkouts/AzooKeyKanaKanjiConverter/Package.swift:156-160` (`.binaryTarget(url: github.com/azooKey/llama.cpp/releases/download/b4846/…, checksum: …)`)、`mac/build.sh:78-82`
- 事実: SwiftPM が checksum (SHA-256) で検証して取得する。現在の trait 構成では Meltype にはリンクされず、ローカルの `build/Meltype.app/Contents/Frameworks` には `libMeltypeNative.dylib` と `libswiftCompatibilitySpan.dylib` のみ (確認済み)。build.sh:78-82 はもし `.framework` があれば同梱する作りで、1.0.0 時点では同梱されていた経緯がある (#13)。
- 影響: azooKey の pin を更新して llama.framework が再び同梱される場合、ソースからビルドしていない第三者バイナリが入る。その際 `THIRD-PARTY-NOTICES.md` に llama.cpp (MIT) の記載が無く (git grep で 0 件)、build.sh:121 のライセンス収集ループにも含まれない。
- 修正案: build.sh の framework 同梱ループで、同梱した名前を echo し、想定外の framework が入ったら失敗させる (ホワイトリスト)。pin 更新時に llama.cpp の通知を足す手順を mac/README に書く。

##### 低-5. Gatekeeper 回避 (`xattr -dr com.apple.quarantine`) の扱い
- 場所: `mac/install.sh:140-161`、`mac/Install Meltype.command:8` (`bash ./install.sh` → 端末なので y/N を聞く)、`Updater.swift:616` (`install.sh --yes` → 聞かない)
- 事実: 公証無しの ad-hoc アプリを起動させるために必須。対話端末では SHA-256 と入手元の確認を促したうえで y/N、`--yes` 無し・非端末では外さない (install.sh:144) という設計で、文書 (INSTALL.txt:44-46、mac/README.md:78、SECURITY.md:55) にも明記。評価: 回避の意味と条件を正しく説明しており、現状の配布モデルでは妥当。
- 残る点: `--yes` 経路 (更新) は検査無しで外す。これは中-1 の信頼モデルの帰結で、スクリプト単体の問題ではない。修正は中-1 に従う。

##### 低-6. pkg postinstall の root 動作時の挙動
- 場所: `mac/pkg/scripts/postinstall:14-39, 44, 60`
- 事実: `Distribution.xml:7` で `enable_localSystem=false`・`enable_currentUserHome=true` なので通常は利用者権限で動くが、root の場合も `stat -f%Su /dev/console` でログイン中ユーザーを取り、`dscl -plist` でホームを取得、空や非絶対パスなら中止 (24-27)、`chown -R` はホーム配下の `$APP` にのみ (38)、`launchctl asuser … sudo -u` で登録 (44)、`pkill -U "$CONSOLE_UID"` で他ユーザーのプロセスを巻き込まない (53,55)。固定名の /tmp ログを使わない理由も明記 (9-11)。評価: 丁寧。
- 残る点: 常に `exit 0` (60) なので失敗が Installer に伝わらない (中-2 と合わせて修正)。`chown -R` は `-R` のみでシンボリックリンクを追わないので安全。

##### 低-7. シェルスクリプト一般 (問題なしの確認結果と小さな指摘)
- `set -euo pipefail` と `cd "$(dirname "$0")"`: build.sh:9-10、install.sh:20-21、uninstall.sh:13、make-pkg.sh:8-9、make-zip.sh:7-8 で統一。`.command` 2 本は `set -e` 無しだが、`cd` 失敗時は install.sh が「Meltype.app が見つかりません」で止まる (install.sh:36-41) ので実害なし。
- 危険な展開: `rm -rf` / `rsync --delete` の対象は `$HOME/Library/Input Methods/Meltype.app/` (install.sh:113,119、build.sh:177)、`mktemp -d` の結果 (install.sh:106-128、make-pkg.sh:14-15 + trap)、固定名 `build/…`・`dist/Meltype-mac` (build.sh:51、make-zip.sh:15)。`set -u` により `$HOME` 未定義は展開時エラーで停止。uninstall.sh:54,99 は rm -rf 前にパスの等価確認までしている。空文字で `/` を消す経路は見つからなかった。
- スペースを含むパス (`Input Methods`、`Application Support`) は全箇所クォート済み。`pkill -f "$TARGET/…/MeltypeDictionary.app/"` (install.sh:134 ほか) はパターンが正規表現だが、絶対パスなので事実上そのパスの子プロセスだけに合う。`pkill -x Meltype` は厳密名。`killall imklaunchagent TextInputMenuAgent` は利用者自身のシステムエージェントを再起動させるだけで、復帰する (コメントどおり)。
- `curl | sh`・`sudo`: 無し (install.sh:19 の宣言どおり。postinstall の `sudo -u` は root から降格する用途のみ)。
- ユーザー PATH 依存: `.command`/手動実行時の install.sh は利用者の PATH で `codesign`/`rsync`/`xattr`/`shasum` を解決する。同一ユーザー権限の攻撃者なら他にもできることがあるため実害は小さいが、更新経路 (Updater.swift:644) に合わせて install.sh 冒頭で `PATH=/usr/bin:/bin:/usr/sbin:/sbin` を固定すると揃う。
- `.command` の cwd 依存: `cd "$(dirname "$0")"` で解決 (Terminal は絶対パスで `$0` を渡す)。INSTALL.txt:23 の `bash ~/Downloads/Meltype-mac/install.sh` も install.sh 内の cd で同じ結果になる。
- ダブルクリック可否: `.command` も無署名なので Gatekeeper 警告が出る。INSTALL.txt:19-23 に macOS 15 以降の「このまま開く」手順と、端末からの代替まで書いてある (良い)。

##### 低-8. Info.plist
- 場所: `mac/Resources/Info.plist`、`mac/Resources/Dictionary/Info.plist`
- 事実: `NSAppTransportSecurity` 無し (= ATS 既定、HTTPS 強制。更新の通信は https 固定 + ホスト制限なので整合)。`LSBackgroundOnly=true` (IME 用、`LSUIElement` 不要)。InputMethod 関連キー (`InputMethodConnectionName`、`InputMethodServerControllerClass`、`TISInputSourceID`、`tsInputMode…`) は IMK の標準構成。辞書アプリ側はネットワーク関連キー無し。問題なし。

##### 低-9. 秘密情報・個人情報の走査結果 (値は書かない)
- トークン/鍵パターン (ghp_、github_pat_、AKIA、PRIVATE KEY、xox*、sk-、api_key=、password=、Bearer <長い文字列>): 追跡ファイル・全履歴 (`git log -p --all`) ともに **0 件**。
- 削除済みの鍵/.env 類のファイル: 履歴に **0 件**。zip/pkg/p12 等のバイナリ: 履歴に **0 件**。
- `/Users/<名前>` 等のホームパス: 追跡ファイルに実パス無し (`docs/UPSTREAM_PR_SURVEY.md:51` と `src/Meltype.Core.Tests/QualityTests.cs:229` は例文の架空パス)。履歴の追加行にも 0 件。
- メールアドレス: `src/Meltype/AppInfo.cs:26` に連絡先 (gmail) が定数として入っている。本家 (yksr-melt/Meltype) の同ファイルにも同じ定数があることを確認したので、本家作者の公開連絡先を引き継いだもの。Mac 側 (`mac/`、`Meltype.Core`、`Meltype.Mac.Native`) から参照は無し。`src/Meltype.Core/Composition/CompositionDetector.cs:798` と `src/Meltype.Core.Tests/SecurityTests.cs:413` はコメント・テスト用の例。THIRD-PARTY-NOTICES.md 内のものはライセンス原文の連絡先。
- git 履歴の author メール: 2 種 (GitHub noreply と、個人の gmail)。公開リポジトリでは `git log` で誰でも見える。個人アドレスを隠したいなら GitHub の「Keep my email addresses private」+ `git config user.email <id>+<name>@users.noreply.github.com` で今後のコミットを切り替える (過去分の書き換えは履歴改変になるので提案のみ)。
- `tools/report-form/Code.gs:80-86`: GitHub トークンは Apps Script のスクリプトプロパティから読む設計で、ハードコード無し。README.md:48-55 で Fine-grained PAT (Issues: Read/Write のみ・単一リポジトリ) を指示している (良い)。`ProjectInfo.cs:21` の `ReportForm` は空文字で、フォーム URL (entry ID) は未コミット。
- `.gitignore`: `bin/ obj/ .build/ mac/build/ mac/dist/ *.zip *.pkg *.dmg`、`.DS_Store`、署名/鍵 (`*.p12 *.pem *.key *.cer *.pfx *.p8 *.keychain .env*`)、`*.log`、`*.before-restore` を除外。`git status --ignored mac` で `.build/ build/ dist/ .DS_Store` が実際に無視されていることを確認。利用者データは `~/Library/Application Support/Meltype` 側でリポジトリ外。問題なし。

##### 低-10. GitHub 側
- `.github/workflows`: 無し (中-4)。
- ISSUE_TEMPLATE: `1-bug.yml:8-9` で「パスワード・個人的な文章・ログを書かない」と警告。`environment` 欄 (46-49) はメニューから開いたとき自動で入る実行環境。中身は Core 側 (`NativeCore.swift:449` → `ProjectInfo`) で組み立てられ、本レビューの範囲外だが、`tools/report-form/README.md:30` は「入力した文字は入りません」と説明している。推測: OS 版・キーボード配列など非個人情報と思われるが、未確認。ユーザー名やホームパスが含まれないことを一度実機で確認して、テンプレートの説明に明記するとよい。
- `config.yml`: `blank_issues_enabled: true` — 空 Issue からでも書けるので、テンプレートの注意書きを読まずに個人情報を書く余地はある (運用判断)。
- SECURITY.md: 窓口は Private vulnerability reporting。GitHub API `GET /repos/whitewater-png/Meltype-for-Mac/private-vulnerability-reporting` が `enabled: true` を返すことを確認 (実在する)。

##### 低-11. tools/ の入力の扱い
- `*.mjs`: 引数のファイルパスを `fs.readFileSync` で読み、固定の出力先に書く開発者向けの生成器。ネットワーク取得・`child_process`・`eval` は無し。`check-dictionaries.mjs:72` は引数を `path.basename` で丸めてから `dictionaries/` 配下に限定する (トラバーサル回避)。`make-manifests.mjs` は zip のハッシュ計算のみ。
- `check-mac-aot-settings.py:582,684`: `subprocess.run/Popen` をリスト引数 (`shell=False`) で呼び、`MELTYPE_DATA_DIR` を一時ディレクトリに向ける。シェル展開無し。問題なし。
- `report-form/Code.gs:40-45,64`: 各欄 4000 文字で切り、``` を全角に置換して Markdown インジェクションを防いでいる。問題なし。

---

### 良い点

1. **install.sh の安全設計が文書と一致している**: 影響範囲の宣言 (install.sh:9-19)、署名検査→退避 (mktemp)→rsync→再検査→失敗なら書き戻し (106-128)、Gatekeeper 回避の明示的同意 (140-161)、非端末では回避しない、同じ内容が INSTALL.txt・mac/README・SECURITY.md に書かれている。更新経路でも PATH を固定している (Updater.swift:644)。
2. **サプライチェーンの縮小**: NuGet の直接依存ゼロ (`nuget.config` で `<clear/>`、テストも自前ランナー)、Swift 依存はコミット固定で Package.resolved と一致、build.sh が RPATH からビルドマシンのパスを除去して署名 (136-149)、ライセンス全文の同梱を欠くと配布を失敗させる (115-135)。
3. SECURITY.md が「GitHub 乗っ取りには無力」「公証なし」という限界を隠さず書いている。

### 総括 (3 行)

スクリプト自体は `set -euo pipefail`・クォート・mktemp・パス確認が徹底され、`rm -rf`/`rsync --delete` が危険な値を取る経路や秘密情報の混入は見つからなかった。
最大のリスクは配布モデルに由来する: ad-hoc 署名 + 同一 Release 由来の SHA-256 なので「作者の物か」を誰も検証できず、無署名 pkg と `xattr` 回避がそれを利用者の習慣として固定しかねない。
現実的な次の一手は、(1) 帯域外の鍵で Release を署名し Updater に公開鍵検証を足す、(2) `global.json` + ロックファイル + `--force-resolved-versions` でビルドを再現可能にする、(3) GitHub Actions で配布物を作って来歴を付ける、の 3 つ。
