// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import Foundation
import MeltypeDictionaryKit
import os

/// Zenzai (ニューラル変換) のモデル (重み。GGUF) の取得と、使ってよいかの管理。IME のプロセスで 1 つだけ動く。
/// 設定の「Zenzai」が ON になったときだけ、HTTPS で 1 回取得する。送るのは取得の要求だけで、打った内容は一切送らない。
/// 取得の作法は Updater.swift と同じ (使い捨てセッション・Cookie/認証/キャッシュ無し・転送先のホストを許可リストで制限)。
/// 取得したファイルは、サイズと SHA-256 がアプリに固定した値と合うことを確かめてから置く (別のファイルを native コードに読ませない)。
/// 状態 (isReady など) はメインスレッドだけで触る。通信・検査は裏のスレッドで行い、結果はメインスレッドに戻す (入力を止めない)。
final class ZenzaiModel {
    /// 取得元と、期待するサイズ・SHA-256。テストは、ここを差し替えた Source (ローカルの file: URL など) で動かす。
    struct Source {
        var url: URL
        var size: Int64
        var sha256: String
        /// 転送 (リダイレクト) 先として許すホスト。https 以外への転送は、ここに関係なく許さない (HostRestrictedDelegate)。
        var allowedHost: (String) -> Bool
        /// 取得元の URL に許すスキーム。本番は https だけ。
        var allowedSchemes: Set<String> = ["https"]
        var fileName: String

        /// zenz-v3.1-small (Miwa Keita、CC BY-SA 4.0) の Q5_K_M。サイズと SHA-256 は 2026-10-14 に実物で確かめた値。
        static let production = Source(
            url: URL(string: "https://huggingface.co/Miwa-Keita/zenz-v3.1-small-gguf/resolve/main/ggml-model-Q5_K_M.gguf")!,
            size: 73_871_968,
            sha256: "4de930c06bef8c263aa1aa40684af206db4ce1b96375b3b8ed0ea508e0b14f6c",
            // huggingface.co は重みを CDN (実測は us.aws.cdn.hf.co) に転送する
            allowedHost: { $0 == "huggingface.co" || $0.hasSuffix(".cdn.hf.co") },
            fileName: "zenz-v3.1-small-Q5_K_M.gguf"
        )
    }

    static let shared = ZenzaiModel(source: .production, dataDirectory: {
        if let path = NativeCore.shared.dataDirectory { return URL(fileURLWithPath: path, isDirectory: true) }
        return FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/Application Support/Meltype", isDirectory: true)
    })

    private static let logger = Logger(subsystem: "io.github.yksr-melt.inputmethod.Meltype", category: "zenzai")

    let source: Source
    private let dataDirectory: () -> URL
    /// 取得に失敗したとき、設定を OFF に戻す処理 (既定は config.json の Zenzai を false にする)。戻せたら true。
    private let turnOffSetting: () -> Bool

    /// モデルがあり、検査も通った (取得が終わった)。
    private(set) var isReady = false
    /// 取得 (または既にあるファイルの検査) をしている。同時に 2 つは走らせない。
    private(set) var isBusy = false
    /// 動かしたが遅い・読み込めなかったので、このプロセスでは使わない (IME を起動し直すと戻る)。
    private(set) var isSuspended = false
    /// 設定が ON のあいだに 1 度試した。失敗しても、設定を OFF → ON にし直すまで、取り直さない (通信を繰り返さない)。
    private var attempted = false
    private var timer: Timer?
    /// 状態が変わったとき (メインスレッド)。テストが待つのに使う。
    var onChange: (() -> Void)?

    init(source: Source, dataDirectory: @escaping () -> URL, turnOffSetting: (() -> Bool)? = nil) {
        self.source = source
        self.dataDirectory = dataDirectory
        self.turnOffSetting = turnOffSetting ?? { NativeCore.shared.setZenzaiEnabled(false) }
    }

    // ---- 保存場所 ----

    var modelsDirectory: URL { dataDirectory().appendingPathComponent("models", isDirectory: true) }
    var weightURL: URL { modelsDirectory.appendingPathComponent(source.fileName) }
    var statusFile: URL { dataDirectory().appendingPathComponent("zenzai.json") }

    /// 変換に使ってよいか (モデルが準備でき、このプロセスで止められていない)。
    var isUsable: Bool { isReady && !isSuspended }

    // ---- 取得を始める ----

    /// IME の起動時に 1 回呼ぶ。2 秒ごとに設定を見て、ON ならモデルを用意する (設定タブでの切り替えは、別のプロセスが config.json を書くので、これで拾う)。
    func start() {
        guard timer == nil else { return }
        poll(enabled: NativeCore.shared.zenzaiEnabled)
        timer = Timer.scheduledTimer(withTimeInterval: 2, repeats: true) { _ in
            ZenzaiModel.shared.poll(enabled: NativeCore.shared.zenzaiEnabled)
        }
    }

    /// 設定の ON/OFF を渡す (メインスレッド)。ON で、まだ用意できていなければ取得を始める。OFF なら何もせず (通信しない)、次に ON になったとき取り直せるようにする。
    func poll(enabled: Bool) {
        guard enabled else { attempted = false; return }
        guard !isReady, !isBusy, !attempted else { return }
        attempted = true
        isBusy = true
        writeStatus(ZenzaiStatus(state: .downloading, progress: 0))
        DispatchQueue.global(qos: .utility).async { [self] in
            // 既にあるファイルが合っていれば、通信しない (IME を起動し直すたびに取り直さない)
            if verify(weightURL) == nil {
                DispatchQueue.main.async { self.finish(error: nil) }
                return
            }
            DispatchQueue.main.async { self.download() }
        }
    }

    // ---- 取得 ----

    private func download() {
        guard let scheme = source.url.scheme?.lowercased(), source.allowedSchemes.contains(scheme) else {
            finish(error: "モデルの取得先が https ではありません。")
            return
        }
        let directory = modelsDirectory
        do {
            // モデルのフォルダーは本人だけが読める権限 (0700)。前の版が別の権限で作ったものも直す
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
            try FileManager.default.setAttributes([.posixPermissions: 0o700], ofItemAtPath: directory.path)
        } catch {
            finish(error: "モデルを置くフォルダーを作れませんでした。")
            return
        }
        let part = directory.appendingPathComponent("download.\(UUID().uuidString).part")
        let delegate = ZenzaiDownloadDelegate(destination: part, limit: source.size, source: source) { [weak self] percent in
            DispatchQueue.main.async { self?.reportProgress(percent) }
        } completion: { [weak self] failure in
            guard let self else { return }
            DispatchQueue.global(qos: .utility).async {
                var error = failure
                if error == nil {
                    // 検査 → 権限 → 置き換え。途中で止まっても、検査前のファイルは weightURL に置かない
                    error = self.verify(part) ?? self.install(part)
                }
                if error != nil { try? FileManager.default.removeItem(at: part) }
                DispatchQueue.main.async { self.finish(error: error) }
            }
        }
        let configuration = URLSessionConfiguration.ephemeral
        configuration.timeoutIntervalForRequest = 30
        configuration.timeoutIntervalForResource = 30 * 60
        configuration.httpCookieStorage = nil
        configuration.urlCache = nil
        configuration.httpShouldSetCookies = false
        let session = URLSession(configuration: configuration, delegate: delegate, delegateQueue: nil)
        var request = URLRequest(url: source.url, cachePolicy: .reloadIgnoringLocalCacheData, timeoutInterval: 30)
        request.setValue("Meltype/\(UpdateManager.shared.currentVersion)", forHTTPHeaderField: "User-Agent")
        session.downloadTask(with: request).resume()
        session.finishTasksAndInvalidate()
    }

    /// サイズと SHA-256 が固定した値と合うか。合えば nil、だめなら理由 (裏のスレッドから呼ぶ)。
    private func verify(_ file: URL) -> String? {
        guard let attributes = try? FileManager.default.attributesOfItem(atPath: file.path),
              attributes[.type] as? FileAttributeType == .typeRegular,
              let size = attributes[.size] as? Int64 ?? (attributes[.size] as? NSNumber)?.int64Value else { return "モデルのファイルを読めませんでした。" }
        guard size == source.size else { return "モデルのサイズが固定した値と一致しませんでした (\(size) バイト)。" }
        guard let actual = Updater.sha256(of: file) else { return "モデルのファイルを読めませんでした。" }
        guard actual == source.sha256 else { return "モデルの SHA-256 が固定した値と一致しませんでした。" }
        return nil
    }

    /// 検査を通した一時ファイルを、本人だけが読める権限 (0600) にして、本来の場所に置き換える。
    private func install(_ part: URL) -> String? {
        let manager = FileManager.default
        do {
            try manager.setAttributes([.posixPermissions: 0o600], ofItemAtPath: part.path)
        } catch {
            return "モデルの権限を設定できませんでした。"
        }
        if rename(part.path, weightURL.path) != 0 { return "モデルを所定の場所に置けませんでした。" }
        return nil
    }

    private var lastReported = (percent: -1, at: Date.distantPast)

    /// 進みを状態ファイルに書く (百分率が変わり、前の書き込みから 1 秒以上たったときだけ)。メインスレッド。
    private func reportProgress(_ percent: Int) {
        guard isBusy, percent != lastReported.percent, Date().timeIntervalSince(lastReported.at) >= 1 else { return }
        lastReported = (percent, Date())
        writeStatus(ZenzaiStatus(state: .downloading, progress: percent))
    }

    /// 取得 (または検査) の終わり。error が nil なら準備完了。メインスレッド。
    private func finish(error: String?) {
        isBusy = false
        if let error {
            // 通信できない・値が合わない。OFF に戻して、理由を設定タブと os_log に出す (取り直しは、設定を ON にし直したとき)
            Self.logger.error("Zenzai のモデルを用意できませんでした: \(error, privacy: .public)")
            writeStatus(ZenzaiStatus(state: .failed, error: error))
            if !turnOffSetting() { Self.logger.error("Zenzai の設定を OFF に戻せませんでした") }
        } else {
            isReady = true
            Self.logger.info("Zenzai のモデルの準備ができました")
            writeStatus(ZenzaiStatus(state: .ready))
        }
        onChange?()
    }

    // ---- このプロセスだけ止める ----

    /// モデルの読み込みに失敗した・遅いので、この IME のプロセスでは使わない (設定は変えない。IME を起動し直すと戻る)。メインスレッド。
    func suspend(reason: String) {
        guard !isSuspended else { return }
        isSuspended = true
        Self.logger.error("Zenzai をこのプロセスでは使いません: \(reason, privacy: .public)")
        writeStatus(ZenzaiStatus(state: .slow, error: reason))
        onChange?()
    }

    // ---- 状態ファイル (zenzai.json) ----

    /// 一時ファイルに書いてから置き換える (途中で止まっても元のファイルを壊さない)。フォルダーは 0700、ファイルは 0600。
    private func writeStatus(_ status: ZenzaiStatus) {
        guard let data = status.encoded() else { return }
        let manager = FileManager.default
        let directory = dataDirectory()
        do {
            try manager.createDirectory(at: directory, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
            let temp = directory.appendingPathComponent("zenzai.\(UUID().uuidString).tmp")
            guard manager.createFile(atPath: temp.path, contents: data, attributes: [.posixPermissions: 0o600]) else { return }
            if rename(temp.path, statusFile.path) != 0 { try? manager.removeItem(at: temp) }
        } catch {
            Self.logger.error("Zenzai の状態を保存できませんでした")
        }
    }
}

/// モデルのダウンロード。サイズが固定した値を超えたら途中で中止する。転送先は source.allowedHost だけ (https のみ)。
/// 結果は completion に 1 回だけ (失敗の理由、成功は nil)。成功のとき、ファイルは destination にある (検査は呼んだ側)。
private final class ZenzaiDownloadDelegate: HostRestrictedDelegate, URLSessionDownloadDelegate {
    private let destination: URL
    private let limit: Int64
    private let source: ZenzaiModel.Source
    private let progress: (Int) -> Void
    private let completion: (String?) -> Void
    private let lock = NSLock()
    private var done = false

    init(destination: URL, limit: Int64, source: ZenzaiModel.Source, progress: @escaping (Int) -> Void, completion: @escaping (String?) -> Void) {
        self.destination = destination
        self.limit = limit
        self.source = source
        self.progress = progress
        self.completion = completion
        super.init(allowed: source.allowedHost)
    }

    private func complete(_ reason: String?) {
        lock.lock()
        let first = !done
        done = true
        lock.unlock()
        if first { completion(reason) }
    }

    func urlSession(_ session: URLSession, downloadTask: URLSessionDownloadTask, didWriteData bytesWritten: Int64,
                    totalBytesWritten: Int64, totalBytesExpectedToWrite: Int64) {
        if totalBytesWritten > limit || totalBytesExpectedToWrite > limit {
            downloadTask.cancel()
            complete("ダウンロードのサイズが固定した値を超えたので中止しました。")
            return
        }
        progress(Int(min(100, totalBytesWritten * 100 / max(limit, 1))))
    }

    func urlSession(_ session: URLSession, downloadTask: URLSessionDownloadTask, didFinishDownloadingTo location: URL) {
        // file: は応答コードが無い (テスト用)。https は 200 だけ
        if let http = downloadTask.response as? HTTPURLResponse, http.statusCode != 200 {
            complete("ダウンロードできませんでした (サーバーの応答: HTTP \(http.statusCode))。")
            return
        }
        // location はこの関数を出ると消えるので、ここで置き場所へ移す
        do {
            try FileManager.default.moveItem(at: location, to: destination)
            complete(nil)
        } catch {
            complete("ダウンロードしたファイルを保存できませんでした。")
        }
    }

    func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: Error?) {
        if error != nil { complete("ダウンロードできませんでした (ネットワークに接続できないか、転送先が許可されていません)。") }
    }
}
