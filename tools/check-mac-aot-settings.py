#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
#
# NativeAOT 版の libMeltypeNative.dylib で、設定 (config.json) と辞書 (userdict.txt・terms-excluded.txt) を保存・読み直しできるか確かめる。
#   cd mac && ./build.sh --no-install        # 先にビルド (インストールはしない)
#   python3 tools/check-mac-aot-settings.py [libMeltypeNative.dylib のパス]
# 既定のパスは、このスクリプトから見た mac/build/Meltype.app/Contents/Frameworks/libMeltypeNative.dylib (環境変数 MELTYPE_LIB でも指定できる)。
# 空の一時フォルダーを MELTYPE_DATA_DIR (データの保存場所の上書き。HOME では変わらない) にして、ライブラリの関数を直接呼ぶ。保存場所が一時フォルダーの外なら、何も書かずに止まる。Meltype.app は起動せず、実際の設定・データには触らない。
# 背景: managed (dotnet) のテストが通っても、AOT では System.Text.Json の reflection で列挙型 (InputMode など) の保存が
# 「metadata が無い」の例外になり、入力メニューの切り替えが保存失敗になっていた (ソース生成に直した: SettingsJsonContext)。
#
# 確かめること:
#   1. 設定: 専門用語集の分野・「変換後も続けて入力」を保存し、別のプロセスで読み直しても保たれる
#   2. 辞書の管理画面の関数 (meltype_userdict_* / meltype_term_*): 登録・重複・編集・削除と復元・取り込み・書き出し・除外・専門用語の編集。
#      保存したファイル (0600) を、別のプロセスで読み直しても保たれる
#   3. 別々のプロセスが同時にユーザー辞書へ登録しても、1 語も消えない (ロック + 読み直してから書く)
#   4. IME のつもりのプロセス (meltype_create のセッション) が動いたまま、別のプロセス (管理画面のつもり) の登録・除外・分野の切り替えが
#      すぐ反映される (変換の結果に出る・版が進む)。IME の側の登録 (meltype_add_user_word) も、管理画面の登録を消さない
# 終了コード: 0 = 通った、1 = 失敗。
import ctypes
import json
import os
import subprocess
import sys
import tempfile
import time

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_LIB = os.path.join(HERE, "..", "mac", "build", "Meltype.app", "Contents", "Frameworks", "libMeltypeNative.dylib")


def load(lib_path):
    lib = ctypes.CDLL(lib_path)
    p, s, i = ctypes.c_void_p, ctypes.c_char_p, ctypes.c_int
    signatures = {
        "meltype_free": ([p], None),
        "meltype_abi_version": ([], i),
        "meltype_data_directory": ([], p),
        "meltype_term_domains": ([], p),
        "meltype_set_term_domain": ([s, i], i),
        "meltype_set_continue_after_conversion": ([i], i),
        "meltype_get_continue_after_conversion": ([], i),
        "meltype_set_shift_enter_newline": ([i], i),
        "meltype_get_shift_enter_newline": ([], i),
        "meltype_userdict_words": ([], p),
        "meltype_userdict_version": ([], i),
        "meltype_userdict_check": ([s, s, s, s], p),
        "meltype_userdict_add": ([s, s], p),
        "meltype_userdict_update": ([s, s, s, s], p),
        "meltype_userdict_remove": ([s, ctypes.POINTER(p)], p),
        "meltype_userdict_restore": ([s], p),
        "meltype_userdict_import": ([s, ctypes.POINTER(p), ctypes.POINTER(p)], p),
        "meltype_userdict_add_many": ([s, ctypes.POINTER(p)], p),
        "meltype_userdict_problem": ([], p),
        "meltype_userdict_export": ([s, ctypes.POINTER(i)], p),
        "meltype_to_reading": ([s], p),
        "meltype_term_words": ([s], p),
        "meltype_term_excluded": ([], p),
        "meltype_term_set_excluded": ([s, i], p),
        "meltype_term_edit": ([s, s, s, s, ctypes.POINTER(i)], p),
        "meltype_term_revision": ([], i),
        "meltype_create": ([], p),
        "meltype_handle_key": ([p, i, i, i, s, s], p),
        "meltype_add_user_word": ([p, s, s], p),
    }
    for name, (args, result) in signatures.items():
        function = getattr(lib, name)
        function.argtypes = args
        function.restype = result
    return lib


def text(lib, pointer):
    """本体が返した文字列 (NULL なら None) を読んで解放する。"""
    if not pointer:
        return None
    value = ctypes.string_at(pointer).decode("utf-8")
    lib.meltype_free(pointer)
    return value


def u(value):
    return value.encode("utf-8") if value is not None else None


def data_directory(lib):
    directory = text(lib, lib.meltype_data_directory())
    safe = os.path.realpath(directory).startswith(os.path.realpath(os.environ["HOME"]) + os.sep)
    return directory, safe


def domains(lib):
    result = {}
    for line in (text(lib, lib.meltype_term_domains()) or "").split("\n"):
        parts = line.split("\t")
        if len(parts) == 4:
            result[parts[0]] = int(parts[3])
    return result


def user_words(lib):
    return [line.split("\t")[:2] for line in (text(lib, lib.meltype_userdict_words()) or "").split("\n") if line]


def type_keys(lib, session, keys):
    """キーを打って、最後の結果の変換中の文字 (view.text) を返す。"""
    view_text = None
    for key in keys:
        if key == " ":
            vk, ch = 0x20, 0
        elif key == "\x1b":
            vk, ch = 0x1B, 0
        else:
            vk, ch = ord(key.upper()), ord(key)
        result = text(lib, lib.meltype_handle_key(session, vk, ch, 0, None, None))
        if result:
            view = json.loads(result).get("view")
            view_text = view.get("text") if view else None
    return view_text


# ---- 子プロセス (それぞれ別のプロセスで本体を読み込む) ----

def child_settings(lib, step):
    out = {}
    out["data_directory"], out["safe"] = data_directory(lib)
    if step == "write" and out["safe"]:
        out["set_term_domain"] = lib.meltype_set_term_domain(b"civil", 1)
        out["set_continue"] = lib.meltype_set_continue_after_conversion(1)
        out["set_shift_enter"] = lib.meltype_set_shift_enter_newline(0)  # 既定は ON なので、OFF にして保たれるかを見る
    out["domains"] = domains(lib)
    out["continue"] = lib.meltype_get_continue_after_conversion()
    out["shift_enter"] = lib.meltype_get_shift_enter_newline()
    return out


def child_dict_ops(lib):
    """管理画面がする操作を一通り行う。"""
    out = {}
    out["data_directory"], out["safe"] = data_directory(lib)
    if not out["safe"]:
        return out
    directory = out["data_directory"]
    out["abi"] = lib.meltype_abi_version()
    out["empty"] = user_words(lib)
    v0 = lib.meltype_userdict_version()
    out["add1"] = text(lib, lib.meltype_userdict_add(u("きごうとう"), u("記号等")))
    out["add2"] = text(lib, lib.meltype_userdict_add(u("めるたいぷ"), u("Meltype")))
    out["version_moved"] = lib.meltype_userdict_version() != v0
    out["duplicate"] = text(lib, lib.meltype_userdict_add(u("きごうとう"), u("記号等")))
    out["invalid"] = text(lib, lib.meltype_userdict_check(u("き"), u("記"), None, None))
    out["check_except"] = text(lib, lib.meltype_userdict_check(u("きごうとう"), u("記号等"), u("きごうとう"), u("記号等")))
    out["update"] = text(lib, lib.meltype_userdict_update(u("きごうとう"), u("記号等"), u("きごうとう"), u("記号党")))
    removed = ctypes.c_void_p()
    out["remove"] = text(lib, lib.meltype_userdict_remove(u("きごうとう\t記号党"), ctypes.byref(removed)))
    out["removed"] = text(lib, removed.value)
    out["after_remove"] = user_words(lib)
    out["restore"] = text(lib, lib.meltype_userdict_restore(u(out["removed"] or "")))
    out["after_restore"] = user_words(lib)
    exported = os.path.join(directory, "export.txt")
    count = ctypes.c_int()
    out["export"] = text(lib, lib.meltype_userdict_export(u(exported), ctypes.byref(count)))
    out["export_count"] = count.value
    summary = ctypes.c_void_p()
    imported = ctypes.c_void_p()
    out["import"] = text(lib, lib.meltype_userdict_import(u(exported), ctypes.byref(summary), ctypes.byref(imported)))
    out["import_summary"] = text(lib, summary.value)
    out["import_added"] = text(lib, imported.value)
    many = ctypes.c_void_p()
    out["add_many"] = text(lib, lib.meltype_userdict_add_many(u("まとめて\t纏めて\nきごうとう\t記号党"), ctypes.byref(many)))
    out["add_many_added"] = text(lib, many.value)
    out["remove_many"] = text(lib, lib.meltype_userdict_remove(u("まとめて\t纏めて"), ctypes.byref(ctypes.c_void_p())))
    out["problem"] = text(lib, lib.meltype_userdict_problem())
    out["to_reading"] = text(lib, lib.meltype_to_reading(u("kigoutou")))

    r0 = lib.meltype_term_revision()
    out["set_civil"] = lib.meltype_set_term_domain(b"civil", 1)
    words = [line.split("\t") for line in (text(lib, lib.meltype_term_words(b"civil")) or "").split("\n") if line]
    out["civil_count"] = len(words)
    out["unknown_domain"] = text(lib, lib.meltype_term_words(b"nosuch"))
    first, second = words[0], words[1]
    out["first"] = first[:2]
    out["second"] = second[:2]
    out["exclude"] = text(lib, lib.meltype_term_set_excluded(u(f"{first[0]}\t{first[1]}"), 1))
    out["revision_moved"] = lib.meltype_term_revision() != r0
    out["first_flag"] = (text(lib, lib.meltype_term_words(b"civil")) or "").split("\n")[0].split("\t")[3]
    added = ctypes.c_int()
    out["edit"] = text(lib, lib.meltype_term_edit(u(second[0]), u(second[1]), u(second[0]), u(second[1] + "（直し）"), ctypes.byref(added)))
    out["edit_added"] = added.value
    out["excluded"] = [line.split("\t") for line in (text(lib, lib.meltype_term_excluded()) or "").split("\n") if line]
    out["words"] = user_words(lib)
    return out


def child_dict_read(lib):
    out = {}
    out["data_directory"], out["safe"] = data_directory(lib)
    out["words"] = user_words(lib)
    out["excluded"] = [line.split("\t") for line in (text(lib, lib.meltype_term_excluded()) or "").split("\n") if line]
    out["domains"] = domains(lib)
    lines = (text(lib, lib.meltype_term_words(b"civil")) or "").split("\n")
    out["civil_flags"] = [line.split("\t")[3] for line in lines[:2] if line]
    return out


def child_add_many(lib, prefix, count):
    """同時に登録する役。合図のファイルができるまで待ってから一斉に登録する。"""
    _, safe = data_directory(lib)
    errors = []
    if safe:
        start = os.environ["MELTYPE_START_FLAG"]
        deadline = time.time() + 20
        while not os.path.exists(start) and time.time() < deadline:
            time.sleep(0.005)
        for n in range(count):
            error = text(lib, lib.meltype_userdict_add(u(f"よみ{prefix}の{n}ばん"), u(f"語{prefix}-{n}")))
            if error:
                errors.append(error)
    return {"errors": errors, "safe": safe}


def child_watch(lib):
    """IME のつもり: セッションを作って打つ。親の合図 (標準入力の 1 行) のあと、もう一度打って、ほかのプロセスの変更が見えるかを返す。"""
    out = {}
    out["data_directory"], out["safe"] = data_directory(lib)
    if not out["safe"]:
        print(json.dumps(out), flush=True)
        return None
    session = lib.meltype_create()
    out["session"] = bool(session)
    out["before"] = type_keys(lib, session, "kigoutou ")
    type_keys(lib, session, "\x1b\x1b\x1b")
    out["version_before"] = lib.meltype_userdict_version()
    out["revision_before"] = lib.meltype_term_revision()
    out["domains_before"] = domains(lib)
    print(json.dumps({"ready": True}), flush=True)
    sys.stdin.readline()            # 親が、別のプロセスで登録・除外・分野の切り替えを終えるまで待つ
    time.sleep(0.8)                 # ファイルの確認の間隔 (0.5 秒) より長く待つ
    out["after"] = type_keys(lib, session, "kigoutou ")
    type_keys(lib, session, "\x1b\x1b\x1b")
    out["version_after"] = lib.meltype_userdict_version()
    out["revision_after"] = lib.meltype_term_revision()
    out["domains_after"] = domains(lib)
    # IME の側の登録 (入力メニュー) も、管理画面の登録を消さない
    out["ime_add"] = text(lib, lib.meltype_add_user_word(session, u("いめのとうろく"), u("IMEの登録")))
    out["words"] = user_words(lib)
    return out


def child_register(lib):
    """管理画面のつもり: 登録・除外・分野の切り替え。"""
    out = {}
    _, safe = data_directory(lib)
    if safe:
        out["add"] = text(lib, lib.meltype_userdict_add(u("きごうとう"), u("記号等")))
        out["medical"] = lib.meltype_set_term_domain(b"medical", 1)
        out["exclude"] = text(lib, lib.meltype_term_set_excluded(u("こうぞうぶつ\t構造物"), 1))
    return out


def child_broken(lib):
    """読めない userdict.txt に対して、問題の報告と、書かないこと。"""
    out = {}
    _, safe = data_directory(lib)
    if safe:
        out["problem"] = text(lib, lib.meltype_userdict_problem())
        out["add"] = text(lib, lib.meltype_userdict_add(u("あたらしい"), u("新しい")))
    return out


def child(lib_path, step, args):
    lib = load(lib_path)
    if step in ("write", "read"):
        out = child_settings(lib, step)
    elif step == "dict-ops":
        out = child_dict_ops(lib)
    elif step == "dict-read":
        out = child_dict_read(lib)
    elif step == "dict-add-many":
        out = child_add_many(lib, args[0], int(args[1]))
    elif step == "dict-watch":
        out = child_watch(lib)
        if out is None:
            return
    elif step == "dict-register":
        out = child_register(lib)
    elif step == "dict-broken":
        out = child_broken(lib)
    else:
        raise SystemExit(f"unknown step {step}")
    print(json.dumps(out, ensure_ascii=False), flush=True)


# ---- 親 ----

def env_for(home, extra=None):
    env = dict(os.environ, HOME=home, MELTYPE_DATA_DIR=os.path.join(home, "data"))
    if extra:
        env.update(extra)
    return env


def command(lib_path, step, *args):
    return [sys.executable, os.path.abspath(__file__), "--child", step, lib_path, *args]


def run(lib_path, home, step, *args):
    result = subprocess.run(command(lib_path, step, *args), env=env_for(home), capture_output=True, text=True)
    if result.returncode != 0:
        raise SystemExit(f"FAIL: 子プロセス ({step}) が異常終了: {result.stderr.strip()[-500:]}")
    return json.loads(result.stdout.strip().splitlines()[-1])


def mode(path):
    return os.stat(path).st_mode & 0o777


def check_settings(lib_path, failures):
    with tempfile.TemporaryDirectory(prefix="meltype-aot-") as home:
        first = run(lib_path, home, "write")
        if not first["safe"]:
            print(f"FAIL: 保存場所が一時フォルダーの外のため、何も書かずに止めた: {first['data_directory']} (古いライブラリでは MELTYPE_DATA_DIR が効かない。ビルドし直す)")
            return False
        if first["set_term_domain"] != 1: failures.append("専門用語集の分野を保存できなかった")
        if first["set_continue"] != 1: failures.append("変換後も続けて入力の設定を保存できなかった")
        if first["set_shift_enter"] != 1: failures.append("Shift+Enter で確定して改行の設定を保存できなかった")
        if first["shift_enter"] != 0: failures.append("Shift+Enter で確定して改行を OFF にした直後に、OFF として見えない")
        if first["domains"].get("civil") != 1 or first["continue"] != 1: failures.append("保存した直後に、有効として見えない")
        # 保存場所は本体に尋ねる (HOME の下になっていること = 実際の設定に触っていないことも確かめる)
        config = os.path.join(first["data_directory"], "config.json")
        if not os.path.exists(config):
            failures.append(f"config.json ができていない: {config}")
        else:
            saved = json.load(open(config, encoding="utf-8"))
            if saved.get("EnabledTermDomains") != ["civil"]: failures.append(f"EnabledTermDomains が違う: {saved.get('EnabledTermDomains')}")
            if saved.get("ContinueAfterConversion") is not True: failures.append("ContinueAfterConversion が保存されていない")
            if saved.get("ShiftEnterNewline") is not False: failures.append("ShiftEnterNewline (OFF) が保存されていない")
            if not isinstance(saved.get("Mode"), str): failures.append(f"列挙型 (Mode) が文字列で保存されていない: {saved.get('Mode')}")
        again = run(lib_path, home, "read")  # 別プロセス (読み直し)
        if again["domains"].get("civil") != 1: failures.append("別プロセスで読み直すと、分野が有効でない")
        if again["continue"] != 1: failures.append("別プロセスで読み直すと、変換後も続けて入力が ON でない")
        if again["shift_enter"] != 0: failures.append("別プロセスで読み直すと、Shift+Enter で確定して改行が OFF でない")
    return True


def check_dictionary(lib_path, failures):
    def expect(condition, message):
        if not condition:
            failures.append(message)

    with tempfile.TemporaryDirectory(prefix="meltype-aot-dict-") as home:
        ops = run(lib_path, home, "dict-ops")
        if not ops["safe"]:
            failures.append(f"保存場所が一時フォルダーの外: {ops['data_directory']}")
            return
        data = ops["data_directory"]
        expect(ops["abi"] >= 6, f"FFI の版数が 6 以上でない: {ops['abi']}")
        expect(ops["empty"] == [], "最初のユーザー辞書が空でない")
        expect(ops["add1"] is None and ops["add2"] is None, f"登録できない: {ops['add1']} / {ops['add2']}")
        expect(ops["version_moved"], "登録しても版が進まない")
        expect(ops["duplicate"] is not None and "すでに" in ops["duplicate"], f"重複の理由が返らない: {ops['duplicate']}")
        expect(ops["invalid"] is not None and "2 文字以上" in ops["invalid"], f"短い読みの理由が返らない: {ops['invalid']}")
        expect(ops["check_except"] is None, "編集中の元の語が重複扱いになる")
        expect(ops["update"] is None, f"編集できない: {ops['update']}")
        expect(ops["remove"] is None and ops["removed"] == "0\tきごうとう\t記号党", f"削除の結果が違う: {ops['remove']} / {ops['removed']}")
        expect(ops["after_remove"] == [["めるたいぷ", "Meltype"]], f"削除のあとの一覧が違う: {ops['after_remove']}")
        expect(ops["restore"] is None and ops["after_restore"] == [["きごうとう", "記号党"], ["めるたいぷ", "Meltype"]], f"元の位置に戻らない: {ops['after_restore']}")
        expect(ops["export"] is None and ops["export_count"] == 2, f"書き出せない: {ops['export']}")
        expect(ops["import"] is None and ops["import_summary"] == "0\t2\t0\tUTF-16" and ops["import_added"] is None, f"取り込みの結果が違う: {ops['import']} / {ops['import_summary']}")
        expect(ops["add_many"] is None and ops["add_many_added"] == "まとめて\t纏めて" and ops["remove_many"] is None, f"まとめての登録の結果が違う: {ops['add_many']} / {ops['add_many_added']}")
        expect(ops["problem"] is None, f"読めているのに問題がある: {ops['problem']}")
        expect(ops["to_reading"] == "きごうとう", f"ローマ字をひらがなにできない: {ops['to_reading']}")
        expect(ops["set_civil"] == 1 and ops["civil_count"] > 1000, f"土木の語を取れない: {ops['civil_count']}")
        expect(ops["unknown_domain"] is None, "未知の分野で NULL にならない")
        expect(ops["exclude"] is None and ops["revision_moved"] and ops["first_flag"] == "1", f"除外できない: {ops['exclude']} / {ops['first_flag']}")
        expect(ops["edit"] is None and ops["edit_added"] == 1, f"専門用語を直せない: {ops['edit']}")
        expect(sorted(map(tuple, ops["excluded"])) == sorted([tuple(ops["first"]), tuple(ops["second"])]), f"除外の一覧が違う: {ops['excluded']}")
        expect([ops["second"][0], ops["second"][1] + "（直し）"] in ops["words"], "直した語がユーザー辞書に無い")

        for name in ("userdict.txt", "userdict.txt.bak", "terms-excluded.txt", "config.json", "export.txt", ".userdict.txt.lock"):
            path = os.path.join(data, name)
            expect(os.path.exists(path) and mode(path) == 0o600, f"{name} が無いか 0600 でない")
        config = json.load(open(os.path.join(data, "config.json"), encoding="utf-8"))
        expect(config.get("EnabledTermDomains") == ["civil"], f"分野が config.json に無い: {config.get('EnabledTermDomains')}")
        expect(ops["first"][1] not in json.dumps(config, ensure_ascii=False), "除外した語が config.json に入っている (terms-excluded.txt に分ける)")

        again = run(lib_path, home, "dict-read")   # 別のプロセスで読み直す
        expect(again["words"] == ops["words"], "別のプロセスで読み直すと、ユーザー辞書が違う")
        expect(sorted(map(tuple, again["excluded"])) == sorted(map(tuple, ops["excluded"])), "別のプロセスで読み直すと、除外の一覧が違う")
        expect(again["domains"].get("civil") == 1 and again["civil_flags"] == ["1", "1"], f"別のプロセスで読み直すと、除外の印が違う: {again['civil_flags']}")

        # 同時に 4 つのプロセスが 25 語ずつ登録しても、全部残る
        before = len(again["words"])
        flag = os.path.join(home, "start")
        processes = [subprocess.Popen(command(lib_path, "dict-add-many", str(n), "25"), env=env_for(home, {"MELTYPE_START_FLAG": flag}),
                                      stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True) for n in range(4)]
        time.sleep(1.5)   # 全部が本体を読み込み終えるのを待つ
        open(flag, "w").close()
        for process in processes:
            stdout, stderr = process.communicate(timeout=120)
            if process.returncode != 0:
                failures.append(f"同時に登録する子プロセスが異常終了: {stderr.strip()[-300:]}")
                continue
            result = json.loads(stdout.strip().splitlines()[-1])
            expect(not result["errors"], f"同時の登録で失敗: {result['errors'][:2]}")
        final = run(lib_path, home, "dict-read")["words"]
        expect(len(final) == before + 100, f"同時に 100 語登録したのに {len(final) - before} 語しか残っていない")
        expect(len({tuple(w) for w in final}) == len(final), "同時の登録で重複ができた")
        expect(not [f for f in os.listdir(data) if f.endswith(".tmp")], "一時ファイルが残っている")

    # Shift_JIS のユーザー辞書は読めないと報告し、書き換えない (U+FFFD にして書き戻さない)
    with tempfile.TemporaryDirectory(prefix="meltype-aot-enc-") as home:
        data = os.path.join(home, "data")
        os.makedirs(data)
        sjis = bytes([0x82, 0xA0, 0x82, 0xA2, 0x09, 0x88, 0xA4, 0x0A])
        with open(os.path.join(data, "userdict.txt"), "wb") as f:
            f.write(sjis)
        broken = run(lib_path, home, "dict-broken")
        expect(broken.get("problem") and "文字コード" in broken["problem"], f"読めない文字コードを報告しない: {broken.get('problem')}")
        expect(broken.get("add") and "文字コード" in broken["add"], f"読めないファイルに書こうとした: {broken.get('add')}")
        expect(open(os.path.join(data, "userdict.txt"), "rb").read() == sjis, "読めないファイルが書き換わった")

    # IME のつもりのプロセスが動いたまま、別のプロセスの変更が反映される
    with tempfile.TemporaryDirectory(prefix="meltype-aot-live-") as home:
        watcher = subprocess.Popen(command(lib_path, "dict-watch"), env=env_for(home), stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        ready = json.loads(watcher.stdout.readline())
        if not ready.get("ready"):
            failures.append(f"IME のつもりのプロセスが始まらない: {ready}")
            watcher.kill()
            return
        register = run(lib_path, home, "dict-register")
        expect(register.get("add") is None and register.get("medical") == 1 and register.get("exclude") is None, f"管理画面のつもりの変更ができない: {register}")
        stdout, stderr = watcher.communicate("go\n", timeout=120)
        if watcher.returncode != 0:
            failures.append(f"IME のつもりのプロセスが異常終了: {stderr.strip()[-500:]}")
            return
        live = json.loads(stdout.strip().splitlines()[-1])
        expect(live["session"], "セッションを作れない")
        expect("記号等" not in (live["before"] or ""), f"登録前から出ている: {live['before']}")
        expect(live["after"] == "記号等", f"別のプロセスの登録が、動いている IME の変換に出ない: {live['after']}")
        expect(live["version_after"] != live["version_before"], "ユーザー辞書の版が進まない")
        expect(live["revision_after"] != live["revision_before"], "専門用語集の版が進まない (除外・分野の切り替え)")
        expect(live["domains_before"].get("medical") == 0 and live["domains_after"].get("medical") == 1, f"分野の切り替えが反映されない: {live['domains_after']}")
        expect(live["ime_add"] is None, f"IME の側で登録できない: {live['ime_add']}")
        expect(["きごうとう", "記号等"] in live["words"] and ["いめのとうろく", "IMEの登録"] in live["words"], f"IME と管理画面の登録の片方が消えた: {live['words']}")


def main():
    if len(sys.argv) > 1 and sys.argv[1] == "--child":
        child(sys.argv[3], sys.argv[2], sys.argv[4:])
        return 0
    lib_path = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else os.environ.get("MELTYPE_LIB", DEFAULT_LIB))
    if not os.path.exists(lib_path):
        print(f"FAIL: ライブラリが無い: {lib_path} (先に cd mac && ./build.sh --no-install)")
        return 1
    failures = []
    if not check_settings(lib_path, failures):
        return 1
    settings_failures = len(failures)
    check_dictionary(lib_path, failures)
    for message in failures:
        print("FAIL:", message)
    if not failures:
        print("PASS: AOT 版で設定を保存・読み直しできた")
        print("PASS: AOT 版で辞書の管理画面の操作・別プロセスへの反映・同時の登録ができた")
    else:
        print(f"FAIL (設定 {settings_failures} 件・辞書 {len(failures) - settings_failures} 件)")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
