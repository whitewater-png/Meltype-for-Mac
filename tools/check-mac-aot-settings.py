#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
#
# NativeAOT 版の libMeltypeNative.dylib で、設定 (config.json) を保存・読み直しできるか確かめる。
#   cd mac && ./build.sh --no-install        # 先にビルド (インストールはしない)
#   python3 tools/check-mac-aot-settings.py [libMeltypeNative.dylib のパス]
# 既定のパスは、このスクリプトから見た mac/build/Meltype.app/Contents/Frameworks/libMeltypeNative.dylib (環境変数 MELTYPE_LIB でも指定できる)。
# 空の一時フォルダーを MELTYPE_DATA_DIR (データの保存場所の上書き。HOME では変わらない) にして、ライブラリの関数を直接呼ぶ。保存場所が一時フォルダーの外なら、何も書かずに止まる。Meltype.app は起動せず、実際の設定・データには触らない。
# 背景: managed (dotnet) のテストが通っても、AOT では System.Text.Json の reflection で列挙型 (InputMode など) の保存が
# 「metadata が無い」の例外になり、入力メニューの切り替えが保存失敗になっていた (ソース生成に直した: SettingsJsonContext)。
# 終了コード: 0 = 通った、1 = 失敗。
import ctypes
import json
import os
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_LIB = os.path.join(HERE, "..", "mac", "build", "Meltype.app", "Contents", "Frameworks", "libMeltypeNative.dylib")


def child(lib_path, step):
    lib = ctypes.CDLL(lib_path)
    lib.meltype_free.argtypes = [ctypes.c_void_p]
    lib.meltype_term_domains.restype = ctypes.c_void_p
    lib.meltype_set_term_domain.argtypes = [ctypes.c_char_p, ctypes.c_int]
    lib.meltype_set_continue_after_conversion.argtypes = [ctypes.c_int]
    lib.meltype_data_directory.restype = ctypes.c_void_p
    out = {}
    directory = lib.meltype_data_directory()
    out["data_directory"] = ctypes.string_at(directory).decode("utf-8")
    lib.meltype_free(directory)
    out["safe"] = os.path.realpath(out["data_directory"]).startswith(os.path.realpath(os.environ["HOME"]) + os.sep)
    if step == "write" and out["safe"]:
        out["set_term_domain"] = lib.meltype_set_term_domain(b"civil", 1)
        out["set_continue"] = lib.meltype_set_continue_after_conversion(1)
    pointer = lib.meltype_term_domains()
    domains = {}
    if pointer:
        for line in ctypes.string_at(pointer).decode("utf-8").split("\n"):
            parts = line.split("\t")
            domains[parts[0]] = int(parts[3])
        lib.meltype_free(pointer)
    out["domains"] = domains
    out["continue"] = lib.meltype_get_continue_after_conversion()
    print(json.dumps(out))


def run(lib_path, home, step):
    env = dict(os.environ, HOME=home, MELTYPE_DATA_DIR=os.path.join(home, "data"))
    result = subprocess.run([sys.executable, os.path.abspath(__file__), "--child", step, lib_path], env=env, capture_output=True, text=True)
    if result.returncode != 0:
        raise SystemExit(f"FAIL: 子プロセス ({step}) が異常終了: {result.stderr.strip()[-500:]}")
    return json.loads(result.stdout.strip().splitlines()[-1])


def main():
    if len(sys.argv) > 1 and sys.argv[1] == "--child":
        child(sys.argv[3], sys.argv[2])
        return 0
    lib_path = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else os.environ.get("MELTYPE_LIB", DEFAULT_LIB))
    if not os.path.exists(lib_path):
        print(f"FAIL: ライブラリが無い: {lib_path} (先に cd mac && ./build.sh --no-install)")
        return 1
    failures = []
    with tempfile.TemporaryDirectory(prefix="meltype-aot-") as home:
        first = run(lib_path, home, "write")
        if not first["safe"]:
            print(f"FAIL: 保存場所が一時フォルダーの外のため、何も書かずに止めた: {first['data_directory']} (古いライブラリでは MELTYPE_DATA_DIR が効かない。ビルドし直す)")
            return 1
        if first["set_term_domain"] != 1: failures.append("専門用語集の分野を保存できなかった")
        if first["set_continue"] != 1: failures.append("変換後も続けて入力の設定を保存できなかった")
        if first["domains"].get("civil") != 1 or first["continue"] != 1: failures.append("保存した直後に、有効として見えない")
        # 保存場所は本体に尋ねる (HOME の下になっていること = 実際の設定に触っていないことも確かめる)
        config = os.path.join(first["data_directory"], "config.json")
        if not os.path.exists(config):
            failures.append(f"config.json ができていない: {config}")
        else:
            saved = json.load(open(config, encoding="utf-8"))
            if saved.get("EnabledTermDomains") != ["civil"]: failures.append(f"EnabledTermDomains が違う: {saved.get('EnabledTermDomains')}")
            if saved.get("ContinueAfterConversion") is not True: failures.append("ContinueAfterConversion が保存されていない")
            if not isinstance(saved.get("Mode"), str): failures.append(f"列挙型 (Mode) が文字列で保存されていない: {saved.get('Mode')}")
        again = run(lib_path, home, "read")  # 別プロセス (読み直し)
        if again["domains"].get("civil") != 1: failures.append("別プロセスで読み直すと、分野が有効でない")
        if again["continue"] != 1: failures.append("別プロセスで読み直すと、変換後も続けて入力が ON でない")
    for message in failures:
        print("FAIL:", message)
    print("PASS: AOT 版で設定を保存・読み直しできた" if not failures else "FAIL")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
