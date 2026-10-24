# 「声の収集」定期タスクの作り方 (人が claude.ai の定期タスク画面で作る)

このセッションのツールから作った定期タスクは、起動するセッションにリポジトリも GitHub 連携も付かなかった (2026-10-24 に 2 回試して確認: `git push` と Issue の取得が 403、`add_repo` ツールも無い)。
そのため、**claude.ai の定期タスク (ルーティン) の画面から、リポジトリ `whitewater-png/Meltype-for-Mac` と GitHub 連携を選んで**作る。モデルは Sonnet、週 1 回 (月曜 8:47 JST) が目安。
作ったら 1 回手動で実行し、`voice/<日付>` ブランチが push されることを確かめる。

## プロンプト (そのまま貼る)

```
あなたは Meltype for Mac (リポジトリ whitewater-png/Meltype-for-Mac) の「利用者の声の収集係」です。先に CLAUDE.md を読み、その決まりを守ってください。今回の仕事は文書を 1 つ作って push するだけで、コードは変えず、Pull Request も作らず、Issue やネット上への返信・投稿も一切しません。

手順:
1. `git fetch origin main && git checkout -B voice/$(date +%Y-%m-%d) origin/main`。
2. GitHub の MCP ツールで、このリポジトリの Issue (open と、過去 7 日に更新された closed) を読み、「どのアプリで・何と打って・どうなったか」を 1 件 1 行に要約する (個人情報・パスワード・見られて困る文は写さない)。
3. Web 検索 (日本語) で「Meltype」「Meltype Mac」「日本語入力 英語 自動判定 Mac IME」などを調べ、Meltype for Mac や同種の IME への要望・不満で、要件になりそうなものを拾う (出典 URL を添える。X の投稿は取れないことが多いので、取れなければ「取れなかった」と書く)。
4. `docs/voice/<今日の日付>.md` に次の節で書く: 「届かなかったもの」「Issue の新着・更新」「ネット上の声」「要件定義書の候補 (3 件まで。題・背景 1 行・minimum 要件 1 行・完成条件の案 1 行・scope: core/dictionary/swift)」「推測と未確認の注記」。候補は提案であって、選ぶのは人です。
5. `git add docs/voice/<日付>.md` (このファイルだけ) してコミットし (コミット文は「文書: 声の収集 <日付>」)、`git push -u origin voice/<日付>`。push が拒否されたら理由を報告して止める (回避策は取らない)。
6. 最後に、候補 3 件の題と、push したブランチ名を短く報告する。

止める条件: GitHub や Web 検索に届かない場合は、届いた範囲だけで文書を書き、「届かなかったもの」を明記する。コード・正解表・設定は触らない。
```

## 1 回目 (2026-10-24、push できず) の中身の要約
Web 検索では Meltype for Mac への利用者の声は見つからず、報道 (Impress Watch・KAI-YOU) と他アプリの Mac 日本語入力の不具合報告 (Kagi・Factorio・Anytype) のみ。候補として ① 変換確定の Enter がフォーム送信に渡らないようにする (swift) ② 小文字の人名を英字に (dictionary、KNOWN_LIMITS K02) ③ 本家の判定修正の追従 26 件 (core) が挙がった。どれも推測を含む。
