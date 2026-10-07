// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import Carbon
import Foundation

/// 初めて入れた Meltype を macOS の入力ソースとして登録する (`Meltype --register`。build.sh / install.sh が初めて入れたときに呼ぶ)。
/// うまくいけば、ログアウトしなくても入力メニューに出て使える。
/// 入れ直すときは使わない (build.sh / install.sh は Meltype.app のフォルダーを残して中身だけを入れ替えるので、登録はそのまま残る)。
/// 登録が残っているのに登録すると、同じ Meltype が二重に登録されて入力メニューにいくつも並ぶ (ログアウトすると直るはず)。
enum InputSourceRegistration {
    static let modeID = "io.github.yksr-melt.inputmethod.Meltype.Japanese"
    static let romanModeID = "io.github.yksr-melt.inputmethod.Meltype.Roman"

    /// `--register-check` の終了コード: 一覧に出ている / 出ていない。
    static let presentExitCode: Int32 = 0
    static let absentExitCode: Int32 = 3

    /// 一覧に出たまま落ち着くまで確かめ、出てこなければ登録する。落ち着いたら有効にし直して true。
    /// - macOS が自分で ~/Library/Input Methods の Meltype.app を見つけて一覧に入れることもあるので、少し待ってから登録する。
    ///   登録してから一覧に出るまでも数秒かかるので、登録し直すのは間を空けてから (どちらも二重に登録しないため)。
    /// - 一覧は 1 つのプロセスの中ではキャッシュされ、あとから変わっても見えないので、確かめるのも登録するのも毎回新しいプロセス
    ///   (`Meltype --register-check` など) で行う。
    static func register() -> Bool {
        let stableChecks = 5      // 1 秒おきに 5 回続けて出ていれば落ち着いたとみなす
        let registerAfter = 5     // 5 秒たっても出てこなければ登録する
        let reregisterAfter = 10  // 登録してから 10 秒たっても出てこなければ、もう一度登録する
        let maxRegistrations = 2  // それでも出てこなければあきらめる (何度も登録すると、遅れて反映されたときに二重になる)
        var presentCount = 0
        var registrations = 0
        var absentSeconds = 0
        var secondsSinceRegistered: Int?
        for _ in 0..<45 {
            switch runChild("--register-check") {
            case presentExitCode:
                presentCount += 1
                if presentCount >= stableChecks { return runChild("--register-enable") == 0 }
            case absentExitCode:
                presentCount = 0
                absentSeconds += 1
                let due = secondsSinceRegistered.map { $0 >= reregisterAfter } ?? (absentSeconds >= registerAfter)
                if due {
                    guard registrations < maxRegistrations, runChild("--register-add") == 0 else { return false }
                    registrations += 1
                    secondsSinceRegistered = 0
                }
            case let status:
                print("入力ソースを確かめられませんでした (\(status))")
                return false
            }
            Thread.sleep(forTimeInterval: 1)
            secondsSinceRegistered = secondsSinceRegistered.map { $0 + 1 }
        }
        return false
    }

    /// `--register-check`: 一覧に出ていれば presentExitCode、無ければ absentExitCode。
    static func check() -> Int32 {
        find(modeID) != nil ? presentExitCode : absentExitCode
    }

    /// `--register-add`: 入力ソースとして登録する。成功したら 0。
    static func add() -> Int32 {
        let status = TISRegisterInputSource(Bundle.main.bundleURL as CFURL)
        if status != noErr { print("入力ソースの登録に失敗しました (\(status))") }
        return status == noErr ? 0 : 1
    }

    /// `--register-enable`: いったん無効にしてから有効にする。成功したら 0。
    /// 設定 (com.apple.inputsources など) ですでに有効になっていると、有効にするだけでは何も変わらず、入力メニューに伝わらない。
    /// 一度無効にしてから有効にすると、有効な入力ソースが変わったことが入力メニューなどに伝わる。
    static func enable() -> Int32 {
        guard let source = find(modeID) else { return 1 }
        // 無効にしたところで止められると、無効のまま残るので、Ctrl+C やターミナルを閉じたときには止めない
        signal(SIGINT, SIG_IGN)
        signal(SIGHUP, SIG_IGN)
        TISDisableInputSource(source)
        Thread.sleep(forTimeInterval: 0.5)
        guard TISEnableInputSource(source) == noErr else { return 1 }
        // 英数モードも一緒に有効にする (Info.plist の DefaultState だけでは有効にならない可能性があるため)。
        // 一覧に無い・有効にできないときも日本語モードは使えるので、失敗は無視する。
        if let roman = find(romanModeID) { TISEnableInputSource(roman) }
        return 0
    }

    /// 自分 (Meltype) を新しいプロセスとして起動し、終了コードを返す。15 秒で終わらないとき・シグナルで終わったときは -1。
    private static func runChild(_ argument: String) -> Int32 {
        guard let executable = Bundle.main.executableURL else { return -1 }
        let process = Process()
        process.executableURL = executable
        process.arguments = [argument]
        do {
            try process.run()
        } catch {
            return -1
        }
        let deadline = Date().addingTimeInterval(15)
        while process.isRunning && Date() < deadline { Thread.sleep(forTimeInterval: 0.05) }
        if process.isRunning {
            process.terminate()
            return -1
        }
        return process.terminationReason == .exit ? process.terminationStatus : -1
    }

    private static func find(_ id: String) -> TISInputSource? {
        let filter = [kTISPropertyInputSourceID as String: id] as CFDictionary
        return (TISCreateInputSourceList(filter, true)?.takeRetainedValue() as? [TISInputSource])?.first
    }
}
