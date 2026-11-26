---
title: Zenzai の変換に右文脈 (カーソルの後ろの文字) を渡す
status: draft   # draft | approved | done。approved にするのは人
source: 要件 049 の測定結果 (docs/ZENZAI_V32_RIGHT_CONTEXT_2026-11-26.md)。2026-11-26 リョウさんが「別の要件で進める」を選んだ
scope: swift (実機必須)
---

# 051 Zenzai の変換に右文脈を渡す

## 背景 (誰が、何に困っているか)
- 文の途中を書き直すとき、今の Zenzai は前の文字しか見ないので、後ろの文字で決まる同音異義語を取り違えうる。
- 要件 049 の測定 (Mac、v3.2-small、306 行): 右文脈なし 271 → あり 287 (McNemar p = 5.2e-3)。ただし:
  - 人が書いた 62 文では差が無い (4 対 5)。
  - 悪くなった行が 7 行ある (公園→講演、髪→紙 など)。
  - 表は文を機械的に切ったもので、実際の書き直しとは性質が違う。
- 文の最後に打ち足す場面 (いちばん多い) では後ろに文字が無いので、変わらない。

## 今の動き (既に分かっていること)
- 入力欄の後ろの文字はもう読んでいる: `InputController.swift` の `surroundingText(of:)` がキャレットの前後 20 文字を読み、`NativeCore.handleKey(..., before:, after:)` で Core の `MeltypeSession.HandleKey` に渡す (未確定の文字があるときは読まない)。
- 変換エンジンへは左文脈だけ: Core → `src/Meltype.Mac.Native/Exports.cs` の `ConvertClauses(hiragana, context)` → Swift の `MeltypeConverter.clauses(for:context:)`。右文脈の引数は無い。
- 計測の口は要件 049 で足した `MeltypeConverter.measurementRightContext` (計測だけが入れる)。
- 本家 (yksr-melt/Meltype) の扱い: 未確認 (クラウドから届かない)。

## minimum の要件 (これだけは満たす) (案。人が直してよい)
1. Core が受け取った `after` を、変換の要求に右文脈として渡す (Core → FFI → Swift の `clauses`)。左文脈と同じく、Zenzai が ON のときだけ効く。
2. 右文脈が空 (文の最後に打つとき) は、今と同じ要求になる。
3. 設定で切れるようにするかは人が決める (判断事項 1)。

## 人が決めること
1. 設定 (ON/OFF) を足すか、Zenzai ON なら常に使うか。
2. 右文脈の長さ (入力欄から読むのは 20 文字。azooKey の `maxRightSideContextLength` で切るか)。
3. 製品に入れる前に、人が書いた文での確認を足すか (例: リョウさんが実機で文の途中を書き直す 10〜20 文。リポジトリには入れない)。

## 非目標 (今回はやらない)
- 入力欄から読む文字数を変えること
- 打った文字をネットワークに送ること (変換は Mac の中だけ)

## 完成条件 (先に決める。機械で判定できる形)
- [ ] 計測: AJIMEE 200・Flash 40 件 (右文脈なし) で、変更前と第一候補が全行同じ
- [ ] 計測: 049 の表で、製品の経路 (Core から渡した右文脈) の結果が 049 の「あり」と同じ水準 (287/306 前後)
- [ ] Core のテスト: `after` が変換の要求に渡ること、空なら渡らないこと
- [ ] 回帰なし: Core のテスト全件通過、`tools/eval-compare.sh` の broken 0

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す)
- [ ] TextEdit: 「〜に乗って帰った」の前にカーソルを置いて `kisha` → 汽車 が先に出る (Zenzai ON)
- [ ] TextEdit: 文の最後に打つときの変換が今と変わらない
- [ ] ターミナル・Safari のフォームで、後ろの文字が読めないときも今と同じに動く

## 止める条件 (この要件に固有のもの。共通は CLAUDE.md §5)
- `InputController.swift` (IMK の入力経路) を変えたくなったとき (読む範囲は今のままのはず)
- FFI の形 (`Exports.cs` の関数の引数) を変えるので、Swift と NativeAOT の両方のビルドが Mac で通るまで提出しない
- トレースログ (`MeltypeTraceIMK`) が要りそうになったとき (取る前に同意を得る)

## 記録 (ループが書く)
- 2026-11-26: 下書き (要件 049 の結果から)。
