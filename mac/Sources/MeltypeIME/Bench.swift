// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import Foundation

/// `MeltypeIME --bench`: IMKServer を立てずに、変換の速度 (従来 = 毎回 stopComposition / 新 = 差分変換) と正しさを計測する。
/// 学習データは一時フォルダーを使う (利用者のデータには触れない)。
enum Bench {
    static let corpus: [String] = [
        "きょうはいいてんきですね",
        "あしたのかいぎはなんじからですか",
        "このほんはとてもおもしろいです",
        "いまからえきまであるいていきます",
        "わたしはまいあさろくじにおきます",
        "おくれてもうしわけございません",
        "ごかくにんのほどよろしくおねがいいたします",
        "ほうこくしょをきょうじゅうにていしゅつしてください",
        "らいしゅうのうちあわせのにっていをちょうせいしたいです",
        "けんとうしたけっかしさくをさいけっていたしました",
        "みなさまにはいつもたいへんおせわになっております",
        "あたらしいしょうひんのはつばいびがきまりました",
        "いただいたごいけんをふまえてけいかくをみなおします",
        "おおあめのためでんしゃがうんてんをみあわせています",
        "こどものころにすんでいたまちをひさしぶりにたずねた",
        "しゅうまつはかぞくでおんせんにいくよていです",
        "さいきんきんじょにあたらしいらーめんやがひらいた",
        "にほんごのにゅうりょくほうほうをかいぜんするけんきゅう",
        "このあぷりはそくどとせいどのりょうりつをめざしている",
        "きのうのばんはおそくまでともだちとはなしていました",
        "でんわをかけなおすのでしばらくおまちくださいませ",
        "にもつはらいしゅうのすいようびにとどくそうです",
        "かれはだれよりもはやくかいじょうにとうちゃくした",
        "ぼくのしゅみはどくしょとさんぽとおかしづくりです",
        "ごちゅうもんいただいたしなものはほんじつはっそういたしました",
        "しがつからあたらしいたいせいでぎょうむをすすめていきます",
        "せんじつはおいそがしいなかおじかんをいただきありがとうございました",
        "ふゆのあさはさむくてふとんからでるのがつらいです",
        "このもんだいのげんいんをとくていするためにろぐをかくにんした",
        "いつもごりよういただきまことにありがとうございます",
    ]

    private static func now() -> Double { Double(DispatchTime.now().uptimeNanoseconds) / 1_000_000 }

    private static func readings(of length: Int) -> String {
        var s = ""
        var i = 0
        while s.count < length { s += corpus[i % corpus.count]; i += 1 }
        return String(s.prefix(length))
    }

    private static func makeConverter(incremental: Bool, memory: URL) -> MeltypeConverter {
        MeltypeConverter(dictionaryURL: MeltypeConverter.resource("Dictionary"), emojiDictionaryURL: MeltypeConverter.emojiDictionary(),
                         memoryDirectory: memory, incremental: incremental)
    }

    /// 呼び出しの種類ごとの累計時間 (ms) と回数。
    struct Split { var p = 0.0, c = 0.0, d = 0.0, pn = 0, cn = 0, dn = 0 }

    /// 1 文字ずつ打つ模擬。Core の 1 キーと同じ 3 呼び出し:
    /// (1) 予測 (読み全体が 40 文字以下のとき) → (2) ライブ変換 (50 文字ごとの区切りの最後) → (3) 文節ごとの候補作成 (各文節の読みを変換。前の文節は resultCache に当たる)。
    private static func typeDown(_ reading: String, converter: MeltypeConverter, split: inout Split) -> [Double] {
        let chars = Array(reading)
        var times: [Double] = []
        for i in 1...chars.count {
            let start = ((i - 1) / 50) * 50
            let engine = String(chars[start..<i])
            let t0 = now()
            if i <= 40 { _ = converter.predictions(for: String(chars[0..<i])) }
            let t1 = now()
            let parts = converter.clauses(for: engine, context: nil)
            let t2 = now()
            for part in parts { _ = converter.clauses(for: part.reading, context: nil) }
            let t3 = now()
            if i <= 40 { split.p += t1 - t0; split.pn += 1 }
            split.c += t2 - t1; split.cn += 1
            split.d += t3 - t2; split.dn += 1
            times.append(t3 - t0)
        }
        return times
    }

    private static func format(_ v: Double) -> String { String(format: "%8.1f", v) }

    private static func top(_ c: MeltypeConverter, _ reading: String) -> [String] {
        c.rawMainResults(for: reading).prefix(10).map(\.text)
    }

    /// 計測と正しさの確認を流す。変換の食い違い (correctness 1 の不一致、correctness 2・3 の変換の mismatches) が 0 なら true。
    /// 予測の違い (predictMismatches・prediction only) は仕様として受け入れた違いを含むので、表示だけで失敗には数えない。
    @discardableResult
    static func run() -> Bool {
        var failures = 0
        let memory = FileManager.default.temporaryDirectory.appendingPathComponent("meltype-bench-\(getpid())", isDirectory: true)
        try? FileManager.default.createDirectory(at: memory, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: memory) }
        let old = makeConverter(incremental: false, memory: memory.appendingPathComponent("old"))
        let new = makeConverter(incremental: true, memory: memory.appendingPathComponent("new"))
        for c in [old, new] {   // ウォームアップ (辞書の読み込み)
            _ = c.clauses(for: "きょうはいいてんき", context: nil)
            _ = c.predictions(for: "きょうは")
            c.endComposition()
        }

        print("== speed (ms per key; 3 calls per key: prediction(<=40 chars) -> live convert(last 50-char chunk) -> per-clause candidates) ==")
        for order in [[false, true], [true, false]] {
            print("\n-- order: \(order.map { $0 ? "new" : "old" }.joined(separator: " -> ")) --")
            print("chars | mode | mean/key | slowest10% mean | key i=51 | total")
            for length in [10, 50, 100, 200, 400] {
                let reading = readings(of: length)
                for incremental in order {
                    let c = incremental ? new : old
                    c.endComposition()
                    var split = Split()
                    let times = typeDown(reading, converter: c, split: &split)
                    c.endComposition()
                    let mean = times.reduce(0, +) / Double(times.count)
                    let tailCount = max(1, times.count / 10)
                    let tailMean = times.sorted().suffix(tailCount).reduce(0, +) / Double(tailCount)
                    let k51 = times.count >= 51 ? format(times[50]) : "     n/a"
                    print(String(format: "%5d | %@ | %@ | %@ | %@ | %@", length, incremental ? "new" : "old", format(mean), format(tailMean), k51, format(times.reduce(0, +))))
                    if length == 100 {
                        print(String(format: "      per-call mean ms: P(%d keys)=%.2f  C=%.2f  clauses(3)=%.2f", split.pn, split.p / Double(max(1, split.pn)), split.c / Double(split.cn), split.d / Double(split.dn)))
                    }
                }
            }
        }

        print("\n== probe: azooKey path cost per call (new converter, ms, mean over corpus sentences >= 20 chars) ==")
        do {
            var all = 0.0, changed = 0.0, same = 0.0, n = 0
            for sentence in corpus where sentence.count >= 20 && sentence.count <= 50 {
                let chars = Array(sentence)
                let prev = String(chars.dropLast()), full = sentence
                new.endComposition()
                _ = new.rawMainResults(for: prev)
                var t = now(); _ = new.rawMainResults(for: full); changed += now() - t          // 1 文字伸ばす (kana2lattice_changed)
                t = now(); _ = new.rawMainResults(for: full); same += now() - t                  // 同じ読みをもう 1 度 (kana2lattice_no_change)
                new.endComposition()
                t = now(); _ = new.rawMainResults(for: full); all += now() - t                   // 先頭から (kana2lattice_all)
                n += 1
            }
            new.endComposition()
            print(String(format: "all(fresh)=%.2f  changed(+1 char)=%.2f  no_change(same reading again)=%.2f   (n=%d)", all / Double(n), changed / Double(n), same / Double(n), n))
        }

        print("\n== trace: per-key ms of P / C / clause-candidates for a 40-char reading (new converter) ==")
        do {
            let chars = Array(readings(of: 40))
            new.endComposition()
            var line = ""
            for i in 1...40 {
                let whole = String(chars[0..<i])
                let t0 = now(); _ = new.predictions(for: whole)
                let t1 = now(); let parts = new.clauses(for: whole, context: nil)
                let t2 = now(); for part in parts { _ = new.clauses(for: part.reading, context: nil) }
                let t3 = now()
                line += String(format: "%2d: %5.1f %5.1f %5.1f  [%@]\n", i, t1 - t0, t2 - t1, t3 - t2, parts.map(\.reading).joined(separator: "|"))
            }
            new.endComposition()
            print(line)
        }

        print("\n== correctness 1: one-shot vs typed one char at a time (top-10 texts of raw azooKey results, after endComposition) ==")
        for (name, c) in [("old", old), ("new", new)] {
            var ok = 0
            var report: [String] = []
            for sentence in corpus where sentence.count <= 50 {
                c.endComposition()
                let oneShot = top(c, sentence)
                c.endComposition()
                var typed: [String] = []
                var prefix = ""
                for ch in sentence { prefix.append(ch); typed = top(c, prefix) }
                if oneShot == typed { ok += 1 } else { report.append("  MISMATCH \(sentence)\n    one-shot: \(oneShot)\n    typed   : \(typed)") }
            }
            c.endComposition()
            print("\(name): \(ok) / \(corpus.count) match")
            report.forEach { print($0) }
            failures += report.count
        }

        print("\n== correctness 2: random edits (seed fixed), new vs old(stopComposition every call), top-10 texts and predictions ==")
        var state: UInt64 = 0x2545F4914F6CDD1D
        func rand(_ n: Int) -> Int {
            state = state &* 6364136223846793005 &+ 1442695040888963407
            return Int((state >> 33) % UInt64(n))
        }
        let base = Array(corpus[2] + corpus[7] + corpus[12] + corpus[18])   // 50 文字の区切りをまたぐ
        var length = 1
        var requests = 0, mismatches = 0, predictChecks = 0, predictMismatches = 0
        var examples: [String] = []
        var crossed50 = false
        new.endComposition(); old.endComposition()
        for step in 0..<400 {
            if step == 200 { new.endComposition(); old.endComposition() }   // 確定して打ち直す
            let roll = rand(100)
            var reading: String? = nil
            if roll < 55 && length < base.count { length += 1 }
            else if roll < 75 && length > 1 { length -= 1 }
            else if roll < 90 && length > 1 {
                // 予測 (読み全体が 40 文字以下)
                let whole = String(base[0..<length])
                if length <= 40 {
                    predictChecks += 1
                    let a = new.predictions(for: whole), b = old.predictions(for: whole)
                    if a != b { predictMismatches += 1; if examples.count < 6 { examples.append("  PREDICT \(whole)\n    new: \(a)\n    old: \(b)") } }
                }
                continue
            }
            if length > 50 { crossed50 = true }
            // 要求する読み: 全体 / 50 文字区切りの最後 / 最後の文節らしい短い読み (末尾 1〜10 文字)
            let whole = Array(base[0..<length])
            switch rand(3) {
            case 0: reading = String(whole)
            case 1: reading = String(whole[(((length - 1) / 50) * 50)...])
            default: reading = String(whole[max(0, length - 1 - rand(10))...])
            }
            requests += 1
            let a = top(new, reading!), b = top(old, reading!)
            if a != b { mismatches += 1; if examples.count < 6 { examples.append("  MISMATCH \(reading!)\n    new: \(a)\n    old: \(b)") } }
        }
        print("requests=\(requests) mismatches=\(mismatches); prediction checks=\(predictChecks) mismatches=\(predictMismatches); crossed 50 chars: \(crossed50)")
        failures += mismatches
        examples.forEach { print($0) }

        print("\n== prediction only (live conversion OFF): new vs old, keys 2...40 per sentence (predictions top 8) ==")
        do {
            var sentences = 0, keys = 0, sentencesDiff = 0, keysOrder = 0, keysContent = 0
            var examples: [String] = []
            for sentence in corpus {
                let chars = Array(sentence.prefix(40))
                guard chars.count >= 2 else { continue }
                new.endComposition(); old.endComposition()
                sentences += 1
                var differs = false
                for i in 2...chars.count {
                    let prefix = String(chars[0..<i])
                    let a = new.predictions(for: prefix), b = old.predictions(for: prefix)
                    keys += 1
                    if a == b { continue }
                    differs = true
                    let orderOnly = Set(a) == Set(b)
                    if orderOnly { keysOrder += 1 } else { keysContent += 1 }
                    if examples.count < 3 || (!orderOnly && !examples.contains { $0.contains("content") } && examples.count < 5) {
                        examples.append("  \(orderOnly ? "order-only" : "content") \(prefix)\n    new: \(a)\n    old: \(b)")
                    }
                }
                if differs { sentencesDiff += 1 }
            }
            new.endComposition(); old.endComposition()
            print("sentences with any difference: \(sentencesDiff) / \(sentences); keys: \(keys), order-only differences: \(keysOrder), content differences: \(keysContent)")
            examples.forEach { print($0) }
        }

        print("\n== correctness 3: after learning (learn -> keep typing), new vs old top-10 ==")
        do {
            let learnSentences = [corpus[0], corpus[2], corpus[12]]
            var requests = 0, mismatches = 0
            var examples: [String] = []
            for c in [old, new] {
                c.endComposition()
                for sentence in learnSentences {
                    let candidates = c.candidates(for: sentence)
                    if candidates.count > 1 { c.learn(context: nil, clauses: [(sentence, candidates[1])]) }
                }
            }
            for sentence in corpus where sentence.count <= 50 {
                new.endComposition(); old.endComposition()
                var prefix = ""
                for ch in sentence {
                    prefix.append(ch)
                    requests += 1
                    let a = top(new, prefix), b = top(old, prefix)
                    if a != b { mismatches += 1; if examples.count < 3 { examples.append("  MISMATCH \(prefix)\n    new: \(a)\n    old: \(b)") } }
                }
            }
            new.endComposition(); old.endComposition()
            print("learned \(learnSentences.count) readings; requests=\(requests) mismatches=\(mismatches)")
            examples.forEach { print($0) }
            failures += mismatches
        }
        print("\n== result: \(failures == 0 ? "PASS" : "FAIL") (conversion mismatches: \(failures)) ==")
        return failures == 0
    }
}
