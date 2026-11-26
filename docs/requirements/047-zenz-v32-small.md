---
title: Zenzai のモデルを zenz-v3.2-small に切り替える (既定 ON は人が決める)
status: approved   # draft | approved | done。approved にするのは人
source: Meltype-Flash との比較 (2026-11-26、リョウさんの指示「いいよ、進めて」)。Mac 実機での計測
scope: swift (実機必須)
---

# 047 Zenzai のモデルを zenz-v3.2-small に切り替える

## 背景 (誰が、何に困っているか)
- Zenzai (ニューラル変換) を ON にすると、かな漢字変換の正解率が大きく上がる (AJIMEE 200 件で 44.0 % → 82.5 %。`docs/AJIMEE_BENCH_2026-11-24.md`)。ただし既定は OFF (2026-11-24 の決定)。
- 今のモデル zenz-v3.1-small は CC BY-SA 4.0。新しい zenz-v3.2-small は精度が同じ水準 (下の計測)。
- **zenz-v3.2-small のライセンスは未確定**: Meltype-Flash の `rust/ai/model-catalog.json` は apache-2.0 とするが、配布元 (2026-11-26 に確認) では、`https://huggingface.co/Miwa-Keita/zenz-v3.2-small-gguf` のモデルの頁が「cc-by-sa-4.0」、同じリポジトリのファイル一覧の頁 (`/tree/main`) が「apache-2.0」と食い違う。LICENSE ファイルは無く、README.md は 31 バイトで本文が無い。xsmall (`zenz-v3.2-xsmall-gguf`) は cc-by-sa-4.0。確定するまでは、v3.1 と同じ CC BY-SA 4.0 として扱う (ライセンスの得は当てにしない)。
- **v3.2 専用のプロンプトは無い** (2026-11-26 に azooKey のソースで確認): azooKey の `Docs/zenzai.md` に「zenz-v3.2: zenz-v3の機能に加えて、`\uEE07`に続けて入力された右文脈を考慮できます」とある。形式は v3 のまま (`versionDependentMode: .v3`) で、右文脈は任意。右文脈の受け口 (`ZenzaiV3DependentMode.rightSideContext`) は azooKey の 6c2a224 (2026-05-10、#340) で入り、今使っている d59a28e に含まれる。Mac 版の `Converter.swift` は今、右文脈を渡していない (`leftSideContext` だけ)。
- 競合の Meltype-Flash (appipinopi/Meltype-Flash) は v3.2 系も選べる (`rust/ai/model-catalog.json`)。コードは写さない (CLAUDE.md §1)。モデルの URL と SHA-256 は配布元 (Hugging Face) で確かめた値を使う。

## 今の動き (既に分かっていること)
- 関係する場所: `mac/Sources/MeltypeIME/ZenzaiModel.swift` の `Source.production` (URL・サイズ・SHA-256・ファイル名を固定)、`mac/Sources/MeltypeIME/ConvertEval.swift` (計測。`--weights` は固定した値と合う重みだけ受け付ける)、`THIRD-PARTY-NOTICES.md` の zenz の行、`mac/README.md` (68・73 行目に旧モデルの名前・URL・サイズ・保存先のファイル名)。README.md と docs/USAGE.md には v3.1 の名前は無い。`docs/requirements/035-zenzai-guidance.md` の旧名は過去の記録なので直さない。
- 今の URL は `resolve/main` (リビジョンを固定していない)。サイズと SHA-256 は固定。`allowedHost` (`huggingface.co` と `*.hf.co`) はリビジョンを固定しても転送先が同じなので変えなくてよい。
- `ZenzaiModel.swift` の `removeLeftovers` は取得途中の `download.*.part` と `zenzai.*.tmp` しか消さない。ファイル名を変えると、**旧ファイル (約 74 MB) は自動では消えず残り**、新しいファイルを 1 回取得し直す。ここを変えるなら取り消せない操作なので、下の「人に決めてほしいこと」2 で決める。
- `--zenzai-self-test` は固定したサイズ・SHA-256 で重みを確かめる (`ZenzaiSelfTest.swift`)。取得に通信が要るか、手元の重みで足りるかは、実装のときに確かめる (未確認)。
- 計測 (2026-11-26、リョウさんの Mac M4 Max / 36 GB / macOS 27.2.1、main e83f963、azooKey d59a28e、`versionDependentMode` は `.v3` のまま。使い捨てブランチで `ZenzaiModel.swift` の固定値だけ書き換えてビルド。計測中の load average は 8〜9 で、ms は揺れが大きい):

| モデル | AJIMEE 200 acc@1 | Flash 40 acc@1 | AJIMEE 1 行 p50 / p95 / 最大 (ms) |
|---|---|---|---|
| OFF | 88 (44.0 %) | 23 (57.5 %) | 12.9 / 53.7 / 97.0 |
| v3.1-small (今) | 165 (82.5 %) | 37 (92.5 %) | 30.1 / 114.4 / 236.3 |
| v3.2-small | 166 (83.0 %) | 39 (97.5 %) | 32.8 / 127.1 / 224.7 |
| v3.2-xsmall (21 MB) | 142 (71.0 %) | 35 (87.5 %) | 17.0 / 71.4 / 126.9 |

  - v3.2-small と v3.1 の McNemar: AJIMEE で v3.1 だけが正解 8 件 / v3.2 だけが正解 9 件、p = 1.00 (差なし)。Flash 40 は 0 件 / 2 件、p = 0.50。AJIMEE の MinCER は 1.8 % → 2.1 %。
  - v3.2-small が v3.1 より悪くなった AJIMEE の行 (8 件): 234 辞典→寺殿、469 過熱→加熱、1145 書く→描く、1414 三十→三重、1682 裸一貫→は高一貫、1820 大政翼賛会 (崩れ)、1893 舟盛り→舟盛、1992 振れ / 歳差→触れ / 再左。
  - v3.2-xsmall は AJIMEE で 30 件 / 7 件、p = 1.9e-4 (有意に悪い)。軽い Mac 用の代わりには、そのままでは向かない。
  - OFF と v3.1 の数字は 2026-11-24 の記録と同じになった (測り方の再現性)。
- 本家 (yksr-melt/Meltype) の扱い: 本家は v1.5.0 から Zenzai を同梱・既定 ON (`docs/ACCURACY_PLAN_2026-11.md` §1.1)。どのモデルかは、クラウドから本家に届かないので未確認。

## 人に決めてほしいこと (approved にする前に)
1. **既定 ON にするか** (2026-11-24 の「既定 OFF」の決定の再検討)。案:
   - (a) 既定 OFF のまま、モデルだけ v3.2-small にする (いちばん小さい変更。推奨)
   - → **決定 (リョウさん、2026-11-26)**: 既定 ON にはしない (「しないわ」)。案 (a) で進める。
   - (b) 既定 ON にする。初回に重み (約 74 MB) を取得する通信が起きる (今の決まり「ON にしたときだけ取得」を覆す)。M1 級・8 GB での速さは未計測のまま
   - (c) 同梱して既定 ON (本家と同じ。配布物が約 74 MB 増える。ライセンスが未確定なので、同梱するなら先に配布元に確かめる。同梱の判断は公開行為に近いので人)
2. **前のモデルのファイル** (`zenz-v3.1-small-Q5_K_M.gguf`、約 74 MB) を、切り替え後にどうするか: 残す / アプリが消す / README で案内する。利用者のフォルダーのファイルを消すのは取り消せない操作なので、消すなら人が決める。 → **決定 (リョウさん、2026-11-26)**: アプリが消す (「1A」)。v3.2 の取得と検査が通ったあとに、アプリの models フォルダーにある `zenz-v3.1-small-Q5_K_M.gguf` だけを、名前とサイズ・SHA-256 が v3.1 の固定値と一致したときに限って消す。ほかのファイル・フォルダーには触らない。シンボリックリンクは追わず、通常のファイルだけを対象にする。
3. **ライセンス**: 配布元の表示が食い違うので、どれを正とするか (配布元に問い合わせる / CC BY-SA 4.0 として扱う)。 → **決定 (リョウさん、2026-11-26)**: 同梱しない今の配り方では実質の差が無いので、CC BY-SA 4.0 として扱う (「実質の差がない場合はOKよ」)。同梱や改変を考えるときに改めて確かめる。
4. **右文脈を使うか** (v3.2 の新しい機能。カーソルの後ろの文字を渡す)。入力欄から後ろの文字を読むのは IMK の入力経路の変更 (CLAUDE.md §5) なので、この要件とは別にする (推奨)。 → **決定 (リョウさん、2026-11-26)**: 別の要件にして先に測る (「おすすめで」)。`docs/requirements/049-zenzai-right-context.md`。

## minimum の要件 (これだけは満たす)
1. `ZenzaiModel.Source.production` を zenz-v3.2-small Q5_K_M にする。URL はリビジョンを固定する (`https://huggingface.co/Miwa-Keita/zenz-v3.2-small-gguf/resolve/c67e03e07d215c869f591b274c1631170d3e11fe/ggml-model-Q5_K_M.gguf`)。サイズ 73,871,936、SHA-256 `29c223d4c23327b80fd13ebb5ab2555057a46317997d5da391584ffbef0db673` (2026-11-26 に Mac で取得したファイルで一致を確かめた)。ファイル名は v3.1 と別にする (`zenz-v3.2-small-Q5_K_M.gguf`)。
2. `THIRD-PARTY-NOTICES.md` の zenz の行を v3.2-small (ライセンスは「人に決めてほしいこと」3 で決めた表記、著作者・配布元・同梱しないこと) に直す。
3. 既定は OFF のまま (`Settings.Zenzai` の初期値は変えない)。
4. v3.2 の取得と検査が通ったあと、v3.1 のファイルを上の決定 2 の条件でだけ消す。消せなかったとき (無い・一致しない・権限) は何もしないで続ける (変換は止めない)。

## 非目標 (今回はやらない)
- xsmall を軽い Mac 用に出し分ける (精度が有意に落ちるため)
- 別系統のモデル (Jinen・kanakanji-edge・shuntelligence など)
- AI の答えの検査 (本家 1.6.0 の IsPlausible の移植。要件 048 で、2026-11-26 に見送りと決まった)
- 右文脈を Zenzai に渡す (要件 049)
- 日英判定 (Core) の変更

## 完成条件 (先に決める。機械で判定できる形)
- [ ] 計測 (Mac): `tools/ajimee-eval.py` で AJIMEE 200 件が 165 / 200 以上、Meltype-Flash の 40 件表が 37 / 40 以上 (しきい値は v3.1 の実測値。「今より下がらない」の意味)。v3.1 との McNemar で有意な悪化 (p < 0.05) が無い。数字を `docs/AJIMEE_BENCH_2026-11-24.md` (または新しい日付の文書) に残す
- [ ] `--zenzai-self-test` が新しい重みで通る (Mac)
- [ ] 検査: サイズか SHA-256 の違うファイルを渡すと、通信せずに止まる (今の ConvertEval の検査と同じ振る舞いが、新しい固定値でも保たれる)
- [ ] 回帰なし: Core のテスト全件通過、`tools/eval-compare.sh` の broken 0 (Core は変えない想定なので、変わらないことの確認)
- [ ] 前のファイルの削除の前提: v3.2 の取得か検査に失敗したとき (サイズ・SHA-256 の不一致、取得の失敗) は、v3.1 のファイルを消さない
- [ ] 前のファイルの削除: v3.1 の固定値と一致するファイルだけが消え、名前が同じでも中身の違うファイル・ほかのファイルは残る (Swift のテスト、または Mac での確認の手順と結果)
- [ ] 文書: THIRD-PARTY-NOTICES.md と `mac/README.md` に v3.1 の名前・URL・サイズが残っていない (過去の記録の文書は除く)

## Mac 実機チェックリスト (人が確かめる。AI は PR に写す)
- [ ] v3.1 を入れてある Mac で更新し、設定の Zenzai を ON のまま再起動すると、v3.2 が 1 回だけ取得され、変換に使われる
- [ ] 新しく入れた Mac で、設定の Zenzai を ON にしたときだけ取得が起きる (既定 OFF の案 (a) のとき)
- [ ] TextEdit: 文を打って Space で、候補が出る (Zenzai ON)
- [ ] 遅い Mac (あれば M1 級) で、変換が止まらない
- [ ] v3.2 を取得し終えたあと、models フォルダーから `zenz-v3.1-small-Q5_K_M.gguf` が消え、v3.2 のファイルだけが残る

## 止める条件 (この要件に固有のもの。共通は CLAUDE.md §5)
- 既定 ON・同梱は入れない (決定どおり)。前のファイルの削除は、決定 2 の条件より広げない (ほかのファイル・フォルダーを消したくなったら止めて人に聞く)
- 右文脈を渡す変更が要りそうになったとき (要件 049 で扱う)

## 記録 (ループが書く)
- 2026-11-26: Mac で測定 (00e1a25)。AJIMEE 166/200 (v3.1 165)・Flash 40 件 39/40 (v3.1 37)、McNemar で有意な悪化なし (p = 1.00 / 0.50)。自己テストは全 PASS (8c2 の 8 項目を含む、--network も)。Core 1371/1371。結果は docs/ZENZAI_V32_RIGHT_CONTEXT_2026-11-26.md。
- 2026-11-26: リョウさんが承認 (「オッケー承認します」)。status を approved にした。
- 2026-11-26: 前のファイルはアプリが確認して消す (決定 2)・右文脈は別の要件 049 (決定 4) をリョウさんが決定。人の判断事項はすべて決まった (status を approved にするのは人)。
- 2026-11-26: 既定 ON にしない (案 a) をリョウさんが決定。
- 2026-11-26: ライセンスの扱いをリョウさんが決定 (CC BY-SA 4.0 として扱う)。
- 2026-11-26: プロンプトの形式 (専用は無い・右文脈は任意) とライセンスの食い違いを追記。
- 2026-11-26: 下書き。計測は Mac 上の使い捨てブランチ (コミット・push なし) で行い、一時フォルダーは片付ける予定
