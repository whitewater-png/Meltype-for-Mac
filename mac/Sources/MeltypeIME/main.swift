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
case "--bench": exit(Bench.run() ? 0 : 1)   // 変換の速度と正しさの計測 (IMKServer は立てない。Bench.swift)。変換の食い違いがあれば終了コード 1
case "--zenzai-self-test": exit(ZenzaiSelfTest.run(network: CommandLine.arguments.contains("--network")) ? 0 : 1)   // Zenzai のモデルの取得の 3 状態 (一時フォルダー。--network で本物の URL から 1 回だけ)
default: break
}

// Input Method Kit のサーバーを起動する。入力欄 (クライアント) ごとに MeltypeInputController が作られる。
let connectionName = Bundle.main.object(forInfoDictionaryKey: "InputMethodConnectionName") as? String ?? "Meltype_Connection"
guard let bundleIdentifier = Bundle.main.bundleIdentifier,
      let server = IMKServer(name: connectionName, bundleIdentifier: bundleIdentifier) else {
    NSLog("Meltype: IMKServer を起動できませんでした (Meltype.app から起動してください)")
    exit(1)
}

/// 格子の候補ウィンドウを作るときにも使う (main.swift の外から参照できるようにする)。
/// トップレベル関数から guard の束縛 (server) を直接つかむと、ほかのファイルから呼んだときに
/// 「closure captures 'server' before it is declared」になるため、モジュール全体で見える変数に入れる。
let candidatesServer: IMKServer = server

/// 変換の候補の一覧 (すべての入力欄で共有する)。1 行の候補バー。
/// 格子 (設定「Tab で候補を格子状に広げる」) は、macOS 26 の IMKit が IMKCandidatePanelType を無視して
/// 常に 1 行で描くため、IMKCandidates ではなく自前の NSPanel (GridCandidatePanel) で出す。
var candidatesWindow: IMKCandidates? = IMKCandidates(server: candidatesServer, panelType: kIMKSingleColumnScrollingCandidatePanel)

// 本体 (libMeltypeNative.dylib) を読み込み、漢字変換・英単語の判定の関数を登録しておく。
NativeCore.shared.initialize()

// Zenzai (ニューラル変換) のモデル。設定が ON のときだけ、必要なら取得する (既定 OFF のあいだは通信しない)。
ZenzaiModel.shared.start()

// 新しい版の確認 (既定 ON。起動 5 分後から、24 時間に 1 回。OFF のときは通信しない)。
UpdateManager.shared.start()

NSApplication.shared.run()
