---
description: 要件定義書 (docs/requirements/NNN-*.md) を読み、完成条件を満たすまで 調査 → 実装 → 査読 + 回帰ゲート を回して Pull Request を作る。引数は要件の番号。
argument-hint: <NNN>
---
要件 `$ARGUMENTS` を回す。先に `CLAUDE.md` を読み、§1 の決まり・§5 の止める条件・§6 の測れないことを守る。
時刻を記録する (開始・終了。`docs/AGENT_STATE.md` の記録に使う)。

## 0. 前提の確認 (満たさなければ止める)
- `docs/requirements/$ARGUMENTS-*.md` が 1 つだけあり、`status: approved` である。違えば止めて報告する (人が approved にする)。
- `git status --short` が空で、ブランチが `main` でも `exp/zenzai-eval` でもない (必要なら `git checkout -b feat/$ARGUMENTS-<slug>` を切る)。
- `dotnet` が使える (`dotnet --version`)。使えなければ `.claude/hooks/session-start.sh` の警告を確かめて止める。
- 要件の `scope` が `swift` なら、Core で出来る範囲だけ実装し、Swift の変更は下書きに留めて止める (実機が要る)。

## 1. 調査
`researcher` エージェントに要件定義書のパスを渡し、調査メモを受け取る。メモに「やらない」「限界」の引用があれば、要件と矛盾しないか自分で読み、矛盾すれば止めて報告する。

## 2. 実装 (最大 3 周)
`implementer` エージェントに要件定義書と調査メモを渡す。1 周目は `model: sonnet` (定義どおり)。
**昇格**: 1 周目で完成条件に届かない、または差分が `src/Meltype.Core/Detection/`・`src/Meltype.Core/Composition/` に触れるときは、2 周目から `implementer` を `model: opus` で起動する。
各周の終わりに `reviewer` と `eval-guard` を**並列**で起動し、指摘と数字を受け取る。
- 「止める」の指摘、または broken ≥ 1 → 次の周で直す。3 周目でも残れば止めて報告する。
- 「直す」の指摘 → 次の周で直す。
- 「任意」の指摘 → 直さない理由を PR 本文に 1 行書く。
- 指摘なし + broken 0 + テスト全件通過 + 完成条件の全項目達成 → 3 へ。

## 3. 記録
`doc-keeper` に、周回数・役割ごとのモデルと起動回数・所要時間・手戻りの原因・最新の数字を渡し、`docs/AGENT_STATE.md`・`docs/LESSONS.md`・要件定義書の記録欄を更新させる (`status` はまだ `approved` のまま。`done` にするのは人がマージしたあと)。

## 4. 提出
1. `git push -u origin <ブランチ>`。
2. Pull Request を作る。本文の節: 「要件」(定義書へのリンク) / 「完成条件の達成表」(項目ごとに ✅・未達) / 「テストと回帰ゲートの数字」(eval-guard の出力から) / 「測れていないこと」(CLAUDE.md §6) / 「Mac 実機チェックリスト」(定義書から写す) / 「査読の任意指摘と見送りの理由」。AI を使ったことを 1 行書く。
3. `subscribe_pr_activity` で PR を見張る。CI が落ちたら `implementer` に直させて push し直し、レビューの指摘は `reviewer` の基準で対応する。
4. 人への報告: PR の URL、達成表、実機で確かめてほしいこと、止めた理由 (あれば) を短く。**マージ・版上げ・リリースはしない。**
