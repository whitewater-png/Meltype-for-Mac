# B-2 実機検証チェックリスト（2026-10-19）

計画: [UPSTREAM_B2_PLAN.md](UPSTREAM_B2_PLAN.md) / 進み具合: [UPSTREAM_PR_TRACKER.md](UPSTREAM_PR_TRACKER.md)

## 0. 前提: 新しいビルドが動いているか

- インストール先: `~/Library/Input Methods/Meltype.app`。dylib は **2026-10-19 15:54:59**、SHA-256 は `b02314dec68d517c59626075b9aff42097e9e6dc40ba90ec55ff20184485eb54`（ビルド成果物と一致）。B2-7 の原因調査後の修正版。
- 稼働中の IME: `ps -axo pid,lstart,command | grep 'Input Methods/Meltype.app/Contents/MacOS/Meltype'` で起動時刻を見る。**15:54:59 より後**なら新しい dylib。前なら IME を再起動する（ログアウト/ログイン、または入力ソースを切り替える）。
- 設定: 「Meltype 辞書」→「設定」タブ（⌘4）。B2-4・B2-5・B2-6 は**既定 OFF**。検証する項目だけ ON にし、終わったら OFF に戻す。
- 入力先: TextEdit の新規書類など、Meltype が有効なプレーンテキスト。ほかの IME に切り替わっていないことを確認する。
- 候補の確定は Space / Enter。候補の並びも見たいときは、確定せずに Space を押したままにする。

## 1. 検証のしかた

1. 表の「操作」どおりに打つ。
2. 「期待」と違うときは、**実際の出力（候補の並び・確定した文字）**を「結果」に書く。
3. 設定 OFF でも同じ結果になるかを見る（回帰か、ON のときだけの限界か）。
4. NG は「実際の出力」「設定」「直前の文脈」をメモして送る。直せるものは直し、回帰なら計画どおり見送りにする。

## 2. B2-1 #848 ローマ字入力表

設定: なし。

| No. | 操作（打つ） | 期待 | 結果 |
| --- | --- | --- | --- |
| 1-1 | `kye` + Enter | `きぇ` | |
| 1-2 | `gye` / `pye` / `mye` + Enter | `ぎぇ` / `ぴぇ` / `みぇ` | |
| 1-3 | `kwi` / `kwu` / `kwe` + Enter | `くぃ` / `くぅ` / `くぇ` | |
| 1-4 | `gwi` / `gwu` / `gwe` + Enter | `ぐぃ` / `ぐぅ` / `ぐぇ` | |
| 1-5 | `zwa` / `zwi` / `zwu` / `zwe` + Enter | `ずぁ` / `ずぃ` / `ずぅ` / `ずぇ` | |
| 1-6 | `trying` + Enter | `trying`（今までどおり） | |
| 1-7 | `highway` + Enter | `highway`（今までどおり） | |
| 1-8 | `swan` + Enter | `swan`（NS が知っている語。今までどおり） | |
| 1-9 | `hyena` + Enter | `hyena`（今までどおり） | |
| 1-10 | `fya` / `fyu` / `fyo` + Enter | `ふゃ` / `ふゅ` / `ふょ`（既存。変わらない） | |

補足: 1-1〜1-5 は **CompositionTable（入力用）** に足しただけで、英語 / 日本語の判定は変えていません。1-6〜1-9 が英語のままなら合格です。

## 3. B2-2 #841 大文字の最後の n

設定: なし。Mac の「日本語として変換」は **Shift+Space** です（JIS の Convert キーは Core では扱っていません）。

| No. | 操作（打つ） | 期待 | 結果 |
| --- | --- | --- | --- |
| 2-1 | `TEN` + **Shift+Space** | 候補の先頭が `てん`。Enter で `てん` | |
| 2-2 | `pen` + **Shift+Space** | `ぺん`（今までどおり。この変更では変えない） | |
| 2-3 | `pen` + Space / Enter（Shift なし） | `pen`（英語のまま） | |
| 2-4 | `VPN` + Shift+Space | 候補に `VPN`（英字）が残る。`VPN` を選べる | |

補足: 2-2 は「Shift+Space で日本語として変換」したときの既存の結果です。英字のままにしたいときは Shift を押さずに確定してください。

## 4. B2-3 #829 長い日本語文

設定: なし。Mac はすでに期待どおりなので、**回帰していないこと**の確認です。

| No. | 操作（打つ） | 期待 | 結果 |
| --- | --- | --- | --- |
| 3-1 | `korewoiPhonedesatueisi,YouTubeniappuro-dosiyou` + Enter | `これをiPhoneでさつえいし、YouTubeにあっぷろーどしよう` | |
| 3-2 | `universityGitHub` + Enter | `universityGitHub`（そのまま） | |
| 3-3 | `internationaliPhone` + Enter | `internationaliPhone`（そのまま） | |
| 3-4 | `iPhone` / `YouTube` + Enter | そのまま（今までどおり） | |
| 3-5 | `kyouhaiPhone` + Enter | 今までどおり（例: `今日はiPhone`。実際の出力を記録） | |

## 5. B2-4 #800 学習を前の文脈ごとに分ける

設定: 「英語 / 日本語の学習を前の文脈ごとに分ける」。

**準備**: 古い学習の影響を避けるため、辞書に無い語 `zukini` を使います（英語: `zukini`、日本語: `ずきに`）。`I like ` の後ろが英語の文脈、`これは` の後ろが日本語の文脈です。F10 = 英字、F6 = かな（Mac では必要なら **Fn+F10 / Fn+F6**）。

| No. | 設定 | 操作（打つ） | 期待 | 結果 |
| --- | --- | --- | --- | --- |
| 4-1 | OFF | `I like ` + `zukini` + **F10** | `zukini`（英語） | |
| 4-2 | OFF | `これは` + `zukini` + **F6** | `ずきに`（日本語） | |
| 4-3 | OFF | そのあと `I like ` + `zukini`、`これは` + `zukini` | 今までどおり（1 つの学習を共有。最後に覚えた向きが両方に効く） | |
| 4-4 | ON | `I like ` + `zukini` + **F10** | `zukini`（英語の文脈として覚える） | |
| 4-5 | ON | `これは` + `zukini` + **F6** | `ずきに`（日本語の文脈として覚える） | |
| 4-6 | ON | `I like ` + `zukini` | 先頭 / 確定が `zukini`（英語の文脈の学習） | |
| 4-7 | ON | `これは` + `zukini` | 先頭 / 確定が `ずきに`（日本語の文脈の学習） | |
| 4-8 | ON | 「Meltype 辞書」→「学習した語」（⌘3） | `zukini` が 2 行。**文脈ラベルは無い**（既知の限界） | |
| 4-9 | ON | ON にする前に覚えた語（例: `api` を英語で確定）を英語 / 日本語の後ろで打つ | どちらの文脈でも古い学習を使う | |

補足: 4-8 は Core の `Learned.Context` に文脈を持たせていますが、画面のラベル表示は未対応です。4-3 の OFF は「今までどおり」の確認なので、実際の出力をそのまま記録してください。

## 6. B2-5 #799 かな + w を笑いにする

設定: 「かな + w を笑い (ねw・さw) にする」。

| No. | 設定 | 操作（打つ） | 期待 | 結果 |
| --- | --- | --- | --- | --- |
| 5-1 | OFF | `naruhodonew` + Enter | `なるほどnew`（今までどおり） | |
| 5-2 | ON | `naruhodonew` + Enter | `なるほどねw` | |
| 5-3 | ON | `Isaw` + Enter | `Isaw`（英文の中の `saw` はそのまま） | |
| 5-4 | ON | `I saw` + Enter | `I saw`（`saw` はそのまま） | |
| 5-5 | ON | `onewnoshinpin` + Enter | ON のときの既知の限界（`おnewの新品` が壊れる）。実際の出力を記録 | |

補足: 5-5 は `お`（1 文字のかな）の後ろの `new` を `ねw` にするため、`おnew` を意図した入力が壊れます。`new`・`saw` を英単語として打つ人は OFF のままにしてください。

## 7. B2-6 #807 クリップボードの文字を候補に出す

設定: 「クリップボードの文字を候補に出す」。読みは `kurippuboodo`（`くりっぷぼおど`）と `kurippubo-do`（`くりっぷぼーど`）の両方、`peesuto` と `pe-suto` の両方で確認できます。

| No. | 設定 | 操作 | 期待 | 結果 |
| --- | --- | --- | --- | --- |
| 6-1 | OFF | `https://example.com` をコピー → `kurippuboodo` + Space | 候補に URL が出ない（今までどおり） | |
| 6-2 | ON | `https://example.com` をコピー → `kurippuboodo` + Space | 候補に `https://example.com` が出る。選んで確定できる | |
| 6-3 | ON | `line1` と `line2` の 2 行をコピー → `kurippuboodo` + Space | 1 行目の `line1` が候補に出る | |
| 6-4 | ON | 201 文字の 1 行をコピー → `kurippuboodo` + Space | その行は候補に出ない | |
| 6-5 | ON | パスワード管理ソフトでパスワードをコピー（Concealed）→ `kurippuboodo` / `peesuto` + Space | パスワードは候補に出ない。出なければ合格 | |
| 6-6 | ON | クリップボードを空にする → `kurippuboodo` + Space | クリップボード由来の候補は出ない（例外にならない） | |

補足: 6-5 は 1Password など「履歴に残さない」印（`org.nspasteboard.ConcealedType` / `TransientType`）を付けるソフトが必要です。無い場合は「未確認」と記録してください。設定 ON は変換した瞬間に 1 回だけ読み、保存・送信はしません。

## 8. B2-7 #788 予測候補の Delete

設定: 予測変換 ON（既定）。**Mac の前方削除 = Fn+Delete** です（`KeyMapping.swift` で `kVK_ForwardDelete` → `0x2E`）。通常の Delete / Backspace は `0x08` なので、予測の削除にはなりません。

**準備**: `kyou` → `今日` を選んで確定し、履歴に覚えさせます。

| No. | 操作 | 期待 | 結果 |
| --- | --- | --- | --- |
| 7-1 | `kyou` + Space → `今日` を選んで確定 | 履歴に `きょう` → `今日` が入る | |
| 7-2 | もう一度 `kyou` と打つ | 予測に `今日` が出る | |
| 7-3 | **Tab** で `今日` を選び、**Fn+Delete** | `今日` が予測から消える。履歴からも消える | |
| 7-4 | もう一度 `kyou` と打つ | `今日` が履歴由来で出ない（azooKey の予測も消えていれば出ない） | |
| 7-5 | `kyou` と打ち、Tab を押さずに **Fn+Delete** | 例外にならない。履歴は消えない（選んでいないため） | |
| 7-6 | ユーザー辞書に `今日`（読み `きょう`）を登録 → 予測で選び **Fn+Delete** | ユーザー辞書の語は残る（方針。一覧に残ることがある） | |

補足: 7-3 は「予測を選んでいる間」の Delete です。Tab を押す前（`SelectedPrediction = -1`）は 7-5 の動きになります。7-6 はユーザー辞書・同梱の専門用語集を消さない方針の確認です。

### 8.1 2026-10-19 の修正（実機で 7-3 が効かなかった原因）

実機で「Tab で青いバーは動くのに、Fn+Delete で何も消えない」ことを確認した。原因は 2 つ。

1. **Tab が Core に届いていなかった。** `IMKCandidatesSendServerKeyEventFirst` を `window.update()` / `window.show()` の**前**に設定していたため、一覧を出し直した直後は属性が既定（候補ウィンドウが先）に戻り、Tab を候補ウィンドウ自身が処理して 2 番目へ動かしていた。Core は `_predicting` に入らず、Fn+Delete も予測削除の分岐に届かなかった。
   - 修正: `InputController` で属性を `update()` / `show()` の**後**にも設定する（予測・変換の両方）。
2. **azooKey の辞書予測が同じ語を返していた。** `forgetMemory` は学習メモリだけを消し、辞書の語は消さない。`RefreshPredictions` が引き直すと「今日」が復活していた。
   - 修正: `CompositionController` に `_forgottenPredictions` を追加し、Delete した語を**変換エンジンと変換履歴の予測からだけ**除く。ユーザー辞書・専門用語集は残す。
   - Delete の後は `_predicting = false` / `_predictionIndex = -1` に戻す（本家と同じ。次の Delete で別の語を消さない）。

**再確認（trace）**: 次のように trace を ON にして IME を再起動し、7-2〜7-5 を行う。

```bash
defaults write io.github.yksr-melt.inputmethod.Meltype MeltypeTraceIMK -bool true
# IME を再起動してから、TextEdit などで再現する
tail -f ~/Library/Application\ Support/Meltype/imk-trace.log
```

期待:
- Tab: `handle keyCode=48` の行で `consumed=true`、`selectedPrediction=0`。バーは先頭のまま（Core が先頭を選ぶ）。
- Fn+Delete: `handle keyCode=117` の行で `consumed=true`。`conversions.json` の `きょう→今日` が消える。
- 7-4: もう一度 `kyou` と打っても「今日」が出ない（azooKey の辞書予測からも除く。IME を再起動すると戻る）。
- 7-6: ユーザー辞書の「今日」は残る。

Tab の trace が**出ない**／`consumed=false` のままなら、属性がまだ効いていない。次の候補は、候補ウィンドウの選択変更を Core に伝える FFI を足すか、予測用の候補ウィンドウを自前にする（今回はまず属性の再設定で確認する）。

## 9. 結果の記録

各表の「結果」に **OK / NG / 未確認** を書きます。NG のときは、次も書いてください。

- 実際の出力（候補の並び・確定した文字）
- 設定 OFF でも同じか（回帰か、ON のときだけの限界か）
- 直前の文脈（英語の文か日本語の文か）
- 使ったキー（Shift+Space / Fn+Delete など）

| 項目 | 結果 | 実際の出力・メモ |
| --- | --- | --- |
| B2-1 ローマ字表 | | |
| B2-2 大文字の n | | |
| B2-3 長い日本語文 | | |
| B2-4 文脈ごとの学習 | | |
| B2-5 かな + w | | |
| B2-6 クリップボード | | |
| B2-7 予測 Delete | 修正版で再確認中 | 原因は §8.1（Tab が Core に届かない／azooKey の辞書予測が復活する） |

## 10. 判定の目安

- **すべて OK**: B2 完了。
- **回帰の可能性が高い NG**:
  - B2-1 の 1-6〜1-9（`trying`・`highway`・`swan`・`hyena` が英語でなくなった）
  - B2-2 の 2-2 / 2-3（小文字の `pen` が変わった）
  - B2-3 の 3-2 / 3-3（長い英単語が日本語に倒れた）
  - B2-5 の 5-3 / 5-4（英文の中の `saw` が `さw` になった）
  - B2-6 の 6-1（OFF なのにクリップボードが候補に出た）
  - B2-7 の 7-5（未選択の Delete で落ちた・履歴が消えた）
  → 設定 OFF でも再現するか確認し、回帰なら計画どおり**見送り**にします。
- **既知の限界（NG ではなく記録）**:
  - B2-4 の 4-8: 「学習した語」に文脈ラベルが無く、同じ語が 2 行。
  - B2-5 の 5-5: `おnewの新品` が ON のとき壊れる。
  - B2-7 の 7-6: ユーザー辞書・専門用語集の予測は消えない。
  - B2-7: 消した語を変換エンジンの予測から除くのは IME のセッション中だけ。IME を再起動すると辞書の予測は戻る。

## 11. 注意

- B2-2: Mac は **Shift+Space** で「日本語として変換」。JIS の Convert キーは Core では扱いません。
- B2-7: **Fn+Delete**（前方削除）を使う。通常の Delete / Backspace では予測を消せません。
- B2-4: F6 / F10 は、環境によって **Fn+F6 / Fn+F10** が必要です。
- B2-5: `new`・`saw` を英単語として打つ人は OFF のまま。
- B2-6: Concealed の確認には 1Password などが必要です。内容は変換時に 1 回だけ読み、保存・送信しません。
- B2-1: `swan`・`hyena` は実機の NSSpellChecker が知っているため、実機では今までどおり英語になります（同梱の英単語表だけを使うテストとは結果が違います）。
- 検証が終わったら、B2-4・B2-5・B2-6 の設定を **OFF** に戻してください。
- 新しい dylib が読み込まれていない場合は、IME を再起動してからやり直してください（§0）。
