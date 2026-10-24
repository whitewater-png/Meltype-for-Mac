# W13-3 — 閉じ括弧の直後の Space が空白を飲む件

2026-10-24。`Japanese(nihongo) input requires too complex IMEs.` を打つと、`Japanese(日本語)input requires too complex IMEs.` になる件の修正。W10 の調査で分けた後続項目。

## 1. 再現と原因

修正前の main `78b9af5` で、同梱の英単語表を使う `--repro` と `CompositionTests.Keyboard` の両方で再現した。

```text
Japanese(nihongo) input requires too complex IMEs.
→ Japanese(日本語)input requires too complex IMEs.
```

`CompositionText` は記号も変換ボックスの単位として保持する。末尾が `)` の入力は英数字ではないため、`CompositionController` の Space 処理が「英語なら確定して空白」の分岐に進まず、日本語の変換を開始していた。この Space は変換の開始に消費され、英文の区切りとしては残らなかった。`]`・`}` も同じ経路だった。

丸括弧・中括弧の表示幅は `CompositionText.Symbol` が設定に従って変えるが、打った記号 (`Raw`) は半角のままなので、表示幅と Space の判定を分けられる。

## 2. 実装

- `CompositionText.EndsWithClosingBracket` を追加し、保留中のローマ字がなく、最後の単位の `Raw` が `)`・`]`・`}` のいずれかのときだけ真にする。表示が全角の `）`・`｝` でも同じ判定になる。
- `CompositionController` の Space 処理で、自動判定モード・固定済み文節なし・末尾が閉じ括弧、かつ**括弧の外に英字がある場合**を追加する (`HasAsciiLetterOutsideBrackets`)。`Japanese(nihongo)` は対象、`(nihongo)` は対象外。
  - 従来の Space 変換と同じ最初の候補を作るが、変換中のままにはせず、その場で確定して半角空白を 1 つ入れる。
  - これにより、報告例は `Japanese(日本語) input ...` になる。
- 設定は追加しない。判定規則・辞書・括弧の幅設定・`SpaceAroundEnglish` の実装は変更しない。

### 適用範囲とトレードオフ (査読 C-1 を反映)

当初は言語に関係なく閉じ括弧の直後の Space を確定にしていたが、日本語だけの括弧 `(kakko)` でも通常の Space 変換が始まらなくなる指摘があった。そこで、**括弧の外に英字がある入力だけ**に絞った。

- `Japanese(nihongo)`・`test[foo]`・`kono (kyou)` は、括弧の外に英字があるので対象。
- `(kakko)` のように括弧の外に英字が無い入力は、従来どおり Space で変換を開始する。したがって `(kakko) desu` は従来どおり `(かっこ)です` になり、空白は残らない (日本語の Space 操作を守るための意図的な残存制限)。
- 明示の **Shift+Space は従来どおり変換を開始し、候補を送れる**。変換後も続けて入力する設定で固定済み文節がある場合、手動で表示モードを変えた場合は新しい分岐を通らない。

空白を打たない `(kakko)desu` は `(かっこ)です` のまま。Esc・Enter の直接入力、2 回目以降の Space の扱いも変えない。

## 3. 回帰テスト

新規 `ParenSpaceTests` (9 件):

1. 報告文全体で `)` の後の空白を保持する。
2. 日本語だけの括弧では従来の Space 変換を残すこと (全角を含む)、英文の括弧で `SpaceAroundEnglish` と併用しても空白が二重にならないこと。
3. `]`・`}`、中括弧の半角設定。
4. `(kakko)desu` のように括弧の直後へ日本語を続ける入力は不変。
5. 英文の括弧で Space 連打では最初の Space だけ確定に使い、後の Space はアプリへ渡す。
6. 閉じ括弧の直後の Enter・Esc は不変。
7. Shift+Space は従来どおり変換を開始する。
8. `ContinueAfterConversion` の固定済み文節があるときは従来の Space 変換のまま。

赤→緑: 報告例のテストを先に走らせ、期待 `Japanese(日本語) input ...` に対して実際 `Japanese(日本語)input ...` で赤を確認した。最初の実装で空白は残ったが変換結果が `にほんご` になったため、従来の最初の候補を確定する形に直して緑にした。`]`・`}` のテストも、分岐を `)` だけにしていた段階で空白欠落により赤を確認してから緑にした。

正解表 `eval/cases/paren-space.tsv` を 12 件追加した。英文・日本語・コードに似た括弧、空白を打たない対照を含む。

## 4. 検証結果

- Core 全テスト: **1077/1077 passed** (main は 1068/1068、今回 +9)。
- `--eval`: **1292/1294 (99.8%)**。変更前と同じ。外れは従来の 2 件 (`my name is taro`、`apinoerror`)。
- 新しい正解表を含む `--expect-all`: **499/615**。`paren-space.tsv` は **12/12**。
- `tools/eval-compare.sh` (base = main `78b9af5` の `git archive` の複製、最終の適用範囲): **489/615 → 497/615、broken 0 / fixed 8 / added 0 / removed 0**。残りの既存表は件数・結果とも不変。
- 追加の前後比較: 閉じ括弧 3 種 × 日本語/英語/コード風の文脈など **731 入力**。same 478 / diff 253。差分 253 件を機械判定し、すべて「閉じ括弧の直後に空白 1 つを足しただけ」(文字の変化・脱落なし) を確認。空白を打たない入力の差分は **0**。

- Mac: `mac/build.sh --no-install` 成功 (`Build complete!`)、`codesign --verify --deep --strict` 成功、辞書アプリ自己テスト **PASS 213 件**、`tools/check-mac-aot-settings.py` は全項目 PASS。

## 5. 実機で確かめる手順 (未実施)

このコミットの `mac/build.sh --no-install` で作ったアプリを、通常の手順で TextEdit から使えるようにして確認する。実機は `NSSpellChecker` を使うため、Core の同梱単語表とは英語判定が違うことがある。

1. `Japanese(nihongo) input requires too complex IMEs.` と打ち、Enter。`Japanese(日本語) input requires too complex IMEs.` となり、`)` と `input` の間に半角空白が 1 つあること。
2. `(kakko)desu` → `(かっこ)です`。`(kakko)` の直後の Space は、日本語だけの入力なので従来どおり変換を開始すること (空白は残らない)。
3. 設定「丸括弧 ( ) の幅」を全角にしても、日本語だけの `(kakko)` + Space は従来どおり変換を開始すること。設定「中括弧 { } の幅」の半角/全角の両方で、英文の括弧の直後の空白が残ること。
4. `test[foo] bar`・`test{foo} bar` で `]`・`}` の直後の空白が残ること。
5. `Japanese(nihongo)` の直後に Space を 2 回打ち、空白が 2 つになること。`(kakko)` の直後の Esc は入力取消、Enter は空白なしの確定のままであること。
6. `(nihongo)` の直後に Shift+Space → 変換候補が開き、Space で候補送りできること。
7. 設定「英単語の前後に半角スペース」の ON/OFF で手順 1 を行い、空白が二重にならないこと。
8. 設定「変換後も続けて入力」を ON にし、`kyou` Space のあと `(nihongo)` Space と打ち、従来どおり未変換部分の候補操作ができること。

## 6. 未確認

- 上記の実機打鍵、および macOS の `NSSpellChecker` を通した判定。
- Windows/Linux は対象外。
- 独立査読は未実施。実装・検証はこの作業内で行った。
