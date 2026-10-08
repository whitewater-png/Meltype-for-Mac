# Meltype for Mac

**雪解けのように、半角/全角の壁を溶かす日本語入力 — Mac 版。**

Mac の標準的な IME の仕組み (Input Method Kit) で動く Meltype です。「英数」「かな」を切り替えなくても、ローマ字のまま日本語と英語を打ち分けられます。
英単語 (`google` `github` …) は英字のまま、英語とも日本語とも読める語は前後の文脈で判定します。メニューバーの入力メニューに、ふつうの日本語入力として表示されます。

このリポジトリは、雪代 / Yukishiro さんの [Meltype](https://github.com/yksr-melt/Meltype) (Windows 版が本家) をもとにした **Mac 版の開発リポジトリ**です。判定・変換の中核 (`src/Meltype.Core`) は本家と共通で、Mac 用の IME (Swift) と、Mac 向けの機能追加を加えています。

> テスト版です。動かないところがあれば、[Issues](https://github.com/whitewater-png/Meltype-for-Mac/issues) から教えてください。
> 「どのアプリで」「何と打って」「どうなったか」(できればスクリーンショット) があると助かります。

> 更新は個人の気ままな対応です。ご意見・不具合・ご要望などは、ご連絡いただければ幸いです。

## できること

### 日本語と英語の自動判定
- 半角/全角を切り替えずに、ローマ字のまま日本語と英語を混ぜて打てます (`kyouhagoogledekensaku` → 今日はgoogleで検索)
- 英文 (`I want to go to the park`) もそのまま打てます
- 絵文字・顔文字の変換 (えがお → 😊)、よくある書き間違いの指摘
- 打った内容をネットワークに送りません。判定・変換はすべて Mac の中で行います (通信するのは新しい版の確認だけ。下の「プライバシー」を参照)

### Mac 版で実装した機能
- **F6〜F10 の表示切り替え**: F6 ひらがな / F7 カタカナ / **F8 半角カナ** / F9 全角英数 / F10 半角英数。変換中でも使えます
- **Ctrl キーの代替**: F キーが macOS のショートカットに取られる場合に備えて、変換ボックスが出ているときは Ctrl キーでも同じ操作ができます (Apple 日本語入力と同じ割り当て)
  | Ctrl + | 動作 |
  | --- | --- |
  | J | ひらがな |
  | K | カタカナ |
  | ; | 半角カナ |
  | L | 全角英数 |
  | ' (JIS は :) | 半角英数 |
- **「英数 (Meltype)」入力モード**: 入力メニューに「Meltype」と「英数 (Meltype)」の 2 つが並びます。JIS キーボードの「英数」「かな」キー、**Caps Lock**、入力メニューで切り替えられます (US 配列は Caps Lock か入力メニュー)
- **予測変換**: ひらがなを 2 文字以上打つと、候補ウィンドウに予測が出ます。**Tab** で予測に入り、↓↑ で選び、**Enter** で確定します。英語と判定した語には出ません。出どころは、ユーザー辞書・azooKey の予測・変換履歴の 3 つです。よく使う語・最近使った語ほど上に出ます (回数が 30 日で半分に減る重み付け) 変換エンジンの予測は、読みが 40 文字を超えると出しません (ユーザー辞書・変換履歴の予測は長くても出ます)。
- **候補の数字キー選択**: 変換中に **1〜9** で候補を選んで確定、**PageUp / PageDown** で 9 個ずつ移動
- **学習**: 選び直した候補を覚えます。azooKey の学習も有効で、使うほど文節の区切りと候補の順が良くなります (`~/Library/Application Support/Meltype/` に保存)
- **ユーザー辞書への登録**: 入力メニューの「選択中の文字をユーザー辞書に登録…」で、選択した文字と読みを登録できます
- **専門用語集(サンプル)**: 同梱の専門用語集 (土木・AI・IT・医療・ネットスラング) はサンプルです。自分の用途に合わせて育てていくための出発点としてお使いください (既定はすべて OFF)
- **辞書の管理 (「Meltype 辞書」)**: 入力メニューの「辞書を管理…」で、ユーザー辞書の一覧・検索・登録・編集・削除 (⌘Z で元に戻せる)・取り込み・書き出しと、専門用語集の分野の ON/OFF・語ごとの除外・直してユーザー辞書へ登録ができる画面が開きます。変更は動いている Meltype にすぐ反映されます ([mac/README.md](mac/README.md) の「辞書の管理」)
- **辞書への登録提案**: 同じ語を選び直して 3 回確定すると、入力メニューに「『語』を辞書に登録」が出ます。「登録しない」を選んだ語は二度と提案しません。提案の履歴は入力メニューから消去できます
- **確定後の再変換**: 確定した文字を選択して **Shift + Space** を押すと、読みに戻して変換し直します (Esc を 2 回で元の文字に戻ります)
- **句読点・記号の設定**: 「、。」「，．」「，。」の選択と、`! ? ~` などを全角にするかどうかを設定できます
- **アプリ別の挙動**: Terminal・iTerm2・VS Code・Cursor・Xcode・JetBrains 系などのコード向けアプリでは、英数 (直接入力) から始まります。アプリ別設定で OFF にしたアプリでは、Meltype はキーを一切処理しません

## インストール

### 配布版 (zip)
1. [Releases](https://github.com/whitewater-png/Meltype-for-Mac/releases) から `Meltype-mac-<version>.zip` をダウンロードして展開する
2. 「Install Meltype.command」をダブルクリックする
   - 「開発元が未確認のため開けません」と出たら、右クリック (control + クリック) →「開く」→「開く」
   - macOS 15 以降は、一度ダブルクリックしてから システム設定 → プライバシーとセキュリティ → 下のほうの「このまま開く」
3. メニューバーの入力メニューで Meltype を選ぶ。出ていないときは、いったんログアウトしてログインし直す (それでも出ないときは、システム設定 → キーボード → 入力ソース →「編集…」→「+」→ 日本語 → Meltype を追加)

必要なもの: **macOS 13 以降、Apple シリコン (M1 以降)** の Mac。署名は ad-hoc で、Apple の公証は受けていません。

新しい版に入れ替えるときも、同じ手順です。`~/Library/Input Methods/Meltype.app` を自分で消して入れ直さないでください (ログアウトするまで入力メニューに出なくなります)。

### アンインストール
**いちばん簡単な方法**: 配布の zip を展開したフォルダーにある **`Uninstall Meltype.command`** をダブルクリックします
(「開発元が未確認」と出たら、右クリック →「開く」→「開く」)。画面の案内に従って進めます。

1. 「アンインストールします。よろしいですか?」と聞かれるので、`y` を入力して Enter を押します。
2. 入力ソース (「Meltype」「英数 (Meltype)」) を外し、動いている Meltype を止めて、`~/Library/Input Methods/Meltype.app` を削除します。
3. 「設定・学習データ・ユーザー辞書も削除しますか?」と聞かれます。
   - **`N` (既定)**: 残します。あとで入れ直すと、そのまま使えます。
   - **`y`**: 削除します。削除の前に、ユーザー辞書をデスクトップに `Meltype-userdict-backup-<日時>.txt` としてバックアップします。
4. 終わったら、**いったんログアウトしてログインし直してください**。入力メニューから Meltype が完全に消えます。

ターミナルから実行することもできます (zip を展開したフォルダー、またはこのリポジトリの `mac/` で):

```bash
bash uninstall.sh                          # 対話式 (上と同じ)
bash uninstall.sh --yes                    # 確認なしでアプリを削除 (設定・学習データは残す)
bash uninstall.sh --yes --remove-data      # 設定・学習データ・ユーザー辞書も削除 (辞書はバックアップする)
```

消すもの・残すもの:

| 場所 | 中身 | 既定 |
| --- | --- | --- |
| `~/Library/Input Methods/Meltype.app` | Meltype 本体 (中の辞書の管理画面「Meltype 辞書」を含む) | 削除する |
| `~/Library/Application Support/Meltype/` | 設定・学習データ・ユーザー辞書 | 残す (聞かれたとき `y` で削除) |

**入力ソースを自動で外せなかったとき** (古い版を入れていた場合など) は、画面に案内が出ます。システム設定 → キーボード → 入力ソース →「編集…」で、Meltype を選んで「−」で外してください。

**手で消す場合** (スクリプトが使えないとき):

1. システム設定 → キーボード → 入力ソース →「編集…」で Meltype を外す
2. ターミナルで `rm -rf ~/Library/Input\ Methods/Meltype.app`
3. (任意) 設定・学習データも消すなら `rm -rf ~/Library/Application\ Support/Meltype`
4. ログアウトしてログインし直す

注意: 入力ソースを外す前にアプリだけを消すと、入力メニューに Meltype が残ることがあります (ログアウトして入り直すと消えます)。学習データだけを消したいときは、アンインストールせずに、入力メニューの「学習データをすべて消去…」を使えます (ユーザー辞書と設定は残ります)。

### ソースからビルドする
必要なもの: Xcode またはコマンドラインツール (`xcode-select --install`)、.NET 10 SDK。

```bash
cd mac
./build.sh
```

詳しい手順とトラブルシューティングは [mac/README.md](mac/README.md) を見てください。

## 使い始める

メモ帳やブラウザーの入力欄で、IME を気にせずそのままローマ字で打ってください。

- 日本語はかなで、英単語は英字のまま、下線付きの変換ボックスに出ます
- **Space** で漢字に変換 (候補の一覧が出ます)、**Enter** で確定、← → で文節を選択、**Esc** で取り消し
- 変換したあとに続けて文字を打つと、ふつうは今の候補で確定して新しい入力になります。入力メニューの「変換後も続けて入力できる」を ON にすると (既定は OFF)、確定せず、打った文字を未変換のかなとして続けられます。続けて Space で変換、← → で前の文節の選び直し、Enter で全体を確定、Esc で変換前の読みに戻ります。ON の間は、変換後の予測変換は出ません
- 設定・学習データ・ユーザー辞書は `~/Library/Application Support/Meltype/` にあります (入力メニューの「Meltype のデータフォルダを開く」から開けます)。設定は `config.json` を直接編集します

詳しいキー操作・判定の強さは [docs/USAGE.md](docs/USAGE.md) にあります (Windows 版の項目も含みます)。

## よくある質問

**F9 などのキーが効かない**
macOS が F キーをショートカットに使っています。システム設定 → キーボード → キーボードショートカットで割り当てを外すか、上の Ctrl キーを使ってください。

**「英数 (Meltype)」が入力メニューに出ない**
いったんログアウトしてログインし直してください。

**英語のつもりがかなになった / かなのつもりが英字になった**
F10 (英字) / F6 (ひらがな) で直して確定すると、次からその語は直した方になります。

**動きがおかしい・止まった**
`pkill -x Meltype` で止められます (次にキーを打つと macOS が起動し直します)。ログは `log stream --predicate 'process == "Meltype"' --level debug` で見られます。

## プライバシー

Meltype は打った内容をネットワークに送りません。変換・学習・辞書の登録提案はすべて Mac の中だけで行い、保存するのは `~/Library/Application Support/Meltype/` の設定・学習データ・ユーザー辞書だけです。

**通信するのは、新しい版の確認だけです。** 既定で 1 日 1 回、GitHub の最新 Release の情報 (`https://api.github.com/repos/whitewater-png/Meltype-for-Mac/releases/latest`) を取得して、新しい版があるかを確かめます。
送るのは取得の要求だけで、GitHub には IP アドレスと Meltype の版 (User-Agent) が見えます。打った内容・設定・辞書は送りません。
入力メニューの「更新を確認する」のチェックを外せば止まります (止めている間は、「今すぐ更新を確認する」を押したとき以外は通信しません。設定は `~/Library/Application Support/Meltype/update.json`)。
更新は、利用者が「新しい版があります…」→「更新する」を押したときだけ行います。Release の zip をダウンロードして SHA-256 と zip の中身を確かめ、署名の検査をしてから入れ替えます (自動では入れません)。入れ替えに失敗したら元の Meltype に戻します。`~/Library/Input Methods/Meltype.app` 以外から動いている Meltype には「更新する」は出ません。
電子署名 (作者の署名) は付いていないので、GitHub のアカウントが乗っ取られた場合は防げません (詳しくは [SECURITY.md](SECURITY.md))。
学習の提案用の履歴 (`suggest.json`) には、「読み」「語」「回数」だけを保存し、前後の文章は保存しません。

## 仕組み

| 部分 | 中身 |
| --- | --- |
| IME 本体 (`mac/Sources/MeltypeIME`, Swift) | Input Method Kit でキーを受け取り、変換中の文字・候補の一覧・確定を入力欄に反映する |
| 判定の本体 (`src/Meltype.Mac.Native`) | 英語 / 日本語の判定・ローマ字・変換の流れ・学習・辞書を持つ共通の C# の部分 (`src/Meltype.Core`) を、NativeAOT で Mac 用のライブラリにしたもの |
| 漢字変換 | [azooKey](https://github.com/azooKey/AzooKeyKanaKanjiConverter) の変換エンジン (MIT License、辞書付き) |
| 英単語の判定 | macOS のスペルチェッカー (英語) |

## ライセンス

Meltype は **GNU General Public License v3.0** ([LICENSE](LICENSE)) で公開されています。本リポジトリも同じ条件で、改造版もソースを公開する条件で自由に使えます。
GPL v3 の条件で使えない場合は、本家の作者にご相談ください。貢献の方法は [CONTRIBUTING.md](CONTRIBUTING.md) を参照してください。

使っているライブラリのライセンスは [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) にあります。

```
Meltype
Copyright (C) 2026 雪代 / Yukishiro (@yksr_melt / @yksr-melt)

This program is free software: you can redistribute it and/or modify it under the terms of the
GNU General Public License as published by the Free Software Foundation, either version 3 of the
License, or (at your option) any later version.

This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without
even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
General Public License for more details.
```

## リポジトリの構成 (Mac で使うもの / 使わないもの)

このリポジトリは、本家 Meltype (Windows 版・Linux 版を含む) のソースをもとにしているため、Mac では使わないファイルも残っています。**Mac 版を使う・作るときは、`mac/` フォルダーのスクリプトと説明書を使ってください。**
`./build.sh`、`install.sh`、`uninstall.sh`、`make-pkg.sh` などは、すべて `mac/` の中にあります。トップにある `.ps1` や、`docs/` の Windows 向けのコマンドは、Mac では使いません。

| 場所 | 中身 | Mac での扱い |
| --- | --- | --- |
| `mac/` | Mac 版 IME (Swift)、`build.sh`・`install.sh`・`uninstall.sh`・`make-pkg.sh`・`make-zip.sh`、`README.md`、`INSTALL.txt` | **使う** |
| `src/Meltype.Core/` | 英語 / 日本語の判定・変換・学習・辞書 (Windows / Linux と共通) | **使う** (NativeAOT で Mac 用のライブラリになる) |
| `src/Meltype.Mac.Native/` | Core を Mac の Swift から呼ぶための橋渡し | **使う** |
| `src/Meltype.Core.Tests/` | Core のテスト (`dotnet run --project src/Meltype.Core.Tests`) | **使う** |
| `dictionaries/` | 辞書データ (Core に埋め込まれる) | **使う** |
| `tools/` | 辞書データを作るスクリプト (Node) | 辞書を作り直すときだけ |
| `src/Meltype/`、`src/Meltype.Tests/` | Windows 版のアプリとそのテスト | 使わない |
| `Build-Package.ps1`、`Install-Meltype.ps1`、`Uninstall-Meltype.ps1`、`packaging/` | Windows 版のビルドとインストール | 使わない |
| `linux/`、`native/mozc/` | Linux 版 (IBus) と、Windows / Linux 用の Mozc 変換 | 使わない |
| `docs/DEVELOPMENT.md`、`docs/USAGE.md` | 本家 (主に Windows 版) の開発・使い方 | 参考程度 (Mac 版の手順は `mac/README.md`) |
| `docs/IMPROVEMENT_PLAN.md` | Mac 版の実装計画と、実装済みの内容 | 参考 |

## 開発に参加する

Mac 版のビルド・インストール・トラブルシューティングは [mac/README.md](mac/README.md)、実装計画と実装済みの内容は [docs/IMPROVEMENT_PLAN.md](docs/IMPROVEMENT_PLAN.md) を見てください。本家 (主に Windows 版) の開発手順は [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) にありますが、コマンドは Windows 向けです。
本家 (Windows 版) の情報は <https://github.com/yksr-melt/Meltype> にあります。
