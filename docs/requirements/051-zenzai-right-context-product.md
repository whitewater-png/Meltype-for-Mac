---
title: Zenzai の変換に右文脈 (カーソルの後ろの文字) を渡す
status: approved   # draft | approved | done。approved にするのは人
source: 要件 049 の測定結果 (docs/ZENZAI_V32_RIGHT_CONTEXT_2026-11-26.md)。2026-11-26 リョウさんが「別の要件で進める」を選んだ
scope: swift (実機必須)
---

# 051 Zenzai の変換に右文脈を渡す

## 背景 (誰が、何に困っているか)
- 文の途中を書き直すとき、今の Zenzai は前の文字しか見ないので、後ろの文字で決まる同音異義語を取り違えうる。
- 要件 049 の測定 (Mac、v3.2-small、306 行): 右文脈なし 271 → あり 287 (McNemar p = 5.2e-3)。ただし:
  - 人が書いた 62 行 (70 文から作った行) では差が無い (4 対 5)。
  - 悪くなった行が 7 行ある (公園→講演、髪→紙 など)。
  - 表は文を機械的に切ったもので、実際の書き直しとは性質が違う。
- 文の最後に打ち足す場面 (いちばん多いと見ている。推測) では後ろに文字が無いので、変わらない。

## 今の動き (既に分かっていること)
- 入力欄の後ろの文字はもう読んでいる: `InputController.swift` の `surroundingText(of:)` がキャレットの前後 20 文字を読み、`NativeCore.handleKey(..., before:, after:)` で Core の `MeltypeSession.HandleKey` に渡す (未確定の文字があるときは読まない)。
- 変換エンジンへは左文脈だけ: Core → `src/Meltype.Mac.Native/Exports.cs` の `ConvertClauses(hiragana, context)` → Swift の `MeltypeConverter.clauses(for:context:)`。右文脈の引数は無い。
- 計測の口は要件 049 で足した `MeltypeConverter.measurementRightContext` (計測だけが入れる)。
- Core が変換エンジンを呼ぶ所は 1 つではない (`src/Meltype.Core/Composition/CompositionController.cs`):
  - `ConvertWithContext` は、左文脈ありとなしで文節の区切りが違えば、左文脈なしの結果を使う。
  - 50 文字を超える読みは区切って変換し (`_chunkContext`)、区切りの右に来るのは続きの読みで、入力欄の `after` ではない。
  - `EngineClauses` のキャッシュの鍵は `(kana, context)` で、右文脈が入っていない。
  - 広い文脈 (`WideContextOf`) は、左文脈の最後の改行より後ろだけを使う。
- FFI の版は `src/Meltype.Mac.Native/Exports.cs` の `AbiVersion = 13`。Swift と食い違うと `NativeCore.swift` が初期化を止める。
- `IKanjiConverter.ConvertClauses` は Windows 側 (`src/Meltype/Composition/KanjiConverter.cs`・Mozc・Hybrid) と共通。
- 本家 (yksr-melt/Meltype) の扱い: 未確認 (クラウドから届かない)。

## minimum の要件 (これだけは満たす) (案。人が直してよい)
1. Core が受け取った `after` を、変換の要求に右文脈として渡す (Core → FFI → Swift の `clauses`)。左文脈と同じく、Zenzai が ON のときだけ効く。
   - 渡すのは、読みの最後の区切り (長い読みを区切ったときは最後の区切りだけ) の要求。
   - `ConvertWithContext` の「左文脈あり / なし」の比べ合いでは、両方の要求に同じ右文脈を付ける (付けないと、049 の Z039「は市の → 橋の」のような区切りの変わる改善が消えうる。推測)。
   - `EngineClauses` のキャッシュの鍵に右文脈を入れる。
2. 右文脈が空 (文の最後に打つとき) は、今と同じ要求になる。
3. FFI は、Windows と共通の `IKanjiConverter` を変えずに、Mac 用の口 (Mac の変換エンジンだけが受け取る右文脈) にする。
   - 実装では `AbiVersion` を上げず、既存の `meltype_set_reader`・`meltype_set_forget_conversion` と同じ「別の入口 (`meltype_set_right_clauses`)」にした。古い dylib と新しい IME・新しい dylib と古い IME のどちらの組み合わせでも、右文脈を渡さないだけで今までどおり動くため (版を上げると、食い違ったときに入力が止まる)。
4. 設定で切れるようにするかは人が決める (判断事項 1)。

## 人が決めること (2026-11-26 リョウさんが「おすすめで始める」を選んだ)
1. 設定: 足さない。Zenzai ON (広い文脈 ON) なら使う (Zenzai を切れば元に戻る)。
2. 右文脈の長さと形: キャレットの後ろの先頭 20 単位で、最初の改行で切る。英字で始まる・空白だけのときは渡さない。
3. 人が書いた文での確認: マージ前に、リョウさんが実機で文の途中を 10 文ほど書き直して確かめる (リポジトリには入れない)。

## 非目標 (今回はやらない)
- 入力欄から読む文字数を変えること
- 候補ウィンドウ (`candidates(for:)`) と予測 (`predictions`) に右文脈を渡すこと (049 で測っていない)
- Windows 側 (`src/Meltype`) の変換
- 打った文字をネットワークに送ること (変換は Mac の中だけ)

## 完成条件 (先に決める。機械で判定できる形)
- [ ] 計測: AJIMEE 200・Flash 40 件 (右文脈なし) で、変更前と第一候補が全行同じ
- [ ] 計測: 049 の表を Swift の口 (`--convert-eval`。`measurementRightContext` ではなく、製品で足す `clauses` の右文脈の引数を通す) で流し、右文脈ありが 287/306 以上。Core の経路 (区切り・左文脈の比べ合い) の差はこの道具では測れないので、「測れていないこと」に書く
- [ ] Core のテスト: `src/Meltype.Core.Tests/CompositionTests.cs` に足す。要点: (1) `after` が最後の区切りの変換の要求に右文脈として渡る、(2) 空なら渡らない (要求が今と同じ)、(3) 左文脈あり / なしの両方の要求に同じ右文脈が付く、(4) 右文脈が違えばキャッシュに当たらない
- [ ] FFI: 別の入口 `meltype_set_right_clauses` を足し (`AbiVersion` は 13 のまま。minimum 3 の理由)、Mac で `mac/build.sh` と `tools/check-mac-aot-settings.py` が通る
- [ ] 回帰なし: Core のテスト全件通過、`tools/eval-compare.sh` の broken 0

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す)
- [ ] TextEdit: 「でご飯を食べる」の前にカーソルを置いて `hashi` → 箸 が出る (Zenzai ON。049 で良くなった形。外れても失敗にせず記録する)
- [ ] TextEdit: 文の最後に打つときの変換が今と変わらない
- [ ] ターミナル・Safari のフォームで、後ろの文字が読めないときも今と同じに動く

## 止める条件 (この要件に固有のもの。共通は CLAUDE.md §5)
- この要件は CLAUDE.md §5 (Swift の変換の経路 `Converter.swift`・`NativeCore.swift` と FFI) にかかる。approved にすること自体が、人がそれを認めたという意味
- Windows 側 (`src/Meltype`) を変えないと済まないとき
- `InputController.swift` (IMK の入力経路) を変えたくなったとき (読む範囲は今のままのはず)
- FFI の形 (`Exports.cs` の関数の引数) を変えるので、Swift と NativeAOT の両方のビルドが Mac で通るまで提出しない
- トレースログ (`MeltypeTraceIMK`) が要りそうになったとき (取る前に同意を得る)

## 記録 (ループが書く)
- 2026-11-26: リョウさんが承認 (「051 承認」)。status を approved にした。
- 2026-11-26: 人が決めること 1〜3 をおすすめで決定。実装: Core (`IRightContextConverter`・`RightConversionContext`)、FFI (`meltype_set_right_clauses`)、Swift (`clauses(for:context:rightContext:)`。049 の `measurementRightContext` は消し、`--convert-eval` も同じ口を使う)。クラウドで Core 1384/1384、eval-compare broken 0。Swift のビルドと測定は Mac で未確認。
- 2026-11-26: 下書き (要件 049 の結果から)。
