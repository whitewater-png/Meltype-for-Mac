# 引き継ぎ兼作業計画書: 「声の収集」定期タスクの作り直し (手元の Mac で行う)

> 作成 2026-10-24 (クラウドのセッション)。基準コミット: `main` の `61bbea9`。
> **この文書を読んだ人 (または Mac 上の Claude Code セッション) が、そのまま作業できる**ことを目的に書いた。
> 推定は「(推定)」、確かめていないことは「(未確認)」と書く。claude.ai の画面の文言は、クラウドのセッションからは見えないので推定を含む。

## 0. 30 秒で分かる現状
| 項目 | 状態 |
|---|---|
| 何を作りたいか | 週 1 回、GitHub の Issue と Web の声を集めて `docs/voice/<日付>.md` に要約し、ブランチ `voice/<日付>` に push するだけの定期タスク (PR・返信・投稿はしない) |
| 何が出来ているか | プロンプトは完成 (`docs/voice/ROUTINE_PROMPT.md`)。1 回目の試行で、文書の中身自体は作れることを確認済み (Web 検索の結果、候補 3 件) |
| 何が出来なかったか | クラウドのセッションの `create_trigger` ツールで作った定期タスクは、**起動するセッションにリポジトリも GitHub 連携も付かない** (`git push` と Issue 取得が 403、`add_repo` ツールも無い)。2 回試して同じ結果 |
| 今の状態 | その定期タスク (`trig_01Yb1BPs2jLaeDBh73iUVt6R`「Meltype 声の収集 (週次)」) は **無効化済み**。消してよい |
| 作業者の決めること | 下の「経路 A / B / C」のどれで動かすか |

## 1. 経路の選択 (おすすめ順)
| 経路 | 方法 | 利点 | 欠点 | 所要 (推定) |
|---|---|---|---|---|
| **A** (おすすめ) | claude.ai の**定期タスク (ルーティン) の画面**から作る。リポジトリと GitHub 連携を選べる (推定) | 完全に放置できる。クラウドで動く | 画面の操作が要る。リポジトリ選択の UI があるかは未確認 | 10 分 |
| B | 手元の Mac の **Claude Code CLI + launchd (または cron)** で週 1 回 `claude -p "<プロンプト>"` を実行 | 確実に動く (Mac には GitHub 連携も clone もある)。画面不要 | Mac が起動していないと動かない。`claude` CLI のヘッドレス実行の引数は要確認 (未確認) | 20 分 |
| C | 既に使っている **Hermes の定期実行** (`docs/DEVELOPMENT.md` の upstream_watch と同じ仕組み) に足す | 既存の運用に乗る | Hermes 側の設定はこのリポジトリの外 (未確認) | Hermes の運用次第 |

迷ったら **A を試し、10 分で出来なければ B**。

## 2. 経路 A: claude.ai の画面から作る
### 2-1. 事前確認 (2 分)
1. ブラウザで https://claude.ai/code を開き、リポジトリ `whitewater-png/Meltype-for-Mac` が選べる状態か (GitHub 連携が切れていれば https://claude.ai/connect-github で再接続)。
2. `main` に `CLAUDE.md` と `docs/voice/ROUTINE_PROMPT.md` があることを確認 (PR #4・#6 で入っている)。

### 2-2. 作成 (5 分)
1. claude.ai の Claude Code で、**定期タスク / Routines / スケジュール** に当たる項目を開く (名称は推定。見当たらなければ、環境 (environment) の設定か、セッション一覧の近くを探す)。
2. 新規作成で次を設定する:
   | 項目 | 値 |
   |---|---|
   | 名前 | `Meltype 声の収集 (週次)` |
   | スケジュール | 毎週 **月曜 8:47 JST** (`CRON_TZ=Asia/Tokyo 47 8 * * 1`。正時を避けるため 47 分) |
   | リポジトリ (sources) | `whitewater-png/Meltype-for-Mac` (**push できる設定**で) |
   | 連携 (connectors) | GitHub (Issue を読むため) |
   | モデル | Sonnet (役割が定型なので十分。`docs/AGENT_STATE.md` の配分方針と同じ) |
   | 通知 | push 通知のみ (メール不要) |
   | プロンプト | `docs/voice/ROUTINE_PROMPT.md` の「プロンプト (そのまま貼る)」のコードブロックの中身を**そのまま** |
3. 保存する。

### 2-3. 1 回手動で実行して確かめる (3 分 + 実行待ち 2〜5 分)
1. 作った定期タスクを**手動で実行** (「今すぐ実行」などのボタン。推定)。
2. 終わったら、次の 3 点を見る:
   - GitHub に **`voice/<今日の日付>` ブランチ**が出来ている (`git fetch origin && git branch -r | grep voice`)
   - その中の `docs/voice/<日付>.md` に「Issue の新着・更新」の節があり、**Issue が実際に読めている** (1 回目の試行では 403 で読めなかった。読めていれば連携は正しい)
   - PR・Issue コメント・外部投稿を**していない** (`gh pr list`、Issue のタイムラインで確認)
3. 合格なら、そのブランチの文書を読んで要件候補を選ぶ (選ぶのは人。`docs/requirements/README.md` の流れ)。ブランチは読み終わったら消してよい (文書は PR にしない運用。残したければ main に取り込む)。

### 2-4. 失敗したとき
| 症状 | 原因 (推定) | 対処 |
|---|---|---|
| `git push` が 403 | リポジトリが sources に無い / push 権限が無い | 2-2 の sources を見直す。無理なら経路 B |
| Issue が 1 件も読めない (403) | GitHub 連携が付いていない | connectors に GitHub を足す |
| `CLAUDE.md` が無いと言われる | 古い `main` を見ている | `git fetch` させる。PR #4 以降の main なら存在する |
| 文書は出来たが push されない | 上 2 つのどれか | ログの最後の報告文を読む (止めた理由が書いてある) |

## 3. 経路 B: 手元の Mac で launchd から Claude Code CLI を回す
前提: Mac に `claude` CLI が入っていて、`claude` を対話で起動すると `whitewater-png/Meltype-for-Mac` の clone で作業できること。

1. 実行スクリプトを作る (リポジトリの外、例 `~/bin/meltype-voice.sh`):
   ```bash
   #!/bin/bash
   # 週 1 回、声の収集を Claude Code に任せる。プロンプトはリポジトリの docs/voice/ROUTINE_PROMPT.md から取る。
   set -euo pipefail
   REPO="$HOME/path/to/Meltype-for-Mac"      # ← 自分の clone に直す
   cd "$REPO" && git fetch -q origin main
   PROMPT="$(awk '/^```$/{f=!f; next} f' docs/voice/ROUTINE_PROMPT.md)"   # コードブロックの中身だけ
   claude -p "$PROMPT" --permission-mode acceptEdits >> "$HOME/Library/Logs/meltype-voice.log" 2>&1
   ```
   `claude -p` (非対話) と `--permission-mode` の名前は、手元の `claude --help` で確かめる (未確認。版で変わる)。
2. `chmod +x ~/bin/meltype-voice.sh` して、まず**手で 1 回実行**し、2-3 と同じ 3 点を確かめる。
3. 通ったら launchd に登録 (`~/Library/LaunchAgents/jp.meltype.voice.plist`、`StartCalendarInterval` で `Weekday=1 Hour=8 Minute=47`)。`launchctl load` 後、`launchctl list | grep meltype` で載っているか見る。
4. Mac がスリープ中は動かない。翌起動時に動かすかは launchd の仕様に従う (未確認)。

## 4. 経路 C: Hermes に足す
`docs/DEVELOPMENT.md` の `tools/upstream_watch.py` と同じ流儀で、Hermes 側に「週 1 回、経路 B のスクリプトを実行し、標準出力の最後の報告文を読む」を足す。Hermes の設定はこのリポジトリの外で、ここでは手順を書けない (未確認)。

## 5. 守ること (経路に関係なく)
- プロンプトは `docs/voice/ROUTINE_PROMPT.md` を正本にし、画面側に貼ったものと**ずれたら正本を直してから貼り直す**。
- 定期タスクは **文書を 1 つ push するだけ**。PR を作らない、Issue に返信しない、外部に投稿しない (`CLAUDE.md` §1 の公開行為の制限)。
- 集めた声に個人情報・見られて困る文が混ざらないよう、文書を人が読んでから要件にする。
- 要件にするかどうか、優先順位は人が決める (候補は提案でしかない)。

## 6. 終わったら
- `docs/AGENT_STATE.md` の「次の一手」に、どの経路で動かしているか・初回の結果 (Issue が読めたか、push できたか) を 1〜2 行書く。
- 無効化したままの古い定期タスク `trig_01Yb1BPs2jLaeDBh73iUVt6R` は、claude.ai の定期タスク一覧から削除してよい (残しても動かない)。
- うまく行かなかった原因が「仕組み側」なら `docs/LESSONS.md` に 1 行足す (今回の「ツールから作ったタスクにはリポジトリが付かない」は記録済み: `docs/voice/ROUTINE_PROMPT.md` 冒頭)。

## 7. 参照
- プロンプト正本: `docs/voice/ROUTINE_PROMPT.md`
- 1 回目の試行の内容 (push できなかったが文書は作れた): 同ファイル末尾の要約
- 改善の流れ: `docs/requirements/README.md`、決まり: `CLAUDE.md`
- クラウド環境の設定の公式文書: https://code.claude.com/docs/en/claude-code-on-the-web
