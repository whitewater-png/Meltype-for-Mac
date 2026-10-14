// swift-tools-version:6.1
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// Meltype の Mac 版 (Input Method Kit の IME)。ビルドとインストールは build.sh で行う。
// 漢字変換には azooKey の変換エンジン (AzooKeyKanaKanjiConverter, MIT License) を使う。
// 英語 / 日本語の判定などの本体は、C# の Meltype.Core を NativeAOT で libMeltypeNative.dylib にしたものを dlopen で読み込む。

import PackageDescription

let package = Package(
    name: "MeltypeIME",
    platforms: [.macOS(.v13)],
    dependencies: [
        // main は API がよく変わるので、確かめたコミットに固定する (更新するときは Converter.swift も合わせて直す)。
        // trait は CPU 版 (ZenzaiCPU) だけを使う。GPU 版 (Zenzai) は llama.cpp b4846 の Metal 初期化 (bf16 カーネルの読み込み) で落ちる
        // (docs/ZENZAI_EXPERIMENT_2026-10.md)。CPU 版でも 1 キー 95% 61 ms で、go の目安 (80 ms) に収まる。
        .package(url: "https://github.com/azooKey/AzooKeyKanaKanjiConverter", revision: "d59a28e4c7ca049aef04f29a91eae9677a7753f2", traits: ["ZenzaiCPU"]),
    ],
    targets: [
        .executableTarget(
            name: "MeltypeIME",
            dependencies: [
                .product(name: "KanaKanjiConverterModuleWithDefaultDictionary", package: "AzooKeyKanaKanjiConverter"),
                // Zenzai の状態ファイル (zenzai.json) の形を、辞書の管理画面と共有する (ZenzaiStatus)
                "MeltypeDictionaryKit",
            ],
            path: "Sources/MeltypeIME",
            // azooKey を Zenzai 付きで使うと、公開される C++ のヘッダー (llama.cpp・marisa) を読むために必要 (azooKey 側の Package.swift と同じ設定)。
            swiftSettings: [.interoperabilityMode(.Cxx)],
            linkerSettings: [
                .linkedFramework("InputMethodKit"),
                .linkedFramework("Carbon"),
            ]
        ),
        // 辞書の管理画面「Meltype 辞書」(MeltypeDictionary.app。build.sh が Meltype.app/Contents/Helpers に入れる) のロジック。
        // AppKit を使わない部分 (FFI・一覧の検索と並べ替え・元に戻す) をここに分け、`MeltypeDictionary --self-test` で確かめる。
        .target(
            name: "MeltypeDictionaryKit",
            path: "Sources/MeltypeDictionaryKit"
        ),
        // 辞書の管理画面 (AppKit)。IME (背面専用) のウインドウはキーボード入力を受けられないので、別のふつうのアプリにしている。
        .executableTarget(
            name: "MeltypeDictionary",
            dependencies: ["MeltypeDictionaryKit"],
            path: "Sources/MeltypeDictionary",
            linkerSettings: [
                .linkedFramework("AppKit"),
            ]
        ),
    ],
    swiftLanguageVersions: [.v5]
)
