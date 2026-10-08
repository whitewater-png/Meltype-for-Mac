// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import AppKit
import Foundation

// libMeltypeNative.dylib (C# の Meltype.Core を NativeAOT にしたもの) の関数。src/Meltype.Mac.Native/Exports.cs と合わせる。
typealias ClausesCallback = @convention(c) (UnsafePointer<CChar>?, UnsafePointer<CChar>?) -> UnsafeMutablePointer<CChar>?
typealias CandidatesCallback = @convention(c) (UnsafePointer<CChar>?) -> UnsafeMutablePointer<CChar>?
typealias IsWordCallback = @convention(c) (UnsafePointer<CChar>?) -> Int32
typealias LearnCallback = @convention(c) (UnsafePointer<CChar>?, UnsafePointer<CChar>?) -> Void
typealias PredictionsCallback = @convention(c) (UnsafePointer<CChar>?) -> UnsafeMutablePointer<CChar>?
typealias ReadingCallback = @convention(c) (UnsafePointer<CChar>?) -> UnsafeMutablePointer<CChar>?

private typealias InitFunction = @convention(c) (ClausesCallback, CandidatesCallback, IsWordCallback, LearnCallback, PredictionsCallback) -> Int32
private typealias SetReaderFunction = @convention(c) (ReadingCallback) -> Void
private typealias CreateFunction = @convention(c) () -> UnsafeMutableRawPointer?
private typealias DestroyFunction = @convention(c) (UnsafeMutableRawPointer?) -> Void
private typealias HandleKeyFunction = @convention(c) (UnsafeMutableRawPointer?, Int32, Int32, Int32, UnsafePointer<CChar>?, UnsafePointer<CChar>?) -> UnsafeMutablePointer<CChar>?
private typealias CommitFunction = @convention(c) (UnsafeMutableRawPointer?) -> UnsafeMutablePointer<CChar>?
private typealias SelectFunction = @convention(c) (UnsafeMutableRawPointer?, Int32) -> UnsafeMutablePointer<CChar>?
private typealias SetDirectFunction = @convention(c) (UnsafeMutableRawPointer?, Int32) -> Void
private typealias ReconvertFunction = @convention(c) (UnsafeMutableRawPointer?, UnsafePointer<CChar>?) -> UnsafeMutablePointer<CChar>?
private typealias SetAppFunction = @convention(c) (UnsafeMutableRawPointer?, UnsafePointer<CChar>?) -> Int32
private typealias AddUserWordFunction = @convention(c) (UnsafeMutableRawPointer?, UnsafePointer<CChar>?, UnsafePointer<CChar>?) -> UnsafeMutablePointer<CChar>?
private typealias SuggestionsFunction = @convention(c) (UnsafeMutableRawPointer?) -> UnsafeMutablePointer<CChar>?
private typealias SuggestRejectFunction = @convention(c) (UnsafeMutableRawPointer?, UnsafePointer<CChar>?, UnsafePointer<CChar>?) -> Void
private typealias SuggestClearFunction = @convention(c) (UnsafeMutableRawPointer?) -> Void
private typealias DataDirectoryFunction = @convention(c) () -> UnsafeMutablePointer<CChar>?
private typealias ReportUrlFunction = @convention(c) (UnsafePointer<CChar>?) -> UnsafeMutablePointer<CChar>?
private typealias FreeFunction = @convention(c) (UnsafeMutableRawPointer?) -> Void
private typealias AbiVersionFunction = @convention(c) () -> Int32
private typealias ClearLearningFunction = @convention(c) () -> Int32
private typealias GetFlagFunction = @convention(c) () -> Int32
private typealias SetFlagFunction = @convention(c) (Int32) -> Int32
private typealias TermDomainsFunction = @convention(c) () -> UnsafeMutablePointer<CChar>?
private typealias SetTermDomainFunction = @convention(c) (UnsafePointer<CChar>?, Int32) -> Int32
private typealias UpdateEvaluateFunction = @convention(c) (UnsafePointer<CChar>?, UnsafePointer<CChar>?) -> UnsafeMutablePointer<CChar>?

// ---- 本体から呼ばれる関数 (文字列は strdup したものを返し、本体が free する) ----

/// (ひらがな, 文脈) → 「読み\t変換結果」を改行でつないだもの (文節ごと)。
private let clausesCallback: ClausesCallback = { hiragana, context in
    guard let hiragana else { return nil }
    let clauses = MeltypeConverter.shared.clauses(for: String(cString: hiragana), context: context.map { String(cString: $0) })
    guard !clauses.isEmpty else { return nil }
    return strdup(clauses.map { "\($0.reading)\t\($0.text)" }.joined(separator: "\n"))
}

/// 読み → 候補を改行でつないだもの。
private let candidatesCallback: CandidatesCallback = { reading in
    guard let reading else { return nil }
    let candidates = MeltypeConverter.shared.candidates(for: String(cString: reading))
    return strdup(candidates.joined(separator: "\n"))
}

/// 読み → 予測候補を改行でつないだもの (azooKey の予測)。
private let predictionsCallback: PredictionsCallback = { reading in
    guard let reading else { return nil }
    let predictions = MeltypeConverter.shared.predictions(for: String(cString: reading))
    return strdup(predictions.joined(separator: "\n"))
}

/// 漢字混じりの文字列 → 読み (ひらがな)。確定後の再変換用 (他の読みが分からなかったときの最後の手段)。
private let readingCallback: ReadingCallback = { text in
    guard let text, let reading = reading(of: String(cString: text)) else { return nil }
    return strdup(reading)
}

/// CFStringTokenizer のローマ字読みをひらがなにする。同音異義語は読み違えることがある。読めなければ nil。
private func reading(of text: String) -> String? {
    let source = text as CFString
    let tokenizer = CFStringTokenizerCreate(kCFAllocatorDefault, source, CFRangeMake(0, CFStringGetLength(source)),
                                            kCFStringTokenizerUnitWord, Locale(identifier: "ja") as CFLocale)
    var latin = ""
    var type = CFStringTokenizerAdvanceToNextToken(tokenizer)
    while !type.isEmpty {
        guard let value = CFStringTokenizerCopyCurrentTokenAttribute(tokenizer, kCFStringTokenizerAttributeLatinTranscription) as? String else { return nil }
        latin += value
        type = CFStringTokenizerAdvanceToNextToken(tokenizer)
    }
    guard !latin.isEmpty else { return nil }
    let result = NSMutableString(string: latin)
    guard CFStringTransform(result, nil, kCFStringTransformLatinHiragana, false) else { return nil }
    return result as String
}

/// 英単語として正しい綴りか (macOS のスペルチェッカー、英語で調べる)。
private let isWordCallback: IsWordCallback = { word in
    guard let word else { return 0 }
    let text = String(cString: word)
    let misspelled = NSSpellChecker.shared.checkSpelling(of: text, startingAt: 0, language: "en", wrap: false, inSpellDocumentWithTag: 0, wordCount: nil)
    return misspelled.location == NSNotFound ? 1 : 0
}

/// 確定した変換を azooKey に学習させる。(文脈 or NULL, 「読み\t文字列」を改行でつないだ文節の列)。
/// 本体の確定処理から裏のスレッドで呼ばれる。KanaKanjiConverter はスレッドセーフではないので、メインスレッドに移す。
/// 引数の C 文字列は呼び出しの間しか有効でないため、async の前に Swift の文字列にしておく。
private let learnCallback: LearnCallback = { context, clauses in
    guard let clauses else { return }
    let contextText = context.map { String(cString: $0) }
    let pairs: [(reading: String, text: String)] = String(cString: clauses)
        .split(separator: "\n", omittingEmptySubsequences: true)
        .compactMap { line in
            guard let tab = line.firstIndex(of: "\t") else { return nil }
            return (String(line[..<tab]), String(line[line.index(after: tab)...]))
        }
    guard !pairs.isEmpty else { return }
    DispatchQueue.main.async { MeltypeConverter.shared.learn(context: contextText, clauses: pairs) }
}

/// Swift 側で扱う結果 (本体の SessionResult.ToJson と同じ形)。
struct SessionResult: Decodable {
    let consumed: Bool
    let commits: [TextEdit]
    let view: CompositionView?
}

struct TextEdit: Decodable {
    let deleteBefore: Int
    let text: String
}

struct CompositionView: Decodable {
    let text: String
    let converting: Bool
    let selectedIndex: Int
    let selectedClause: Int
    let hint: String
    let candidates: [String]
    let clauses: [String]
    /// 選んでいる候補の意味 (無ければ nil)。候補で少し止まったら注釈に出す。
    let meaning: String?
    /// 予測変換の候補と、Tab で入って選んでいる位置 (入っていなければ -1)。
    let predictions: [String]
    let selectedPrediction: Int
}

/// 専門用語集の分野 (meltype_term_domains の 1 行)。
struct TermDomain {
    let id: String
    let name: String
    let count: Int
    let enabled: Bool
}

/// 入力欄のアプリの種類 (meltype_set_app の戻り値)。
enum AppKind: Int {
    case general = 0
    /// コードエディター・ターミナル。英数から始める。
    case code = 1
    /// アプリ別設定で OFF にしたアプリ・ゲーム。Meltype はキーを触らない。
    case disabled = 2
}

/// libMeltypeNative.dylib を読み込んで呼ぶ。Meltype.app/Contents/Frameworks に置く (build.sh)。
final class NativeCore {
    static let shared = NativeCore()

    /// この Swift が前提にしている FFI の版数。src/Meltype.Mac.Native/Exports.cs の AbiVersion と、
    /// 辞書の管理画面の NativeDictionary.expectedAbiVersion (Sources/MeltypeDictionaryKit) と必ず同じにする。
    /// 食い違う dylib (別の版が混ざった) を読むと関数の引数が合わずに落ちるので、食い違ったら初期化を止める。
    static let expectedAbiVersion: Int32 = 6

    /// dylib の版数が expectedAbiVersion と合っているか (initialize で確かめる)。合わなければ入力を一切扱わない (キーはアプリに渡る)。
    private(set) var isCompatible = false

    private let library: UnsafeMutableRawPointer?
    private let initFunction: InitFunction?
    private let setReaderFunction: SetReaderFunction?
    private let createFunction: CreateFunction?
    private let destroyFunction: DestroyFunction?
    private let handleKeyFunction: HandleKeyFunction?
    private let commitFunction: CommitFunction?
    private let selectFunction: SelectFunction?
    private let selectPredictionFunction: SelectFunction?
    private let setDirectFunction: SetDirectFunction?
    private let reconvertFunction: ReconvertFunction?
    private let setAppFunction: SetAppFunction?
    private let addUserWordFunction: AddUserWordFunction?
    private let suggestionsFunction: SuggestionsFunction?
    private let suggestAcceptFunction: AddUserWordFunction?
    private let suggestRejectFunction: SuggestRejectFunction?
    private let suggestClearFunction: SuggestClearFunction?
    private let suggestHintFunction: SuggestionsFunction?
    private let dataDirectoryFunction: DataDirectoryFunction?
    private let reportUrlFunction: ReportUrlFunction?
    private let freeFunction: FreeFunction?
    private let abiVersionFunction: AbiVersionFunction?
    private let clearLearningFunction: ClearLearningFunction?
    private let getContinueAfterConversionFunction: GetFlagFunction?
    private let setContinueAfterConversionFunction: SetFlagFunction?
    private let termDomainsFunction: TermDomainsFunction?
    private let setTermDomainFunction: SetTermDomainFunction?
    private let updateEvaluateFunction: UpdateEvaluateFunction?

    private init() {
        let path = (Bundle.main.privateFrameworksPath ?? "") + "/libMeltypeNative.dylib"
        // 初期化が終わるまで self のプロパティは使えないので、ローカルの handle から関数を探す。
        let handle = dlopen(path, RTLD_NOW)
        library = handle
        if handle == nil, let error = dlerror() {
            NSLog("Meltype: %@ を読み込めませんでした: %@", path, String(cString: error))
        }
        func symbol<T>(_ name: String, as type: T.Type) -> T? {
            guard let handle, let pointer = dlsym(handle, name) else { return nil }
            return unsafeBitCast(pointer, to: type)
        }
        initFunction = symbol("meltype_init", as: InitFunction.self)
        setReaderFunction = symbol("meltype_set_reader", as: SetReaderFunction.self)
        createFunction = symbol("meltype_create", as: CreateFunction.self)
        destroyFunction = symbol("meltype_destroy", as: DestroyFunction.self)
        handleKeyFunction = symbol("meltype_handle_key", as: HandleKeyFunction.self)
        commitFunction = symbol("meltype_commit", as: CommitFunction.self)
        selectFunction = symbol("meltype_select_candidate", as: SelectFunction.self)
        selectPredictionFunction = symbol("meltype_select_prediction", as: SelectFunction.self)
        setDirectFunction = symbol("meltype_set_direct", as: SetDirectFunction.self)
        reconvertFunction = symbol("meltype_reconvert", as: ReconvertFunction.self)
        setAppFunction = symbol("meltype_set_app", as: SetAppFunction.self)
        addUserWordFunction = symbol("meltype_add_user_word", as: AddUserWordFunction.self)
        suggestionsFunction = symbol("meltype_suggestions", as: SuggestionsFunction.self)
        suggestAcceptFunction = symbol("meltype_suggest_accept", as: AddUserWordFunction.self)
        suggestRejectFunction = symbol("meltype_suggest_reject", as: SuggestRejectFunction.self)
        suggestClearFunction = symbol("meltype_suggest_clear", as: SuggestClearFunction.self)
        suggestHintFunction = symbol("meltype_suggest_hint", as: SuggestionsFunction.self)
        dataDirectoryFunction = symbol("meltype_data_directory", as: DataDirectoryFunction.self)
        reportUrlFunction = symbol("meltype_report_url", as: ReportUrlFunction.self)
        freeFunction = symbol("meltype_free", as: FreeFunction.self)
        abiVersionFunction = symbol("meltype_abi_version", as: AbiVersionFunction.self)
        clearLearningFunction = symbol("meltype_clear_learning", as: ClearLearningFunction.self)
        getContinueAfterConversionFunction = symbol("meltype_get_continue_after_conversion", as: GetFlagFunction.self)
        setContinueAfterConversionFunction = symbol("meltype_set_continue_after_conversion", as: SetFlagFunction.self)
        termDomainsFunction = symbol("meltype_term_domains", as: TermDomainsFunction.self)
        setTermDomainFunction = symbol("meltype_set_term_domain", as: SetTermDomainFunction.self)
        updateEvaluateFunction = symbol("meltype_update_evaluate", as: UpdateEvaluateFunction.self)
    }

    func initialize() {
        // 版数が合わないときはクラッシュさせず、ログを残して初期化しない (createSession も nil を返し、キーはアプリに素通しになる)。
        guard let abiVersionFunction else {
            NSLog("Meltype: libMeltypeNative.dylib に meltype_abi_version がありません (古い版か、読み込めていません)。初期化を中止します。")
            return
        }
        let actual = abiVersionFunction()
        guard actual == Self.expectedAbiVersion else {
            NSLog("Meltype: libMeltypeNative.dylib の版数が合いません (期待 %d / 実際 %d)。初期化を中止します。", Self.expectedAbiVersion, actual)
            return
        }
        isCompatible = true
        _ = initFunction?(clausesCallback, candidatesCallback, isWordCallback, learnCallback, predictionsCallback)
        setReaderFunction?(readingCallback)
    }

    func createSession() -> UnsafeMutableRawPointer? { isCompatible ? createFunction?() : nil }

    func destroySession(_ session: UnsafeMutableRawPointer?) { destroyFunction?(session) }

    func handleKey(_ session: UnsafeMutableRawPointer?, vk: Int32, character: Int32, modifiers: Int32, before: String?, after: String?) -> SessionResult? {
        guard isCompatible, let handleKeyFunction else { return nil }
        return withOptionalCString(before) { beforePointer in
            withOptionalCString(after) { afterPointer in
                decode(handleKeyFunction(session, vk, character, modifiers, beforePointer, afterPointer))
            }
        }
    }

    func commit(_ session: UnsafeMutableRawPointer?) -> SessionResult? {
        guard isCompatible, let commitFunction else { return nil }
        return decode(commitFunction(session))
    }

    func selectCandidate(_ session: UnsafeMutableRawPointer?, index: Int) -> SessionResult? {
        guard isCompatible, let selectFunction else { return nil }
        return decode(selectFunction(session, Int32(index)))
    }

    func selectPrediction(_ session: UnsafeMutableRawPointer?, index: Int) -> SessionResult? {
        guard isCompatible, let selectPredictionFunction else { return nil }
        return decode(selectPredictionFunction(session, Int32(index)))
    }

    /// 確定済みの文字列を読みに戻して変換を始める。読みに戻せなければ consumed が false の結果 (または nil)。
    func reconvert(_ session: UnsafeMutableRawPointer?, text: String) -> SessionResult? {
        guard isCompatible, let reconvertFunction else { return nil }
        return text.withCString { decode(reconvertFunction(session, $0)) }
    }

    /// 入力欄のアプリ (bundle ID) を本体に伝え、種類を受け取る。取れなければ .general (何も変えない)。
    func setApp(_ session: UnsafeMutableRawPointer?, bundleIdentifier: String?) -> AppKind {
        guard isCompatible, let setAppFunction else { return .general }
        let raw = withOptionalCString(bundleIdentifier) { setAppFunction(session, $0) }
        return AppKind(rawValue: Int(raw)) ?? .general
    }

    func setDirect(_ session: UnsafeMutableRawPointer?, _ direct: Bool) {
        guard isCompatible else { return }
        setDirectFunction?(session, direct ? 1 : 0)
    }

    /// ユーザー辞書に登録する。登録できなければ理由を返し、できたら nil。
    func addUserWord(_ session: UnsafeMutableRawPointer?, reading: String, word: String) -> String? {
        guard isCompatible, let addUserWordFunction else { return "この版では登録に対応していません。" }
        let pointer = reading.withCString { readingPointer in
            word.withCString { addUserWordFunction(session, readingPointer, $0) }
        }
        guard let pointer else { return nil }
        defer { freeFunction?(pointer) }
        return String(cString: pointer)
    }

    /// 入力メニューに出す登録の提案 (読みと語。最大 3 件)。無ければ空。
    func suggestions(_ session: UnsafeMutableRawPointer?) -> [(reading: String, word: String)] {
        guard isCompatible, let pointer = suggestionsFunction?(session) else { return [] }
        defer { freeFunction?(pointer) }
        let pairs: [(reading: String, word: String)] = String(cString: pointer)
            .split(separator: "\n", omittingEmptySubsequences: true)
            .compactMap { (line: Substring) -> (reading: String, word: String)? in
                let parts = line.split(separator: "\t", maxSplits: 1, omittingEmptySubsequences: false)
                return parts.count == 2 ? (String(parts[0]), String(parts[1])) : nil
            }
        return pairs
    }

    /// 提案を受けてユーザー辞書に登録し、提案待ちから消す。登録できなければ理由を返し、できたら nil。
    func acceptSuggestion(_ session: UnsafeMutableRawPointer?, reading: String, word: String) -> String? {
        guard isCompatible, let suggestAcceptFunction else { return "この版では登録に対応していません。" }
        let pointer = reading.withCString { readingPointer in
            word.withCString { suggestAcceptFunction(session, readingPointer, $0) }
        }
        guard let pointer else { return nil }
        defer { freeFunction?(pointer) }
        return String(cString: pointer)
    }

    /// 提案を「登録しない」にする。
    func rejectSuggestion(_ session: UnsafeMutableRawPointer?, reading: String, word: String) {
        guard isCompatible else { return }
        reading.withCString { readingPointer in
            word.withCString { suggestRejectFunction?(session, readingPointer, $0) }
        }
    }

    /// 提案の履歴をすべて消す。
    func clearSuggestions(_ session: UnsafeMutableRawPointer?) {
        guard isCompatible else { return }
        suggestClearFunction?(session)
    }

    /// 提案待ちができた直後の 1 行のヒント (1 日 1 回まで)。出さないときは nil。
    func suggestionHint(_ session: UnsafeMutableRawPointer?) -> String? {
        guard isCompatible, let pointer = suggestHintFunction?(session) else { return nil }
        defer { freeFunction?(pointer) }
        return String(cString: pointer)
    }

    /// 学習データ (変換・登録提案・英語/日本語・英訳・ユーザーモデル) をすべて消す。ユーザー辞書と設定は消さない。全部消せたら true。
    func clearLearning() -> Bool {
        guard isCompatible, let clearLearningFunction else { return false }
        return clearLearningFunction() == 1
    }

    /// 「変換後も続けて入力できる」が ON か。設定はすべての入力欄で共通 (本体が持つ)。本体が合わないときは false。
    var continueAfterConversion: Bool {
        guard isCompatible, let getContinueAfterConversionFunction else { return false }
        return getContinueAfterConversionFunction() == 1
    }

    /// 「変換後も続けて入力できる」を切り替えて config.json に保存する。すべての入力欄にすぐ反映される。保存できたら true。
    func setContinueAfterConversion(_ on: Bool) -> Bool {
        guard isCompatible, let setContinueAfterConversionFunction else { return false }
        return setContinueAfterConversionFunction(on ? 1 : 0) == 1
    }

    /// 専門用語集の分野の一覧 (ID・名称・語数・有効か)。設定はすべての入力欄で共通 (本体が持つ)。取れなければ空。
    var termDomains: [TermDomain] {
        guard isCompatible, let pointer = termDomainsFunction?() else { return [] }
        defer { freeFunction?(pointer) }
        return String(cString: pointer)
            .split(separator: "\n", omittingEmptySubsequences: true)
            .compactMap { (line: Substring) -> TermDomain? in
                let parts = line.split(separator: "\t", omittingEmptySubsequences: false)
                guard parts.count == 4, let count = Int(parts[2]) else { return nil }
                return TermDomain(id: String(parts[0]), name: String(parts[1]), count: count, enabled: parts[3] == "1")
            }
    }

    /// 専門用語集の分野を有効/無効にして config.json に保存する。すべての入力欄にすぐ反映される。保存できたら true。
    func setTermDomain(_ id: String, enabled: Bool) -> Bool {
        guard isCompatible, let setTermDomainFunction else { return false }
        return id.withCString { setTermDomainFunction($0, enabled ? 1 : 0) } == 1
    }

    /// GitHub の最新 Release の JSON を本体で判定する (通信は Swift 側)。更新してよい新しい版があれば、その情報。無い・不正なら nil。
    /// 本体の関数は文字列を判定するだけでセッションを使わないので、任意のスレッドから呼べる。
    func evaluateUpdate(releaseJson: String, currentVersion: String) -> UpdateOffer? {
        guard isCompatible, let updateEvaluateFunction else { return nil }
        let pointer = releaseJson.withCString { json in
            currentVersion.withCString { updateEvaluateFunction(json, $0) }
        }
        guard let pointer else { return nil }
        defer { freeFunction?(pointer) }
        let lines = String(cString: pointer).split(separator: "\n", omittingEmptySubsequences: false).map(String.init)
        guard lines.count == 5, let size = Int64(lines[3]), let url = URL(string: lines[1]), let releaseUrl = URL(string: lines[4]) else { return nil }
        return UpdateOffer(version: lines[0], downloadUrl: url, sha256: lines[2], size: size, releaseUrl: releaseUrl)
    }

    /// 設定・学習データ・ユーザー辞書の保存場所。
    var dataDirectory: String? {
        guard let pointer = dataDirectoryFunction?() else { return nil }
        defer { freeFunction?(pointer) }
        return String(cString: pointer)
    }

    /// 不具合報告を開く URL (OS・版・実行環境を入れたもの)。
    var reportUrl: URL? {
        guard let pointer = "Mac".withCString({ reportUrlFunction?($0) }) else { return nil }
        defer { freeFunction?(pointer) }
        return URL(string: String(cString: pointer))
    }

    private func decode(_ pointer: UnsafeMutablePointer<CChar>?) -> SessionResult? {
        guard let pointer else { return nil }
        defer { freeFunction?(pointer) }
        let json = Data(bytes: pointer, count: strlen(pointer))
        do {
            return try JSONDecoder().decode(SessionResult.self, from: json)
        } catch {
            NSLog("Meltype: 結果を読めませんでした: %@", String(describing: error))
            return nil
        }
    }

    private func withOptionalCString<R>(_ text: String?, _ body: (UnsafePointer<CChar>?) -> R) -> R {
        guard let text else { return body(nil) }
        return text.withCString { body($0) }
    }
}
