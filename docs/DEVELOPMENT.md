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

OS に依存しない部分 (`src/Meltype.Core`: 英語 / 日本語の判定・ローマ字・変換ボックスの中身・辞書・学習・設定) と、そのテスト (`src/Meltype.Core.Tests`) は Mac・Linux でも動きます (Mac 版・Linux 版の土台)。.NET 10 SDK を入れて次を実行します。GitHub Actions でも Ubuntu と macOS で毎回流しています。

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
