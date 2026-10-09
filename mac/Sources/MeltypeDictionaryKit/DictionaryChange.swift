// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import Foundation

/// 画面からの変更 1 回分。実行すると、元に戻すための変更 (逆の操作) を返す。画面は NSUndoManager にそれを積む
/// (元に戻すときも同じ perform を使うので、やり直しも同じ仕組みで積まれる)。
public enum DictionaryChange: Equatable {
    /// ユーザー辞書に登録する。
    case add(WordKey)
    /// まとめて登録する (登録済みは飛ばす。取り消すと、新しく登録した語だけを消す)。
    case addMany([WordKey])
    /// ユーザー辞書から消す (同じ読み・単語は全部)。
    case remove([WordKey])
    /// 消した語を元の位置に戻す。
    case restore([RemovedEntry])
    /// ユーザー辞書の語を直す (位置はそのまま)。
    case update(from: WordKey, to: WordKey)
    /// 専門用語を除外する (変換に使わない)。
    case exclude([WordKey])
    /// 専門用語の除外をやめる。
    case include([WordKey])
    /// 専門用語を直す (直した語をユーザー辞書に登録し、元の語を除外する)。originalExcluded は、直す前から元の語を除外していたか。
    case editTerm(original: WordKey, to: WordKey, originalExcluded: Bool)
    /// 専門用語の編集を取り消す (ユーザー辞書に新しく登録していたら消し、直す前は除外していなかった元の語の除外をやめる)。
    case revertTermEdit(original: WordKey, to: WordKey, removeFromUser: Bool, originalExcluded: Bool)
    /// ユーザー辞書の語を、自作の専門用語集へ移す (ユーザー辞書からは消える)。
    case moveToDomain(id: String, keys: [WordKey])
    /// 移したのを取り消す (専門用語集に足した語を消し、ユーザー辞書へ元の位置に戻す)。
    case undoMove(id: String, added: [WordKey], removed: [RemovedEntry])
    /// 自作の専門用語集に語を足す。
    case addTerms(id: String, keys: [WordKey])
    /// 自作の専門用語集から語を消す。
    case removeTerms(id: String, keys: [WordKey])
    /// 自作の専門用語集から消した語を元の位置に戻す。
    case restoreTerms(id: String, entries: [RemovedTerm])
    /// 自作の専門用語集の語を直す。
    case updateTerm(id: String, from: WordKey, to: WordKey)
    /// 自作の専門用語集を消す。
    case deleteDomain(id: String)
    /// 消した自作の専門用語集を戻す。
    case restoreDomain(DeletedDomain)
    /// 自作の専門用語集の名前を変える。
    case renameDomain(id: String, to: String, from: String)

    /// 「取り消す」メニューに出す名前。
    public var actionName: String {
        switch self {
        case .add: return "単語の登録"
        case .addMany: return "ユーザー辞書への複製"
        case .remove, .restore: return "単語の削除"
        case .update: return "単語の編集"
        case .exclude, .include: return "専門用語の除外"
        case .editTerm, .revertTermEdit: return "専門用語の編集"
        case .moveToDomain, .undoMove: return "専門用語集へ移す"
        case .addTerms: return "専門用語の登録"
        case .removeTerms, .restoreTerms: return "専門用語の削除"
        case .updateTerm: return "専門用語の編集"
        case .deleteDomain, .restoreDomain: return "専門用語集の削除"
        case .renameDomain: return "専門用語集の名前の変更"
        }
    }

    /// 実行する。だめなら理由 (何も変わっていない)。よければ、元に戻すための変更 (変わらなかったときは nil)。
    public func perform(on operations: DictionaryOperations) -> (error: String?, undo: DictionaryChange?) {
        switch self {
        case let .add(key):
            if let error = operations.add(key) { return (error, nil) }
            return (nil, .remove([key]))
        case let .addMany(keys):
            let result = operations.addMany(keys)
            if let error = result.error { return (error, nil) }
            return (nil, result.added.isEmpty ? nil : .remove(result.added))
        case let .remove(keys):
            let result = operations.remove(keys)
            if let error = result.error { return (error, nil) }
            return (nil, result.removed.isEmpty ? nil : .restore(result.removed))
        case let .restore(entries):
            if let error = operations.restore(entries) { return (error, nil) }
            return (nil, .remove(entries.map(\.key)))
        case let .update(old, new):
            if let error = operations.update(from: old, to: new) { return (error, nil) }
            return (nil, old == new ? nil : .update(from: new, to: old))
        case let .exclude(keys):
            if let error = operations.setExcluded(keys, excluded: true) { return (error, nil) }
            return (nil, .include(keys))
        case let .include(keys):
            if let error = operations.setExcluded(keys, excluded: false) { return (error, nil) }
            return (nil, .exclude(keys))
        case let .editTerm(original, new, originalExcluded):
            let result = operations.editTerm(original: original, to: new)
            // 本体は途中でだめになると戻すが、戻すことにも失敗して登録だけ残ったとき (added) は、それを消す取り消しを積む
            if let error = result.error { return (error, result.added ? .remove([new]) : nil) }
            return (nil, .revertTermEdit(original: original, to: new, removeFromUser: result.added, originalExcluded: originalExcluded))
        case let .revertTermEdit(original, new, removeFromUser, originalExcluded):
            // 先に除外をやめ (だめなら何も変えていない)、次にユーザー辞書から消す。消せなければ除外をやり直して、元の状態に戻す
            if !originalExcluded, let error = operations.setExcluded([original], excluded: false) { return (error, nil) }
            if removeFromUser, let error = operations.remove([new]).error {
                if !originalExcluded { _ = operations.setExcluded([original], excluded: true) }
                return (error, nil)
            }
            return (nil, .editTerm(original: original, to: new, originalExcluded: originalExcluded))
        case let .moveToDomain(id, keys):
            let result = operations.moveToDomain(id: id, keys: keys)
            let undo: DictionaryChange? = result.outcome.added.isEmpty && result.outcome.removed.isEmpty ? nil : .undoMove(id: id, added: result.outcome.added, removed: result.outcome.removed)
            // 専門用語集に書けたのにユーザー辞書から消せなかったとき (error) も、足した語を取り消せるようにしておく
            return (result.error, undo)
        case let .undoMove(id, added, removed):
            // 先にユーザー辞書へ戻す (だめでも、語は専門用語集にあるまま消えない)。次に専門用語集から消す。
            if !removed.isEmpty, let error = operations.restore(removed) { return (error, nil) }
            if !added.isEmpty, let error = operations.removeTerms(id: id, keys: added).error { return (error, nil) }
            return (nil, removed.isEmpty ? nil : .moveToDomain(id: id, keys: removed.map(\.key)))
        case let .addTerms(id, keys):
            let result = operations.addTerms(id: id, keys: keys)
            if let error = result.error { return (error, nil) }
            return (nil, result.added.isEmpty ? nil : .removeTerms(id: id, keys: result.added))
        case let .removeTerms(id, keys):
            let result = operations.removeTerms(id: id, keys: keys)
            if let error = result.error { return (error, nil) }
            return (nil, result.removed.isEmpty ? nil : .restoreTerms(id: id, entries: result.removed))
        case let .restoreTerms(id, entries):
            if let error = operations.restoreTerms(id: id, entries: entries) { return (error, nil) }
            return (nil, .removeTerms(id: id, keys: entries.map(\.key)))
        case let .updateTerm(id, old, new):
            if let error = operations.updateTerm(id: id, from: old, to: new) { return (error, nil) }
            return (nil, old == new ? nil : .updateTerm(id: id, from: new, to: old))
        case let .deleteDomain(id):
            let result = operations.deleteDomain(id: id)
            if let error = result.error { return (error, nil) }
            return (nil, result.deleted.map { .restoreDomain($0) })
        case let .restoreDomain(deleted):
            // 戻せなかったときは、同じ内容をもう一度戻せるようにしておく (消した専門用語集の中身を、取り消しと一緒に失わないため)
            if let error = operations.restoreDomain(deleted) { return (error, self) }
            return (nil, .deleteDomain(id: deleted.id))
        case let .renameDomain(id, new, old):
            if let error = operations.renameDomain(id: id, name: new) { return (error, nil) }
            return (nil, .renameDomain(id: id, to: old, from: new))
        }
    }
}
