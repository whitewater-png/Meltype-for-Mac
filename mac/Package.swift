// swift-tools-version:5.10
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
        .package(url: "https://github.com/azooKey/AzooKeyKanaKanjiConverter", revision: "d59a28e4c7ca049aef04f29a91eae9677a7753f2"),
    ],
    targets: [
        .executableTarget(
            name: "MeltypeIME",
            dependencies: [
                .product(name: "KanaKanjiConverterModuleWithDefaultDictionary", package: "AzooKeyKanaKanjiConverter"),
            ],
            path: "Sources/MeltypeIME",
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
