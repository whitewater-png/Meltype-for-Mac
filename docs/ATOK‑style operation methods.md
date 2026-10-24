ATOKの左矢印/右矢印キーによる候補の広げ縮め挙動をMeltypeにオプションで追加するための設計と実装手順を以下にまとめます。

## 実装概要

Meltypeに「ATOKスタイルキー操作」オプションを追加し、有効時は変換中の左矢印キーで候補を展開（より多くの候補を表示）、右矢印キーで候補を縮め（最初の数個の候補のみ表示）します。無効時は現在の文節選択挙動を維持します。

### 追加する設定項目
- **設定名**: `TokMode` (Boolean)
- **デフォルト**: `false` (オフ)
- **説明**: 「左矢印キーで候補を広げ、右矢印キーで候補を縮める（ATOKのような操作）」
- **カテゴリ**: 「1. 全般」または「変換・候補」

## 具体的な実装手順

### 1. Settingsクラスにプロパティを追加
`src/Meltype.Core/Config/Settings.cs` に以下を追加：

```csharp
[Category("1. 全般"), DisplayName("ATOK スタイルのキー操作"), 
 Description("左矢印キーで候補を広げ、右矢印キーで候補を縮める（ATOK のような操作）。")]
public bool TokMode { get; set; } = false;
```

### 2. Mac設定カタログに項目を追加
`src/Meltype.Core/Config/MacSettingsCatalog.cs` の `Items` コレクションに以下を追加（適切なグループ、例えば「変換・候補」）：

```csharp
Bool(nameof(Settings.TokMode), "ATOK スタイルのキー操作", 
     "左矢印キーで候補を広げ、右矢印キーで候補を縮める（ATOK のような操作）。",
     s => s.TokMode, (s, v) => s.TokMode = v)
```

### 3. CompositionOptionsにプロパティを追加
`src/Meltype.Core/Composition/CompositionController.cs` の `CompositionOptions` クラスに：

```csharp
/// <summary>ATOK スタイルのキー操作（左矢印で候補を広げ、右矢印で候補を縮める）。</summary>
public Func<bool> TokMode { get; init; } = () => false;
```

### 4. オプション生成ロジックを更新
`MeltypeEngine.cs` または `SharedSettings.cs` での `CompositionOptions` 生成部分に：

```csharp
TokMode = () => settings.Tokmode,
```

### 5. ConversionControllerのキー処理を修正
`src/Meltype.Core/Composition/CompositionController.cs` の `HandleConversionKey` メソッドの冒頭（`_reconvertRomaji` ブロック後、`switch` 文前）に以下を追加：

```csharp
// TokMode: 左右キーで候補の広げ縮め
if (_options.TokMode())
{
    switch (vk)
    {
        case VirtualKeys.Left:
            var clause = _clauses[_selectedClause];
            if (!clause.Expanded)
            {
                Expand(clause); // 候補を全表示に展開
            }
            return true;
        case VirtualKeys.Right:
            var clause = _clauses[_selectedClause];
            if (clause.Expanded)
            {
                clause.Expanded = false;
                clause.Changed = true; // 候補を最初の9個に縮小
            }
            return true;
    }
}
```

### 6. 動作の説明
- **TokMode オン時**:
  - **← キー**: 現在の候補が縮小表示中なら全表示に展開（`Expand()`を呼び出し）
  - **→ キー**: 現在の候補が全表示なら最初の9個に縮小（`Expanded`フラグをfalseに）
  - 既に目的の状態なら何もしない
  - Space/↓/↑/Enterなどの他キーは現在の挙動を維持
- **TokMode オフ時（デフォルト）**: 現在の文節選択挙動（←/→で文節切り替え）を維持

## 注意点
- この変更は変換中（`_converting == true`）のみ有効です
- 候補の「広げ縮め」は表示される候補数を変更するだけで、選択中の候補インデックスは変更しません
- 縮小時は最初の9個の候補（または総数が9未満の場合は全候補）が表示されます
- 既存の `ShiftEnterNewline` 設定と同様の実装パターンに従っています

この実装により、ユーザーは設定で「ATOK スタイルのキー操作」をオンにすることで、ATOKと同様の左矢印/右矢印キーによる候補操作をMeltypeで使用できるようになります。オフにすれば現在のMeltypeの挙動に戻ります。