# AutoIME 技術設計書 v2

## 1. プロジェクト概要

### プロジェクト名

**AutoIME**

### 目的

Windowsにおける日本語入力・英語入力の切り替え操作を自動化する。

ユーザーが入力を開始した直後のキー入力を短時間だけ解析し、

- 日本語入力である可能性が高い → 日本語IMEへ切り替える
- 英語入力である可能性が高い → 現在の状態を維持する
- 判定不能 → 現在の状態を維持する

という保守的な自動切替を行う。

### 設計思想

**「常に入力を解析する」のではなく、「入力開始時だけ判定する」。**

また、

**「日本語だと思ったら切り替える」のではなく、「十分な確信がある場合だけ切り替える」。**

---

## 2. 対象環境

- Windows 10
- Windows 11
- x64 / ARM64

初期対象IME：

- Microsoft 日本語 IME

将来的に：

- Google 日本語入力
- その他のTSF対応IME

への対応を検討する。

---

## 3. AutoIMEが担当する範囲

AutoIME自身はIMEではない。

```text
アプリ
   ↓
Windows入力システム
   ↓
既存IME
```

という既存の入力システムを利用し、

```text
「現在の入力は日本語っぽい」
```

と判断した場合に、適切な入力状態へ変更する。

したがって、

- 日本語変換
- 変換候補表示
- かな漢字変換
- 文節解析

などはAutoIME自身では実装しない。

---

## 4. 全体アーキテクチャ

```text
┌──────────────────────────────┐
│          AutoIME             │
│                              │
│  ┌────────────────────────┐  │
│  │ KeyboardMonitor        │  │
│  │ キー入力監視           │  │
│  └───────────┬────────────┘  │
│              ↓               │
│  ┌────────────────────────┐  │
│  │ InputSession           │  │
│  │ 入力セッション管理     │  │
│  └───────────┬────────────┘  │
│              ↓               │
│  ┌────────────────────────┐  │
│  │ PendingInput            │  │
│  │ 判定中の入力保持        │  │
│  └───────────┬────────────┘  │
│              ↓               │
│  ┌────────────────────────┐  │
│  │ Detector               │  │
│  │                        │  │
│  │ ├─ RomajiDetector      │  │
│  │ ├─ KanaDetector        │  │
│  │ ├─ EnglishDetector     │  │
│  │ ├─ TypoDetector        │  │
│  │ └─ ScoreEngine         │  │
│  └───────────┬────────────┘  │
│              ↓               │
│       判定結果               │
│         ↙       ↘            │
│     日本語       それ以外     │
│       ↓             ↓        │
│ ImeController    通常処理     │
│       ↓                      │
│  PendingInput再入力          │
│                              │
│  ┌────────────────────────┐  │
│  │ UserModel              │  │
│  │ ユーザー学習           │  │
│  └────────────────────────┘  │
└──────────────────────────────┘
```

---

## 5. 入力セッション

AutoIMEでは「入力セッション」を単位として判定する。

### セッション開始

以下の場合に新しいセッションを開始する。

- 文字入力開始
- Enter後
- Space後
- Escape後
- 一定時間入力がない
- フォーカス変更

### セッション状態

```text
Idle
  ↓
Collecting
  ↓
Classifying
  ↓
Committed
```

### 状態説明

#### Idle

入力待機中。

#### Collecting

入力開始直後。

数文字を取得して判定材料を集める。

#### Classifying

日本語/英語の判定を行う。

#### Committed

判定終了。

同じセッションでは再判定しない。

---

## 6. Pending Input

入力判定中に入力を失わないため、最初のキー入力を一時的に保持する。

```text
ユーザー
  ↓
"k"
  ↓
PendingInput
  ↓
"ko"
  ↓
"konn"
  ↓
判定
```

判定結果によって処理する。

### 日本語の場合

```text
IME状態変更
      ↓
PendingInput再投入
      ↓
既存IMEが処理
```

### 英語の場合

```text
PendingInputをそのまま出力
```

### 不明の場合

```text
PendingInputをそのまま出力
```

---

## 7. 再入力

IME切り替え後にPendingInputを再入力する。

ただし、再入力したキーをAutoIME自身が再度検出するとループする。

そのため、入力イベントに対して、

```text
InjectedInput
```

と

```text
PhysicalInput
```

を区別する。

再入力したキーはAutoIMEの判定対象から除外する。

---

## 8. IME状態モデル

「IME ON/OFF」と「入力言語」を別々に扱う。

AutoIME内部では以下を独立した状態として管理する。

```text
InputLanguage
    ├─ Japanese
    └─ English / Other

ImeMode
    ├─ Open
    └─ Closed
```

AutoIMEが必要とする状態は、

```text
Japanese + Open
```

である。

単純に「IME ON」というフラグだけを見て判断しない。

---

## 9. IME Controller

IME操作は専用モジュールに分離する。

```text
ImeController
```

責務：

- 現在の入力言語取得
- 現在のIME状態取得
- 日本語入力への切り替え
- IME開閉
- 必要に応じて元の入力状態を復元

---

## 10. Windows入力API

実装候補：

### Text Services Framework

現代Windowsの入力システムとの互換性を考慮し、主要な実装候補とする。

### IMM32

互換性・簡易操作用として利用する。

```text
ImeController
    ├─ TsfImeController
    └─ Imm32ImeController
```

のように抽象化する。

アプリケーションごとの互換性問題をAutoIME本体から切り離す。

---

## 11. キー入力監視

初期実装ではWindowsのキーボードイベントを監視する。

候補：

```text
SetWindowsHookEx
WH_KEYBOARD_LL
```

ただし、

- IME
- TSF
- 管理者権限アプリ
- UWP/WinUI
- Electron
- ゲーム
- Remote Desktop

などで挙動が異なる可能性がある。

そのためKeyboardMonitorも独立モジュールにする。

---

## 12. Detector

Detectorは「日本語か英語か」を直接決定するのではなく、複数の判定器からスコアを取得する。

```text
Detector
├─ RomajiDetector
├─ KanaDetector
├─ EnglishDetector
├─ DictionaryDetector
└─ TypoDetector
```

最終判定：

```text
ScoreEngine
```

---

## 13. ローマ字判定

ローマ字→かな変換可能性を評価する。

例：

```text
konnichiwa
arigatou
ohayou
watashi
```

など。

ただし、

```text
kana
sushi
radio
```

などは英語としても成立するため、

**ローマ字として解釈できるだけではIME ONにしない。**

日本語辞書・パターン・ユーザー学習などと組み合わせる。

---

## 14. かな入力判定

かな入力についても、単純な文字列判定ではなく、

```text
現在のキーボードレイアウト
+
キー入力パターン
+
日本語としての妥当性
```

を利用する。

AutoIMEは、

> 「ローマ字入力なのか、かな入力なのか」

を完全に識別する必要はない。

最終的に、

> 「日本語入力を開始した可能性が高い」

と判断できればよい。

---

## 15. Typo対応

Typoは補助的な判定材料として使用する。

例：

```text
konnichiwa
konnitiwa
konnichia
```

など。

Levenshtein distance等を利用して類似度を計算する。

ただし、

**Typo一致だけではIMEをONにしない。**

他の判定材料と組み合わせる。

---

## 16. 英語判定

明らかな英語や技術用語を除外する。

例：

```text
github
typescript
javascript
npm
node
discord
localhost
server
terminal
commit
```

など。

英語スコアが高い場合、

```text
日本語スコアが多少高い
```

程度ではIMEを切り替えない。

---

## 17. Score Engine

各Detectorの結果を統合する。

概念：

```text
JapaneseScore =
    RomajiScore
  + KanaScore
  + DictionaryScore
  + TypoScore
  + UserScore

EnglishScore =
    EnglishDictionaryScore
  + UserEnglishScore
  + ExclusionScore
```

判定：

```text
JapaneseScore >= HIGH_THRESHOLD
    ↓
Japanese

それ以外
    ↓
NoChange
```

重要なのは、

**「日本語ではない」と判断する必要もない**

ということ。

```text
Japanese
English
Unknown
```

の3値判定とする。

Unknownなら何もしない。

---

## 18. 誤爆防止

AutoIMEはFalse Positiveを特に重視する。

つまり、

```text
英語なのに日本語IMEになる
```

ことを、

```text
日本語なのにIMEが切り替わらない
```

より重大な問題として扱う。

そのため初期設定では高い閾値を使用する。

---

## 19. ユーザー学習

ユーザーごとに判定結果を記録する。

ただし、入力内容そのものを大量保存しない。

例：

```json
{
  "prefixes": {
    "konn": {
      "japanese": 15,
      "english": 0
    },
    "dev": {
      "japanese": 0,
      "english": 21
    }
  }
}
```

ユーザーが日本語入力を続けた場合：

```text
JapaneseWeight += 1
```

英語だった場合：

```text
EnglishWeight += 1
```

誤判定した場合は該当パターンの重みを下げる。

---

## 20. 学習の目的

機械学習モデルを作ることが目的ではない。

目的は、

```text
初期状態
 ↓
一般的な判定
 ↓
ユーザー固有の入力傾向を学習
 ↓
個人に合わせて閾値・スコアを調整
```

すること。

---

## 21. データ保存

保存場所：

```text
%LOCALAPPDATA%\AutoIME\
```

例：

```text
config.json
model.json
```

ネットワークへ入力データを送信しない。

---

## 22. アプリ別設定

将来的にアプリごとに挙動を変更できるようにする。

例：

```text
VS Code
  → 自動切替 ON

ブラウザ
  → 自動切替 ON

ターミナル
  → 自動切替 ON

ゲーム
  → 自動切替 OFF
```

アプリ識別にはプロセス情報等を利用する。

---

## 23. モジュール構成

```text
AutoIME/
├── src/
│   ├── AutoIME/
│   │   ├── Program.cs
│   │   │
│   │   ├── Input/
│   │   │   ├── KeyboardMonitor.cs
│   │   │   ├── InputSession.cs
│   │   │   └── PendingInput.cs
│   │   │
│   │   ├── Detection/
│   │   │   ├── RomajiDetector.cs
│   │   │   ├── KanaDetector.cs
│   │   │   ├── EnglishDetector.cs
│   │   │   ├── TypoDetector.cs
│   │   │   └── ScoreEngine.cs
│   │   │
│   │   ├── IME/
│   │   │   ├── ImeController.cs
│   │   │   ├── TsfImeController.cs
│   │   │   └── Imm32ImeController.cs
│   │   │
│   │   ├── Learning/
│   │   │   └── UserModel.cs
│   │   │
│   │   └── Config/
│   │       └── Settings.cs
│   │
│   └── AutoIME.Tests/
│
├── dictionaries/
├── docs/
└── README.md
```

---

## 24. MVP

最初のバージョンでは機能を絞る。

### v0.1

実装するもの：

- キーボード監視
- 入力セッション
- PendingInput
- ローマ字判定
- 英語除外
- 日本語IME切り替え
- 再入力

まだ実装しない：

- Typo
- ユーザー学習
- GUI
- かな入力の高度な判定
- 複数IME対応

---

## 25. v0.2

- かな入力対応
- 判定精度改善
- 英語辞書
- 除外語
- アプリ別除外

---

## 26. v0.3

- Typo対応
- 編集距離
- 日本語候補辞書

---

## 27. v0.4

- ユーザー学習
- 個人別スコア
- 誤判定フィードバック

---

## 28. v0.5

- タスクトレイ
- 設定画面
- ログ表示
- 判定理由のデバッグ表示

---

## 29. v1.0

- TSF対応の安定化
- IMM32フォールバック
- 複数IME対応
- アプリ別設定
- インストーラー
- 自動起動
- テスト強化

---

## 30. テスト方針

最低限、以下をテストする。

### 日本語

```text
konnichiwa
arigatou
ohayou
watashi
ashita
```

### Typo

```text
konnitiwa
konnichia
arigatouu
```

### 英語

```text
hello
github
typescript
javascript
server
terminal
```

### 技術入力

```text
npm install
git commit
git push
localhost
http://
```

### かな入力

かな入力環境で一般的な日本語入力を実際に行い、入力欠落・二重入力・誤切替がないことを確認する。

---

## 31. 重要な品質基準

以下を最優先する。

1. **入力を失わない**
2. **二重入力しない**
3. **勝手にIMEを切り替えすぎない**
4. **入力遅延を発生させない**
5. **既存IMEの変換機能を壊さない**

判定精度よりも、まず入力システムとしての安全性を優先する。

---

## 32. 最終的な動作イメージ

### 英語

```text
hello world
```

そのまま入力。

### 日本語

```text
konnichiwa
```

入力開始
↓
日本語と判定
↓
日本語IMEへ切り替え
↓
保留していた入力を再投入
↓
通常のIMEとして入力継続

### 不明

```text
test
```

判断できない
↓
何もしない

---

## 33. 最終目標

AutoIMEは新しいIMEを作るプロジェクトではない。

**既存のIMEをユーザーが意識せず使えるようにする入力補助ツール**である。

最終的なユーザー体験：

```text
英語を書き始める
→ そのまま英語

日本語を書き始める
→ 自動で日本語IME

判断できない
→ 何もしない
```

ユーザーが「英数」「かな」を切り替える操作そのものを減らす。

---

## 34. 実装上の原則

- 判定は入力開始時のみ
- 不明なら何もしない
- 誤爆を最小化する
- 入力を必ず保全する
- IMEそのものは実装しない
- Windowsの既存入力システムを利用する
- IME操作を独立モジュールにする
- Detectorを独立させる
- 学習データはローカルに保存する
- AI/LLMは必須にしない

**まず「安全に切り替えられるMVP」を完成させ、その後に判定精度を上げる。**
