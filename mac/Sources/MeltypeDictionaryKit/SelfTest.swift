// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import Foundation

/// 「Meltype 辞書」の画面のロジックの確認 (`MeltypeDictionary --self-test`)。画面 (AppKit) は出さない。
/// コマンドライン ツールだけの環境では Swift のテスト (XCTest / swift-testing) が動かないので、実行ファイルに組み込んでおく。
///   1. ロジック: FFI の文字列の形式・検索のそろえ方・一覧の検索と並べ替え・元に戻す (偽の操作で)
///   2. 本体 (libMeltypeNative.dylib) を通した操作: 環境変数 MELTYPE_DATA_DIR が一時フォルダー (の中の空のフォルダー) を指すときだけ。
///      実際の ~/Library/Application Support/Meltype には書かない (保存場所が一時フォルダーでなければ、本体の確認は飛ばす)。
public enum SelfTest {
    /// 失敗の一覧 (空なら成功) と、出力した行。
    public static func run(libraryPath: String?, environment: [String: String] = ProcessInfo.processInfo.environment, log: (String) -> Void) -> Bool {
        var failures: [String] = []
        var checks = 0
        func check(_ condition: Bool, _ message: String) {
            checks += 1
            if !condition { failures.append(message) }
        }
        checkLogic(check)
        log("ロジック: \(checks) 件")

        let before = checks
        if let directory = environment["MELTYPE_DATA_DIR"], isSafeTemporary(directory) {
            do {
                let native = try NativeDictionary(libraryPath: libraryPath)
                checkNative(native, directory: directory, check)
                log("本体を通した操作: \(checks - before) 件 (保存場所 \(directory))")
            } catch {
                failures.append("本体を読み込めない: \(error)")
            }
        } else {
            log("本体を通した操作: 飛ばした (MELTYPE_DATA_DIR に、一時フォルダーの中の空のフォルダーを指定すると確かめる)")
        }
        for failure in failures { log("FAIL: \(failure)") }
        log(failures.isEmpty ? "PASS: \(checks) 件" : "FAIL: \(failures.count) / \(checks) 件")
        return failures.isEmpty
    }

    /// 一時フォルダーの中で、まだ何も無い (か空の) フォルダーか。実際のデータに書かないための確認。
    static func isSafeTemporary(_ path: String) -> Bool {
        let resolved = URL(fileURLWithPath: path).resolvingSymlinksInPath().path
        let temporary = URL(fileURLWithPath: NSTemporaryDirectory()).resolvingSymlinksInPath().path
        let roots = [temporary, "/private/tmp", "/private/var/folders"]
        guard roots.contains(where: { resolved.hasPrefix($0.hasSuffix("/") ? $0 : $0 + "/") }) else { return false }
        let contents = (try? FileManager.default.contentsOfDirectory(atPath: resolved)) ?? []
        return contents.isEmpty
    }

    // ---- 1. ロジック ----

    static func checkLogic(_ check: (Bool, String) -> Void) {
        // FFI の形式
        let keys = [WordKey(reading: "きごう", word: "記号"), WordKey(reading: "めるたいぷ", word: "Meltype")]
        check(FfiFormat.parseKeys(FfiFormat.formatKeys(keys)) == keys, "読み Tab 語 の往復")
        // 設定タブの JSON
        let settingsJson = """
        {"items":[{"key":"LiveConversion","label":"ライブ変換","description":"説明","group":"入力","kind":"bool","value":true},
        {"key":"DetectionLevel","label":"自動判定の強さ","description":"説明","group":"入力","kind":"choice","value":"Balanced","options":[{"value":"Balanced","label":"標準 (Balanced)"},{"value":"Manual","label":"手動 (提案のみ)"}]},
        {"key":"PredictionMinLength","label":"最小の文字数","description":"説明","group":"変換・候補","kind":"int","value":2,"min":1,"max":5}]}
        """
        let catalog = SettingsCatalog.parse(settingsJson)
        check(catalog?.items.count == 3, "設定の JSON を読める")
        check(catalog?.items[0].value == .bool(true) && catalog?.items[1].value == .string("Balanced") && catalog?.items[2].value == .int(2), "値の型 (真偽・文字列・数)")
        check(catalog?.items[1].options?.map(\.label) == ["標準 (Balanced)", "手動 (提案のみ)"] && catalog?.items[2].min == 1 && catalog?.items[2].max == 5, "選択肢・範囲")
        check(catalog?.groups.map(\.name) == ["入力", "変換・候補"] && catalog?.groups[0].items.count == 2, "グループは出てきた順")
        check(SettingsCatalog.parse("{ 壊れた") == nil, "壊れた JSON は nil")
        check(SettingValue.bool(false).jsonLiteral == "false" && SettingValue.int(3).jsonLiteral == "3" && SettingValue.string("a\"b").jsonLiteral == "\"a\\\"b\"", "本体に渡す JSON の値")
        // 専門用語集の一覧 (5 つ目の欄は無くてもよい)
        let domainsText = "ai\tAI\t10\t1\nuser-0123abcd\t自作\t3\t0\t1\nold\t古い形\t2\t0\nbad\tx\tn\t1\nfuture\t先の形\t5\t1\t0\t余分"
        let domains = FfiFormat.parseDomains(domainsText)
        check(domains.map(\.id) == ["ai", "user-0123abcd", "old", "future"], "専門用語集の一覧 (語数が数でない行は飛ばす・欄が増えても読める)")
        check(domains[0].enabled && !domains[0].isUser && !domains[1].enabled && domains[1].isUser && !domains[2].isUser, "有効・自作の欄")
        check(FfiFormat.parseRemovedTerms(FfiFormat.formatRemovedTerms([RemovedTerm(index: 2, key: keys[0], note: "注記")])) == [RemovedTerm(index: 2, key: keys[0], note: "注記")], "位置 Tab 読み Tab 語 Tab 注記 の往復")
        check(FfiFormat.parseSkipped("あ\tA\t理由\n欄なし").map(\.reason) == ["理由"], "入れられなかった語と理由")
        check(FfiFormat.parseDomainImportSummary("3\t1\t2\t名前", id: "user-0123abcd") == DomainImportSummary(id: "user-0123abcd", name: "名前", added: 3, skipped: 1, duplicates: 2), "専門用語集の取り込みの結果")
        check(FfiFormat.parseKeys("きごう\t記号\n\n欄なし\n\t空の読み\n").count == 1, "欄が足りない行・空の読みは飛ばす")
        check(FfiFormat.parseUserEntries("あ\tA\nい\tI").map(\.order) == [0, 1], "登録順はファイルの順")
        let removed = [RemovedEntry(index: 3, key: keys[0]), RemovedEntry(index: 10, key: keys[1])]
        check(FfiFormat.parseRemoved(FfiFormat.formatRemoved(removed)) == removed, "位置 Tab 読み Tab 語 の往復")
        check(FfiFormat.parseRemoved("x\tあ\tA\n-1\tい\tI").isEmpty, "位置が数でない・負なら飛ばす")
        check(FfiFormat.parseDomains("civil\t土木・建設\t2902\t1\nai\tAI\t1111\t0\n壊れた行") ==
              [TermDomainInfo(id: "civil", name: "土木・建設", count: 2902, enabled: true), TermDomainInfo(id: "ai", name: "AI", count: 1111, enabled: false)], "分野の一覧")
        let terms = FfiFormat.parseTermWords("こうぞうぶつ\t構造物\t\t0\nほそう\t舗装\t道路\t1\n欄が足りない\t語")
        check(terms.count == 2 && terms[1].note == "道路" && terms[1].excluded && !terms[0].excluded && terms[1].order == 1, "専門用語の一覧")
        check(FfiFormat.parseImportSummary("3\t1\t2\tUTF-16") == ImportSummary(added: 3, duplicates: 1, skipped: 2, encoding: "UTF-16"), "取り込みの結果")
        check(FfiFormat.parseImportSummary("壊れた") == nil, "取り込みの結果 (不正)")

        // 検索のそろえ方
        check(SearchText.normalize("カタカナＡＢＣabc") == "かたかなabcabc", "カタカナ → ひらがな、全角 → 半角、大文字 → 小文字")

        // 一覧の検索・並べ替え
        let rows = FfiFormat.parseUserEntries("かきくけこ\t書\nあいうえお\t愛\nさしすせそ\tSAS\nあいう\t藍")
        let list = RowList<UserEntry>(sort: SortSpec(column: .order, ascending: false))
        list.replace(rows)
        check(list.visible.map(\.word) == ["藍", "SAS", "愛", "書"], "既定は新しい順")
        list.sort = SortSpec(column: .reading, ascending: true)
        list.apply()
        check(list.visible.map(\.reading) == ["あいう", "あいうえお", "かきくけこ", "さしすせそ"], "読みの順 (五十音)")
        list.sort.ascending = false
        list.apply()
        check(list.visible.first?.reading == "さしすせそ", "読みの逆順")
        list.query = "アイウ"
        list.apply()
        check(list.visible.count == 2, "カタカナで探しても、ひらがなの読みが見つかる")
        list.query = "sas"
        list.apply()
        check(list.visible.map(\.word) == ["SAS"], "英字は大文字・小文字を区別しない")
        list.query = ""
        list.include = { $0.word != "書" }
        list.apply()
        check(list.visible.count == 3, "絞り込み")

        // 複数キーワード (AND)・関連度順・カタカナ/全角
        let search = RowList<UserEntry>(sort: SortSpec(column: .order, ascending: false))
        search.replace(FfiFormat.parseUserEntries("ほけんがいしゃ\t保険会社\nほけん\t保険\nしんほけん\t新保険\nほけんしょう\t保険証\nかいごほけん\t介護保険\nほご\t保護"))
        search.query = "ほけん"
        search.apply()
        check(search.visible.count == 5, "「ほけん」は 5 件 (保護は含まない)")
        check(search.visible.first?.word == "保険", "読みが完全一致する語が先頭")
        check(Set(search.visible.prefix(3).map(\.word)) == ["保険", "保険証", "保険会社"], "前方一致の 3 件が上位")
        check(Set(search.visible.suffix(2).map(\.word)) == ["新保険", "介護保険"], "途中だけの一致は後ろ")
        search.query = "ほけん 保険"
        search.apply()
        check(search.visible.count == 5 && search.visible.first?.word == "保険", "空白区切りは AND (半角)")
        search.query = "ほけん\u{3000}会社"
        search.apply()
        check(search.visible.map(\.word) == ["保険会社"], "全角の空白でも AND")
        search.query = "ホケン　ｶｲｺﾞ"
        search.apply()
        check(search.visible.isEmpty, "半角カナは対象外 (どれも含まない)")
        search.query = "ＨＯ　ほけん"
        search.apply()
        check(search.visible.isEmpty, "全角英字は半角にそろえるが、読みに無ければ 0 件")
        search.query = "ホケン 介護"
        search.apply()
        check(search.visible.map(\.word) == ["介護保険"], "カタカナ + 語の AND")
        search.query = "   "
        search.apply()
        check(search.visible.count == 6, "空白だけの検索は全部")
        search.query = "ほけん"
        search.sort = SortSpec(column: .word, ascending: true)
        search.apply()
        check(search.visible.map(\.word) == search.visible.map(\.word).sorted { Array($0.utf8).lexicographicallyPrecedes(Array($1.utf8)) }, "並べ替えの列を選んでいる間は、その指定を優先")
        check(RowList<UserEntry>.keywords(" ほけん\u{3000}ＡＢ  ") == ["ほけん", "ab"], "キーワードの分け方")
        let fullWidth = RowList<UserEntry>(sort: SortSpec(column: .order, ascending: false))
        fullWidth.replace(FfiFormat.parseUserEntries("えーびーしー\tABC\nえーびー\tAB"))
        fullWidth.query = "ＡＢ"
        fullWidth.apply()
        check(fullWidth.visible.map(\.word) == ["AB", "ABC"], "全角で探しても半角の語が見つかり、完全一致が先")

        let termList = RowList<TermWord>(sort: SortSpec(column: .state, ascending: false))
        termList.replace(terms)
        check(termList.visible.first?.excluded == true, "状態の列で並べると、除外中が先 (逆順)")
        termList.query = "道路"
        termList.apply()
        check(termList.visible.map(\.word) == ["舗装"], "注記でも探せる")

        // 大きな一覧 (専門用語集の IT は約 1.5 万語) でも軽いこと
        var big: [TermWord] = []
        for i in 0..<20000 { big.append(TermWord(key: WordKey(reading: "よみ\(i % 997)ばん\(i)", word: "語\(i)"), note: "", excluded: i % 7 == 0, order: i)) }
        let start = Date()
        let bigList = RowList<TermWord>(sort: SortSpec(column: .reading, ascending: true))
        bigList.replace(big)
        bigList.query = "ばん1"
        bigList.apply()
        bigList.sort = SortSpec(column: .word, ascending: false)
        bigList.apply()
        let elapsed = Date().timeIntervalSince(start)
        check(bigList.visible.count > 1000 && elapsed < 3.0, "2 万語の読み込み・検索・並べ替えが 3 秒以内 (実際 \(String(format: "%.2f", elapsed)) 秒)")

        // 元に戻す (偽の操作で)
        let fake = FakeOperations()
        let a = WordKey(reading: "えー", word: "A"), b = WordKey(reading: "びー", word: "B"), c = WordKey(reading: "しー", word: "C")
        for key in [a, b, c] { _ = DictionaryChange.add(key).perform(on: fake) }
        let removal = DictionaryChange.remove([b, WordKey(reading: "ない", word: "無")]).perform(on: fake)
        check(removal.error == nil && fake.words == [a, c], "削除")
        check(removal.undo == .restore([RemovedEntry(index: 1, key: b)]), "削除の取り消しは、元の位置への復元")
        let restore = removal.undo!.perform(on: fake)
        check(fake.words == [a, b, c], "取り消すと元の位置に戻る")
        check(restore.undo == .remove([b]), "やり直しは、もう一度の削除")
        let update = DictionaryChange.update(from: a, to: WordKey(reading: "えー", word: "Ａ")).perform(on: fake)
        check(fake.words.first?.word == "Ａ" && update.undo == .update(from: WordKey(reading: "えー", word: "Ａ"), to: a), "編集と、その取り消し")
        _ = update.undo!.perform(on: fake)
        check(fake.words.first == a, "編集の取り消し")
        let duplicate = DictionaryChange.add(a).perform(on: fake)
        check(duplicate.error != nil && duplicate.undo == nil, "だめなら理由を返し、取り消しを積まない")

        let original = WordKey(reading: "こうぞうぶつ", word: "構造物"), fixed = WordKey(reading: "こうぞうぶつ", word: "構造仏")
        let edit = DictionaryChange.editTerm(original: original, to: fixed, originalExcluded: false).perform(on: fake)
        check(fake.excluded.contains(original) && fake.words.contains(fixed), "専門用語の編集 = 元を除外 + ユーザー辞書に登録")
        _ = edit.undo!.perform(on: fake)
        check(!fake.excluded.contains(original) && !fake.words.contains(fixed), "専門用語の編集の取り消し")
        fake.excluded.insert(original)
        let editExcluded = DictionaryChange.editTerm(original: original, to: fixed, originalExcluded: true).perform(on: fake)
        _ = editExcluded.undo!.perform(on: fake)
        check(fake.excluded.contains(original), "直す前から除外していた語は、取り消しても除外のまま")
        fake.words.append(fixed)
        let editExisting = DictionaryChange.editTerm(original: WordKey(reading: "ほそう", word: "舗装"), to: fixed, originalExcluded: false).perform(on: fake)
        _ = editExisting.undo!.perform(on: fake)
        check(fake.words.contains(fixed), "前からユーザー辞書にあった語は、取り消しても消さない")
        let exclude = DictionaryChange.exclude([original, fixed]).perform(on: fake)
        check(exclude.undo == .include([original, fixed]), "除外の取り消しは、除外をやめること")
        let d = WordKey(reading: "でぃー", word: "D")
        let many = DictionaryChange.addMany([a, d]).perform(on: fake)
        check(many.undo == .remove([d]), "まとめての登録の取り消しは、新しく登録した語だけを消す")
        // 自作の専門用語集: 移す・取り消す・やり直す
        fake.domains["user-0000abcd"] = ("自作", [], true)
        fake.words = [a, b, c]
        let move = DictionaryChange.moveToDomain(id: "user-0000abcd", keys: [a, b]).perform(on: fake)
        check(move.error == nil && fake.words == [c] && fake.domains["user-0000abcd"]!.words == [a, b], "移すと、ユーザー辞書から消えて専門用語集に入る")
        guard let moveUndo = move.undo else { check(false, "移したら取り消しを返す"); return }
        let moveRedo = moveUndo.perform(on: fake)
        check(moveRedo.error == nil && fake.words == [a, b, c] && fake.domains["user-0000abcd"]!.words.isEmpty, "取り消すと、ユーザー辞書の元の位置に戻り、専門用語集から消える")
        check(moveRedo.undo == .moveToDomain(id: "user-0000abcd", keys: [a, b]), "取り消しの取り消しは、同じ語をもう一度移すこと")
        _ = moveRedo.undo?.perform(on: fake)
        check(fake.words == [c] && fake.domains["user-0000abcd"]!.words == [a, b], "やり直せる")
        let removeTerm = DictionaryChange.removeTerms(id: "user-0000abcd", keys: [a]).perform(on: fake)
        check(fake.domains["user-0000abcd"]!.words == [b] && removeTerm.undo?.actionName == "専門用語の削除", "専門用語を消す")
        _ = removeTerm.undo?.perform(on: fake)
        check(fake.domains["user-0000abcd"]!.words == [a, b], "消した専門用語を元の位置に戻せる")
        let deleted = DictionaryChange.deleteDomain(id: "user-0000abcd").perform(on: fake)
        check(fake.domains.isEmpty && deleted.undo != nil, "専門用語集を消す")
        _ = deleted.undo?.perform(on: fake)
        check(fake.domains["user-0000abcd"]?.words == [a, b] && fake.domains["user-0000abcd"]?.enabled == true, "消した専門用語集を、同じ中身で戻せる")
        let rename = DictionaryChange.renameDomain(id: "user-0000abcd", to: "新しい名前", from: "自作").perform(on: fake)
        check(fake.domains["user-0000abcd"]?.name == "新しい名前" && rename.undo == .renameDomain(id: "user-0000abcd", to: "自作", from: "新しい名前"), "名前の変更と、その取り消し")
        check(DictionaryChange.moveToDomain(id: "user-ffffffff", keys: [a]).perform(on: fake).error != nil, "無い専門用語集へは移せない")
        fake.words = [a]
        let none = DictionaryChange.addMany([a]).perform(on: fake)
        check(none.error == nil && none.undo == nil, "1 語も増えなければ、取り消しを積まない")
    }

    // ---- 2. 本体を通した操作 ----

    static func checkNative(_ dictionary: NativeDictionary, directory: String, _ check: (Bool, String) -> Void) {
        let resolved = URL(fileURLWithPath: directory).resolvingSymlinksInPath().path
        let reported = dictionary.dataDirectory.map { URL(fileURLWithPath: $0).resolvingSymlinksInPath().path }
        check(reported == resolved, "本体の保存場所が MELTYPE_DATA_DIR (\(reported ?? "nil"))")
        guard reported == resolved else { return }   // 実際のデータには書かない

        check(dictionary.userEntries() == [], "最初は空")
        let version = dictionary.userVersion()
        let key = WordKey(reading: "きごうとう", word: "記号等")
        check(dictionary.add(key) == nil, "登録")
        check(dictionary.userVersion() != version, "版が進む")
        check(dictionary.add(key) != nil, "重複は理由を返す")
        check(dictionary.check(WordKey(reading: "き", word: "記"), except: nil) != nil, "短い読みは理由を返す")
        check(dictionary.check(key, except: key) == nil, "編集中の元の語は重複ではない")
        let changed = WordKey(reading: "きごうとう", word: "記号党")
        check(dictionary.update(from: key, to: changed) == nil, "編集")
        check(dictionary.userEntries()?.map(\.key) == [changed], "編集が保存される")
        _ = dictionary.add(WordKey(reading: "あたらしい", word: "新しい"))
        let removal = dictionary.remove([changed])
        check(removal.error == nil && removal.removed == [RemovedEntry(index: 0, key: changed)], "削除 (元の位置つき)")
        check(dictionary.restore(removal.removed) == nil && dictionary.userEntries()?.first?.key == changed, "元の位置に戻る")
        check(dictionary.toReading("kigoutou") == "きごうとう", "ローマ字の読みをひらがなに")
        check(FileManager.default.fileExists(atPath: resolved + "/userdict.txt.bak"), "削除の直前の内容が .bak に残る")

        let exported = resolved + "/export.txt"
        let export = dictionary.exportFile(path: exported)
        check(export.error == nil && export.count == 2, "書き出し")
        let imported = dictionary.importFile(path: exported)
        check(imported.error == nil && imported.summary?.added == 0 && imported.summary?.duplicates == 2 && imported.added.isEmpty, "取り込み (登録済みは増やさない)")
        let bulk = dictionary.addMany([WordKey(reading: "まとめて", word: "纏めて"), changed, WordKey(reading: "x", word: "短い")])
        check(bulk.error == nil && bulk.added == [WordKey(reading: "まとめて", word: "纏めて")], "まとめての登録は、新しい語だけを返す")
        check(dictionary.userProblem() == nil, "読めているときは問題なし")

        let domains = dictionary.termDomains()
        check(Set(domains.map(\.id)) == ["ai", "business", "civil", "it", "medical", "netslang", "video"], "同梱の分野 (\(domains.map(\.id)))")
        check(domains.allSatisfy { !$0.enabled }, "既定はすべて OFF")
        let revision = dictionary.termRevision()
        check(dictionary.setTermDomain(id: "civil", enabled: true), "分野を有効にできる")
        check(dictionary.termDomains().first { $0.id == "civil" }?.enabled == true, "有効になる")
        guard let words = dictionary.termWords(domain: "civil"), words.count > 1000 else {
            check(false, "土木の語を取れる")
            return
        }
        check(dictionary.termWords(domain: "nosuch") == nil, "未知の分野は nil")
        let first = words[0].key
        check(dictionary.setExcluded([first], excluded: true) == nil, "除外できる")
        check(dictionary.termRevision() != revision, "専門用語集の版が進む")
        check(dictionary.termWords(domain: "civil")?.first?.excluded == true, "一覧に除外の印")
        check(dictionary.excludedTerms() == [first], "除外の一覧")
        let fixed = WordKey(reading: words[1].reading, word: words[1].word + "（直し）")
        let edit = DictionaryChange.editTerm(original: words[1].key, to: fixed, originalExcluded: false).perform(on: dictionary)
        check(edit.error == nil && dictionary.userEntries()?.contains { $0.key == fixed } == true, "専門用語を直すと、ユーザー辞書に入る")
        check(dictionary.excludedTerms().contains(words[1].key), "元の語は除外")
        _ = edit.undo?.perform(on: dictionary)
        check(!dictionary.excludedTerms().contains(words[1].key) && dictionary.userEntries()?.contains { $0.key == fixed } == false, "専門用語の編集を取り消せる")
        check(dictionary.setExcluded([first], excluded: false) == nil && dictionary.excludedTerms().isEmpty, "除外をやめられる")
        check(dictionary.setTermDomain(id: "civil", enabled: false), "分野を無効に戻せる")

        // 自作の専門用語集 (ユーザー辞書の語を移す)
        let created = dictionary.createDomain(name: "自作の用語")
        guard created.error == nil, let domainId = created.id else {
            check(false, "自作の専門用語集を作れる (\(created.error ?? "ID なし"))")
            return
        }
        check(dictionary.termDomains().first { $0.id == domainId }.map { $0.isUser && $0.enabled && $0.name == "自作の用語" } == true, "一覧に自作・有効で出る")
        check(dictionary.createDomain(name: "自作の用語").error != nil && dictionary.createDomain(name: "").error != nil, "名前がかぶる・空は理由を返す")
        check(dictionary.termDomains().filter(\.isUser).count == 1 && dictionary.termDomains().filter { !$0.isUser }.count == 7, "同梱の 7 分野は自作ではない")
        _ = dictionary.add(WordKey(reading: "うつしたい", word: "移したい語"))
        let moved = DictionaryChange.moveToDomain(id: domainId, keys: [WordKey(reading: "うつしたい", word: "移したい語"), WordKey(reading: "x", word: "短い")]).perform(on: dictionary)
        check(moved.error == nil && dictionary.userEntries()?.contains { $0.word == "移したい語" } == false, "移した語はユーザー辞書から消える")
        check(dictionary.termWords(domain: domainId)?.map(\.word) == ["移したい語"], "専門用語集に入る")
        let move = dictionary.moveToDomain(id: domainId, keys: [WordKey(reading: "x", word: "短い")])
        check(move.error == nil && move.outcome.skipped.count == 1 && move.outcome.skipped[0].reason.count > 0, "入れられない語は理由つきで返る")
        check(dictionary.checkTerm(id: domainId, WordKey(reading: "うつしたい", word: "移したい語"), except: nil) != nil, "同じ専門用語集の中の重複は理由を返す")
        check(dictionary.updateTerm(id: domainId, from: WordKey(reading: "うつしたい", word: "移したい語"), to: WordKey(reading: "うつしたい", word: "移した語")) == nil, "専門用語を直せる")
        _ = moved.undo?.perform(on: dictionary)
        check(dictionary.userEntries()?.contains { $0.word == "移したい語" } == true, "取り消すとユーザー辞書に戻る (直した語は別なので専門用語集には残る)")
        let exportPath = resolved + "/domain-export.txt"
        _ = dictionary.addTerms(id: domainId, keys: [WordKey(reading: "ほかのよみ", word: "ほかの語")])
        let domainExport = dictionary.exportDomain(id: domainId, path: exportPath)
        check(domainExport.error == nil && domainExport.count == 2, "自作の専門用語集を書き出せる")
        let domainImport = dictionary.importDomain(path: exportPath)
        check(domainImport.error == nil && domainImport.summary?.added == 2 && domainImport.summary?.name == "自作の用語 (2)", "取り込むと新しい専門用語集 (名前は (2))")
        let deleted = DictionaryChange.deleteDomain(id: domainId).perform(on: dictionary)
        check(deleted.error == nil && dictionary.termDomains().contains { $0.id == domainId } == false, "専門用語集を消せる")
        _ = deleted.undo?.perform(on: dictionary)
        check(dictionary.termDomains().first { $0.id == domainId }?.count == 2, "消した専門用語集を、同じ中身で戻せる")
        if let importedId = domainImport.summary?.id { _ = dictionary.deleteDomain(id: importedId) }
        _ = dictionary.deleteDomain(id: domainId)

        // 設定タブ
        guard let catalog = dictionary.settings() else {
            check(false, "設定の一覧を取れる")
            return
        }
        check(catalog.items.count >= 15 && catalog.groups.map(\.name) == ["入力", "変換・候補", "辞書", "ログ"], "設定のグループ (\(catalog.groups.map(\.name)))")
        func item(_ key: String) -> SettingItem? { dictionary.settings()?.items.first { $0.key == key } }
        check(item("LiveConversion")?.value == .bool(true), "ライブ変換は既定で ON")
        check(dictionary.setSetting(key: "LiveConversion", value: .bool(false)) == nil && item("LiveConversion")?.value == .bool(false), "設定(真偽)を変えられる")
        check(dictionary.setSetting(key: "DetectionLevel", value: .string("Conservative")) == nil && item("DetectionLevel")?.value == .string("Conservative"), "設定(選択肢)を変えられる")
        check(dictionary.setSetting(key: "PredictionMinLength", value: .int(4)) == nil && item("PredictionMinLength")?.value == .int(4), "設定(数)を変えられる")
        check(dictionary.setSetting(key: "PredictionMinLength", value: .int(9)) != nil && item("PredictionMinLength")?.value == .int(4), "範囲の外は理由を返して変えない")
        check(dictionary.setSetting(key: "DetectionLevel", value: .string("Nope")) != nil, "知らない選択肢は理由を返す")
        check(dictionary.setSetting(key: "NoSuchKey", value: .bool(true)) != nil, "知らない項目は理由を返す")
        check(dictionary.setSetting(key: "LiveConversion", value: .int(1)) != nil, "型の違う値は理由を返す")
        check(dictionary.resetSettings() == nil && item("LiveConversion")?.value == .bool(true) && item("PredictionMinLength")?.value == .int(2), "既定値に戻せる")
    }
}

/// --self-test 用の、メモリの上だけの偽の操作 (本体の UserDictionary・TermDomains と同じ決まり)。
final class FakeOperations: DictionaryOperations {
    var words: [WordKey] = []
    var excluded: Set<WordKey> = []

    func add(_ key: WordKey) -> String? {
        if words.contains(key) { return "同じ読みと単語が、すでに登録されています。" }
        words.append(key)
        return nil
    }

    func addMany(_ keys: [WordKey]) -> (error: String?, added: [WordKey]) {
        let added = keys.filter { add($0) == nil }
        return (nil, added)
    }

    func update(from old: WordKey, to new: WordKey) -> String? {
        guard let index = words.firstIndex(of: old) else { return "元の語が見つかりません" }
        if old != new && words.contains(new) { return "同じ読みと単語が、すでに登録されています。" }
        words[index] = new
        return nil
    }

    func remove(_ keys: [WordKey]) -> (error: String?, removed: [RemovedEntry]) {
        let targets = Set(keys)
        let removed = words.enumerated().filter { targets.contains($0.element) }.map { RemovedEntry(index: $0.offset, key: $0.element) }
        words.removeAll { targets.contains($0) }
        return (nil, removed)
    }

    func restore(_ entries: [RemovedEntry]) -> String? {
        for entry in entries.sorted(by: { $0.index < $1.index }) where !words.contains(entry.key) {
            words.insert(entry.key, at: min(entry.index, words.count))
        }
        return nil
    }

    func setExcluded(_ keys: [WordKey], excluded: Bool) -> String? {
        if excluded { self.excluded.formUnion(keys) } else { self.excluded.subtract(keys) }
        return nil
    }

    func editTerm(original: WordKey, to new: WordKey) -> (error: String?, added: Bool) {
        let added = add(new) == nil
        if original != new { excluded.insert(original) }
        return (nil, added)
    }

    // 自作の専門用語集 (メモリの上だけ)
    var domains: [String: (name: String, words: [WordKey], enabled: Bool)] = [:]

    func moveToDomain(id: String, keys: [WordKey]) -> (error: String?, outcome: MoveOutcome) {
        guard domains[id] != nil else { return ("その専門用語集が見つかりません", MoveOutcome()) }
        var outcome = MoveOutcome()
        let movable = keys.filter { $0.reading.count >= 2 }
        outcome.skipped = keys.filter { $0.reading.count < 2 }.map { SkippedWord(key: $0, reason: "読みが短い") }
        outcome.added = movable.filter { !domains[id]!.words.contains($0) }
        domains[id]!.words.append(contentsOf: outcome.added)
        outcome.removed = remove(movable).removed
        return (nil, outcome)
    }

    func addTerms(id: String, keys: [WordKey]) -> (error: String?, added: [WordKey], skipped: [SkippedWord]) {
        guard domains[id] != nil else { return ("その専門用語集が見つかりません", [], []) }
        let added = keys.filter { !domains[id]!.words.contains($0) }
        domains[id]!.words.append(contentsOf: added)
        return (nil, added, [])
    }

    func removeTerms(id: String, keys: [WordKey]) -> (error: String?, removed: [RemovedTerm]) {
        guard let domain = domains[id] else { return ("その専門用語集が見つかりません", []) }
        let targets = Set(keys)
        let removed = domain.words.enumerated().filter { targets.contains($0.element) }.map { RemovedTerm(index: $0.offset, key: $0.element) }
        domains[id]!.words.removeAll { targets.contains($0) }
        return (nil, removed)
    }

    func restoreTerms(id: String, entries: [RemovedTerm]) -> String? {
        guard domains[id] != nil else { return "その専門用語集が見つかりません" }
        for entry in entries.sorted(by: { $0.index < $1.index }) where !domains[id]!.words.contains(entry.key) {
            domains[id]!.words.insert(entry.key, at: min(entry.index, domains[id]!.words.count))
        }
        return nil
    }

    func updateTerm(id: String, from old: WordKey, to new: WordKey) -> String? {
        guard let index = domains[id]?.words.firstIndex(of: old) else { return "元の語が見つかりません" }
        domains[id]!.words[index] = new
        return nil
    }

    func deleteDomain(id: String) -> (error: String?, deleted: DeletedDomain?) {
        guard let domain = domains.removeValue(forKey: id) else { return ("その専門用語集が見つかりません", nil) }
        return (nil, DeletedDomain(id: id, name: domain.name, content: domain.words.map { "\($0.reading)\t\($0.word)" }.joined(separator: "\n"), wasEnabled: domain.enabled))
    }

    func restoreDomain(_ deleted: DeletedDomain) -> String? {
        domains[deleted.id] = (deleted.name, FfiFormat.parseKeys(deleted.content), deleted.wasEnabled)
        return nil
    }

    func renameDomain(id: String, name: String) -> String? {
        guard domains[id] != nil else { return "その専門用語集が見つかりません" }
        domains[id]!.name = name
        return nil
    }
}
