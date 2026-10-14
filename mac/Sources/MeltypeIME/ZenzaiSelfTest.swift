// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import CryptoKit
import Foundation
import MeltypeDictionaryKit

/// `MeltypeIME --zenzai-self-test`: Zenzai のモデルの取得 (ZenzaiModel) の 3 つの状態 (ダウンロード中・失敗・準備完了) を、
/// 一時フォルダーとローカルの file: URL で確かめる (通信しない・設定や導入済みの Meltype に触らない)。
/// `--network` を付けると、本物の URL から 1 回だけ取得し、サイズと SHA-256 が固定した値と合うことも確かめる (約 74 MB)。
enum ZenzaiSelfTest {
    private static var failures: [String] = []

    private static func check(_ condition: Bool, _ message: String) {
        print("[\(condition ? "PASS" : "FAIL")] \(message)")
        if !condition { failures.append(message) }
    }

    /// メインのループを回して、model の取得が終わる (isBusy が false になる) のを待つ。
    private static func wait(_ model: ZenzaiModel, seconds: TimeInterval) -> Bool {
        let limit = Date().addingTimeInterval(seconds)
        while model.isBusy && Date() < limit { RunLoop.main.run(mode: .default, before: Date().addingTimeInterval(0.05)) }
        return !model.isBusy
    }

    private static func status(_ model: ZenzaiModel) -> ZenzaiStatus? {
        (try? Data(contentsOf: model.statusFile)).flatMap { ZenzaiStatus(data: $0) }
    }

    private static func permissions(_ url: URL) -> Int? {
        ((try? FileManager.default.attributesOfItem(atPath: url.path))?[.posixPermissions] as? NSNumber)?.intValue
    }

    private static func hex(_ data: Data) -> String { SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined() }

    /// 設定を OFF に戻す処理が呼ばれた回数。
    private final class Counter { var count = 0 }

    private static func makeModel(_ directory: URL, source: ZenzaiModel.Source, offCalls: Counter) -> ZenzaiModel {
        ZenzaiModel(source: source, dataDirectory: { directory }, turnOffSetting: { offCalls.count += 1; return true })
    }

    private static func convertCheck(root: URL, weights: URL, cases: URL, offCalls: Counter) {
        guard let weightData = try? Data(contentsOf: weights), let table = try? String(contentsOfFile: cases.path, encoding: .utf8) else {
            check(false, "(変換) 重みか正解表を読めない")
            return
        }
        let dir = root.appendingPathComponent("convert", isDirectory: true)
        let source = ZenzaiModel.Source(url: weights, size: Int64(weightData.count), sha256: hex(weightData), allowedHost: { _ in false },
                                        allowedSchemes: ["file"], fileName: "zenz-test.gguf")
        let model = makeModel(dir, source: source, offCalls: offCalls)
        model.poll(enabled: true)
        check(wait(model, seconds: 120) && model.isUsable, "(変換) 重みを取得と同じ検査で用意できる")
        guard model.isUsable else { return }
        // 列: category reading expected left_context note accepted。先頭 (見出し) は飛ばす。accepted は | 区切りの別の正解 (あれば)
        let rows: [(reading: String, accepted: Set<String>, context: String)] = table.split(separator: "\n").dropFirst().compactMap { line in
            let columns = line.split(separator: "\t", omittingEmptySubsequences: false).map(String.init)
            guard columns.count >= 4, !columns[1].isEmpty else { return nil }
            let extra = columns.count > 5 ? columns[5].split(separator: "|").map(String.init) : []
            return (columns[1], Set([columns[2]] + extra), String(columns[3].suffix(10)))
        }
        func run(zenzai: Bool) -> (hits: Int, p95: Double) {
            let memory = root.appendingPathComponent("memory-\(zenzai)", isDirectory: true)
            let converter = MeltypeConverter(dictionaryURL: MeltypeConverter.resource("Dictionary"), emojiDictionaryURL: MeltypeConverter.emojiDictionary(),
                                             memoryDirectory: memory, incremental: true, zenzai: zenzai ? model : nil, zenzaiEnabled: { zenzai })
            var hits = 0
            var times: [Double] = []
            for row in rows {
                converter.endComposition()
                let start = Date()
                let top = converter.clauses(for: row.reading, context: row.context.isEmpty ? nil : row.context).map(\.text).joined()
                times.append(Date().timeIntervalSince(start) * 1000)
                if row.accepted.contains(top) { hits += 1 }
            }
            times.sort()
            return (hits, times[min(times.count - 1, Int(Double(times.count) * 0.95))])
        }
        let off = run(zenzai: false)
        let on = run(zenzai: true)
        print("  変換 (\(rows.count) 文、第一候補の完全一致。1 回の変換の時間): Zenzai なし \(off.hits) 件 (p95 \(String(format: "%.1f", off.p95)) ms) → あり \(on.hits) 件 (p95 \(String(format: "%.1f", on.p95)) ms)")
        check(!rows.isEmpty && on.hits > off.hits, "(変換) Zenzai を ON にすると、第一候補の一致が増える (製品の経路で効いている)")
        check(model.isUsable, "(変換) 計測のあいだ、安全弁で止められていない")
    }

    static func run(network: Bool) -> Bool {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("meltype-zenzai-test-\(UUID().uuidString)", isDirectory: true)
        try? FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: root) }

        // 取得元に見立てる、ローカルのファイル (1 MB)
        let payload = Data((0..<(1024 * 1024)).map { UInt8($0 % 251) })
        let origin = root.appendingPathComponent("origin.gguf")
        try? payload.write(to: origin)
        func local(size: Int64, sha256: String) -> ZenzaiModel.Source {
            ZenzaiModel.Source(url: origin, size: size, sha256: sha256, allowedHost: { _ in false }, allowedSchemes: ["file"], fileName: "zenz-test.gguf")
        }
        let offCalls = Counter()

        // 1. 準備完了: ダウンロード中の状態が先に書かれ、検査を通ると ready、権限は 0700 / 0600
        do {
            let dir = root.appendingPathComponent("ok", isDirectory: true)
            let model = makeModel(dir, source: local(size: Int64(payload.count), sha256: hex(payload)), offCalls: offCalls)
            model.poll(enabled: false)
            check(!model.isBusy && status(model) == nil, "OFF のあいだは何もしない (状態ファイルも作らない)")
            model.poll(enabled: true)
            check(model.isBusy && !model.isUsable && status(model)?.state == .downloading, "ON にすると『ダウンロード中』になり、まだ使わない")
            check(wait(model, seconds: 20), "取得が終わる")
            check(model.isReady && model.isUsable && status(model)?.state == .ready, "検査を通ると『準備できました』")
            check(permissions(model.modelsDirectory) == 0o700 && permissions(model.weightURL) == 0o600, "models は 0700、ファイルは 0600")
            check((try? Data(contentsOf: model.weightURL)) == payload, "置かれたファイルが元と同じ")
            check(((try? FileManager.default.contentsOfDirectory(atPath: model.modelsDirectory.path)) ?? []).filter { $0.hasSuffix(".part") }.isEmpty, "一時ファイルが残らない")
            check(offCalls.count == 0, "成功のとき設定を OFF に戻さない")
            // 2 度目 (IME の再起動のつもり): 既にあるファイルが合っていれば取り直さない
            try? FileManager.default.removeItem(at: origin)
            let again = makeModel(dir, source: local(size: Int64(payload.count), sha256: hex(payload)), offCalls: offCalls)
            again.poll(enabled: true)
            check(wait(again, seconds: 20) && again.isReady, "既にある正しいファイルは、取得元が無くてもそのまま使える (通信しない)")
            try? payload.write(to: origin)
        }

        // 2. 失敗: SHA-256 が違う → ファイルを置かず、失敗の理由、設定を OFF に戻す。OFF → ON にし直すまで取り直さない
        do {
            let dir = root.appendingPathComponent("bad-sha", isDirectory: true)
            let before = offCalls.count
            let model = makeModel(dir, source: local(size: Int64(payload.count), sha256: String(repeating: "0", count: 64)), offCalls: offCalls)
            model.poll(enabled: true)
            check(wait(model, seconds: 20), "(SHA-256 違い) 取得が終わる")
            let s = status(model)
            check(!model.isReady && s?.state == .failed && (s?.error ?? "").contains("SHA-256"), "SHA-256 が違うと『失敗: …』")
            check(!FileManager.default.fileExists(atPath: model.weightURL.path), "検査に通らないファイルは置かない")
            check(((try? FileManager.default.contentsOfDirectory(atPath: model.modelsDirectory.path)) ?? []).isEmpty, "検査に通らない一時ファイルは消す")
            check(offCalls.count == before + 1, "設定を OFF に戻す")
            model.poll(enabled: true)
            check(!model.isBusy, "ON のままでは取り直さない")
            model.poll(enabled: false)
            model.poll(enabled: true)
            check(model.isBusy, "OFF → ON にし直すと取り直す")
            _ = wait(model, seconds: 20)
        }

        // 3. 失敗: サイズが違う
        do {
            let dir = root.appendingPathComponent("bad-size", isDirectory: true)
            let model = makeModel(dir, source: local(size: Int64(payload.count) + 1, sha256: hex(payload)), offCalls: offCalls)
            model.poll(enabled: true)
            check(wait(model, seconds: 20), "(サイズ違い) 取得が終わる")
            check(!model.isReady && status(model)?.state == .failed && !FileManager.default.fileExists(atPath: model.weightURL.path), "サイズが違うと失敗し、置かない")
        }

        // 4. 失敗: 取得元が無い (通信できない場合の代わり)
        do {
            let dir = root.appendingPathComponent("no-origin", isDirectory: true)
            var source = local(size: Int64(payload.count), sha256: hex(payload))
            source.url = root.appendingPathComponent("missing.gguf")
            let model = makeModel(dir, source: source, offCalls: offCalls)
            model.poll(enabled: true)
            check(wait(model, seconds: 20), "(取得元なし) 取得が終わる")
            check(!model.isReady && status(model)?.state == .failed, "取得できないと失敗")
        }

        // 5. https 以外の取得元は断る (本番と同じ許可スキームのとき)
        do {
            let dir = root.appendingPathComponent("scheme", isDirectory: true)
            var source = local(size: Int64(payload.count), sha256: hex(payload))
            source.allowedSchemes = ["https"]
            let model = makeModel(dir, source: source, offCalls: offCalls)
            model.poll(enabled: true)
            check(wait(model, seconds: 20) && status(model)?.state == .failed, "file: など https 以外の取得元は断る")
        }

        // 6. このプロセスだけ止める (遅い・読み込めない)
        do {
            let dir = root.appendingPathComponent("suspend", isDirectory: true)
            let model = makeModel(dir, source: local(size: Int64(payload.count), sha256: hex(payload)), offCalls: offCalls)
            model.poll(enabled: true)
            _ = wait(model, seconds: 20)
            check(model.isUsable, "(止める前) 使える")
            model.suspend(reason: "変換が遅いため一時的に OFF")
            check(model.isReady && !model.isUsable && status(model)?.state == .slow, "止めると使わず、状態は『遅いため一時的に OFF』")
        }

        // 7. 本物の URL (1 回だけ。サイズと SHA-256 が固定した値と合う)
        if network {
            let dir = root.appendingPathComponent("network", isDirectory: true)
            let model = makeModel(dir, source: .production, offCalls: offCalls)
            let start = Date()
            model.poll(enabled: true)
            check(model.isBusy && status(model)?.state == .downloading, "(本物の URL) ダウンロード中になる")
            var percents = Set<Int>()
            let limit = Date().addingTimeInterval(20 * 60)
            while model.isBusy && Date() < limit {
                RunLoop.main.run(mode: .default, before: Date().addingTimeInterval(0.25))
                if let p = status(model)?.progress { percents.insert(p) }
            }
            let size = ((try? FileManager.default.attributesOfItem(atPath: model.weightURL.path))?[.size] as? NSNumber)?.int64Value
            let sha = Updater.sha256(of: model.weightURL)
            print("  本物の URL: \(String(format: "%.1f", Date().timeIntervalSince(start))) 秒、サイズ \(size.map(String.init) ?? "なし")、SHA-256 \(sha ?? "なし")、進みの値 \(percents.count) 種類")
            check(model.isReady && status(model)?.state == .ready, "(本物の URL) 取得できて、準備完了になる")
            check(size == ZenzaiModel.Source.production.size && sha == ZenzaiModel.Source.production.sha256, "(本物の URL) サイズと SHA-256 が固定した値と一致する")
            check(permissions(model.weightURL) == 0o600, "(本物の URL) ファイルは 0600")
        } else {
            print("[SKIP] 本物の URL からの取得 (--network を付けたときだけ)")
        }

        // 8. 製品の経路 (MeltypeConverter) で、Zenzai ON が効くか。--weights <gguf> --cases <kanji-cases.tsv> を付けたときだけ。
        //    重みは一時フォルダーにコピーして、取得と同じ検査 (サイズ・SHA-256) を通してから使う。精度の計測そのものは exp/zenzai-1-1 の ZenzaiEval の役目で、ここは配線の確認。
        let arguments = CommandLine.arguments
        if let weightsIndex = arguments.firstIndex(of: "--weights"), arguments.indices.contains(weightsIndex + 1),
           let casesIndex = arguments.firstIndex(of: "--cases"), arguments.indices.contains(casesIndex + 1) {
            convertCheck(root: root, weights: URL(fileURLWithPath: arguments[weightsIndex + 1]), cases: URL(fileURLWithPath: arguments[casesIndex + 1]), offCalls: offCalls)
        } else {
            print("[SKIP] 製品の経路での変換 (--weights と --cases を付けたときだけ)")
        }

        print(failures.isEmpty ? "zenzai self-test: すべて PASS" : "zenzai self-test: \(failures.count) 件 FAIL")
        return failures.isEmpty
    }
}
