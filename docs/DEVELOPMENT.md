# Meltype の開発

ソースからのビルド、テスト、動作の仕組みです。使い方は [USAGE.md](USAGE.md)、貢献の方法は [CONTRIBUTING.md](../CONTRIBUTING.md)、リリースの手順は [RELEASE.md](RELEASE.md)。

## ソースからビルドして入れる

```powershell
powershell -ExecutionPolicy Bypass -File .\Install-Meltype.ps1
```

ビルドしてスタートアップに登録し、起動します。タスクトレイに「あ」のアイコンが出れば動いています。
`-ExecutionPolicy Bypass` はこのコマンドにだけ効き、PC 全体の設定は変えません。

アンインストール:

```powershell
powershell -ExecutionPolicy Bypass -File .\Uninstall-Meltype.ps1            # 停止とスタートアップ解除
powershell -ExecutionPolicy Bypass -File .\Uninstall-Meltype.ps1 -RemoveData # 設定と学習データも削除
```

必要なもの: Windows 10 / 11 (x64 / ARM64)、.NET 10 SDK、Microsoft IME (漢字変換に使います)。

## 協力者に渡すテスト版

```powershell
powershell -ExecutionPolicy Bypass -File .\Build-Package.ps1
```

`dist\Meltype-test-<日付>.zip` ができます。中身はビルド済みの `app` フォルダー、`Install.cmd` / `Uninstall.cmd` (ダブルクリックで実行)、協力者向けの `README.txt` です。
.NET ランタイムを同梱しているので、協力者の PC に .NET は不要です。この環境は NuGet が使えないため、自己完結ビルドの代わりに、この PC にインストール済みの .NET ランタイムを `app\dotnet` にコピーし、`Meltype.exe` がそこを使うようにしています (`AppHostDotNetSearch=AppRelative`)。

スマホからも受け取れる大きさ (30MB 未満、現在約 24MB) にするため、ランタイムから Meltype が使わない部品を削っています。

1. Meltype.dll が使う型から参照をたどり、要らないアセンブリを削る (`Meltype.Tests -- --runtime-closure`)
2. 自己診断 (`Meltype.exe --selftest`) を走らせ、実際に読み込まれなかった大きなアセンブリ (XML・ネットワーク・暗号など) を削る
3. もう一度自己診断を走らせ、削りすぎていないことを確かめる (失敗したら zip を作らない)

自己診断は、設定・判定・辞書・変換エンジン・Windows の候補 API・UI Automation・各画面・タスクトレイ・データの保存を、キーボードフックを掛けずに一通り動かします。UI Automation は WPF に頼らず COM で直接使っているので、WPF 一式は同梱していません。
インストール先は `%LOCALAPPDATA%\Programs\Meltype` で、管理者権限は不要です。配布用のファイルの元は [packaging/](../packaging/) にあります。

## 動作の仕組み

どちらのモードも、キーボードフック (`WH_KEYBOARD_LL`) を専用スレッドで受けます。Meltype が送り直したキーには印 (`dwExtraInfo = "MELT"`) を付け、自分では判定しません。

Meltype キーボード:

```
物理キー ─▶ KeyboardMonitor ─▶ CaptureGate (変換ボックスが開いている間は、キーとクリックをすべて順番どおりに保留)
                                   │
                                   ▼ (UI スレッド)
                           CompositionController ◀─ CompositionDetector (英語の区間の判定)
                                   │                  MsImeKanjiConverter (Microsoft IME の変換エンジン)
                                   │                  ContextRules / ConversionHistory / CandidateDictionary
                                   │                  FocusInspector (UI Automation: 入力欄か・カーソルの前後の文字)
                                   ▼
                  変換ボックスに表示 ─▶ Enter で確定した文字列を SendInput (Unicode) で入力欄へ
```

IME 自動切替:

```
物理キー ─▶ KeyboardMonitor ─▶ InputSession (Idle → Collecting → Flushing → Committed)
                                   │  Collecting 中は打鍵をすべて保留 (キーアップ・記号も届いた順に)
                                   ▼
                             ScoreEngine ◀─ RomajiDetector / KanaDetector / DictionaryDetector
                                   │         EnglishDetector / TypoDetector / UserModel
                                   ▼
              日本語 ─▶ ImeController で IME を ON ─▶ 保留分を送り直す ─▶ Microsoft IME が処理
              英語 / 不明 ─────────────────────────▶ 保留分をそのまま送り直す
```

- 日本語と判定する条件: `JapaneseScore >= 閾値` かつ `JapaneseScore - EnglishScore >= 閾値` (既定の閾値 4)
- 保留は最大 6 文字、無入力 0.7 秒、最初の打鍵から 2.5 秒で打ち切り、そのまま出す
- IME の切替は IMM32 → TSF → `VK_IME_ON` の順に試し、すべて失敗したら切り替えずにそのまま出す

## テスト

```powershell
dotnet build Meltype.sln
dotnet run --project src/Meltype.Tests                                   # テスト (Windows: 共通のテスト + Windows のスペルチェッカー)
dotnet run --project src/Meltype.Tests -- CompositionTests               # 名前に一致するテストだけ
dotnet run --project src/Meltype.Tests -- --explain konnichiwa hello      # 1 文字ずつの判定理由 (IME 自動切替)
dotnet run --project src/Meltype.Tests -- --convert きょうはいいてんき     # 変換エンジンの結果と文節の区切り
dotnet run --project src/Meltype.Tests -- --context この本は:あつい        # 文脈を渡したときの変換結果
dotnet run --project src/Meltype.Tests -- --eval                          # 品質テスト: カテゴリーごとの正解率と外れた例
```

### Mac・Linux でのテスト

OS に依存しない部分 (`src/Meltype.Core`: 英語 / 日本語の判定・ローマ字・変換ボックスの中身・辞書・学習・設定) と、そのテスト (`src/Meltype.Core.Tests`) は Mac・Linux でも動きます (Mac 版・Linux 版の土台)。.NET 10 SDK (10.0.401 以上、`global.json`) を入れて次を実行します。このリポジトリには CI (GitHub Actions) が無いので、コミット前に手元で流します。

```bash
dotnet run --project src/Meltype.Core.Tests                 # すべてのテスト
dotnet run --project src/Meltype.Core.Tests -- --eval       # 品質テスト (スペルチェッカーは使わない)
```

### 品質テスト (採点テスト)

`src/Meltype.Core.Tests/QualityTests.cs` に、日本語の文・英文・混在・英語とも日本語とも読める語・英語の後の短い語・記号と数字・小書き文字・大文字・かな入力・コードの行 (コメント / 文字列の判定)・文章ファイルの判定・絵文字・もしかして の例をまとめてあります。期待値は「理想の結果」で書いてあり、`--eval` でカテゴリーごとの正解率と外れた例を表示します。通常のテストでは、全体 95% 以上・どのカテゴリーも 80% 以上を基準にして、ある直しで別の場所が壊れたら気づけるようにしています。Windows の英語スペルチェッカーが使える環境では実際と同じくそれも使います (`MELTYPE_NO_SPELLCHECK=1` で使わずに測れます)。新しい不具合の報告を受けたら、まずここに例を足してから直すのがおすすめです。

- `src/Meltype.Core/` — OS に依存しない部分 (Windows・Mac・Linux 共通)
  - `Composition/` 変換ボックスの中身 (英語の区間の判定・変換の流れ・候補・文脈・学習・ユーザー辞書・もしかして)
  - `Detection/` ローマ字・英語・辞書・かな・Typo の判定器
  - `Input/` キーの表し方、IME 自動切替の入力セッション、コードの行の判定
  - `Learning/` `Config/` `Diagnostics/` IME 自動切替の学習・設定・ログ
- `src/Meltype/` — Windows 版 (キーボードフック、変換ボックスの画面、Microsoft IME の変換エンジン・IMM32 / TSF、UI Automation、スペルチェッカー、トレイ)
- `src/Meltype.Mac.Native/` — Mac 版の IME から呼ぶ C の関数 (Meltype.Core を NativeAOT で dylib にする)
- `mac/` — Mac 版の IME (Swift, Input Method Kit。漢字変換は azooKey)。ビルドは `mac/build.sh`
- `src/Meltype.Core.Tests/` — 共通部分のテスト (判定・入力セッション・変換ボックス・学習・品質テスト)。Mac・Linux でも動く
- `src/Meltype.Tests/` — Windows 版のテストと調査用の道具 (共通部分のテストもまとめて流す)
- `dictionaries/` — 組み込み辞書

## 評価基盤 (`eval/`)

同じ「正解表」で、本家 Meltype の Core と Mac 版の Core の判定結果を並べ、回帰 (本家や前回で当たっていたのに今は外れる例) を 0 件にするための仕組みです。表の形式・比べ方・実機の結果の足し方は [eval/README.md](../eval/README.md) にあります。

### 正解表

`eval/cases/*.tsv`。1 行 = `打鍵<Tab>期待<Tab>比べ方<Tab>出典` の 4 列です。比べ方は `full` (完全一致。期待にカタカナがあるときだけ、カタカナ→ひらがなと英字の大小を吸収。本家の `Expect` と同じ)・`ascii` (印字可能 ASCII の連続の並びだけ)・`nojp` (日本語・全角記号・半角カナが 1 文字も無い。英文が崩れないことだけ見たい行用)。`upstream-quality.tsv` (本家の品質テストの写し)・`mac-device.tsv` (実機の記録と計画書の期待)・`mac-terminal.tsv` (ターミナル・AI ツール名) の 3 つがあります。

### 1 つの作業ツリーで流す (`--expect-all`)

```bash
cd src/Meltype.Core.Tests
dotnet run -c Release -- --expect-all ../../eval/cases /tmp/eval.json
```

`eval/cases/*.tsv` をすべて確かめ、`{file, typed, expected, mode, source, actual, ok}` の配列を JSON で出します (`file` は表のファイル名)。1 つの表だけなら、これまでどおり `--expect <表> <出力>` です (2 列だけの古い表も動きます)。変換エンジンはテスト用の偽物なので、漢字の変換は測りません。

### 本家との比較と回帰ゲート

```bash
git clone --depth 1 https://github.com/yksr-melt/Meltype.git /tmp/meltype-upstream
tools/eval-compare.sh /tmp/meltype-upstream . eval/cases /tmp/eval-out
```

`tools/eval-compare.sh <base-tree> <new-tree> <cases-dir> <out-dir>` は、2 つの作業ツリーで同じ表を回し、`tools/eval_compare.py` で比べます。`out-dir` に `base.json`・`new.json`・`compare.md` (ファイルごとの `ok/total` の表と、`broken`・`fixed` の一覧) ができます。

- `broken` … base で当たり、new で外れた例 (回帰)。**1 件でもあれば終了コード 1** です (dotnet や入力の失敗は 2、問題なしは 0。本家のゲートは broken + addedFailing、こちらは broken のみ)。これが回帰ゲートで、`broken` が 0 件になるまで取り込んだ変更を「回帰なし」と言いません。
- `fixed` … base で外れ、new で当たった例。`added` / `removed` … 片方にしかない例 (表の増減)。
- base には Mac 版の前回のコミットの作業ツリー (`git worktree add /tmp/prev <コミット>`) も使えます。
- 本家の `Meltype.Core.Tests` には `--expect-all` と 3 つの比べ方が無いので、本家側は `tools/eval_legacy_base.py` が `--expect` を表ごとに回し、返ってきた `actual` を同じ規則で判定し直します。この判定は C# の `Checks.Judge` と同じで、`python3 tools/eval_legacy_base.py --rejudge <--expect-all の JSON>` で食い違いが無いことを確かめられます。
- `dotnet` は作業ツリーの中に `bin/` `obj/` を作ります。本家の clone を汚したくないときは、コピーを渡します。
- `/tmp` のようにシンボリックリンクを通る絶対パスで `dotnet run --project` を呼ぶとビルドが失敗することがあったので、スクリプトは作業ツリーに入って相対パスで呼びます。

この比較は Core のテスト用キーボードの結果で、**実機の確認の代わりにはなりません**。Windows・Linux は対象外です。

### 本家追従 (`tools/upstream_watch.py`)

本家 Meltype の新しいコミットを PR 単位で拾い、Mac 版に取り込み済みか・そのまま当たるか・新しい正解ケースがあるかを [docs/UPSTREAM_WATCH.md](UPSTREAM_WATCH.md) の候補表にまとめます。

```bash
git clone https://github.com/yksr-melt/Meltype.git ~/meltype-upstream   # 初回だけ。完全な履歴が要ります (--depth 1 は不可)
export MELTYPE_UPSTREAM=~/meltype-upstream
python3 tools/upstream_watch.py --since <hash>   # 初回だけ --since。2 回目からは UPSTREAM_WATCH.md 先頭の seen から
python3 tools/upstream_watch.py                  # 以降
```

- 引数: `--upstream <clone>` (既定: `MELTYPE_UPSTREAM`)、`--mac <tree>` (既定: このスクリプトのあるリポジトリ)、`--since <hash>`、`--no-fetch` (fetch しない)、`--dry-run` (文書も候補ファイルも書かず、標準出力だけ)、`--no-eval` (dotnet を回さない)。通信は本家の clone への `git fetch` だけです。終了コードは 0 = 正常 (新コミット 0 件も 0)、2 = 引数・環境の誤り (dotnet の失敗も 2。文書は更新しません)。標準出力に 3〜6 行の要約が出ます (Hermes の定期実行はこれを読みます。定期実行はこのリポジトリの外です)。
- 出力: `docs/UPSTREAM_WATCH.md` の先頭に、新しい順で節が増えます。本家の `QualityTests.cs` に足された `new("カテゴリ", "打鍵", "期待")` の行は `eval/candidates/upstream-<日付>.tsv` に書き (4 列。同じ日の再実行は上書き)、Mac 版で何件当たるかを表と節の末尾に出します。
- **候補ファイルは正解表に自動では足されません。** 正解表に移すのは人です:
  1. 候補ファイルの行を見て、期待が本家の意図どおりか、私的な文が入っていないかを確かめる (当たる行も、外れる行も。外れる行は Mac 版にまだ無い修正を示します)。
  2. 入れる行を `eval/cases/upstream-quality.tsv` の**末尾**に移す (出典の列は `本家 <hash> #NNN` のまま)。
  3. 上の「1 つの作業ツリーで流す」で回し、数字が期待どおりか見る。
  4. push すれば表は公開されます。`git add` した時点で履歴にも残るので、迷う行は入れません。
- `Before:` などの名前付き引数がある行、かなで打つ行、複数行の行は 4 列で表せないので、候補ファイルには出さず、表の「新ケース」の列に件数だけ出ます。必要なら本家のテストを見て手で足します。
- 自己テスト: `python3 tools/test_upstream_watch.py` (git・dotnet は使いません)。Windows・Linux の変更は一覧に出すだけで、取り込み・評価はしません。
