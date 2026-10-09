// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import Foundation

// libMeltypeNative.dylib (C# の Meltype.Core を NativeAOT にしたもの) の、辞書の管理画面用の関数。src/Meltype.Mac.Native/Exports.cs と合わせる。
private typealias AbiVersionFunction = @convention(c) () -> Int32
private typealias FreeFunction = @convention(c) (UnsafeMutableRawPointer?) -> Void
private typealias TextFunction = @convention(c) () -> UnsafeMutablePointer<CChar>?
private typealias IntFunction = @convention(c) () -> Int32
private typealias Text1Function = @convention(c) (UnsafePointer<CChar>?) -> UnsafeMutablePointer<CChar>?
private typealias Text2Function = @convention(c) (UnsafePointer<CChar>?, UnsafePointer<CChar>?) -> UnsafeMutablePointer<CChar>?
private typealias Text4Function = @convention(c) (UnsafePointer<CChar>?, UnsafePointer<CChar>?, UnsafePointer<CChar>?, UnsafePointer<CChar>?) -> UnsafeMutablePointer<CChar>?
private typealias TextOutFunction = @convention(c) (UnsafePointer<CChar>?, UnsafeMutablePointer<UnsafeMutablePointer<CChar>?>?) -> UnsafeMutablePointer<CChar>?
private typealias TextOut2Function = @convention(c) (UnsafePointer<CChar>?, UnsafeMutablePointer<UnsafeMutablePointer<CChar>?>?, UnsafeMutablePointer<UnsafeMutablePointer<CChar>?>?) -> UnsafeMutablePointer<CChar>?
private typealias TextCountFunction = @convention(c) (UnsafePointer<CChar>?, UnsafeMutablePointer<Int32>?) -> UnsafeMutablePointer<CChar>?
private typealias TextFlagFunction = @convention(c) (UnsafePointer<CChar>?, Int32) -> UnsafeMutablePointer<CChar>?
private typealias SetTermDomainFunction = @convention(c) (UnsafePointer<CChar>?, Int32) -> Int32
private typealias Text5Function = @convention(c) (UnsafePointer<CChar>?, UnsafePointer<CChar>?, UnsafePointer<CChar>?, UnsafePointer<CChar>?, UnsafePointer<CChar>?) -> UnsafeMutablePointer<CChar>?
private typealias Text2OutFunction = @convention(c) (UnsafePointer<CChar>?, UnsafePointer<CChar>?, UnsafeMutablePointer<UnsafeMutablePointer<CChar>?>?) -> UnsafeMutablePointer<CChar>?
private typealias Text2Out2Function = @convention(c) (UnsafePointer<CChar>?, UnsafePointer<CChar>?, UnsafeMutablePointer<UnsafeMutablePointer<CChar>?>?, UnsafeMutablePointer<UnsafeMutablePointer<CChar>?>?) -> UnsafeMutablePointer<CChar>?
private typealias Text2Out3Function = @convention(c) (UnsafePointer<CChar>?, UnsafePointer<CChar>?, UnsafeMutablePointer<UnsafeMutablePointer<CChar>?>?, UnsafeMutablePointer<UnsafeMutablePointer<CChar>?>?, UnsafeMutablePointer<UnsafeMutablePointer<CChar>?>?) -> UnsafeMutablePointer<CChar>?
private typealias Text2CountFunction = @convention(c) (UnsafePointer<CChar>?, UnsafePointer<CChar>?, UnsafeMutablePointer<Int32>?) -> UnsafeMutablePointer<CChar>?
private typealias TextOutIntFunction = @convention(c) (UnsafePointer<CChar>?, UnsafeMutablePointer<UnsafeMutablePointer<CChar>?>?, UnsafeMutablePointer<Int32>?) -> UnsafeMutablePointer<CChar>?
private typealias Text2FlagFunction = @convention(c) (UnsafePointer<CChar>?, UnsafePointer<CChar>?, Int32) -> UnsafeMutablePointer<CChar>?
private typealias TermEditFunction = @convention(c) (UnsafePointer<CChar>?, UnsafePointer<CChar>?, UnsafePointer<CChar>?, UnsafePointer<CChar>?, UnsafeMutablePointer<Int32>?) -> UnsafeMutablePointer<CChar>?

/// 辞書の管理画面が使う操作 (本物は NativeDictionary。--self-test は偽物でも確かめる)。理由の文字列は、だめなときだけ (画面にそのまま出す)。
public protocol DictionaryOperations: AnyObject {
    func add(_ key: WordKey) -> String?
    /// まとめて登録する (登録済み・不正は飛ばす)。新しく登録した語を返す。
    func addMany(_ keys: [WordKey]) -> (error: String?, added: [WordKey])
    func update(from old: WordKey, to new: WordKey) -> String?
    func remove(_ keys: [WordKey]) -> (error: String?, removed: [RemovedEntry])
    func restore(_ entries: [RemovedEntry]) -> String?
    func setExcluded(_ keys: [WordKey], excluded: Bool) -> String?
    func editTerm(original: WordKey, to new: WordKey) -> (error: String?, added: Bool)

    // ---- 自作の専門用語集 ----

    /// ユーザー辞書の語を自作の専門用語集へ移す。専門用語集に書けたのにユーザー辞書から消せなかったときは理由を返すが、outcome.added は返す。
    func moveToDomain(id: String, keys: [WordKey]) -> (error: String?, outcome: MoveOutcome)
    /// 自作の専門用語集に語を足す (すでにある語は飛ばす)。足した語と、入れられなかった語。
    func addTerms(id: String, keys: [WordKey]) -> (error: String?, added: [WordKey], skipped: [SkippedWord])
    func removeTerms(id: String, keys: [WordKey]) -> (error: String?, removed: [RemovedTerm])
    func restoreTerms(id: String, entries: [RemovedTerm]) -> String?
    func updateTerm(id: String, from old: WordKey, to new: WordKey) -> String?
    func deleteDomain(id: String) -> (error: String?, deleted: DeletedDomain?)
    func restoreDomain(_ deleted: DeletedDomain) -> String?
    func renameDomain(id: String, name: String) -> String?
}

/// libMeltypeNative.dylib を読み込んで、ユーザー辞書・専門用語集を操作する (中身は C# の DictionaryManagement / UserDictionary / TermDomains)。
/// 関数はどれも、C# 側でロックしているので、どのスレッドから呼んでもよい (専門用語の一覧は重いので裏のスレッドで呼ぶ)。
public final class NativeDictionary: DictionaryOperations {
    /// この Swift が前提にしている FFI の版数。src/Meltype.Mac.Native/Exports.cs の AbiVersion・IME の NativeCore.expectedAbiVersion と必ず同じにする。
    public static let expectedAbiVersion: Int32 = 8

    /// 読み込めなかった理由 (画面に出す)。
    public enum LoadError: Error, CustomStringConvertible {
        case notFound
        case cannotOpen(String)
        case missingFunction(String)
        case abiMismatch(expected: Int32, actual: Int32)

        public var description: String {
            switch self {
            case .notFound: return "Meltype の本体 (libMeltypeNative.dylib) が見つかりません。Meltype を入れ直してください。"
            case let .cannotOpen(detail): return "Meltype の本体 (libMeltypeNative.dylib) を読み込めませんでした: \(detail)"
            case let .missingFunction(name): return "Meltype の本体に \(name) がありません (版が合っていません)。Meltype を入れ直してください。"
            case let .abiMismatch(expected, actual): return "Meltype の本体の版が合いません (期待 \(expected) / 実際 \(actual))。Meltype を入れ直してください。"
            }
        }
    }

    /// 本体の場所: この画面は Meltype.app/Contents/Helpers/MeltypeDictionary.app に入っているので、外側の Meltype.app/Contents/Frameworks のもの。
    /// (自分の Contents/Frameworks に置いた場合も探す。) 見つからなければ nil。環境変数では差し替えない (別のライブラリを読ませる口を作らないため)。
    public static func defaultLibraryPath(bundle: Bundle = .main) -> String? {
        let outer = bundle.bundleURL.deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("Frameworks/libMeltypeNative.dylib").path
        let own = (bundle.privateFrameworksPath ?? "") + "/libMeltypeNative.dylib"
        return [outer, own].first { FileManager.default.fileExists(atPath: $0) }
    }

    private let freeFunction: FreeFunction
    private let dataDirectoryFunction: TextFunction
    private let wordsFunction: TextFunction
    private let versionFunction: IntFunction
    private let checkFunction: Text4Function
    private let addFunction: Text2Function
    private let addManyFunction: TextOutFunction
    private let problemFunction: TextFunction
    private let updateFunction: Text4Function
    private let removeFunction: TextOutFunction
    private let restoreFunction: Text1Function
    private let importFunction: TextOut2Function
    private let exportFunction: TextCountFunction
    private let toReadingFunction: Text1Function
    private let termDomainsFunction: TextFunction
    private let setTermDomainFunction: SetTermDomainFunction
    private let termWordsFunction: Text1Function
    private let termExcludedFunction: TextFunction
    private let termSetExcludedFunction: TextFlagFunction
    private let termEditFunction: TermEditFunction
    private let termRevisionFunction: IntFunction
    private let termCreateFunction: TextOutFunction
    private let termRenameFunction: Text2Function
    private let termDeleteFunction: TextOutIntFunction
    private let termRestoreDomainFunction: Text2FlagFunction
    private let termUserCheckFunction: Text5Function
    private let termUserAddFunction: Text2Out2Function
    private let termUserRemoveFunction: Text2OutFunction
    private let termUserRestoreFunction: Text2Function
    private let termUserUpdateFunction: Text5Function
    private let termMoveFunction: Text2Out3Function
    private let termExportFunction: Text2CountFunction
    private let termImportFunction: TextOut2Function
    private let settingsGetFunction: TextFunction
    private let settingsSetFunction: Text2Function
    private let settingsResetFunction: TextFunction

    public init(libraryPath: String?) throws {
        guard let libraryPath else { throw LoadError.notFound }
        guard let handle = dlopen(libraryPath, RTLD_NOW) else {
            throw LoadError.cannotOpen(dlerror().map { String(cString: $0) } ?? libraryPath)
        }
        func symbol<T>(_ name: String, as type: T.Type) throws -> T {
            guard let pointer = dlsym(handle, name) else { throw LoadError.missingFunction(name) }
            return unsafeBitCast(pointer, to: type)
        }
        // 版数を先に確かめる (違う版の関数を、引数の合わないまま呼ばないため)
        let abi = try symbol("meltype_abi_version", as: AbiVersionFunction.self)()
        guard abi == Self.expectedAbiVersion else { throw LoadError.abiMismatch(expected: Self.expectedAbiVersion, actual: abi) }
        freeFunction = try symbol("meltype_free", as: FreeFunction.self)
        dataDirectoryFunction = try symbol("meltype_data_directory", as: TextFunction.self)
        wordsFunction = try symbol("meltype_userdict_words", as: TextFunction.self)
        versionFunction = try symbol("meltype_userdict_version", as: IntFunction.self)
        checkFunction = try symbol("meltype_userdict_check", as: Text4Function.self)
        addFunction = try symbol("meltype_userdict_add", as: Text2Function.self)
        addManyFunction = try symbol("meltype_userdict_add_many", as: TextOutFunction.self)
        problemFunction = try symbol("meltype_userdict_problem", as: TextFunction.self)
        updateFunction = try symbol("meltype_userdict_update", as: Text4Function.self)
        removeFunction = try symbol("meltype_userdict_remove", as: TextOutFunction.self)
        restoreFunction = try symbol("meltype_userdict_restore", as: Text1Function.self)
        importFunction = try symbol("meltype_userdict_import", as: TextOut2Function.self)
        exportFunction = try symbol("meltype_userdict_export", as: TextCountFunction.self)
        toReadingFunction = try symbol("meltype_to_reading", as: Text1Function.self)
        termDomainsFunction = try symbol("meltype_term_domains", as: TextFunction.self)
        setTermDomainFunction = try symbol("meltype_set_term_domain", as: SetTermDomainFunction.self)
        termWordsFunction = try symbol("meltype_term_words", as: Text1Function.self)
        termExcludedFunction = try symbol("meltype_term_excluded", as: TextFunction.self)
        termSetExcludedFunction = try symbol("meltype_term_set_excluded", as: TextFlagFunction.self)
        termEditFunction = try symbol("meltype_term_edit", as: TermEditFunction.self)
        termRevisionFunction = try symbol("meltype_term_revision", as: IntFunction.self)
        termCreateFunction = try symbol("meltype_term_create", as: TextOutFunction.self)
        termRenameFunction = try symbol("meltype_term_rename", as: Text2Function.self)
        termDeleteFunction = try symbol("meltype_term_delete", as: TextOutIntFunction.self)
        termRestoreDomainFunction = try symbol("meltype_term_restore_domain", as: Text2FlagFunction.self)
        termUserCheckFunction = try symbol("meltype_term_user_check", as: Text5Function.self)
        termUserAddFunction = try symbol("meltype_term_user_add", as: Text2Out2Function.self)
        termUserRemoveFunction = try symbol("meltype_term_user_remove", as: Text2OutFunction.self)
        termUserRestoreFunction = try symbol("meltype_term_user_restore", as: Text2Function.self)
        termUserUpdateFunction = try symbol("meltype_term_user_update", as: Text5Function.self)
        termMoveFunction = try symbol("meltype_term_move", as: Text2Out3Function.self)
        termExportFunction = try symbol("meltype_term_export", as: Text2CountFunction.self)
        termImportFunction = try symbol("meltype_term_import", as: TextOut2Function.self)
        settingsGetFunction = try symbol("meltype_settings_get", as: TextFunction.self)
        settingsSetFunction = try symbol("meltype_settings_set", as: Text2Function.self)
        settingsResetFunction = try symbol("meltype_settings_reset", as: TextFunction.self)
    }

    /// 本体が返した文字列を Swift の文字列にして解放する。
    private func take(_ pointer: UnsafeMutablePointer<CChar>?) -> String? {
        guard let pointer else { return nil }
        defer { freeFunction(pointer) }
        return String(cString: pointer)
    }

    // ---- 保存場所 ----

    /// 設定・ユーザー辞書の保存場所 (~/Library/Application Support/Meltype。環境変数 MELTYPE_DATA_DIR があればそこ)。
    public var dataDirectory: String? { take(dataDirectoryFunction()) }

    // ---- ユーザー辞書 ----

    /// ユーザー辞書の語 (ファイルの順 = 登録した順)。取れなければ nil。
    public func userEntries() -> [UserEntry]? {
        take(wordsFunction()).map(FfiFormat.parseUserEntries)
    }

    /// userdict.txt を読めなかった (権限・文字コード)・大きすぎて読み込まなかったときの理由 (画面に警告を出す)。読めていれば nil。
    public func userProblem() -> String? { take(problemFunction()) }

    /// ユーザー辞書の版 (ほかのプロセスが書き換えたときも増える)。変わっていたら一覧を読み直す。
    public func userVersion() -> Int32 { versionFunction() }

    /// 登録・編集してよいか (保存はしない)。だめなら理由。except は編集中の元の語。
    public func check(_ key: WordKey, except: WordKey?) -> String? {
        key.reading.withCString { reading in
            key.word.withCString { word in
                withOptionalCString(except?.reading) { exceptReading in
                    withOptionalCString(except?.word) { exceptWord in
                        take(checkFunction(reading, word, exceptReading, exceptWord))
                    }
                }
            }
        }
    }

    public func add(_ key: WordKey) -> String? {
        key.reading.withCString { reading in key.word.withCString { take(addFunction(reading, $0)) } }
    }

    public func addMany(_ keys: [WordKey]) -> (error: String?, added: [WordKey]) {
        var addedPointer: UnsafeMutablePointer<CChar>?
        let error = FfiFormat.formatKeys(keys).withCString { take(addManyFunction($0, &addedPointer)) }
        return (error, take(addedPointer).map(FfiFormat.parseKeys) ?? [])
    }

    public func update(from old: WordKey, to new: WordKey) -> String? {
        old.reading.withCString { oldReading in
            old.word.withCString { oldWord in
                new.reading.withCString { reading in
                    new.word.withCString { take(updateFunction(oldReading, oldWord, reading, $0)) }
                }
            }
        }
    }

    public func remove(_ keys: [WordKey]) -> (error: String?, removed: [RemovedEntry]) {
        var removedPointer: UnsafeMutablePointer<CChar>?
        let error = FfiFormat.formatKeys(keys).withCString { take(removeFunction($0, &removedPointer)) }
        let removed = take(removedPointer).map(FfiFormat.parseRemoved) ?? []
        return (error, removed)
    }

    public func restore(_ entries: [RemovedEntry]) -> String? {
        FfiFormat.formatRemoved(entries).withCString { take(restoreFunction($0)) }
    }

    /// ほかの日本語入力の辞書ファイルを取り込む。added は新しく登録した語 (取り込みを取り消すときに消す)。
    public func importFile(path: String) -> (error: String?, summary: ImportSummary?, added: [WordKey]) {
        var summaryPointer: UnsafeMutablePointer<CChar>?
        var addedPointer: UnsafeMutablePointer<CChar>?
        let error = path.withCString { take(importFunction($0, &summaryPointer, &addedPointer)) }
        return (error, take(summaryPointer).flatMap(FfiFormat.parseImportSummary), take(addedPointer).map(FfiFormat.parseKeys) ?? [])
    }

    /// Microsoft IME の形式 (UTF-16) で書き出す。
    public func exportFile(path: String) -> (error: String?, count: Int) {
        var count: Int32 = 0
        let error = path.withCString { take(exportFunction($0, &count)) }
        return (error, Int(count))
    }

    /// 読みの入力をひらがなにしたもの (ローマ字・カタカナ → ひらがな)。取れなければ元のまま。
    public func toReading(_ text: String) -> String {
        text.withCString { take(toReadingFunction($0)) } ?? text
    }

    // ---- 専門用語集 ----

    /// 分野の一覧 (ID・名称・語数・有効か)。
    public func termDomains() -> [TermDomainInfo] {
        take(termDomainsFunction()).map(FfiFormat.parseDomains) ?? []
    }

    /// 分野を有効/無効にして config.json に保存する (IME にもすぐ反映)。保存できたら true。
    public func setTermDomain(id: String, enabled: Bool) -> Bool {
        id.withCString { setTermDomainFunction($0, enabled ? 1 : 0) } == 1
    }

    /// 分野の語 (ファイルの順。除外の印つき)。未知の ID・取れなければ nil。重いので裏のスレッドで呼ぶ。
    public func termWords(domain id: String) -> [TermWord]? {
        id.withCString { take(termWordsFunction($0)) }.map(FfiFormat.parseTermWords)
    }

    /// 除外した語 (すべての分野)。
    public func excludedTerms() -> [WordKey] {
        (take(termExcludedFunction()).map(FfiFormat.parseKeys)) ?? []
    }

    public func setExcluded(_ keys: [WordKey], excluded: Bool) -> String? {
        FfiFormat.formatKeys(keys).withCString { take(termSetExcludedFunction($0, excluded ? 1 : 0)) }
    }

    public func editTerm(original: WordKey, to new: WordKey) -> (error: String?, added: Bool) {
        var added: Int32 = 0
        let error = original.reading.withCString { oldReading in
            original.word.withCString { oldWord in
                new.reading.withCString { reading in
                    new.word.withCString { take(termEditFunction(oldReading, oldWord, reading, $0, &added)) }
                }
            }
        }
        return (error, added == 1)
    }

    /// 専門用語集の版 (有効な分野・除外した語が、ほかのプロセスで変わったときも増える)。
    public func termRevision() -> Int32 { termRevisionFunction() }

    // ---- 自作の専門用語集 ----

    /// 自作の専門用語集を作る (すぐ有効)。名前が空・長い・かぶるときは理由。
    public func createDomain(name: String) -> (error: String?, id: String?) {
        var idPointer: UnsafeMutablePointer<CChar>?
        let error = name.withCString { take(termCreateFunction($0, &idPointer)) }
        return (error, take(idPointer))
    }

    public func renameDomain(id: String, name: String) -> String? {
        id.withCString { idPointer in name.withCString { take(termRenameFunction(idPointer, $0)) } }
    }

    public func deleteDomain(id: String) -> (error: String?, deleted: DeletedDomain?) {
        var contentPointer: UnsafeMutablePointer<CChar>?
        var wasEnabled: Int32 = 0
        let name = termDomains().first { $0.id == id }?.name ?? id
        let error = id.withCString { take(termDeleteFunction($0, &contentPointer, &wasEnabled)) }
        let content = take(contentPointer)
        if error != nil { return (error, nil) }
        return (nil, DeletedDomain(id: id, name: name, content: content ?? "", wasEnabled: wasEnabled == 1))
    }

    public func restoreDomain(_ deleted: DeletedDomain) -> String? {
        deleted.id.withCString { id in deleted.content.withCString { take(termRestoreDomainFunction(id, $0, deleted.wasEnabled ? 1 : 0)) } }
    }

    /// 自作の専門用語集に語を登録・編集してよいか (保存はしない)。だめなら理由。except は編集中の元の語。
    public func checkTerm(id: String, _ key: WordKey, except: WordKey?) -> String? {
        id.withCString { idPointer in
            key.reading.withCString { reading in
                key.word.withCString { word in
                    withOptionalCString(except?.reading) { exceptReading in
                        withOptionalCString(except?.word) { exceptWord in
                            take(termUserCheckFunction(idPointer, reading, word, exceptReading, exceptWord))
                        }
                    }
                }
            }
        }
    }

    public func addTerms(id: String, keys: [WordKey]) -> (error: String?, added: [WordKey], skipped: [SkippedWord]) {
        var addedPointer: UnsafeMutablePointer<CChar>?
        var skippedPointer: UnsafeMutablePointer<CChar>?
        let error = id.withCString { idPointer in FfiFormat.formatKeys(keys).withCString { take(termUserAddFunction(idPointer, $0, &addedPointer, &skippedPointer)) } }
        return (error, take(addedPointer).map(FfiFormat.parseKeys) ?? [], take(skippedPointer).map(FfiFormat.parseSkipped) ?? [])
    }

    public func removeTerms(id: String, keys: [WordKey]) -> (error: String?, removed: [RemovedTerm]) {
        var removedPointer: UnsafeMutablePointer<CChar>?
        let error = id.withCString { idPointer in FfiFormat.formatKeys(keys).withCString { take(termUserRemoveFunction(idPointer, $0, &removedPointer)) } }
        return (error, take(removedPointer).map(FfiFormat.parseRemovedTerms) ?? [])
    }

    public func restoreTerms(id: String, entries: [RemovedTerm]) -> String? {
        id.withCString { idPointer in FfiFormat.formatRemovedTerms(entries).withCString { take(termUserRestoreFunction(idPointer, $0)) } }
    }

    public func updateTerm(id: String, from old: WordKey, to new: WordKey) -> String? {
        id.withCString { idPointer in
            old.reading.withCString { oldReading in
                old.word.withCString { oldWord in
                    new.reading.withCString { reading in
                        new.word.withCString { take(termUserUpdateFunction(idPointer, oldReading, oldWord, reading, $0)) }
                    }
                }
            }
        }
    }

    public func moveToDomain(id: String, keys: [WordKey]) -> (error: String?, outcome: MoveOutcome) {
        var removedPointer: UnsafeMutablePointer<CChar>?
        var addedPointer: UnsafeMutablePointer<CChar>?
        var skippedPointer: UnsafeMutablePointer<CChar>?
        let error = id.withCString { idPointer in
            FfiFormat.formatKeys(keys).withCString { take(termMoveFunction(idPointer, $0, &removedPointer, &addedPointer, &skippedPointer)) }
        }
        let outcome = MoveOutcome(removed: take(removedPointer).map(FfiFormat.parseRemoved) ?? [],
                                  added: take(addedPointer).map(FfiFormat.parseKeys) ?? [],
                                  skipped: take(skippedPointer).map(FfiFormat.parseSkipped) ?? [])
        return (error, outcome)
    }

    /// 自作の専門用語集を、同梱と同じ形式のテキストファイルに書き出す。
    public func exportDomain(id: String, path: String) -> (error: String?, count: Int) {
        var count: Int32 = 0
        let error = id.withCString { idPointer in path.withCString { take(termExportFunction(idPointer, $0, &count)) } }
        return (error, Int(count))
    }

    /// ファイルを新しい自作の専門用語集として取り込む (すぐ有効)。
    public func importDomain(path: String) -> (error: String?, summary: DomainImportSummary?) {
        var idPointer: UnsafeMutablePointer<CChar>?
        var summaryPointer: UnsafeMutablePointer<CChar>?
        let error = path.withCString { take(termImportFunction($0, &idPointer, &summaryPointer)) }
        let id = take(idPointer)
        let summary = take(summaryPointer)
        guard error == nil, let id, let summary else { return (error, nil) }
        return (nil, FfiFormat.parseDomainImportSummary(summary, id: id))
    }

    // ---- 設定 ----

    /// 設定タブの項目と今の値 (config.json から読み直す)。config.json が読めない (壊れている)・取れないときは nil。
    public func settings() -> SettingsCatalog? {
        take(settingsGetFunction()).flatMap(SettingsCatalog.parse)
    }

    /// 設定を 1 つ変えて config.json に保存する (動いている IME の入力欄にもすぐ反映される)。だめなら理由 (画面にそのまま出す)。
    public func setSetting(key: String, value: SettingValue) -> String? {
        key.withCString { name in value.jsonLiteral.withCString { take(settingsSetFunction(name, $0)) } }
    }

    /// 設定タブにある項目をすべて既定値に戻す (ほかの設定は触らない)。だめなら理由。
    public func resetSettings() -> String? { take(settingsResetFunction()) }
}

private func withOptionalCString<R>(_ text: String?, _ body: (UnsafePointer<CChar>?) -> R) -> R {
    guard let text else { return body(nil) }
    return text.withCString { body($0) }
}
