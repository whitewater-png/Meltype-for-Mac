# 失敗と再発防止 (LESSONS)

> AI (または人) が間違えたら、直して終わりにせず、ここに 1 行足す。防ぎ方は 3 つのどれか:
> **決まり** (`CLAUDE.md` に書く) / **正解表** (`eval/cases/*.tsv` に行を足す) / **テスト** (`src/Meltype.Core.Tests` に足す)。
> 形式: `| 日付 | 何が起きたか | 防ぎ方 | どこに足したか |`

| 日付 | 何が起きたか | 防ぎ方 | どこに足したか |
|---|---|---|---|
| 2026-10-24 | `dotnet-install.sh --jsonfile global.json` が、このリポジトリの 1 行形式の global.json を読めず失敗した | 決まり | `.claude/hooks/session-start.sh` は `--channel 10.0` を使う (コメントに理由) |
| 2026-10-24 | root で動くクラウドでは、権限で失敗させるテスト (`SaveFailure_RestoresMemory`) が落ちた。既存の `P46_*` は `Environment.IsPrivilegedProcess` でスキップしていたが、この 1 件に無かった | テスト | 同じ流儀のスキップを足した |
| 2026-10-24 | 作成時刻 (birth time) で置き換えを見分けるテストが、作成時刻を区別しない一時フォルダーで落ちた | テスト | 環境を調べてスキップ (`CreationTimeDistinguishesReplacedFiles`) |
| 2026-10-24 | 文書のテスト件数 (559 / 619) と正解表の数 (3) が実態 (938 / 4 表) とずれていた | 決まり | 数字は `docs/AGENT_STATE.md` だけに書き、他の文書からは参照する |
| 2026-10-24 | 要件 001 の背景を `--explain` だけで書いて「全体が英語になる」と誤認した (実際は `あぴのerror`)。完成条件に「既に通る行」(`urlnoerror`) を入れた | 決まり | 要件の「今の挙動」は `--repro` と `--segments` の結果を写す。完成条件の正解表の行は、書く前に `--repro` で外れることを確かめる (`docs/requirements/README.md`) |
