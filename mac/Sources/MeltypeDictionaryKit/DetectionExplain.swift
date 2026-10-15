// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import Foundation

/// 判定の強さ (C# の DetectionLevel の並びと同じ。meltype_explain_detection の level 引数はこの rawValue)。
public enum DetectionStrength: Int32, CaseIterable {
    case aggressive = 0
    case balanced
    case conservative
    case manual

    /// 設定 (config.json) での名前 (meltype_settings_get の DetectionLevel の値)。
    public var settingName: String {
        switch self {
        case .aggressive: return "Aggressive"
        case .balanced: return "Balanced"
        case .conservative: return "Conservative"
        case .manual: return "Manual"
        }
    }

    /// 設定タブの選択肢の名前と同じ呼び方 (画面でどれのことか分かるように)。
    public var label: String {
        switch self {
        case .aggressive: return "積極的"
        case .balanced: return "標準"
        case .conservative: return "慎重"
        case .manual: return "手動"
        }
    }

    public init?(settingName: String) {
        guard let found = Self.allCases.first(where: { $0.settingName == settingName }) else { return nil }
        self = found
    }
}

/// 「判定を調べる」の操作 (本物は NativeDictionary。--self-test は偽物でも確かめる)。
public protocol DetectionExplainOperations: AnyObject {
    /// 打ったローマ字の区間分けと、条件を 1 つ変えたときの結果 (TSV。DetectionExplanation.parse が読む)。取れなければ nil。
    /// ローマ字入力としてだけ調べる (かな入力の判定は調べられない)。学習 (languages.json) は読むだけで変えない。メインスレッド専用。
    func explainDetection(typed: String, before: String, after: String, level: DetectionStrength) -> String?
}

/// 区間 1 つ (英語なら打った英字、日本語ならかな)。
public struct DetectionSegment: Hashable {
    public let isEnglish: Bool
    public let text: String

    public init(isEnglish: Bool, text: String) {
        self.isEnglish = isEnglish
        self.text = text
    }
}

/// 条件を 1 つだけ変えて判定し直した結果。
public struct DetectionVariant: Hashable {
    public let name: String
    public let segments: [DetectionSegment]
    public let changed: Bool

    public init(name: String, segments: [DetectionSegment], changed: Bool) {
        self.name = name
        self.segments = segments
        self.changed = changed
    }
}

/// 画面の 1 行。removableWord があれば、行の右に「この語を消す」を出す (学習した語タブと同じ削除操作)。
public struct ExplainLine: Hashable {
    public let text: String
    public let removableWord: String?
    /// 学習した語の行が多いとき、先頭の 10 件より後ろは折りたたむ (画面が「残りを表示」で開く)。
    public let foldable: Bool

    public init(_ text: String, removableWord: String? = nil, foldable: Bool = false) {
        self.text = text
        self.removableWord = removableWord
        self.foldable = foldable
    }
}

/// meltype_explain_detection の結果。
/// 形は「segments Tab 区間」「variant Tab 条件 Tab 区間 Tab 変わったなら 1」「memory-pending Tab 語」(区間は EN:英字 と JA:かな を | でつなぐ)。
public struct DetectionExplanation: Equatable {
    public let segments: [DetectionSegment]
    public let variants: [DetectionVariant]
    /// 学習したが 2 回目待ちで、まだ効いていない語。
    public let pending: [String]

    public init(segments: [DetectionSegment], variants: [DetectionVariant], pending: [String]) {
        self.segments = segments
        self.variants = variants
        self.pending = pending
    }

    /// 読めなければ (segments の行が無い) nil。知らない行・欄が足りない行は飛ばす (本体が新しくなって行が増えても読める)。
    public static func parse(_ text: String) -> DetectionExplanation? {
        var segments: [DetectionSegment]?
        var variants: [DetectionVariant] = []
        var pending: [String] = []
        for line in text.split(separator: "\n", omittingEmptySubsequences: true) {
            let fields = line.split(separator: "\t", omittingEmptySubsequences: false).map(String.init)
            switch fields.first {
            case "segments" where fields.count >= 2:
                segments = parseSegments(fields[1])
            case "variant" where fields.count >= 4 && !fields[1].isEmpty:
                variants.append(DetectionVariant(name: fields[1], segments: parseSegments(fields[2]), changed: fields[3] == "1"))
            case "memory-pending" where fields.count >= 2 && !fields[1].isEmpty:
                pending.append(fields[1])
            default:
                break
            }
        }
        // 打った文字が空のとき、本体は「segments」の行だけを返す (欄は空)
        if segments == nil, text.hasPrefix("segments") { segments = [] }
        guard let segments else { return nil }
        return DetectionExplanation(segments: segments, variants: variants, pending: pending)
    }

    /// 「EN:api|JA:をつかう」→ 区間。| は打った英字にも入りうるので、EN: / JA: で始まらない断片は前の区間に続ける。
    static func parseSegments(_ text: String) -> [DetectionSegment] {
        var result: [DetectionSegment] = []
        for piece in text.split(separator: "|", omittingEmptySubsequences: false).map(String.init) {
            if piece.hasPrefix("EN:") {
                result.append(DetectionSegment(isEnglish: true, text: String(piece.dropFirst(3))))
            } else if piece.hasPrefix("JA:") {
                result.append(DetectionSegment(isEnglish: false, text: String(piece.dropFirst(3))))
            } else if let last = result.popLast() {
                result.append(DetectionSegment(isEnglish: last.isEnglish, text: last.text + "|" + piece))
            }
        }
        return result
    }

    /// 区間を 1 行の文にする: 英語は [ ] で囲み、区間の間は空白をあける (きょうは [sushi] です)。日本語だけなら空白は入らない。
    public static func render(_ segments: [DetectionSegment]) -> String {
        segments.isEmpty ? "(なし)" : segments.map { $0.isEnglish ? "[\($0.text)]" : $0.text }.joined(separator: " ")
    }

    /// 学習した語を外す行を、折りたたまずに出す件数 (これより多い分は foldable)。
    public static let visibleWordRows = 10

    /// 条件の名前 → 画面の言い方 (知らない名前は nil = 出さない)。
    static func describe(_ name: String) -> String? {
        switch name {
        case "memory-off": return "学習した語をすべて外す"
        case "before-english": return "前の文字が英語"
        case "before-japanese": return "前の文字が日本語"
        case "before-none": return "前の文字が無い"
        case "after-english": return "後ろの文字が英語"
        case "after-none": return "後ろの文字が無い"
        default:
            if name.hasPrefix("memory-without:") { return "学習した語「\(name.dropFirst("memory-without:".count))」を外す" }
            if name.hasPrefix("level:"), let strength = DetectionStrength(settingName: String(name.dropFirst("level:".count))) {
                return "判定の強さを「\(strength.label)」に"
            }
            return nil
        }
    }

    /// 画面に出す行 (固定幅の文字で出す)。学習した語を外して結果が変わった行には、その語を消すボタン用の語を付ける。
    /// 語を外しても変わらない行は出さない (区間に含まれるだけで、効いていない語なので)。
    public var lines: [ExplainLine] {
        var result = [ExplainLine("結果 (確定前の区間分け): " + Self.render(segments))]
        if !segments.isEmpty {
            result.append(ExplainLine("1 つだけ条件を変えた場合:"))
            var any = false
            var wordRows = 0
            for variant in variants {
                guard let label = Self.describe(variant.name) else { continue }
                let isWord = variant.name.hasPrefix("memory-without:")
                if isWord && !variant.changed { continue }
                any = true
                if isWord { wordRows += 1 }
                let outcome = variant.changed ? Self.render(variant.segments) : "変わりません"
                result.append(ExplainLine("  \(label) → \(outcome)",
                                          removableWord: isWord ? String(variant.name.dropFirst("memory-without:".count)) : nil,
                                          foldable: isWord && wordRows > Self.visibleWordRows))
            }
            if !any { result.append(ExplainLine("  (試せる条件がありません)")) }
        }
        if !pending.isEmpty {
            result.append(ExplainLine("まだ効いていない学習 (もう一度英字にすると効きます): " + pending.map { "「\($0)」" }.joined(separator: " ")))
        }
        return result
    }

    /// lines を 1 つの文にしたもの (--self-test・VoiceOver 用)。ボタンのある行には [この語を消す] を付ける。
    public var displayText: String {
        lines.map { $0.removableWord == nil ? $0.text : $0.text + "   [この語を消す]" }.joined(separator: "\n")
    }
}

/// 「判定を調べる」タブの画面の外のきまり (--self-test で確かめる)。
public enum DetectionExplainInput {
    /// 前後の文字は IME が Core に渡す長さ (キャレットの前後 20 UTF-16 単位) に合わせて切る。前は末尾、後ろは先頭。
    public static let contextLength = 20

    /// タブ下部の案内。english.txt の読み直しの時期は、コードで確かめた事実 (Core の CompositionDetector.CreateDefault は
    /// MeltypeSession.CreateDefault = 入力欄ごとの入力のセッションを作るときに 1 回だけ辞書ファイルを読む。IME の InputController.init が作る。
    /// この画面の判定器も最初に調べるときに 1 回読む) のとおりに書く。
    public static let guidance =
        "ここに出るのは確定する前の区間分けです。確定のしかた (Space / Enter) や、確定したあとの自動の直しで変わることがあります。"
        + "「前の文字」が空のときは、入力欄の直前に何も無いときの判定です (IME は、自分が直前に確定した文字の向きを引き継ぐので、続けて打つと違う答えになることがあります)。"
        + "英語にしたい語は『学習した語』に足すか、データフォルダの dictionaries/english.txt (固有名詞は propernouns.txt) に書きます (ユーザー辞書と専門用語集は日英の判定には効きません)。"
        + "english.txt・propernouns.txt は、IME が入力のセッションを作るとき (アプリや入力欄ごと。作り直しは macOS 任せ) に読み込むので、"
        + "直したあとは、Meltype を入れ直すかログインし直すまで、開いたままの入力欄に反映されないことがあります。"
        + "この画面は「Meltype 辞書」を開き直すと反映されます。"

    /// かな入力の設定が ON のときに結果欄の上に出す 1 行 (この画面の判定はローマ字入力だけ)。
    public static let kanaNotice = "かな入力の判定はこの画面では調べられません (ローマ字入力として調べています)"

    /// 打つローマ字に空白が入っていたときの理由 (入っていなければ nil)。Space は変換のキーなので、区間分けには現れない。
    public static func problem(typed: String) -> String? {
        typed.contains { $0.isWhitespace } ? "空白は打てません (Space は変換のキーです)" : nil
    }
    public static func clipBefore(_ text: String) -> String { clip(text, tail: true) }
    public static func clipAfter(_ text: String) -> String { clip(text, tail: false) }

    private static func clip(_ text: String, tail: Bool) -> String {
        // 改行は文脈にならない (次の行・前の行は別の文) ので、そのまま渡す。長さは UTF-16 で数え、文字の途中では切らない。
        var units = 0
        var kept: [Character] = []
        for character in (tail ? Array(text.reversed()) : Array(text)) {
            let size = String(character).utf16.count
            if units + size > contextLength { break }
            units += size
            kept.append(character)
        }
        return String(tail ? kept.reversed() : kept)
    }
}
