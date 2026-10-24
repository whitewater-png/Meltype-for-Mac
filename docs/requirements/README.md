# 要件定義書 (docs/requirements/)

利用者の声 (Issue・入力メニューからの報告・`docs/voice/`) を、AI に任せられる形にするための置き場です。
投稿や Issue を 1 つずつプロンプトに書く代わりに、**完成条件を先に決めた要件定義書** を 1 件書き、`/improve NNN` で回します。

## 流れ
1. `docs/voice/<日付>.md` や Issue から、何を作るかを人が選ぶ (優先順位も人)。
2. `TEMPLATE.md` を写して `NNN-<slug>.md` を作る (NNN は 3 桁の連番)。AI に下書きさせてよい。
3. 人が中身を確かめて `status: approved` にする。**approved でない要件は AI が実装を始めない** (`CLAUDE.md` §5)。
4. `/improve NNN` (`.claude/commands/improve.md`)。作業ブランチで、完成条件を満たすまで回り、Pull Request を作って止まる。
5. 人が PR の「Mac 実機チェックリスト」を実機で確かめ、マージ・リリースを判断する (公開行為は人)。
6. 終わったら `status: done` にし、`docs/AGENT_STATE.md` の記録を埋める。

## 完成条件の書き方 (機械で判定できる形にする)
| 種類 | 書き方 | 判定 |
|---|---|---|
| 判定の期待 | `eval/cases/<表>.tsv` に足す行 (打鍵・期待・比べ方・出典) | `tools/eval-compare.sh` で broken 0、足した行が ok |
| 振る舞い | `src/Meltype.Core.Tests` に足すテストの名前と要点 | `dotnet run -c Release --project src/Meltype.Core.Tests` が全件通る |
| 回帰なし | 常に | eval-compare の broken 0、品質テストの基準 (全体 95 %・各カテゴリー 80 %) |
| 実機 | TextEdit・Safari・ターミナルなどで打つ内容と期待 | **人が確かめる** (AI は PR にチェックリストとして書く) |

正解表の形式と比べ方 (`full` / `ascii` / `nojp`) は [eval/README.md](../../eval/README.md)。`ascii` の行は期待に ASCII の語が 1 つも無いと読み込みエラーになります。
Core のテストは偽の変換エンジンで動くので、漢字変換と実機の挙動は測れません。それは「実機」の行に書いてください。

## 一覧
| 番号 | 題 | status |
|---|---|---|
| (無し) | | |
