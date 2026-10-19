# B2-7 予測候補の Delete 実機検証表（2026-10-19）

対象: B2-7（本家 #788）予測候補を選んでいる間の Delete で学習から消す
修正: `f810b64`（Tab の配送と azooKey 辞書予測の復活） / テスト: `f539c96`

計画: [UPSTREAM_B2_PLAN.md](UPSTREAM_B2_PLAN.md) / 全体のチェックリスト: [B2_DEVICE_CHECK.md](B2_DEVICE_CHECK.md) §8

## 0. 前提（インストール後）

- インストール先: `~/Library/Input Methods/Meltype.app`
- インストール済み dylib の SHA-256: `（下の「入れ替え結果」に記入）`
- IME の起動時刻が dylib の更新時刻より後であること。前なら IME を再起動する。
- 入力先: TextEdit の**新規書類**。検証用の書類は保存せずに閉じる。
- 予測変換は ON（既定）。
- 履歴を消す操作をするので、必要なら先に `conversions.json` を控える:

```bash
cp "$HOME/Library/Application Support/Meltype/conversions.json" /tmp/conversions.before-b2-7.md
```

## 1. 入れ替え結果

| 項目 | 結果 |
| --- | --- |
| `mac/build.sh` の完了 | 2026-10-19 16:20 完了（build/install 成功） |
| dylib の SHA-256 | `680eecd119b1eb042c06564d094851b78f804affa950184f5529146fb510b92e` |
| dylib の更新時刻 | 2026-10-19 16:20:21 |
| IME の起動時刻（更新時刻より後か） | 2026-10-19 16:20:32（新しい dylib） |
| trace の ON/OFF | OFF（`MeltypeTraceIMK=0`。trace を見るときだけ ON にする） |

## 2. 検証表

結果は **OK / NG / 未確認** で記入してください。NG のときは、実際の候補の並びと、trace の該当行をメモしてください。

| No. | 操作 | 期待 | 結果 | メモ（実際の候補・trace） |
| --- | --- | --- | --- | --- |
| 1 | `kyou` と打つ | 予測ウィンドウが出る | | |
| 2 | **Tab** を押す | 青いバーが先頭の候補を選ぶ。trace が `keyCode=48` で `consumed=true`、`selectedPrediction=0` | | |
| 3 | そのまま **Fn+Delete** | 選んだ候補が一覧から消える。trace が `keyCode=117` で `consumed=true`、`selectedPrediction=-1` | | |
| 4 | **Esc** を押す | 変換中の文字が消える（確定して学習し直さない） | | |
| 5 | `kyouha` と打つ | `今日は寿司` / `今日はメエ` / `今日は会議` / `今日は` / `今日はいい天気` などが出る（履歴） | | |
| 6 | **Tab** を押して、バーを `今日は` に合わせる | `今日は` が選ばれる（何番目かは一覧を見て合わせる。上の例では 4 番目） | | |
| 7 | **Fn+Delete** | `今日は` が一覧から消える | | |
| 8 | **Esc** を押してから書類を閉じる | `conversions.json` から `きょうは` が消えている（閉じる前に Esc を押さないと、閉じたときの確定で学習し直される） | | |
| 9 | もう一度 `kyouha` と打つ | `今日は` が一覧に出ない（IME を再起動するまで） | | |
| 10 | `kyou` と打ち、**Tab を押さずに Fn+Delete** | 例外にならない。履歴は消えない（選んでいないため） | | |
| 11 | `kyou` と打ち、**Tab** で先頭（例: `脅威`）を選び **Fn+Delete** | 専門用語集の語は消えない（方針）。一覧に残る | | |
| 12 | ユーザー辞書に `今日`（読み `きょう`）を登録して予測で選び **Fn+Delete** | ユーザー辞書の語は残る（方針） | | |

### 補足（重要）

- **`kyou` の予測は同梱の専門用語集 8 件で埋まることがあります**（`脅威` / `脅威アクター` / … / `境界防御`）。この場合、履歴の `今日` は 8 件の外に出て一覧に表示されません。**履歴の Delete は `kyouha` の `今日は` で確認してください。**
- 用語集・ユーザー辞書の語は Delete の対象外です。一覧に出ている語が用語集由来かどうかは、候補の並び（`脅威` など）で判断してください。
- Delete 後に **書類を閉じる／アプリを切り替える**と、未確定の読みが確定されて履歴に学習し直されます。履歴から消えたことを確認するときは、必ず先に **Esc** を押してください。
- エンジンの辞書予測の抑制（`_forgottenPredictions`）は **IME のセッション中だけ**です。IME を再起動すると、azooKey の辞書予測は戻ります。

## 3. trace の確認方法

```bash
defaults write io.github.yksr-melt.inputmethod.Meltype MeltypeTraceIMK -bool true
# IME を再起動してから、上の表を実施
tail -f "$HOME/Library/Application Support/Meltype/imk-trace.log"
```

見る行:

- Tab: `handle keyCode=48 ... consumed=true ... selectedPrediction=0`
- Fn+Delete: `handle keyCode=117 flags=10485760 ... consumed=true ... selectedPrediction=-1`
- Delete 後の `view=... predictions=[...]` に、消した語が入っていない

Tab の `handle keyCode=48` が**出ない**、または `consumed=false` のときは、候補ウィンドウがキーを取っています。そのときは trace の前後 20 行を添えて報告してください。

## 4. 履歴ファイルの確認

```bash
python3 - <<'PY'
import json, os
p = os.path.expanduser("~/Library/Application Support/Meltype/conversions.json")
d = json.load(open(p))
for k in ("きょうは", "きょう"):
    print(k, d.get(k))
PY
```

`きょうは` が `None` なら履歴から消えています。`きょう` は別の履歴なので、`kyouha` の検証では消えません。

## 5. 検証後の後片付け

```bash
defaults write io.github.yksr-melt.inputmethod.Meltype MeltypeTraceIMK -bool false
# 必要なら履歴を戻す
# cp /tmp/conversions.before-b2-7.md "$HOME/Library/Application Support/Meltype/conversions.json"
```

- 検証用の TextEdit 書類は保存せずに閉じる。
- trace を ON にしたままにしない（打った内容が `imk-trace.log` に残ります）。

## 6. 結果のまとめ

| 項目 | 結果 | メモ |
| --- | --- | --- |
| キー配送（No.2〜3） | | |
| 履歴の Delete（No.5〜8） | | |
| 打ち直し（No.9） | | |
| 未選択 Delete（No.10） | | |
| 用語集・ユーザー辞書（No.11〜12） | | |
| 総合 | | |
