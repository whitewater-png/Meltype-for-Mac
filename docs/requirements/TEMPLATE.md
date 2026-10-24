---
title: <短い題>
status: draft   # draft | approved | done。approved にするのは人
source: <Issue #NN / docs/voice/<日付>.md / 実機の報告>
scope: core     # core (C#) | dictionary | swift (実機必須) | docs
---

# NNN <題>

## 背景 (誰が、何に困っているか)
- 利用者の声をそのまま (個人情報・見られて困る文は書かない)
- 今の挙動: `<打鍵>` → `<結果>` (実機 / Core のどちらで見たか)

## 今の動き (既に分かっていること)
- 関係する場所: `src/Meltype.Core/...`、正解表の行、`docs/KNOWN_LIMITS.md` の該当
- 本家 (yksr-melt/Meltype) の扱い: 取り込み済み / 未取り込み / 本家に無い (分かる範囲で。クラウドからは本家に届かない)

## minimum の要件 (これだけは満たす)
1.
2.

## 非目標 (今回はやらない)
-

## 完成条件 (先に決める。機械で判定できる形)
- [ ] 正解表: `eval/cases/<表>.tsv` に次の行を足し、実装前は外れ、実装後は当たる
  ```
  <打鍵>	<期待>	<full|ascii|nojp>	<出典>
  ```
- [ ] テスト: `src/Meltype.Core.Tests/<File>Tests.cs` に `<名前>` を足す (要点: ...)
- [ ] 回帰なし: `tools/eval-compare.sh` の broken 0、Core のテスト全件通過
- [ ] 文書: `docs/KNOWN_LIMITS.md` / README の該当を直す (必要なら)

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す)
- [ ] TextEdit: `<打鍵>` → `<期待>`
- [ ] ターミナル (zsh): `<打鍵>` → `<期待>`
- [ ] Safari のフォーム: `<打鍵>` → `<期待>`

## 止める条件 (この要件に固有のもの。共通は CLAUDE.md §5)
-

## 記録 (ループが書く)
- 周回数 / モデル / 所要時間 / 手戻りの原因 / PR の URL
