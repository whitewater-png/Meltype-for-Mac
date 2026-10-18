# 実験 2-2 A: ホスト別に「キャレット前の文字」が取れているかを測る手順書

> **決定 2026-10-16: この実験 A はやらない。** 2-2 (前 20 文字の英日比率を判定に使う) が no-go ([EXPERIMENT_2-2_RESULT.md](EXPERIMENT_2-2_RESULT.md)) なので、取れるかを測っても使い道が無い。理由: no-go の項目を実機で測る手間に見合わない (2-4 の保留を閉じたのと同じ判断)。方針を変えるときに必要なら再検討する。下の手順と「結果はまだありません」は、そのときに書いたまま残す。
>
> 行番号は 54a2002 時点 (main に入れたときに書き直していない)。

実機 (リョウさんの Mac) が要る実験です。この文書は手順だけで、結果はまだありません。判定側の実験 B は [EXPERIMENT_2-2_RESULT.md](EXPERIMENT_2-2_RESULT.md) にあります。

**この手順はトレースを ON にし、その間は測定以外も含め、Meltype で打ったすべての打鍵の中身がログに残ります (秘匿入力中・アプリ別 OFF のアプリを除く)。** 始める前に「始める前に」を読み、リョウさんの同意を取ってから進めます。終わったら「後片付け」を最後まで行います。

## 何を測るか

`InputController.surroundingText(of:)` が返す「キャレットの前の最大 20 文字」(`before`) が、アプリごとに次のどれになるか。

| 分類 | 意味 |
|---|---|
| 正しい | 画面でキャレットの前にある文字の末尾 20 文字と一致する |
| 空 | `before=""` (入力欄が空の場所では正しい。文字があるのに空なら「取れていない」) |
| nil | `before=nil` (取れていない) |
| ずれている | 文字は返るが、画面の前文と違う (別の場所・古い内容・途中で欠ける・位置がずれる) |

ずれている が最も悪い: nil なら Core は前文脈なしで判定する (今の動き) が、ずれた文字は誤った向きに判定を傾けます。

## 始める前に: ログに残るものと同意

トレースを ON にしている間、Meltype は **測定の打鍵だけでなく、Meltype を入力ソースにしているすべてのアプリの打鍵** をログに書きます。書くもの (`InputController.swift` の `Self.trace(…)` の呼び出しで確認):

| ログの行 | 中身 |
|---|---|
| `handle keyCode=… flags=… … bundle=…` (L158) | 押したキーの番号と修飾キー、アプリの bundle id。**英数モードの打鍵も 1 キーごとに書く** ので、キーの番号から打った英字を復元できます |
| `surroundingText before=… after=…` (L215) | **キャレットの前後 各 20 字** (変換中でないときの打鍵ごと)。入力欄にもともとある文字も入ります |
| `handleKey … commits=(… orig=… text=…)` (L222)・`apply: insertText text=…` (L632) | **確定した文字** |
| `handleKey … view=text=… clauses=…` (L222)・`showComposition: setMarkedText text=…` (L682) | **変換中の文字** と文節 |

- 文字は 1 項目 40 字で切って書きます (前後の文字は取る時点で各 20 字なので切れません)。
- 打鍵の中身を書かないとき: 秘匿入力中 (パスワード欄などで macOS が `IsSecureEventInputEnabled` にしている間) と、アプリ別設定で Meltype を OFF にしたアプリ (L141-158)。Meltype 以外の入力ソースに切り替えている間は Meltype が呼ばれないので書きません。
- 秘匿入力中は、どの行も書きません (`trace()` の入口 L80 で止まる。下の行も含む)。
- アプリ別 OFF のアプリでは、打鍵の中身でない行は残ります: アプリの切り替え (`activateServer`・`deactivateServer`、L280・L297。アプリの bundle id)、`deactivateServer` から呼ばれる `commitComposition` (L231。選択範囲の位置と長さの数)、入力モードの変更 (`setValue`、L241。bundle id)、マウスの押下 (L137。アプリ別 OFF の判定より前で書く)。打った文字は入りませんが、**いつ・どのアプリを使ったか** は分かります。
- **全ての行の頭に時刻 (時:分:秒.ミリ秒、L86-88) が付きます。** 打鍵の間隔も読み取れます。
- ON の期間は、準備 3 の再ログインから、後片付けの再ログインまでです (「計測が結果を歪める可能性」の ON/OFF の比べで再ログインを挟む間も含む)。**その間の私的なチャット・メール・文書の打鍵もログに残ります。**
- ログは自分だけが読める権限 (0600) で作られます。Time Machine などでバックアップしていれば、ON の間に取られたバックアップにもログが入ります (設定次第。未確認)。

そのため:

1. **始める前に、リョウさんの同意を取ります** (上の範囲を読んでもらってから)。
2. **測定中は、パスワード・暗証番号・API キーなどの秘密を打ちません。** 秘匿入力になるかは欄とアプリ次第で、普通の入力欄に打った秘密はそのまま残り、貼った秘密もキャレットの前後 20 字に入れば残ります。
3. 測定中は私的な入力を避けます (どうしても打つときは、入力ソースを Meltype 以外に切り替えてから)。
4. 終わったら、必ず末尾の「後片付け」を順番どおりに最後まで行います。

## 準備

1. トレースを ON にします。
   ```bash
   defaults write io.github.yksr-melt.inputmethod.Meltype MeltypeTraceIMK -bool true
   ```
2. 記録先は `~/Library/Application Support/Meltype/imk-trace.log` です。古い行を消しておきます (**次の再ログインより前に** 消します。消すのが後だと、ON の確かめに使う行も消えます)。
   ```bash
   : > ~/Library/Application\ Support/Meltype/imk-trace.log
   ```
3. **一度ログアウトしてログインし直します。** 設定は IME のプロセスで最初に使うときに 1 回だけ読む (`InputController.swift` の `private static let trace`) ので、入力メソッドを選び直しても同じプロセスが動き続け、ON にならない見込みが高いです。
4. ON になったかを確かめます。入力メニューで **Meltype** を選び (macOS 標準の「日本語 (ローマ字)」ではありません)、アプリ別 OFF にしていない普通の入力欄 (TextEdit など) で 1 打鍵し (Esc で取り消し)、次を実行します。
   ```bash
   grep 'trace start' ~/Library/Application\ Support/Meltype/imk-trace.log
   ```
   `trace start <版> <bundle id>` の行が出れば ON です。**出なければ測定を始めません** (ON になっていないとログは 1 行も出ず、結果を「全部 nil」と取り違えます)。この行はプロセスごとに 1 回しか出ないので、確かめたあとはログを消さずに続けます (以降は `=== APP P` の目印で区切ります)。
5. 前文は **打たずに貼り付け** ます。Meltype は自分の確定から 1.5 秒以内は入力欄の文字を読まず、自分が確定した文字で代用するので、打った前文では入力欄から取れたかが分かりません。貼る文は下の 5 種で、**私的な文は使いません** (貼った文は、キャレットの前後 各 20 字としてログに残ります。ほかに残るものは「始める前に」の表)。

## 前文の 5 種 (パターン)

| 記号 | 入力欄に貼る文 (末尾にキャレット) | 見たいこと |
|---|---|---|
| P1 | `今日は晴れです。` | 日本語の後 |
| P2 | `I want to go to the ` (末尾に半角スペース) | 英文の後 |
| P3 | `今日は GitHub ` (末尾に半角スペース) | 日本語の中の英単語の後 |
| P4 | (空欄) | 空の入力欄 |
| P5 | `1 行目です。` + 改行 (キャレットは 2 行目の行頭) | 行頭 |

## 1 回の試し方 (アプリ × パターンごとに)

1. 目印をログに足します (ターミナルは別ウィンドウで)。`APP` と `P` は書き換えます。
   ```bash
   echo "=== APP P1" >> ~/Library/Application\ Support/Meltype/imk-trace.log
   ```
2. 対象アプリの入力欄に前文を貼り、キャレットを末尾に置きます。
3. 入力メニューで **Meltype** が選ばれていることを確かめてから (macOS 標準の「日本語 (ローマ字)」や「英数 (Meltype)」ではありません)、**英字 1 つだけ** (`s`) 打ちます (英数モードだと `s` は変換中にならず、下の「2 打鍵目」の説明と合いません)。そのあと Esc で取り消し、**何も確定しません**。
4. 次を実行して、貼る材料を出します。
   ```bash
   awk '/=== APP P1/{f=1} f' ~/Library/Application\ Support/Meltype/imk-trace.log | grep -E '===|handle keyCode|surroundingText' | head -6
   ```

## ログのどの行を見るか

- 見るのは **1 打鍵目** の `surroundingText before=… after=…` の行だけです。`handle keyCode=1 … hasMarkedText=false … bundle=…` の行 (keyCode 1 = `s` のキー) の直後にあります。貼り付けの Cmd+V がホストによって IME に届き、`handle keyCode=9 …` (= `v`) の行が先に出ることがある (未確認) ので、keyCode=1 の行で探します。
- **2 打鍵目以降 (`hasMarkedText=true`) の `before=nil after=nil` は無視します。** 変換中は読まない作りなので、取れていない証拠ではありません (`InputController.swift` の `hasMarkedText ? (nil, nil) : surroundingText(of:)`)。
- `handle` の行の `selected={位置, 長さ}` も写します。`selected={0, 0}` で文字があるのに前文が空なら「アプリが範囲を返していない」手がかりです。
- 表示は 40 文字で切れ、改行は `\n` と出ます。

## リョウさんが貼る形式

アプリごとに、次の 5 行 (パターンごとに 1 行) を貼ってください。ログの該当 2 行をそのまま貼る形でも構いません。

```
アプリ | パターン | 画面の前文 | before=(ログのまま) | selected=(ログのまま) | 分類(正しい/空/nil/ずれている) | 一言
TextEdit | P1 | 今日は晴れです。 | "今日は晴れです。" | {8, 0} | 正しい |
```

対象は [HOST_COMPAT.md](HOST_COMPAT.md) の 9 アプリ (TextEdit・Hermes のチャット欄・LibreOffice Writer・Chrome の入力欄・Google ドキュメント・Discord・Terminal・VS Code・Slack)。全部が無理なら、日常で使う順に、少なくとも P1・P2・P3 だけでも。

## 判定の目安 (事前に決めておく)

- **go の目安**: 日常で使う主要ホストの過半で、P1〜P3 が「正しい」。
- **no-go の目安**: 「ずれている」が複数のホストで出る。このときは前文脈を強める前に、ずれの出るホストでは前文脈を使わない対策が先です。
- 「nil」は縮退が今も安全なので、no-go の理由にはなりません (前文脈の恩恵を受けられないだけ)。

## 計測が結果を歪める可能性 (損)

- トレース ON のとき、`handle` の行などで `selectedRange` / `markedRange` を **追加で** 呼びます (`InputController.swift` の `ranges(_:)`)。コードのコメントは「呼び出しがアプリの状態を変える疑い」と書いています。`surroundingText` の読み取り自体は OFF でも同じく行います。
- 確かめ方: 各アプリで HOST_COMPAT の打鍵 2 (`kyouhameetingdesu`) を、トレース ON のときと OFF のときで 1 回ずつ打ち、結果が同じかを見ます。違えば、トレース ON の結果は通常状態を表しません。ON と OFF の切り替えも、そのたびに再ログインが要ります (準備 3)。

## 後片付け (必ずこの順で)

OFF は再ログインのあとでしか効きません。先にログを消すと、ログアウトまでの打鍵でファイルが作り直されます (`appendTrace` はファイルが無ければ作る)。ログは 5MB を超えると `imk-trace.log.old` に回るので、2 つとも消します。

1. OFF にします。
   ```bash
   defaults delete io.github.yksr-melt.inputmethod.Meltype MeltypeTraceIMK
   defaults read io.github.yksr-melt.inputmethod.Meltype MeltypeTraceIMK   # 「does not exist」と出れば OFF
   ```
2. **ログアウトしてログインし直します** (IME のプロセスが設定を読み直し、OFF になります)。
3. ログを 2 つとも消します。
   ```bash
   rm -f ~/Library/Application\ Support/Meltype/imk-trace.log ~/Library/Application\ Support/Meltype/imk-trace.log.old
   ```
4. 確かめます。入力メニューで **Meltype** を選び (macOS 標準の「日本語 (ローマ字)」では打鍵が Meltype に届かず、OFF が効いていなくてもファイルが作られないため確認になりません)、アプリ別 OFF にしていない普通の入力欄 (TextEdit など) で 1 打鍵し (Esc で取り消し)、次を実行します。
   ```bash
   ls -l ~/Library/Application\ Support/Meltype/imk-trace.log ~/Library/Application\ Support/Meltype/imk-trace.log.old
   ```
   2 つとも「No such file or directory」なら完了です。ファイルがあれば OFF が効いていないので、1 から繰り返します。
5. 結果として貼った行 (「リョウさんが貼る形式」) は前文の 5 種だけのはずです。ほかの文字が混ざっていないかを見てから共有します。

## 未確認

- 設定は IME のプロセスで 1 回だけ読むため、再ログインを確実な手順にしました。入力メソッドの選び直しや IME のプロセスの終了で足りるかは試していません (足りるかどうかは、準備 4 の `trace start` の行で分かります)。
- Terminal.app は貼り付けた文が「入力欄の中身」として IME に見えるか不明です (シェルのプロンプトの文字は IME から読めない可能性がある)。P4 と他を見て分類します。
- 貼り付けの Cmd+V が IME の `handle` に届くかはホスト次第で、試していません (Cocoa のアプリではメニューが先に処理するので届かない見込み)。届いても、1 打鍵目を keyCode=1 の行で探せば取り違えません。
- 「始める前に」の表は、コードを読んで書いたものです (トレースを実際に ON にして中身を確かめてはいません)。
