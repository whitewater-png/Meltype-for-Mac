// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

namespace Meltype.Input;

/// <summary>
/// 判定中に保留している打鍵 (設計書 §6)。キーアップや英字以外のキーも含め、届いた順のまま保持する。
/// 判定後は同じ順序で再入力するので、入力の欠落・二重入力・順序の入れ替わりが起きない。
/// </summary>
internal sealed class PendingInput
{
    private List<KeyEvent> _events = [];

    public int Count => _events.Count;

    public IReadOnlyList<KeyEvent> Events => _events;

    public void Add(KeyEvent e) => _events.Add(e);

    /// <summary>保留分をすべて取り出して空にする。</summary>
    public List<KeyEvent> TakeAll()
    {
        var taken = _events;
        _events = [];
        return taken;
    }

    public void Clear() => _events.Clear();
}
