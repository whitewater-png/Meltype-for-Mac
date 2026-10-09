// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

import Cocoa
import InputMethodKit

// `Meltype --register`: 入力ソースとして登録するだけで終わる (build.sh / install.sh が初めて入れたときに呼ぶ)。
// `--register-check` / `--register-add` / `--register-enable` は、--register が新しいプロセスで確かめる・登録する・
// 有効にするのに使う (Registration.swift)。`--unregister` は uninstall.sh が入力ソースを外すのに使う。
switch CommandLine.arguments.dropFirst().first {
case "--register": exit(InputSourceRegistration.register() ? 0 : 1)
case "--register-check": exit(InputSourceRegistration.check())
case "--register-add": exit(InputSourceRegistration.add())
case "--register-enable": exit(InputSourceRegistration.enable())
case "--unregister": exit(InputSourceRegistration.disable())
case "--bench": Bench.run(); exit(0)   // 変換の速度と正しさの計測 (IMKServer は立てない。Bench.swift)
default: break
}

// Input Method Kit のサーバーを起動する。入力欄 (クライアント) ごとに MeltypeInputController が作られる。
let connectionName = Bundle.main.object(forInfoDictionaryKey: "InputMethodConnectionName") as? String ?? "Meltype_Connection"
guard let bundleIdentifier = Bundle.main.bundleIdentifier,
      let server = IMKServer(name: connectionName, bundleIdentifier: bundleIdentifier) else {
    NSLog("Meltype: IMKServer を起動できませんでした (Meltype.app から起動してください)")
    exit(1)
}

/// 変換の候補の一覧 (すべての入力欄で共有する)。
var candidatesWindow: IMKCandidates? = IMKCandidates(server: server, panelType: kIMKSingleColumnScrollingCandidatePanel)

// 本体 (libMeltypeNative.dylib) を読み込み、漢字変換・英単語の判定の関数を登録しておく。
NativeCore.shared.initialize()

// 新しい版の確認 (既定 ON。起動 5 分後から、24 時間に 1 回。OFF のときは通信しない)。
UpdateManager.shared.start()

NSApplication.shared.run()
