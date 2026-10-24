---
title: 短い英単語の直後の「の」が英語に吸われる (apinoerror → apiのerror)
status: approved   # 2026-10-24 リョウさんがループの通し検証の題材として選定
source: 品質テスト (src/Meltype.Core.Tests/QualityTests.cs:140、本家 QualityTests の写し) で外れている例
scope: core
---

# 001 短い英単語の直後の「の」が英語に吸われる

## 背景 (誰が、何に困っているか)
- `apinoerror` と打つと `apiのerror` になってほしいのに、`api` が日本語側に吸われて **`あぴのerror`** になる (品質テストの「混在」カテゴリーで外れている 1 件。本家の期待値も `apiのerror`)。
- 今の挙動 (Core の `--repro apinoerror` / `--segments apinoerror`、2026-10-24): 区間は `JA:apino | EN:error`。`api` は辞書にある英単語だが、ローマ字として最後まで読める (あぴ) ので「英語とも日本語とも読める語」の点数勝負になり、後ろの文脈が常に「日本語」として渡される (`CompositionDetector.cs` の `IsEnglishSpanCore`、817〜836 行付近) ため英語に届かない。
- 訂正 (2026-10-24、researcher の実測): 当初「全体が英語になる」と書いたのは誤り。`--explain` は先頭からの接頭辞の判定だけを出すので、区間分けは `--segments` で見ること (`docs/LESSONS.md`)。
- 似た形: `<ローマ字として最後まで読める短い英単語><助詞><英単語>`。`urlnoerror`・`windowsnoupdate`・`pythonnobug` は英単語側に読めない文字 (`r`・`l`・`w`) があるので別の経路で既に正しく分かれる。問題になるのは `api` のような「読める 3 文字以上の辞書語」。ターミナルや技術文書で「API の error」「URL の設定」のように打つ場面。

## 今の動き (既に分かっていること)
- 期待は既に 2 か所にある: `src/Meltype.Core.Tests/QualityTests.cs:140` (`new("混在", "apinoerror", "apiのerror")`) と `eval/cases/upstream-quality.tsv:106` (`full`)。どちらも今は外れている (品質テスト 1269/1271、upstream-quality 177/203 の外れに含まれる)。
- 関係する場所 (推測。researcher が確かめる): `src/Meltype.Core/Composition/` の英語区間の判定 (既知の英単語 `api` と、その後ろに助詞 `no` が続くときの区切り)。`docs/KNOWN_LIMITS.md` §1 (判定精度) に同種の限界が書かれていないか確認する。
- 本家の扱い: 本家の品質テストにも同じ期待があるので、本家も外れているか、本家で直っているかは未確認 (クラウドからは本家に届かない)。

## minimum の要件 (これだけは満たす)
1. `apinoerror` → `apiのerror` (Core のテスト用キーボードで、`full` 一致)。
2. 既存の正解表・品質テストに回帰を出さない (broken 0、品質テストの外れた例が増えない)。

## 非目標 (今回はやらない)
- 「英単語 + 助詞」の一般化 (`google de kensaku` のような空白入りは別の仕組みで既に動く)。
- `my name is taro` (品質テストのもう 1 件の外れ) は別の要件にする。
- 辞書 (`dictionaries/`) への語の追加で誤魔化さない (判定の仕組み側で直す。辞書で直すなら、その理由を報告する)。

## 完成条件 (先に決める。機械で判定できる形)
- [ ] 正解表: `eval/cases/upstream-quality.tsv:106` の `apinoerror	apiのerror	full` が、実装前は外れ (eval-guard で確認)、実装後は当たる。
- [ ] 正解表: `eval/cases/mac-terminal.tsv` に、`apinoerror` と同じ形 (ローマ字として読める 3 文字以上の辞書の英単語 + `no` + 辞書の英単語) の行を **1〜2 行** 足し、実装前は外れ (`--repro` で確認)、実装後は当たる (出典の列は `docs/requirements/001-api-no-error.md`)。候補は implementer が `--repro` で探す (`urlnoerror` は既に通るので使わない)。
- [ ] 正解表: 守るものとして `urlnoerror\turlのerror\tfull` と `sushigatabetai`・`linewookuru` 相当の既存行が壊れない (broken 0 に含まれる)。
- [ ] テスト: `src/Meltype.Core.Tests/CompositionTests.cs` (または最も近いファイル) に `ShortEnglishWord_FollowedByNo_SplitsParticle` を足す (要点: `apinoerror` の英語区間が `api` と `error` の 2 つに分かれ、間が `の` になる)。
- [ ] 回帰なし: `tools/eval-compare.sh` (base = `ccr-27ad1b0c-qzazzd`) の broken 0、Core のテスト全件通過、品質テストの外れた例が `my name is taro` 以外に増えない。
- [ ] 文書: `docs/KNOWN_LIMITS.md` に該当の限界が書かれていれば、直した旨に更新する。

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す)
- [ ] TextEdit: `apinoerror` + Enter → `apiのerror` (ライブ変換 ON のまま)
- [ ] ターミナル (zsh): `apinoerror` → `apiのerror`。`api` 単体と `error` 単体が英字のままであること
- [ ] TextEdit: `youtubenodouga` → `youtubeの動画` のように、長い英単語 + の でも壊れていないこと (回帰の目視)

## 止める条件 (この要件に固有のもの。共通は CLAUDE.md §5)
- 直すために `dictionaries/` の英単語辞書から `api` を外す必要があるとき (他の判定を壊すので止めて相談)
- 直すと品質テストの「英語」カテゴリーの正解率が下がるとき

## 記録 (ループが書く)
- 周回数 / モデル / 所要時間 / 手戻りの原因 / PR の URL
