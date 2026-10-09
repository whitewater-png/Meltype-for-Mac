// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import AppKit
import CryptoKit
import Foundation
import UserNotifications

/// 本体 (UpdateCheck.Evaluate) が「更新してよい」と判定した新しい版。
struct UpdateOffer {
    let version: String
    let downloadUrl: URL
    let sha256: String
    let size: Int64
    let releaseUrl: URL

    /// ダウンロード URL が、このリポジトリの Release の「そのタグ・そのファイル名」だけを指しているか (本体の判定を、受け取った側でもう一度確かめる)。
    /// 接頭辞だけだと `..` やパーセントエンコードで別のパスへ逃げられるので、パス全体を完全一致で見る。
    var hasValidDownloadUrl: Bool {
        let text = downloadUrl.absoluteString
        guard text.count <= 300, !text.contains(".."),
              !text.unicodeScalars.contains(where: { $0.value <= 0x20 || $0.value >= 0x7F || "%\\#?@".unicodeScalars.contains($0) }),
              downloadUrl.scheme == "https", downloadUrl.host == "github.com", downloadUrl.port == nil,
              downloadUrl.user == nil, downloadUrl.query == nil, downloadUrl.fragment == nil else { return false }
        let fileName = "Meltype-mac-\(version).zip"
        let prefix = "https://github.com/whitewater-png/Meltype-for-Mac/releases/download/"
        guard text.hasPrefix(prefix), text.hasSuffix("/" + fileName) else { return false }
        // 間のタグは、v1.0.4 / v1.0.4-mac / 1.0.4 の形で、版と同じ数字のものだけ
        let tag = String(text.dropFirst(prefix.count).dropLast(fileName.count + 1))
        var core = tag
        if core.hasPrefix("v") || core.hasPrefix("V") { core.removeFirst() }
        if core.hasSuffix("-mac") { core.removeLast(4) }
        return core == version && !tag.contains("/")
    }
}

/// 新しい版の確認・通知・更新。IME のプロセスで 1 つだけ動く (入力欄ごとのコントローラーからは、これを呼ぶだけ)。
/// 通信は GitHub の最新 Release の情報 (GET) と、利用者が「更新する」を押したときの zip だけ。打った内容は一切送らない。
/// 状態はメインスレッドだけで触り、通信・展開・検査は裏のスレッドで行う (入力を止めない)。
final class UpdateManager {
    static let shared = UpdateManager()

    /// 取得先 (これ以外には問い合わせない)。
    private static let latestReleaseUrl = URL(string: "https://api.github.com/repos/whitewater-png/Meltype-for-Mac/releases/latest")!
    /// 最初の確認までの待ち (IME の起動直後は入力の準備を優先する)。
    private static let firstCheckDelay: TimeInterval = 5 * 60
    /// 確認の間隔。
    private static let interval: TimeInterval = 24 * 60 * 60
    /// 通信に失敗したあと、次に試すまでの最短の間隔 (オフラインで毎回メニューを開くたびに通信を試みないため)。
    private static let retryInterval: TimeInterval = 60 * 60
    private static let maxJsonBytes = 1024 * 1024
    private static let maxZipBytes: Int64 = 100 * 1024 * 1024

    /// 今見つかっている新しい版 (メニューに出す)。IME を起動し直すと消え、次の確認で出る。
    private(set) var offer: UpdateOffer?

    private var timer: Timer?
    private var isChecking = false
    private var isUpdating = false
    private var lastAttempt: Date?

    /// IME の今の版 (Info.plist)。更新の「現在の版」にはこれだけを使う。
    var currentVersion: String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0.0.0"
    }

    // ---- 設定 (update.json) ----

    private var dataDirectory: URL {
        if let path = NativeCore.shared.dataDirectory { return URL(fileURLWithPath: path, isDirectory: true) }
        return FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/Application Support/Meltype", isDirectory: true)
    }

    private var settingsFile: URL { dataDirectory.appendingPathComponent("update.json") }
    private var logFile: URL { dataDirectory.appendingPathComponent("update.log") }

    func loadSettings() -> UpdateSettings {
        let path = settingsFile.path
        let exists = FileManager.default.fileExists(atPath: path)
        return UpdateSettings(data: exists ? try? Data(contentsOf: settingsFile) : nil, fileExists: exists)
    }

    /// 一時ファイルに書いてから置き換える (途中で止まっても元のファイルを壊さない)。フォルダーは 0700、ファイルは 0600。
    private func saveSettings(_ settings: UpdateSettings) {
        guard let data = settings.encoded() else { return }
        let manager = FileManager.default
        do {
            try manager.createDirectory(at: dataDirectory, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
            let temp = dataDirectory.appendingPathComponent("update.\(UUID().uuidString).tmp")
            guard manager.createFile(atPath: temp.path, contents: data, attributes: [.posixPermissions: 0o600]) else { return }
            if rename(temp.path, settingsFile.path) != 0 { try? manager.removeItem(at: temp) }
        } catch {
            NSLog("Meltype: 更新の設定を保存できませんでした")
        }
    }

    var isEnabled: Bool { loadSettings().enabled }

    func setEnabled(_ enabled: Bool) {
        var settings = loadSettings()
        settings.enabled = enabled
        saveSettings(settings)
        if !enabled { offer = nil }
    }

    // ---- 定期の確認 ----

    /// IME の起動時に 1 回呼ぶ。起動 5 分後に確認し、その後は 1 時間ごとに「前の確認から 24 時間たったか」を見る。
    /// OFF のときはタイマーが動いても通信しない (checkIfDue が見る)。
    func start() {
        guard timer == nil else { return }
        Self.cleanStaleWorkDirectories()
        timer = Timer.scheduledTimer(withTimeInterval: Self.firstCheckDelay, repeats: false) { [weak self] _ in
            self?.checkIfDue()
            let hourly = Timer.scheduledTimer(withTimeInterval: 60 * 60, repeats: true) { _ in UpdateManager.shared.checkIfDue() }
            self?.timer = hourly
        }
    }

    /// 前回の更新が途中で止まって残った一時フォルダー (meltype-update-*。24 時間以上前のもの) を消す。
    /// 自分の接頭辞のフォルダーだけを対象にし、シンボリックリンクは辿らない。
    private static func cleanStaleWorkDirectories() {
        let manager = FileManager.default
        let root = manager.temporaryDirectory
        guard let names = try? manager.contentsOfDirectory(atPath: root.path) else { return }
        for name in names where name.hasPrefix("meltype-update-") {
            let url = root.appendingPathComponent(name)
            guard let attributes = try? manager.attributesOfItem(atPath: url.path),
                  attributes[.type] as? FileAttributeType == .typeDirectory,
                  let modified = attributes[.modificationDate] as? Date,
                  Date().timeIntervalSince(modified) > 24 * 60 * 60 else { continue }
            try? manager.removeItem(at: url)
        }
    }

    /// ON で、前の確認から 24 時間以上たっていれば確認する (メニューを開いたときにも呼ぶ)。
    func checkIfDue() {
        let settings = loadSettings()
        guard settings.enabled, !isChecking, !isUpdating else { return }
        if let last = settings.lastCheck, Date().timeIntervalSince(last) < Self.interval { return }
        if let attempt = lastAttempt, Date().timeIntervalSince(attempt) < Self.retryInterval { return }
        check(manual: false)
    }

    /// 入力メニューの「今すぐ更新を確認する」。OFF のままでも、利用者が押したときだけは確認する。
    func checkNow() {
        // 黙って何もしないと、押しても反応が無いように見えるので、理由を出す
        guard !isUpdating else { showMessage("更新を進めています。終わるまでお待ちください。"); return }
        guard NativeCore.shared.isCompatible else {
            showMessage("この Meltype の部品の版がそろっていないため、更新を確認できません。リリースページの zip から入れ直してください。")
            return
        }
        guard !isChecking else { return }
        check(manual: true)
    }

    private func check(manual: Bool) {
        guard NativeCore.shared.isCompatible else { return }
        isChecking = true
        lastAttempt = Date()
        var request = URLRequest(url: Self.latestReleaseUrl, cachePolicy: .reloadIgnoringLocalCacheData, timeoutInterval: 15)
        request.httpMethod = "GET"
        request.setValue("Meltype/\(currentVersion)", forHTTPHeaderField: "User-Agent")
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        // 認証・Cookie・キャッシュは使わない。リダイレクトは api.github.com の中だけ。
        let configuration = URLSessionConfiguration.ephemeral
        configuration.timeoutIntervalForRequest = 15
        configuration.timeoutIntervalForResource = 15
        configuration.httpCookieStorage = nil
        configuration.urlCache = nil
        configuration.httpShouldSetCookies = false
        let session = URLSession(configuration: configuration, delegate: HostRestrictedDelegate(allowed: { $0 == "api.github.com" }), delegateQueue: nil)
        let current = currentVersion
        session.dataTask(with: request) { [weak self] data, response, error in
            session.finishTasksAndInvalidate()
            // 通信できなかった (オフラインなど) ときは lastCheck を進めない。HTTP の応答があれば (404・403 でも) 確認したことにする
            var reachedServer = false
            var found: UpdateOffer?
            // 手動の確認で「最新の版です」以外を伝えるときの文 (nil なら最新、または新しい版が見つかった)
            var problem: String?
            if let http = response as? HTTPURLResponse {
                reachedServer = true
                if http.statusCode != 200 {
                    problem = "更新を確認できませんでした (GitHub の応答: HTTP \(http.statusCode))。時間をおいて、もう一度お試しください。"
                } else if let data, data.count <= Self.maxJsonBytes, let json = String(data: data, encoding: .utf8) {
                    found = NativeCore.shared.evaluateUpdate(releaseJson: json, currentVersion: current)
                    if found == nil { problem = Self.whyNoOffer(releaseJson: data, currentVersion: current) }
                } else {
                    problem = "更新の情報を読み取れませんでした。時間をおいて、もう一度お試しください。"
                }
            } else if error != nil {
                NSLog("Meltype: 更新の確認に失敗しました")
            }
            DispatchQueue.main.async { self?.finishCheck(found: found, reachedServer: reachedServer, manual: manual, problem: problem) }
        }.resume()
    }

    /// 新しい版が見つからなかった (evaluateUpdate が nil) ときに、それが「最新だから」か「Release の情報が足りない・読めないから」かを分ける。
    /// 最新なら nil、そうでなければ利用者に見せる理由。判定そのもの (ダウンロードしてよいか) は本体の evaluateUpdate だけが行う。
    static func whyNoOffer(releaseJson: Data, currentVersion: String) -> String? {
        guard let object = try? JSONSerialization.jsonObject(with: releaseJson) as? [String: Any],
              let tag = object["tag_name"] as? String else {
            return "更新の情報を読み取れませんでした。時間をおいて、もう一度お試しください。"
        }
        // 「v1.2.3-mac」→ 1.2.3
        var version = tag.hasPrefix("v") ? String(tag.dropFirst()) : tag
        if let dash = version.firstIndex(of: "-") { version = String(version[..<dash]) }
        let latest = version.split(separator: ".").compactMap { Int($0) }
        let mine = currentVersion.split(separator: ".").compactMap { Int($0) }
        guard !latest.isEmpty else { return "更新の情報を読み取れませんでした。時間をおいて、もう一度お試しください。" }
        for i in 0..<max(latest.count, mine.count) {
            let a = i < latest.count ? latest[i] : 0, b = i < mine.count ? mine[i] : 0
            if a != b {
                return a > b ? "新しい版 (\(tag)) がありますが、配布ファイルや検査値 (SHA-256) が確認できないため、ここからは更新できません。リリースページから手で入れ替えてください。" : nil
            }
        }
        return nil
    }

    private func finishCheck(found: UpdateOffer?, reachedServer: Bool, manual: Bool, problem: String? = nil) {
        isChecking = false
        // 確認している間に OFF にされたら、結果は捨てる (通知も出さない)。手動の確認だけは OFF でも結果を返す
        guard manual || loadSettings().enabled else { return }
        if reachedServer {
            var settings = loadSettings()
            settings.lastCheck = Date()
            saveSettings(settings)
        }
        offer = found
        guard let found else {
            if manual {
                if !reachedServer {
                    showMessage("更新を確認できませんでした。ネットワークに接続できないか、GitHub から応答がありません。")
                } else {
                    showMessage(problem ?? "Meltype は最新の版です (v\(currentVersion))。")
                }
            }
            return
        }
        if manual {
            // 利用者が確認を押したので、通知ではなくそのまま案内する
            promptUpdate()
        } else {
            notifyIfNew(found)
        }
    }

    // ---- 通知 ----

    /// 同じ版の通知は 1 回だけ。通知の文に打った内容や個人情報は入れない。
    private func notifyIfNew(_ offer: UpdateOffer) {
        var settings = loadSettings()
        guard settings.notifiedVersion != offer.version else { return }
        settings.notifiedVersion = offer.version
        saveSettings(settings)
        let title = "Meltype の新しい版があります (v\(offer.version))"
        let body = "メニューバーの入力メニューの「新しい版があります…」から更新できます。"
        let center = UNUserNotificationCenter.current()
        center.getNotificationSettings { notificationSettings in
            switch notificationSettings.authorizationStatus {
            case .denied:
                // 利用者が通知を断っているので、別の手段で通知はしない (入力メニューの項目だけ)
                return
            case .notDetermined:
                center.requestAuthorization(options: [.alert]) { granted, error in
                    if error != nil {
                        Self.fallbackNotification(title: title, body: body)
                    } else if granted {
                        Self.post(title: title, body: body, center: center)
                    }
                }
            default:
                Self.post(title: title, body: body, center: center)
            }
        }
    }

    private static func post(title: String, body: String, center: UNUserNotificationCenter) {
        let content = UNMutableNotificationContent()
        content.title = title
        content.body = body
        let request = UNNotificationRequest(identifier: "meltype-update", content: content, trigger: nil)
        center.add(request) { error in
            if error != nil { fallbackNotification(title: title, body: body) }
        }
    }

    /// 通知センターが使えないときの代わり (osascript の display notification。文字は引数で渡す)。
    private static func fallbackNotification(title: String, body: String) {
        runOsascript(["-e", "on run argv", "-e", "display notification (item 2 of argv) with title (item 1 of argv)", "-e", "end run", "--", title, body]) { _, _ in }
    }

    // ---- 更新の案内と実行 ----

    /// 実行中の IME が、入れ替え先 (~/Library/Input Methods/Meltype.app) にあるものか。
    /// 開発中のビルド (build/ や別の場所) から動いているときは、入れ替えても今動いているものは変わらないので、更新ボタンを出さない。
    private var canUpdateInPlace: Bool {
        let expected = (NSHomeDirectory() as NSString).appendingPathComponent("Library/Input Methods/Meltype.app")
        let actual = Bundle.main.bundlePath
        return URL(fileURLWithPath: actual).resolvingSymlinksInPath().path == URL(fileURLWithPath: expected).resolvingSymlinksInPath().path
    }

    /// 入力メニューの「新しい版があります…」。確認のダイアログを出し、「更新する」のときだけ更新を始める。
    /// ダイアログは 5 分で時間切れ (「あとで」と同じ扱い)。
    func promptUpdate() {
        guard let offer, !isUpdating else { return }
        isUpdating = true
        let updatable = canUpdateInPlace
        var text = "Meltype の新しい版 (v\(offer.version)) があります。今は v\(currentVersion) です。\n\n"
        let buttons: String
        let defaultButton: String
        if updatable {
            text += "「更新する」を押すと、GitHub からダウンロードし、SHA-256 を確かめてから入れ替えます。入れ替えたあと、次にキーを打ったときから新しい版になります。"
            buttons = "{\"あとで\", \"詳細\", \"更新する\"}"
            defaultButton = "更新する"
        } else {
            text += "この Meltype は ~/Library/Input Methods の外から動いているので、ここからは更新できません。「詳細」でリリースページを開いて、手で入れ替えてください。"
            buttons = "{\"あとで\", \"詳細\"}"
            defaultButton = "詳細"
        }
        Self.runOsascript([
            "-e", "on run argv",
            "-e", "set answer to display dialog (item 1 of argv) with title \"Meltype の更新\" buttons \(buttons) default button \"\(defaultButton)\" cancel button \"あとで\" giving up after 300",
            "-e", "return button returned of answer",
            "-e", "end run",
            "--", text,
        ]) { [weak self] status, output in
            guard let self else { return }
            self.isUpdating = false
            // 時間切れ (output が空)・「あとで」・失敗は、何もしない (メニューの項目は残る)
            guard status == 0 else { return }
            switch output {
            case "更新する" where updatable: self.performUpdate(offer)
            case "詳細": NSWorkspace.shared.open(offer.releaseUrl)
            default: break
            }
        }
    }

    private func performUpdate(_ offer: UpdateOffer) {
        isUpdating = true
        Self.runOsascript(["-e", "on run argv", "-e", "display notification (item 1 of argv) with title \"Meltype\"", "-e", "end run", "--", "更新をダウンロードしています…"]) { _, _ in }
        let log = logFile
        let directory = dataDirectory
        DispatchQueue.global(qos: .utility).async { [weak self] in
            let result = Updater.run(offer: offer, logFile: log, dataDirectory: directory)
            DispatchQueue.main.async {
                if case .failure(let failure) = result {
                    self?.isUpdating = false
                    self?.showMessage("更新できませんでした。\(failure.reason)\n今の Meltype はそのまま使えます。")
                } else {
                    // 切り離した install.sh が動いている間は isUpdating を true のままにし、メニューの項目も消す (二重起動を防ぐ)。
                    // 結果のダイアログは、その install.sh が出す (この IME は入れ替えで止められるため)。
                    self?.offer = nil
                }
            }
        }
    }

    /// 切り離した install.sh が終わったのに、この IME がまだ動いている (= 入れ替えが失敗・中止された) ときに呼ばれる (メインスレッド)。
    /// 成功したときは install.sh がこの IME を止めるので、ここには来ない。
    /// isUpdating を戻さないと、IME を起動し直すまで自動の確認も「今すぐ更新を確認する」も黙って何もしなくなる。
    func detachedInstallDidEnd(status: Int32) {
        guard isUpdating else { return }
        isUpdating = false
        lastAttempt = Date()
        NSLog("Meltype: 更新の install.sh が終わりました (status %d)。IME は旧版のまま動いています。", status)
        // 新しい版をもう一度メニューに出す。自動の確認が OFF なら通信しない (結果のダイアログは install.sh が出している。
        // 手動で「今すぐ更新を確認する」を押せば、また出る)
        if loadSettings().enabled { check(manual: false) }
    }

    /// 結果などの短い知らせ (OK だけのダイアログ)。メインスレッドを止めない。
    func showMessage(_ text: String) {
        Self.runOsascript([
            "-e", "on run argv",
            "-e", "display dialog (item 1 of argv) with title \"Meltype\" buttons {\"OK\"} default button \"OK\" giving up after 60",
            "-e", "end run",
            "--", text,
        ]) { _, _ in }
    }

    /// osascript を非同期で実行する。文字は AppleScript のソースに埋め込まず引数 (argv) で渡すので、命令として解釈されない。
    /// IME は背面専用のアプリ (LSBackgroundOnly) で自前のウィンドウに入力できないため、ダイアログは別プロセスの osascript に任せる
    /// (ユーザー辞書の登録と同じ)。メインスレッドで completion を呼ぶ。
    static func runOsascript(_ arguments: [String], completion: @escaping (Int32, String) -> Void) {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/osascript")
        process.arguments = arguments
        let pipe = Pipe()
        process.standardOutput = pipe
        process.standardError = FileHandle.nullDevice
        process.terminationHandler = { finished in
            let data = pipe.fileHandleForReading.readDataToEndOfFile()
            let text = String(data: data, encoding: .utf8)?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
            DispatchQueue.main.async { completion(finished.terminationStatus, text) }
        }
        do {
            try process.run()
        } catch {
            NSLog("Meltype: osascript を起動できませんでした")
            DispatchQueue.main.async { completion(-1, "") }
        }
    }
}

// ---- 更新の実行 (ダウンロード → SHA-256 → 展開 → 検査 → 切り離して install.sh) ----

struct UpdateFailure: Error {
    let reason: String
}

enum Updater {
    /// 裏のスレッドで呼ぶ。どの段階で失敗しても、入れてある Meltype には触れず、一時ファイルを消して理由を返す。
    /// 成功 (install.sh を切り離して起動できた) のときは一時フォルダーを消さない (切り離した側が終わりに消す)。
    static func run(offer: UpdateOffer, logFile: URL, dataDirectory: URL) -> Result<Void, UpdateFailure> {
        let manager = FileManager.default
        let work = manager.temporaryDirectory.appendingPathComponent("meltype-update-\(UUID().uuidString)", isDirectory: true)
        do {
            try manager.createDirectory(at: work, withIntermediateDirectories: false, attributes: [.posixPermissions: 0o700])
        } catch {
            return .failure(UpdateFailure(reason: "作業用のフォルダーを作れませんでした。"))
        }
        func fail(_ reason: String) -> Result<Void, UpdateFailure> {
            try? manager.removeItem(at: work)
            return .failure(UpdateFailure(reason: reason))
        }

        // (1) ダウンロード。URL は本体が GitHub の Release のものだけを通しているが、ここでも同じ規則で確かめる
        guard offer.hasValidDownloadUrl, offer.size > 0, offer.size <= 100 * 1024 * 1024 else { return fail("ダウンロード先が Release の URL として正しくありませんでした。") }
        let zip = work.appendingPathComponent("Meltype-mac-\(offer.version).zip")
        if let reason = download(offer.downloadUrl, to: zip, expectedSize: offer.size) { return fail(reason) }

        // (2) SHA-256。Release の digest と 1 文字でも違えば中止してファイルを消す
        guard let actual = sha256(of: zip) else { return fail("ダウンロードしたファイルを読めませんでした。") }
        guard actual == offer.sha256 else { return fail("ダウンロードしたファイルの SHA-256 が Release の値と一致しませんでした。") }

        // (3) 展開の前に、zip の中身の数・展開後の大きさ・名前を見る (巨大な zip や、../ で外へ書く名前を展開しない)
        if let reason = inspectZip(zip) { return fail(reason) }
        let extracted = work.appendingPathComponent("extracted", isDirectory: true)
        guard run("/usr/bin/ditto", ["-x", "-k", zip.path, extracted.path], timeout: 120).status == 0 else { return fail("zip を展開できませんでした。") }

        // 展開物の中の install.sh と Meltype.app (zip は Meltype-mac/ の下に入っている)
        guard let folder = [extracted.appendingPathComponent("Meltype-mac", isDirectory: true), extracted].first(where: { isInstallFolder($0) }) else {
            return fail("zip の中に install.sh と Meltype.app が見つかりませんでした。")
        }
        let app = folder.appendingPathComponent("Meltype.app", isDirectory: true)

        // (4) 署名の封印 (全ファイルの検査値) と、版・バンドル ID が期待どおりか
        guard run("/usr/bin/codesign", ["--verify", "--deep", "--strict", app.path], timeout: 120).status == 0 else { return fail("Meltype.app の署名の検査に失敗しました (壊れているか、書き換えられています)。") }
        guard let info = NSDictionary(contentsOf: app.appendingPathComponent("Contents/Info.plist")),
              info["CFBundleShortVersionString"] as? String == offer.version else {
            return fail("ダウンロードした Meltype.app の版が、Release の版 (v\(offer.version)) と一致しませんでした。")
        }
        guard info["CFBundleIdentifier"] as? String == Bundle.main.bundleIdentifier else { return fail("ダウンロードした Meltype.app が、今の Meltype と別のアプリでした。") }
        // (4b) この Mac の CPU で動くか。配布の zip は Apple シリコン用だけなので、Intel Mac でソースから入れた Meltype を
        //      入れ替えると、起動できなくなって日本語入力が使えなくなる (codesign の検査は CPU を見ない)
        if let reason = checkArchitectures(app) { return fail(reason) }

        // (5) install.sh を、この IME が止められても続くように切り離して起動する
        guard launchDetachedInstall(folder: folder, work: work, version: offer.version, logFile: logFile, dataDirectory: dataDirectory) else {
            return fail("インストールを開始できませんでした。")
        }
        return .success(())
    }

    /// Meltype.app の中の実行ファイル (IME 本体・辞書の管理画面・C# のライブラリ) が、この Mac の CPU 向けを含むか。だめなら理由。
    private static func checkArchitectures(_ app: URL) -> String? {
        let binaries = [
            app.appendingPathComponent("Contents/MacOS/Meltype"),
            app.appendingPathComponent("Contents/Helpers/MeltypeDictionary.app/Contents/MacOS/MeltypeDictionary"),
            app.appendingPathComponent("Contents/Frameworks/libMeltypeNative.dylib"),
        ]
        let accepted = acceptedCPUTypes()
        for binary in binaries {
            guard let types = machOCPUTypes(binary) else { return "ダウンロードした Meltype.app の実行ファイル (\(binary.lastPathComponent)) を読めませんでした。" }
            if types.isDisjoint(with: accepted) {
                return "この版は、この Mac の CPU では動きません (\(binary.lastPathComponent))。配布の版は Apple シリコン用です。Intel の Mac では、ソースから build.sh で入れ直してください。"
            }
        }
        return nil
    }

    private static let cpuTypeX86_64: Int32 = 0x0100_0007
    private static let cpuTypeARM64: Int32 = 0x0100_000C

    /// この Mac で動かせる CPU の種類。Rosetta で動いている x86_64 の IME なら、arm64 も動かせる。
    private static func acceptedCPUTypes() -> Set<Int32> {
        #if arch(arm64)
        return [cpuTypeARM64]
        #else
        var translated: Int32 = 0
        var size = MemoryLayout<Int32>.size
        let underRosetta = sysctlbyname("sysctl.proc_translated", &translated, &size, nil, 0) == 0 && translated == 1
        return underRosetta ? [cpuTypeX86_64, cpuTypeARM64] : [cpuTypeX86_64]
        #endif
    }

    /// Mach-O (単一、または fat/universal) の先頭を読み、含まれる CPU の種類を返す。Mach-O でなければ nil。
    /// (/usr/bin/lipo は Command Line Tools が無い Mac では動かないので使わない)
    static func machOCPUTypes(_ url: URL) -> Set<Int32>? {
        guard let handle = try? FileHandle(forReadingFrom: url) else { return nil }
        defer { try? handle.close() }
        guard let header = try? handle.read(upToCount: 4096), header.count >= 8 else { return nil }
        let bytes = [UInt8](header)
        func little(_ offset: Int) -> UInt32 { UInt32(bytes[offset]) | UInt32(bytes[offset + 1]) << 8 | UInt32(bytes[offset + 2]) << 16 | UInt32(bytes[offset + 3]) << 24 }
        func big(_ offset: Int) -> UInt32 { UInt32(bytes[offset]) << 24 | UInt32(bytes[offset + 1]) << 16 | UInt32(bytes[offset + 2]) << 8 | UInt32(bytes[offset + 3]) }
        switch big(0) {
        case 0xCAFE_BABE, 0xCAFE_BABF: // fat (32 ビットの表 / 64 ビットの表)
            let entrySize = big(0) == 0xCAFE_BABE ? 20 : 32
            let count = Int(big(4))
            guard count > 0, count <= 16, 8 + count * entrySize <= bytes.count else { return nil }
            return Set((0..<count).map { Int32(bitPattern: big(8 + $0 * entrySize)) })
        default:
            // 単一の Mach-O (リトルエンディアン): MH_MAGIC_64 / MH_MAGIC
            guard little(0) == 0xFEED_FACF || little(0) == 0xFEED_FACE else { return nil }
            return [Int32(bitPattern: little(4))]
        }
    }

    /// 展開してよい zip か。エントリ数 5000 以下・展開後の合計 300MB 以下で、絶対パス・`..` を含む名前が無いこと。問題があれば理由 (OK なら nil)。
    private static func inspectZip(_ zip: URL) -> String? {
        let totals = run("/usr/bin/zipinfo", ["-t", zip.path], timeout: 60)
        guard totals.status == 0, let match = totals.output.range(of: #"(\d+) files?, (\d+) bytes uncompressed"#, options: .regularExpression) else {
            return "zip の中身を調べられませんでした。"
        }
        let numbers = totals.output[match].split(whereSeparator: { !$0.isNumber }).compactMap { Int64($0) }
        guard numbers.count >= 2, numbers[0] <= 5000, numbers[1] <= 300 * 1024 * 1024 else { return "zip の中身が大きすぎる (または多すぎる) ので中止しました。" }
        let names = run("/usr/bin/zipinfo", ["-1", zip.path], timeout: 60)
        guard names.status == 0 else { return "zip の中身を調べられませんでした。" }
        for line in names.output.split(separator: "\n", omittingEmptySubsequences: true) {
            if line.hasPrefix("/") || line.hasPrefix("~") || line.contains("\\") || line.split(separator: "/").contains("..") {
                return "zip に、外へ書き出す恐れのある名前が含まれていたので中止しました。"
            }
        }
        return nil
    }

    /// install.sh (普通のファイル) と Meltype.app (フォルダー) が並んでいるか。シンボリックリンクは認めない。
    private static func isInstallFolder(_ folder: URL) -> Bool {
        let manager = FileManager.default
        guard let script = try? manager.attributesOfItem(atPath: folder.appendingPathComponent("install.sh").path),
              script[.type] as? FileAttributeType == .typeRegular,
              let app = try? manager.attributesOfItem(atPath: folder.appendingPathComponent("Meltype.app").path),
              app[.type] as? FileAttributeType == .typeDirectory else { return false }
        return true
    }

    /// 失敗の理由を返す (成功は nil)。サイズが上限を超えたら途中で止める。リダイレクトは github.com / githubusercontent.com だけ。
    private static func download(_ url: URL, to destination: URL, expectedSize: Int64) -> String? {
        let limit = min(expectedSize, 100 * 1024 * 1024)
        let delegate = DownloadDelegate(destination: destination, limit: limit)
        let configuration = URLSessionConfiguration.ephemeral
        configuration.timeoutIntervalForRequest = 30
        configuration.timeoutIntervalForResource = 15 * 60
        configuration.httpCookieStorage = nil
        configuration.urlCache = nil
        configuration.httpShouldSetCookies = false
        let session = URLSession(configuration: configuration, delegate: delegate, delegateQueue: nil)
        var request = URLRequest(url: url, cachePolicy: .reloadIgnoringLocalCacheData, timeoutInterval: 30)
        request.setValue("Meltype/\(UpdateManager.shared.currentVersion)", forHTTPHeaderField: "User-Agent")
        session.downloadTask(with: request).resume()
        delegate.finished.wait()
        session.finishTasksAndInvalidate()
        return delegate.failureReason
    }

    private static func sha256(of file: URL) -> String? {
        guard let handle = try? FileHandle(forReadingFrom: file) else { return nil }
        defer { try? handle.close() }
        var hasher = SHA256()
        while true {
            // read(upToCount:) は、ファイルの終わりで空のデータではなく nil を返す (macOS)。nil は「終わり」、例外だけを「読めない」とする
            // (以前は try? で両方を nil にまとめていたので、最後まで読むと必ず失敗し、1.0.4〜1.0.9 の「更新する」は動かなかった)
            let chunk: Data?
            do { chunk = try handle.read(upToCount: 1024 * 1024) } catch { return nil }
            guard let chunk, !chunk.isEmpty else { break }
            hasher.update(data: chunk)
        }
        return hasher.finalize().map { String(format: "%02x", $0) }.joined()
    }

    /// 同期でコマンドを実行する (裏のスレッドから呼ぶ)。時間切れなら止めて status -1。
    private static func run(_ path: String, _ arguments: [String], timeout: TimeInterval) -> (status: Int32, output: String) {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: path)
        process.arguments = arguments
        let pipe = Pipe()
        process.standardOutput = pipe
        process.standardError = pipe
        do { try process.run() } catch { return (-1, "") }
        let timeoutItem = DispatchWorkItem { if process.isRunning { process.terminate() } }
        DispatchQueue.global().asyncAfter(deadline: .now() + timeout, execute: timeoutItem)
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        process.waitUntilExit()
        timeoutItem.cancel()
        return (process.terminationStatus, String(data: data, encoding: .utf8) ?? "")
    }

    /// install.sh を --yes で、新しいセッション (POSIX_SPAWN_SETSID) で起動する。
    /// install.sh は rsync で入れ替えてから pkill -x Meltype でこの IME を止めるが、止められるのは IME だけで、
    /// 切り離した bash は続く (SIGHUP も来ない)。終わったら、結果のダイアログを出して一時フォルダーを消す。
    /// 標準出力・標準エラーは update.log に書く (0600、毎回上書き)。
    private static func launchDetachedInstall(folder: URL, work: URL, version: String, logFile: URL, dataDirectory: URL) -> Bool {
        let script = """
        cd "$1" || exit 1
        dialog() { /usr/bin/osascript -e 'on run argv' -e 'display dialog (item 1 of argv) with title "Meltype" buttons {"OK"} default button "OK" giving up after 120' -e 'end run' -- "$1"; }
        # 排他ロック (ユーザー専用のデータフォルダー内の固定の場所)。同時に 2 つの更新を走らせない。1 時間以上前の古いロックは、落ちた跡とみなして消す
        find "$5" -maxdepth 0 -type d -mmin +60 -exec rmdir {} \\; 2>/dev/null
        if ! mkdir "$5" 2>/dev/null; then
            case "$2" in */meltype-update-*) rm -rf "$2" ;; esac
            dialog "別の更新が進行中のため、この更新は中止しました。"
            exit 1
        fi
        trap 'rmdir "$5" 2>/dev/null' EXIT
        if /bin/bash install.sh --yes; then
            message="Meltype を v$3 に更新しました。次にキーを打ったときから新しい版になります。"
        else
            message="更新の途中で失敗しました。ログ: $4"
        fi
        cd /
        case "$2" in */meltype-update-*) rm -rf "$2" ;; esac
        rmdir "$5" 2>/dev/null
        dialog "$message"
        """
        try? FileManager.default.createDirectory(at: dataDirectory, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])

        var attributes: posix_spawnattr_t?
        posix_spawnattr_init(&attributes)
        defer { posix_spawnattr_destroy(&attributes) }
        // SETSID: 新しいセッションにして、IME との関係を切る。CLOEXEC_DEFAULT: IME が開いている fd (Mach ポートなど) を引き継がない
        posix_spawnattr_setflags(&attributes, Int16(POSIX_SPAWN_SETSID | POSIX_SPAWN_CLOEXEC_DEFAULT))

        var actions: posix_spawn_file_actions_t?
        posix_spawn_file_actions_init(&actions)
        defer { posix_spawn_file_actions_destroy(&actions) }
        posix_spawn_file_actions_addopen(&actions, 0, "/dev/null", O_RDONLY, 0)
        posix_spawn_file_actions_addopen(&actions, 1, logFile.path, O_WRONLY | O_CREAT | O_TRUNC, 0o600)
        posix_spawn_file_actions_adddup2(&actions, 1, 2)

        let arguments = ["/bin/bash", "-c", script, "meltype-update", folder.path, work.path, version, logFile.path, dataDirectory.appendingPathComponent("update.lock").path]
        var argv: [UnsafeMutablePointer<CChar>?] = arguments.map { strdup($0) } + [nil]
        defer { argv.forEach { free($0) } }
        let environment = ["PATH=/usr/bin:/bin:/usr/sbin:/sbin", "HOME=\(NSHomeDirectory())", "LANG=ja_JP.UTF-8"]
        var envp: [UnsafeMutablePointer<CChar>?] = environment.map { strdup($0) } + [nil]
        defer { envp.forEach { free($0) } }

        var pid: pid_t = 0
        guard posix_spawn(&pid, "/bin/bash", &actions, &attributes, &argv, &envp) == 0 else { return false }
        // IME が生きているあいだに終わったときのゾンビを残さない。終わったのにこの IME が生きていれば、入れ替えは失敗・中止なので、
        // 更新の状態を戻す (成功なら install.sh がこの IME を止めるので、ここまで来ない)
        DispatchQueue.global(qos: .utility).async {
            var status: Int32 = 0
            waitpid(pid, &status, 0)
            DispatchQueue.main.async { UpdateManager.shared.detachedInstallDidEnd(status: status) }
        }
        return true
    }
}

/// リダイレクト先のホストを制限する (https だけ、許可したホストだけ)。外れたら要求を打ち切る。
class HostRestrictedDelegate: NSObject, URLSessionTaskDelegate {
    private let allowed: (String) -> Bool

    init(allowed: @escaping (String) -> Bool) {
        self.allowed = allowed
    }

    func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse,
                    newRequest request: URLRequest, completionHandler: @escaping (URLRequest?) -> Void) {
        if let url = request.url, url.scheme == "https", let host = url.host?.lowercased(), allowed(host) {
            completionHandler(request)
        } else {
            completionHandler(nil)
        }
    }
}

/// zip のダウンロード。リダイレクト先は github.com / githubusercontent.com だけ、サイズが上限を超えたら途中で中止する。
final class DownloadDelegate: HostRestrictedDelegate, URLSessionDownloadDelegate {
    let finished = DispatchSemaphore(value: 0)
    private(set) var failureReason: String?
    private let destination: URL
    private let limit: Int64
    private var done = false

    init(destination: URL, limit: Int64) {
        self.destination = destination
        self.limit = limit
        super.init(allowed: { host in
            host == "github.com" || host == "githubusercontent.com" || host.hasSuffix(".githubusercontent.com")
        })
    }

    private func complete(_ reason: String?) {
        guard !done else { return }
        done = true
        failureReason = reason
        finished.signal()
    }

    func urlSession(_ session: URLSession, downloadTask: URLSessionDownloadTask, didWriteData bytesWritten: Int64,
                    totalBytesWritten: Int64, totalBytesExpectedToWrite: Int64) {
        // Release に載っているサイズ (と 100MB) を超える通信は、途中で止める
        if totalBytesWritten > limit || totalBytesExpectedToWrite > limit {
            downloadTask.cancel()
            complete("ダウンロードのサイズが Release の値を超えたので中止しました。")
        }
    }

    func urlSession(_ session: URLSession, downloadTask: URLSessionDownloadTask, didFinishDownloadingTo location: URL) {
        guard let http = downloadTask.response as? HTTPURLResponse, http.statusCode == 200 else {
            complete("ダウンロードできませんでした (GitHub の応答が正常ではありません)。")
            return
        }
        // location はこの関数を出ると消えるので、ここで作業フォルダーへ移す
        do {
            try FileManager.default.moveItem(at: location, to: destination)
            complete(nil)
        } catch {
            complete("ダウンロードしたファイルを保存できませんでした。")
        }
    }

    func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: Error?) {
        if error != nil { complete("ダウンロードできませんでした (ネットワークの問題か、リダイレクト先が許可されていません)。") }
    }
}
