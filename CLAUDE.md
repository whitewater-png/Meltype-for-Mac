# Meltype for Mac — AI (Claude Code) で作業するときの決まり

このファイルは、Claude Code のどのセッション (作者の Mac・クラウド) でも最初に読まれる。守る決まり・実行するコマンド・止める条件をここに集める。
人の仕事は「何を作るか・何を優先するか・どこで止めるか」。AI の仕事は「決めた完成条件を満たすまで作って確かめる」。
現在地は `docs/AGENT_STATE.md`、過去の失敗と再発防止は `docs/LESSONS.md`、改善の流れは `docs/requirements/README.md`。

## 1. 守る決まり (ガードレール)
- **公開行為は、リョウさんの明示の許可があるときだけ**: `main` へのマージ、版の更新、タグ、Release、Homebrew tap への push、Issue/PR への返信以外の外部投稿。自動化の範囲は **Pull Request を作るまで**。
- 取り消せない操作 (削除・上書き・公開・送信) は、先に懸念と代替案を出して判断を仰ぐ。
- コミットは 1 項目 1 コミット。`git add` は変更したファイルを明示する (`git add -A` 禁止)。コミット文は日本語で、先頭に種別 (`文書:` `B2-5:` など既存の流儀)。
- **実機 (Mac の入力欄) で確かめていないことは「未確認」と書く。** 実行していないことを実行したと書かない。Windows・Linux は「対象外」と書く (未検証にしない)。
- **提出前に、実装者とは別の査読員 (別エージェント) が確認する。** 指摘が無くなってから提出する。
- `exp/zenzai-eval` ブランチと、他のセッションの未コミットの変更には触らない。要件ごとに自分のブランチで作業する。
- トレースログ (`MeltypeTraceIMK`) は打った文字が残る。**取る前に毎回同意を得る。** 他人のトレースの退避には触らない。
- 打った文字をネットワークに送る機能は作らない (判定・変換・学習は Mac の中だけ)。
- ライセンスは GPL-3.0-or-later。本家 (yksr-melt/Meltype、GPL) 以外のコードを写さない。AI の出力が他プロジェクトのコードをそのまま含んでいないか、査読で見る。
- 文書・コード・エージェント定義に AI のモデル名を書かない (コミットの `Co-Authored-By` は除く)。
- 利用者向けの文書 (`README.md`・`CONTRIBUTING.md`・`docs/USAGE.md`) は、内部の仕組みの話で膨らませない。

## 2. 環境
- `src/Meltype.Core` (C#) と `src/Meltype.Core.Tests` は Mac・Linux で動く。NuGet は使わない (`nuget.config` でソース 0)。.NET 10 SDK (`global.json`: 10.0.401 以上) だけあればオフラインでビルドできる。
- Swift の IME (`mac/`) と NativeAOT (`src/Meltype.Mac.Native`) は **Mac でしかビルドできない** (`mac/build.sh`)。
- クラウド セッションでは `.claude/hooks/session-start.sh` が `~/.dotnet` に SDK を入れる。手元の Mac では `~/.dotnet` の dotnet を使い、`bash --noprofile --norc -c '...'` で流す (対話プロンプトが混ざるため)。
- **クラウドからは本家 `github.com/yksr-melt/Meltype` に届かない** (403)。本家との比較と `tools/upstream_watch.py` は Mac 側で行う。
- 実行ユーザーが root の環境 (クラウド) では、権限で失敗させるテストはスキップになる (出力に「スキップ」と出る。失敗ではない)。

## 3. コマンド (2026-10-24 にクラウドで実測)
```bash
# Core のテスト (938 件、約 50 秒)。出力の最後が N/N passed なら合格
dotnet run -c Release --project src/Meltype.Core.Tests
dotnet run -c Release --project src/Meltype.Core.Tests -- --eval          # 品質テスト (カテゴリーごとの正解率と外れた例)
dotnet run -c Release --project src/Meltype.Core.Tests -- CompositionTests # 名前に一致するテストだけ
dotnet run -c Release --project src/Meltype.Core.Tests -- --explain konnichiwa hello   # 判定の理由

# 正解表 (eval/cases/*.tsv) の回帰ゲート。前のコミットと比べ、broken が 1 件でもあれば終了コード 1
git worktree add /tmp/meltype-base HEAD~1
tools/eval-compare.sh /tmp/meltype-base . eval/cases /tmp/eval-out && cat /tmp/eval-out/compare.md
git worktree remove --force /tmp/meltype-base

# 辞書・スクリプトの検査 (Linux でも動く)
node tools/check-dictionaries.mjs
node tools/check-terms.mjs
python3 tools/test_update_cask.py
python3 tools/test_upstream_watch.py
```
Mac 専用 (クラウドでは動かない): `mac/build.sh`、`tools/check-mac-aot-settings.py`、`tools/check-release.py`、`tools/check-install-stop-ime.py`。

## 4. 改善の流れ (要件 → 実装 → 検証 → PR)
1. 要件定義書 `docs/requirements/NNN-<slug>.md` を書く (型は `docs/requirements/TEMPLATE.md`)。**完成条件を先に決める。** `status: approved` にするのは人。
2. `/improve NNN` で回す (`.claude/commands/improve.md`)。役割: researcher → implementer → reviewer + eval-guard → doc-keeper。
3. 先に正解表 (`eval/cases/*.tsv`) とテストに期待を足して**赤になることを確かめてから**直す。
4. Core のテストが全部通り、回帰ゲートの broken が 0 で、査読の指摘が無くなったら、作業ブランチに push して PR を作る。PR 本文に「完成条件の達成表」「測れていないこと」「Mac 実機チェックリスト」を書く。
5. PR を作ったら `subscribe_pr_activity` で CI とレビューを見張り、落ちたら直して push する。
6. 終わったら `docs/AGENT_STATE.md` を更新する。AI が間違えたら、直すだけで終わりにせず `docs/LESSONS.md` に 1 行足す (決まり / 正解表 / テストのどれで防ぐか)。

## 5. 止める条件 (止めて、理由と現状を報告する)
- 3 周しても完成条件に届かない / 回帰ゲートの broken が直らない
- 変更が Swift・IMK の入力経路 (`mac/Sources/MeltypeIME/InputController.swift` など)、設定・学習データのファイル形式、セキュリティ (`SECURITY.md` の範囲) に及ぶ
- ライセンスの判断が要る / 打った文字の外部送信に関わる
- 要件定義書の `status` が `approved` でない / 要件に無い機能を足したくなった (Issue で相談する方針)
- 実機でしか確かめられないことが完成条件の中心 (チェックリストを書いて人に渡す)

## 6. 測れないこと
Core のテストと正解表は、テスト用の偽の変換エンジンで動く。**azooKey の漢字変換・実機の入力欄の挙動 (TextEdit・Safari・ターミナル・LibreOffice) は測れない。** PR には必ず「測れていないこと」を書き、実機チェックリストを添える。
