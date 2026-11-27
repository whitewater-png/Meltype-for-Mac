---
title: 記号だけ (#・//) を打って Space を押すと、空白が消える (# parseJSONtoObject → #parseJSONtoObject)
status: approved
source: 要件 050 の実機確認 (2026-11-26、ターミナル。2026-11-27 リョウさん「要件立てて調査して」)
scope: core
---

# 055 記号だけを打った後の Space で空白が消える

## 背景 (誰が、何に困っているか)
- ターミナル (Mac、main 91fef24、自動打鍵) で `# parseJSONtoObject` と打つと、`#parseJSONtoObject` になった。語 (parseJSONtoObject) は正しいが、# の後ろの空白が消えた。
- シェルのコメント・Markdown の見出し (`# 見出し`)・C 系のコメント (`// メモ`) を打つ人が困る。

## 今の動き (既に分かっていること)
- 2026-11-27 にクラウドの Core (main ecbb495) で、テスト用キーボードで 1 文字ずつ打って調べた。

| 打鍵 | コード系アプリ | 一般アプリ |
|---|---|---|
| `# parseJSONtoObject` + Enter | `#parseJSONtoObject` | `#parseJSONtoObject` |
| `# hello` + Enter | `#hello` | `#hello` |
| `# kyouha` + Enter | `#きょうは` | `#きょうは` |
| `// hello` + Enter | `//hello` | `//hello` |
| `- hello` + Enter (対照) | `- hello` | `- hello` |
| `echo hello` + Enter (対照) | `echo hello` | `echo hello` |
| `## hello` + Enter | `##hello` | `##hello` |
| `> hello` + Enter | `＞hello` | `＞hello` |
| `* hello` + Enter | `＊hello` | `＊hello` |
| `kyou# hello` + Enter | `今日#hello` | `今日#hello` |

- Core で再現する (実機だけの問題ではない)。
- 打っている途中の見え方: `#` → Space の後も `#` が入力中のまま残り、次の `p` で `#` だけが確定する (空白は入らない。テスト用キーボードの表示で確認)。
- `>`・`*` は空白が消えるうえ、設定「全角の記号」(テスト用キーボードの既定は ON) で全角になる。`#`・`/` はもともと半角に固定 (`CompositionText.cs` 1764〜1766 行付近)。
- Shift+Space は今も、記号だけの入力でも変換を始める (`CompositionController.cs` 1192 行付近)。
- 原因 (コードを読んだ範囲): `CompositionController` の Space の分岐 (1202 行付近)。記号だけの入力は「英語で終わっている」「英数字」のどちらにも当たらないので、最後の `StartConversion()` (変換を始める) に行く。Space が変換のキーとして使われ、空白にならない。
- `-` だけは `KeepLoneHyphen` の分岐 (1198 行付近、箇条書きの `- `) で「確定して空白」にしているので消えない。`#`・`//` には同じ分岐が無い。
- 本家 (yksr-melt/Meltype) の扱い: 未確認 (クラウドからは本家に届かない)。

## minimum の要件 (これだけは満たす)
1. 入力が ASCII の記号だけ (`#`・`##`・`//`・`>`・`*` など。範囲は実装の前に決める) で Space を押したら、記号と空白で確定する (`- ` と同じ)。
2. 全角にしたい記号の変換 (記号の候補を出す) が今 Space でできているなら、その使い方を残すかを人が決める (下の「決めること」)。
3. 語の後ろの記号 (`kyou#`) や、記号 + 文字のあとの Space は今までどおり。

## 決めること (人の判断)
- 記号だけの Space で変換 (＃・♯ などの候補) を使っている人がいるか。いるなら、コード系アプリだけにするか (Shift+Space は今も変換になるので、そちらに任せてもよい)。
- `>`・`*` などは、設定「全角の記号」どおりの幅にするか、常に半角にするか。

### 決めたこと (2026-11-27、リョウさんがスレッドの選択カードで「いつも半角で確定」を選んだ。選べば承認として進めると伝えた上での選択)
- 記号だけ + Space は、設定「全角の記号」に関係なく**半角の記号と空白で確定**する。＃・＠ などの記号の候補は **Shift+Space** で出す (既存の 3 テストを Shift+Space に変えた)。
- 対象外 (今までどおり Space で変換): `, . [ ] ( ) { } ! ? ~ ' "`、記号以外の表示になった単位 (日本語の後ろの `/` → ・、`--` → ーー)、固定した文節がある入力、F7 などで表示を変えた入力。
- Tab で変換 (設定 ON) も、`-` と同じく記号だけなら変換を始めない (確定して Tab をアプリに渡す。査読の指摘で揃えた)。

## 非目標 (今回はやらない)
- `kwa kwi` のような日本語の文の途中の空白 (Space は変換のキー。この fork の設計)。

## 完成条件 (先に決める。機械で判定できる形)
- [ ] 正解表: `eval/cases/` に `// hello`・`> hello` など `#` で始まらない行を足し、実装前は外れ、実装後は当たる (`#` で始まる行は表の形式ではコメントとして読み飛ばされるので、テストだけで見る。`eval/README.md`)。一般アプリの表と、コード系アプリの表 (`# app: code`) を分ける。守る行: `- hello`・`kyou# hello`・`echo hello`
- [ ] テスト: `src/Meltype.Core.Tests/` に `Req055_...` を足す (`# hello`・`## hello`・`# parseJSONtoObject` を含む。コード系アプリと一般アプリの両方)
- [ ] 回帰なし: `tools/eval-compare.sh` の broken 0、Core のテスト全件通過

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す)
- [ ] ターミナル (zsh): `# parseJSONtoObject` → `# parseJSONtoObject`
- [ ] TextEdit: `# midashi` → `# みだし`
- [ ] VS Code: `// memo` → `// ` の後ろに空白が残る (`memo` がかな・メモになるのは語の判定で、この要件の外。Core では `// めも`)
- [ ] TextEdit: `@` → Shift+Space で ＠ の候補が出る
- [ ] Tab で変換を ON にして、`#` + Tab で # が確定して Tab がアプリに渡る

## 止める条件 (この要件に固有のもの。共通は CLAUDE.md §5)
- 記号の候補の変換を使う形が壊れて、規則の狭め方で残せない。

## 記録 (ループが書く)
- 2026-11-27 要件を立てて Core で原因を調べた (draft)。実装はまだ。
- 2026-11-27 承認 (上の「決めたこと」)。`CompositionText.KeepSymbolsOnly` と Space・Tab の分岐を足した (ブランチ claude/project-thread-rsfj8l)。Core のテスト全件通過、回帰ゲート broken 0 (新しい 2 表は 3/7 → 7/7)、品質テストは base と同じ (査読員が確認)。実機は未確認。
