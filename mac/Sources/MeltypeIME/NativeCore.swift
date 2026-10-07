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
    }

    func initialize() {
        _ = initFunction?(clausesCallback, candidatesCallback, isWordCallback, learnCallback, predictionsCallback)
        setReaderFunction?(readingCallback)
    }

    func createSession() -> UnsafeMutableRawPointer? { createFunction?() }

    func destroySession(_ session: UnsafeMutableRawPointer?) { destroyFunction?(session) }

    func handleKey(_ session: UnsafeMutableRawPointer?, vk: Int32, character: Int32, modifiers: Int32, before: String?, after: String?) -> SessionResult? {
        guard let handleKeyFunction else { return nil }
        return withOptionalCString(before) { beforePointer in
            withOptionalCString(after) { afterPointer in
                decode(handleKeyFunction(session, vk, character, modifiers, beforePointer, afterPointer))
            }
        }
    }

    func commit(_ session: UnsafeMutableRawPointer?) -> SessionResult? {
        guard let commitFunction else { return nil }
        return decode(commitFunction(session))
    }

    func selectCandidate(_ session: UnsafeMutableRawPointer?, index: Int) -> SessionResult? {
        guard let selectFunction else { return nil }
        return decode(selectFunction(session, Int32(index)))
    }

    func selectPrediction(_ session: UnsafeMutableRawPointer?, index: Int) -> SessionResult? {
        guard let selectPredictionFunction else { return nil }
        return decode(selectPredictionFunction(session, Int32(index)))
    }

    /// 確定済みの文字列を読みに戻して変換を始める。読みに戻せなければ consumed が false の結果 (または nil)。
    func reconvert(_ session: UnsafeMutableRawPointer?, text: String) -> SessionResult? {
        guard let reconvertFunction else { return nil }
        return text.withCString { decode(reconvertFunction(session, $0)) }
    }

    /// 入力欄のアプリ (bundle ID) を本体に伝え、種類を受け取る。取れなければ .general (何も変えない)。
    func setApp(_ session: UnsafeMutableRawPointer?, bundleIdentifier: String?) -> AppKind {
        guard let setAppFunction else { return .general }
        let raw = withOptionalCString(bundleIdentifier) { setAppFunction(session, $0) }
        return AppKind(rawValue: Int(raw)) ?? .general
    }

    func setDirect(_ session: UnsafeMutableRawPointer?, _ direct: Bool) {
        setDirectFunction?(session, direct ? 1 : 0)
    }

    /// ユーザー辞書に登録する。登録できなければ理由を返し、できたら nil。
    func addUserWord(_ session: UnsafeMutableRawPointer?, reading: String, word: String) -> String? {
        guard let addUserWordFunction else { return "この版では登録に対応していません。" }
        let pointer = reading.withCString { readingPointer in
            word.withCString { addUserWordFunction(session, readingPointer, $0) }
        }
        guard let pointer else { return nil }
        defer { freeFunction?(pointer) }
        return String(cString: pointer)
    }

    /// 入力メニューに出す登録の提案 (読みと語。最大 3 件)。無ければ空。
    func suggestions(_ session: UnsafeMutableRawPointer?) -> [(reading: String, word: String)] {
        guard let pointer = suggestionsFunction?(session) else { return [] }
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
        guard let suggestAcceptFunction else { return "この版では登録に対応していません。" }
        let pointer = reading.withCString { readingPointer in
            word.withCString { suggestAcceptFunction(session, readingPointer, $0) }
        }
        guard let pointer else { return nil }
        defer { freeFunction?(pointer) }
        return String(cString: pointer)
    }

    /// 提案を「登録しない」にする。
    func rejectSuggestion(_ session: UnsafeMutableRawPointer?, reading: String, word: String) {
        reading.withCString { readingPointer in
            word.withCString { suggestRejectFunction?(session, readingPointer, $0) }
        }
    }

    /// 提案の履歴をすべて消す。
    func clearSuggestions(_ session: UnsafeMutableRawPointer?) {
        suggestClearFunction?(session)
    }

    /// 提案待ちができた直後の 1 行のヒント (1 日 1 回まで)。出さないときは nil。
    func suggestionHint(_ session: UnsafeMutableRawPointer?) -> String? {
        guard let pointer = suggestHintFunction?(session) else { return nil }
        defer { freeFunction?(pointer) }
        return String(cString: pointer)
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
