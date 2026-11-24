// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import Foundation

/// `MeltypeIME --convert-eval <tsv>`: かな → 漢字の正解表を、製品と同じ `MeltypeConverter` の経路で流し、
/// 行ごとに第一候補と一致を TSV で出します (`tools/ajimee-eval.py` が読んで採点する)。
///
/// 入力の列 (Tab 区切り、4 列。先頭が `#` の行は無視):
/// `id`	`reading`	`accepted`	`left_context`
/// - `reading`: ひらがなの読み (空の行は飛ばす)
/// - `accepted`: 正解。複数許容は `|` 区切り
/// - `left_context`: 左文脈 (無ければ空 → `context: nil`)
///
/// 出力 (stdout): 1 行 1 件 `id<TAB>hit<TAB>top<TAB>ms` と、末尾に `# summary ...`。
/// `--top <N>` を付けると 5 列目に、同じ読みの候補を上位 N 個 (重複なし、U+001F 区切り) 出す。第一候補しか見えないと、
/// 外れが「正解が候補に並んでいるのに順位が低い」のか「そもそも候補に無い」のか分けられないため。
/// 候補は `MeltypeConverter.rawMainResults` (製品が `clauses` で使う `results` と同じ azooKey の結果) から取り、時間は測らない。
/// `--product-candidates` を付けると、`--top` の候補を `MeltypeConverter.candidates(for:)` (製品の候補ウィンドウに渡る一覧。
/// 末尾が助詞のときの語幹の候補を足し、キャッシュと 30 個の上限を通る。左文脈は常に無し) から取る。画面と同じ経路の azooKey 側の候補を見るための口。
/// 学習データは一時フォルダーを使い、利用者の `~/Library/Application Support/Meltype/azooKey` に触りません。
///
/// `--weights <gguf>` を付けると Zenzai (ニューラル変換) を ON にして測ります。製品の `ZenzaiModel` と同じ検査
/// (サイズ・SHA-256 をアプリに固定した値と比べる) を通したモデルだけ使い、検査が通らなければ測らずに終了します。
/// モデルのファイルは一時フォルダーに置いて検査するので、アプリのデータフォルダーには書きません (通信もしません)。
enum ConvertEval {
    struct Row {
        let id: String
        let reading: String
        let accepted: Set<String>
        let context: String
    }

    private static func parse(_ text: String) -> [Row] {
        var rows: [Row] = []
        for (index, line) in text.split(separator: "\n", omittingEmptySubsequences: false).enumerated() {
            if line.isEmpty || line.hasPrefix("#") { continue }
            let columns = line.split(separator: "\t", omittingEmptySubsequences: false).map(String.init)
            guard columns.count >= 3, !columns[1].isEmpty else { continue }
            let accepted = columns[2].split(separator: "|").map(String.init).filter { !$0.isEmpty }
            guard !accepted.isEmpty else { continue }
            rows.append(Row(id: columns.count > 0 ? columns[0] : String(index),
                            reading: columns[1],
                            accepted: Set(accepted),
                            context: columns.count >= 4 ? columns[3] : ""))
        }
        return rows
    }

    /// `--weights` で渡した GGUF を、製品の `ZenzaiModel` と同じ検査 (サイズ・SHA-256) を通して使えるようにする。
    /// 一時フォルダーの `models/` に置いて `poll(enabled: true)` を呼ぶので、通信もアプリのデータフォルダーへの書き込みも起きません。
    /// CLI は RunLoop を回さないので、検査の結果 (メインキューに戻るブロック) は自分で回して待ちます。
    private static func makeZenzai(weights: String) -> ZenzaiModel? {
        let source = ZenzaiModel.Source.production
        // 通信しないための前検査: `ZenzaiModel.poll` は、置かれたファイルが検査に落ちると本物のモデルを取得し直してしまう。
        // それを避けるため、コピーの前に、`ZenzaiModel` と同じ検査 (サイズ・SHA-256) を自分で行い、合わなければ何も置かずに止める。
        let attributes = try? FileManager.default.attributesOfItem(atPath: weights)
        let size = (attributes?[.size] as? NSNumber)?.int64Value
        guard attributes?[.type] as? FileAttributeType == .typeRegular, size == source.size else {
            print("--weights のファイルのサイズが、アプリに固定した値 (\(source.size) バイト) と合いません。通信して取り直すことはしないので、測らずに終わります: \(weights)")
            return nil
        }
        guard Updater.sha256(of: URL(fileURLWithPath: weights)) == source.sha256 else {
            print("--weights のファイルの SHA-256 が、アプリに固定した値と合いません。通信して取り直すことはしないので、測らずに終わります: \(weights)")
            return nil
        }
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("meltype-convert-eval-zenzai-\(getpid())", isDirectory: true)
        let models = directory.appendingPathComponent("models", isDirectory: true)
        try? FileManager.default.createDirectory(at: models, withIntermediateDirectories: true)
        let destination = models.appendingPathComponent(source.fileName)
        try? FileManager.default.removeItem(at: destination)
        do { try FileManager.default.copyItem(at: URL(fileURLWithPath: weights), to: destination) } catch {
            print("Zenzai のモデルをコピーできません: \(weights) (\(error.localizedDescription))")
            try? FileManager.default.removeItem(at: directory)
            return nil
        }
        let model = ZenzaiModel(source: source, dataDirectory: { directory }, turnOffSetting: { false })
        model.poll(enabled: true)
        let deadline = Date().addingTimeInterval(120)
        while !model.isReady && Date() < deadline {
            RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.2))
        }
        // モデルのファイルは、azooKey が変換のときに読むので、測り終わるまで残す (run() の末尾で消す)。
        // 検査が通らなかったときは、コピーしたモデル (約 74 MB) を残さない
        if model.isUsable { return model }
        try? FileManager.default.removeItem(at: directory)
        return nil
    }

    static func run(_ arguments: [String]) -> Bool {
        guard let flag = arguments.firstIndex(of: "--convert-eval"), arguments.indices.contains(flag + 1) else {
            print("--convert-eval <tsv> の形が必要です")
            return false
        }
        let path = arguments[flag + 1]
        guard let text = try? String(contentsOfFile: path, encoding: .utf8) else {
            print("正解表が読めません: \(path)")
            return false
        }
        var rows = parse(text)
        if let limitFlag = arguments.firstIndex(of: "--limit"), arguments.indices.contains(limitFlag + 1),
           let limit = Int(arguments[limitFlag + 1]), limit > 0 {
            rows = Array(rows.prefix(limit))
        }
        if rows.isEmpty {
            print("正解表に行がありません: \(path)")
            return false
        }
        let productCandidates = arguments.contains("--product-candidates")
        var topCount = 0
        if let topFlag = arguments.firstIndex(of: "--top"), arguments.indices.contains(topFlag + 1),
           let value = Int(arguments[topFlag + 1]), value > 0 {
            topCount = min(value, 100)
        }

        // 利用者の学習データに触れない (Bench.swift と同じ作法)
        let memory = FileManager.default.temporaryDirectory.appendingPathComponent("meltype-convert-eval-\(getpid())", isDirectory: true)
        try? FileManager.default.createDirectory(at: memory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: memory) }
        // `--weights` があれば Zenzai を ON にする。通らなければ測らない (推測で測った数字にしない)
        var zenzai: ZenzaiModel?
        if let weightsFlag = arguments.firstIndex(of: "--weights"), arguments.indices.contains(weightsFlag + 1) {
            guard let model = makeZenzai(weights: arguments[weightsFlag + 1]) else {
                print("Zenzai のモデルの検査が通りませんでした (--weights のファイルを確認してください)")
                return false
            }
            zenzai = model
        }
        let converter = MeltypeConverter(dictionaryURL: MeltypeConverter.resource("Dictionary"), emojiDictionaryURL: MeltypeConverter.emojiDictionary(),
                                         memoryDirectory: memory, incremental: true,
                                         zenzai: zenzai, zenzaiEnabled: { zenzai != nil })
        _ = converter.clauses(for: "きょうはいいてんき", context: nil)   // ウォームアップ (辞書・モデルの読み込み)
        converter.endComposition()
        print("# summary Zenzai \(zenzai == nil ? "なし (AzooKeyKanaKanjiConverter の変換だけ)" : "ON (\(ZenzaiModel.Source.production.fileName))")")

        var hits = 0
        var withContext = (0, 0)
        var withoutContext = (0, 0)
        var times: [Double] = []
        var firstMismatch = 0   // --top のとき、候補の先頭が第一候補 (clauses) と食い違った行数 (0 のはず)
        for row in rows {
            // 行ごとに組版を終えて、前の行の差分変換・学習が次の行に食い込まないようにする
            converter.endComposition()
            let start = Date()
            let top = converter.clauses(for: row.reading, context: row.context.isEmpty ? nil : row.context).map(\.text).joined()
            let ms = Date().timeIntervalSince(start) * 1000
            times.append(ms)
            let hit = row.accepted.contains(top)
            if hit { hits += 1 }
            if row.context.isEmpty { withoutContext.1 += 1; if hit { withoutContext.0 += 1 } } else { withContext.1 += 1; if hit { withContext.0 += 1 } }
            var line = "\(row.id)\t\(hit ? 1 : 0)\t\(top)\t\(String(format: "%.1f", ms))"
            if topCount > 0 {
                // 時間を測ったあとで候補を取る (測定に入れない)。表の都合で、タブ・改行は空白にして U+001F で区切る
                var seen = Set<String>()
                var list: [String] = []
                let texts: [String]
                if productCandidates {
                    texts = converter.candidates(for: row.reading)   // 製品の候補ウィンドウの経路 (左文脈なし)
                } else {
                    texts = converter.rawMainResults(for: row.reading, context: row.context.isEmpty ? nil : row.context).map(\.text)
                }
                for text in texts where seen.insert(text).inserted {
                    list.append(text.replacingOccurrences(of: "\t", with: " ").replacingOccurrences(of: "\n", with: " "))
                    if list.count >= topCount { break }
                }
                if !productCandidates, list.first != top { firstMismatch += 1 }
                line += "\t" + list.joined(separator: "\u{1F}")
            }
            print(line)
        }
        times.sort()
        // nearest-rank (昇順で ⌈q·n⌉ 番目)。整数だけで計算する (0.95 の浮動小数点の誤差を避ける)
        let p50 = times[(times.count + 1) / 2 - 1]
        let p95 = times[(times.count * 95 + 99) / 100 - 1]
        print("# summary 行数 \(rows.count) 第一候補の完全一致 \(hits) 件 (\(String(format: "%.1f", Double(hits) * 100 / Double(rows.count)))%)")
        print("# summary 左文脈あり \(withContext.0)/\(withContext.1) 左文脈なし \(withoutContext.0)/\(withoutContext.1)")
        if topCount > 0 {
            // --product-candidates のときは、文脈なしの候補 (candidates(for:)) と、文脈ありの第一候補 (clauses) は先頭が違ってよいので、数えない
            print(productCandidates ? "# summary --top \(topCount) (--product-candidates): 先頭と第一候補の食い違いは数えない"
                                    : "# summary --top \(topCount): 候補の先頭と第一候補の食い違い \(firstMismatch) 件")
        } else if productCandidates {
            print("# summary --product-candidates は --top <N> と一緒に付けたときだけ効く (候補は出していない)")
        }
        print("# summary 1 行の変換 p50 \(String(format: "%.1f", p50)) ms / p95 \(String(format: "%.1f", p95)) ms")
        if let model = zenzai { try? FileManager.default.removeItem(at: model.modelsDirectory.deletingLastPathComponent()) }   // 一時フォルダーのモデルの片付け
        return true
    }
}
