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
#   4. 設定タブの関数 (meltype_settings_*): 一覧の取得・真偽/選択肢/数の保存・不正な値の拒否・既定値に戻す。別のプロセスで読み直しても保たれ、
#      読めない config.json は上書きしない。IME のつもりのプロセス (meltype_create のセッション) が、作り直さずに設定の変更に追随する
#   5. 自作の専門用語集 (meltype_term_create ほか): 作成・名前の変更・削除と復元・語の追加と編集・ユーザー辞書から移す (移した語は変換に使われ続ける)・書き出しと取り込み。
#      別のプロセスで読み直しても保たれ、IME のつもりのプロセスが動いたまま、別のプロセスが作った専門用語集の語が変換に出る
#   6. IME のつもりのプロセス (meltype_create のセッション) が動いたまま、別のプロセス (管理画面のつもり) の登録・除外・分野の切り替えが
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
        "meltype_term_create": ([s, ctypes.POINTER(p)], p),
        "meltype_term_rename": ([s, s], p),
        "meltype_term_delete": ([s, ctypes.POINTER(p), ctypes.POINTER(i)], p),
        "meltype_term_restore_domain": ([s, s, i], p),
        "meltype_term_user_check": ([s, s, s, s, s], p),
        "meltype_term_user_add": ([s, s, ctypes.POINTER(p), ctypes.POINTER(p)], p),
        "meltype_term_user_remove": ([s, s, ctypes.POINTER(p)], p),
        "meltype_term_user_restore": ([s, s], p),
        "meltype_term_user_update": ([s, s, s, s, s], p),
        "meltype_term_move": ([s, s, ctypes.POINTER(p), ctypes.POINTER(p), ctypes.POINTER(p)], p),
        "meltype_term_export": ([s, s, ctypes.POINTER(i)], p),
        "meltype_term_import": ([s, ctypes.POINTER(p), ctypes.POINTER(p)], p),
        "meltype_settings_get": ([], p),
        "meltype_settings_set": ([s, s], p),
        "meltype_settings_reset": ([], p),
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
        if len(parts) >= 4:   # 5 つ目の欄 (自作なら 1) は user_domains が読む
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


def settings_items(lib):
    """設定タブの一覧 (key → 項目)。取れなければ None。"""
    raw = text(lib, lib.meltype_settings_get())
    if raw is None:
        return None
    return {item["key"]: item for item in json.loads(raw)["items"]}


def setting(lib, key, value):
    """設定を 1 つ変える (value は JSON の値として渡す)。だめなら理由、できたら None。"""
    return text(lib, lib.meltype_settings_set(u(key), u(json.dumps(value, ensure_ascii=False))))


def child_settings_ops(lib):
    """設定タブがする操作を一通り行う。"""
    out = {}
    out["data_directory"], out["safe"] = data_directory(lib)
    if not out["safe"]:
        return out
    out["abi"] = lib.meltype_abi_version()
    items = settings_items(lib)
    out["keys"] = list(items)
    out["detection"] = items["DetectionLevel"]
    out["min_length"] = items["PredictionMinLength"]
    out["live_default"] = items["LiveConversion"]["value"]
    out["set_bool"] = setting(lib, "LiveConversion", False)
    out["set_enum"] = setting(lib, "DetectionLevel", "Conservative")
    out["set_punct"] = setting(lib, "Punctuation", "CommaJapanese")
    out["set_int"] = setting(lib, "PredictionMinLength", 4)
    out["set_continue"] = setting(lib, "ContinueAfterConversion", True)
    out["set_shift_enter"] = setting(lib, "ShiftEnterNewline", False)
    out["set_log"] = setting(lib, "LogTypedText", True)
    out["bad_unknown"] = setting(lib, "NoSuchKey", True)
    out["bad_type"] = setting(lib, "LiveConversion", "yes")
    out["bad_range"] = setting(lib, "PredictionMinLength", 99)
    out["bad_enum"] = setting(lib, "DetectionLevel", "Nope")
    out["bad_int_for_enum"] = setting(lib, "DetectionLevel", 2)
    out["bad_json"] = text(lib, lib.meltype_settings_set(u("LiveConversion"), u("{ 壊れた")))
    after = settings_items(lib)
    out["after"] = {key: after[key]["value"] for key in ("LiveConversion", "DetectionLevel", "Punctuation", "PredictionMinLength", "ContinueAfterConversion", "ShiftEnterNewline", "LogTypedText")}
    # 入力メニュー用の getter (ConfigFlag) にも、設定タブの変更が見える
    out["flag_continue"] = lib.meltype_get_continue_after_conversion()
    out["flag_shift_enter"] = lib.meltype_get_shift_enter_newline()
    return out


def child_settings_read(lib):
    out = {}
    out["data_directory"], out["safe"] = data_directory(lib)
    items = settings_items(lib)
    out["values"] = {key: item["value"] for key, item in items.items()} if items is not None else None
    return out


def child_settings_reset(lib):
    out = {}
    _, safe = data_directory(lib)
    if safe:
        out["reset"] = text(lib, lib.meltype_settings_reset())
        out["values"] = {key: item["value"] for key, item in settings_items(lib).items()}
        out["flag_continue"] = lib.meltype_get_continue_after_conversion()
        out["flag_shift_enter"] = lib.meltype_get_shift_enter_newline()
    return out


def child_settings_broken(lib):
    """読めない config.json に対して、取得は NULL・変更と既定値に戻すは理由を返し、書かないこと。"""
    out = {}
    _, safe = data_directory(lib)
    if safe:
        out["get"] = text(lib, lib.meltype_settings_get())
        out["set"] = setting(lib, "LiveConversion", False)
        out["reset"] = text(lib, lib.meltype_settings_reset())
    return out


def comma_result(lib, session):
    """日本語の入力の途中で「,」を打ち、変換中の文字を返す (句読点の設定が見える)。"""
    type_keys(lib, session, "a")
    result = text(lib, lib.meltype_handle_key(session, 0xBC, ord(","), 0, None, None))
    view = json.loads(result).get("view") if result else None
    type_keys(lib, session, "\x1b\x1b\x1b")
    return view.get("text") if view else None


def child_settings_watch(lib):
    """IME のつもり: セッションを作って打つ。親の合図のあと、作り直さずにもう一度打って、設定タブの変更が見えるかを返す。"""
    out = {}
    out["data_directory"], out["safe"] = data_directory(lib)
    if not out["safe"]:
        print(json.dumps(out), flush=True)
        return None
    session = lib.meltype_create()
    out["session"] = bool(session)
    out["before"] = comma_result(lib, session)
    out["flag_continue_before"] = lib.meltype_get_continue_after_conversion()
    print(json.dumps({"ready": True}), flush=True)
    sys.stdin.readline()            # 親が、別のプロセスで設定を変え終えるまで待つ
    time.sleep(0.8)                 # ファイルの確認の間隔 (0.5 秒) より長く待つ
    out["after"] = comma_result(lib, session)
    out["flag_continue_after"] = lib.meltype_get_continue_after_conversion()
    return out


def user_domains(lib):
    """専門用語集の一覧のうち、自作のもの (ID → [名称, 語数, 有効, 自作])。5 つ目の欄を読む。"""
    result = {}
    for line in (text(lib, lib.meltype_term_domains()) or "").split("\n"):
        parts = line.split("\t")
        if len(parts) == 5 and parts[4] == "1":
            result[parts[0]] = [parts[1], int(parts[2]), int(parts[3]), 1]
    return result


def out_text(lib, function, *args):
    """出力用のポインター 1 つを持つ関数を呼ぶ。(理由, 出力) を返す。"""
    out = ctypes.c_void_p()
    reason = text(lib, function(*args, ctypes.byref(out)))
    return reason, text(lib, out.value)


def child_term_ops(lib):
    """画面がする、自作の専門用語集の操作を一通り行う。"""
    out = {}
    out["data_directory"], out["safe"] = data_directory(lib)
    if not out["safe"]:
        return out
    directory = out["data_directory"]
    out["abi"] = lib.meltype_abi_version()
    for reading, word in [("きごう", "記号"), ("あいこん", "icon"), ("こうせい", "構成"), ("ぬるぽが", "nullpo")]:
        lib.meltype_userdict_add(u(reading), u(word))
    out["user_before"] = user_words(lib)
    out["create_bad"] = out_text(lib, lib.meltype_term_create, u(""))[0]
    reason, domain_id = out_text(lib, lib.meltype_term_create, u("自作の用語"))
    out["create"] = reason
    out["id"] = domain_id
    out["dup_name"] = out_text(lib, lib.meltype_term_create, u("自作の用語"))[0]
    out["bundled_name"] = out_text(lib, lib.meltype_term_create, u("土木・建設"))[0]
    out["listed"] = user_domains(lib).get(domain_id)
    out["bundled_not_user"] = [k for k in domains(lib) if k in user_domains(lib)]
    # 移す: 専門用語集に入れられない語 (読みが 1 文字) は無いので、全部移る。ユーザー辞書に無い語を混ぜても、飛ばすだけ
    removed, added, skipped = ctypes.c_void_p(), ctypes.c_void_p(), ctypes.c_void_p()
    out["move"] = text(lib, lib.meltype_term_move(u(domain_id), u("きごう\t記号\nあいこん\ticon\nこうせい\t構成\nぬるぽが\tnullpo"), ctypes.byref(removed), ctypes.byref(added), ctypes.byref(skipped)))
    out["move_removed"] = text(lib, removed.value)
    out["move_added"] = text(lib, added.value)
    out["move_skipped"] = text(lib, skipped.value)
    out["user_after_move"] = user_words(lib)
    out["words_after_move"] = [line.split("\t")[:2] for line in (text(lib, lib.meltype_term_words(u(domain_id))) or "").split("\n") if line]
    out["listed_after_move"] = user_domains(lib).get(domain_id)
    # 取り消し: ユーザー辞書へ元の位置に戻し、専門用語集から消す
    out["undo_restore"] = text(lib, lib.meltype_userdict_restore(u(out["move_removed"] or "")))
    out["undo_remove"], undo_removed = out_text(lib, lib.meltype_term_user_remove, u(domain_id), u(out["move_added"] or ""))
    out["user_after_undo"] = user_words(lib)
    out["words_after_undo"] = [line.split("\t")[:2] for line in (text(lib, lib.meltype_term_words(u(domain_id))) or "").split("\n") if line]
    out["redo_restore_terms"] = text(lib, lib.meltype_term_user_restore(u(domain_id), u(undo_removed or "")))
    out["words_after_redo"] = [line.split("\t")[:2] for line in (text(lib, lib.meltype_term_words(u(domain_id))) or "").split("\n") if line]
    # 語の編集
    out["check_ok"] = text(lib, lib.meltype_term_user_check(u(domain_id), u("ほそう"), u("舗装"), None, None))
    out["check_dup"] = text(lib, lib.meltype_term_user_check(u(domain_id), u("きごう"), u("記号"), None, None))
    out["check_short"] = text(lib, lib.meltype_term_user_check(u(domain_id), u("き"), u("記"), None, None))
    out["add"], add_added = out_text(lib, lambda *a: lib.meltype_term_user_add(a[0], a[1], a[2], ctypes.byref(ctypes.c_void_p())), u(domain_id), u("ほそう\t舗装"))
    out["update"] = text(lib, lib.meltype_term_user_update(u(domain_id), u("ほそう"), u("舗装"), u("ほそう"), u("舗装路")))
    out["update_missing"] = text(lib, lib.meltype_term_user_update(u(domain_id), u("ないよみ"), u("無い"), u("あたらしい"), u("新しい")))
    out["rename"] = text(lib, lib.meltype_term_rename(u(domain_id), u("改名した用語")))
    out["rename_bundled"] = text(lib, lib.meltype_term_rename(b"civil", u("x")))
    # 書き出し・取り込み
    path = os.path.join(directory, "domain-export.txt")
    count = ctypes.c_int()
    out["export"] = text(lib, lib.meltype_term_export(u(domain_id), u(path), ctypes.byref(count)))
    out["export_count"] = count.value
    out["export_text"] = open(path, encoding="utf-8").read()
    out["export_bundled"] = text(lib, lib.meltype_term_export(b"civil", u(path), ctypes.byref(ctypes.c_int())))
    reason, imported_id = out_text(lib, lambda p_, o: lib.meltype_term_import(p_, o, ctypes.byref(ctypes.c_void_p())), u(path))
    out["import"] = reason
    out["imported"] = user_domains(lib).get(imported_id)
    # 削除と復元
    content, was_enabled = ctypes.c_void_p(), ctypes.c_int()
    out["delete"] = text(lib, lib.meltype_term_delete(u(imported_id), ctypes.byref(content), ctypes.byref(was_enabled)))
    deleted_content = text(lib, content.value)
    out["deleted_enabled"] = was_enabled.value
    out["after_delete"] = imported_id in user_domains(lib)
    out["delete_bundled"] = text(lib, lib.meltype_term_delete(b"civil", ctypes.byref(ctypes.c_void_p()), ctypes.byref(ctypes.c_int())))
    out["restore"] = text(lib, lib.meltype_term_restore_domain(u(imported_id), u(deleted_content or ""), was_enabled.value))
    out["after_restore"] = user_domains(lib).get(imported_id)
    out["files"] = sorted(f for f in os.listdir(os.path.join(directory, "terms")) if f.endswith(".txt"))
    return out


def child_term_read(lib):
    out = {}
    out["data_directory"], out["safe"] = data_directory(lib)
    out["domains"] = user_domains(lib)
    out["user_words"] = user_words(lib)
    out["words"] = {key: [line.split("\t")[:2] for line in (text(lib, lib.meltype_term_words(u(key))) or "").split("\n") if line] for key in user_domains(lib)}
    return out


def child_term_create(lib):
    """画面のつもり: 自作の専門用語集を作って語を足す (別のプロセスの IME が動いたまま、変換に出るか)。"""
    out = {}
    _, safe = data_directory(lib)
    if safe:
        reason, domain_id = out_text(lib, lib.meltype_term_create, u("外で作った用語"))
        out["create"] = reason
        added = ctypes.c_void_p()
        skipped = ctypes.c_void_p()
        out["add"] = text(lib, lib.meltype_term_user_add(u(domain_id), u("ぬるぽが\tnullpo"), ctypes.byref(added), ctypes.byref(skipped)))
        out["id"] = domain_id
    return out


def child_term_watch(lib):
    """IME のつもり: 専門用語集が無い状態で打つ → 親の合図 (別のプロセスが専門用語集を作る) → 作り直さずにもう一度打つ。"""
    out = {}
    out["data_directory"], out["safe"] = data_directory(lib)
    if not out["safe"]:
        print(json.dumps(out), flush=True)
        return None
    session = lib.meltype_create()
    out["session"] = bool(session)
    out["before"] = type_keys(lib, session, "nurupoga ")
    type_keys(lib, session, "\x1b\x1b\x1b")
    out["revision_before"] = lib.meltype_term_revision()
    print(json.dumps({"ready": True}), flush=True)
    sys.stdin.readline()
    time.sleep(0.8)                 # ファイルの確認の間隔 (0.5 秒) より長く待つ
    out["after"] = type_keys(lib, session, "nurupoga ")
    type_keys(lib, session, "\x1b\x1b\x1b")
    out["revision_after"] = lib.meltype_term_revision()
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
    elif step == "settings-ops":
        out = child_settings_ops(lib)
    elif step == "settings-read":
        out = child_settings_read(lib)
    elif step == "settings-reset":
        out = child_settings_reset(lib)
    elif step == "settings-broken":
        out = child_settings_broken(lib)
    elif step == "settings-watch":
        out = child_settings_watch(lib)
        if out is None:
            return
    elif step == "term-ops":
        out = child_term_ops(lib)
    elif step == "term-read":
        out = child_term_read(lib)
    elif step == "term-create":
        out = child_term_create(lib)
    elif step == "term-watch":
        out = child_term_watch(lib)
        if out is None:
            return
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


def check_settings_tab(lib_path, failures):
    def expect(condition, message):
        if not condition:
            failures.append(message)

    with tempfile.TemporaryDirectory(prefix="meltype-aot-tab-") as home:
        ops = run(lib_path, home, "settings-ops")
        if not ops["safe"]:
            failures.append(f"保存場所が一時フォルダーの外: {ops['data_directory']}")
            return
        expect(ops["abi"] == 8, f"FFI の版数が 8 でない: {ops['abi']}")
        expect(len(ops["keys"]) >= 15 and ops["keys"][0] == "LiveConversion" and "DetectionLevel" in ops["keys"], f"設定の一覧が違う: {ops['keys']}")
        expect(not {"Mode", "InputStyle", "AutoUpdate", "AppRules", "Profiles"} & set(ops["keys"]), "Mac で効かない設定が一覧にある")
        expect(ops["detection"]["kind"] == "choice" and [o["value"] for o in ops["detection"]["options"]] == ["Aggressive", "Balanced", "Conservative", "Manual"], f"選択肢が違う: {ops['detection']}")
        expect(ops["detection"]["label"] == "自動判定の強さ" and ops["detection"]["group"] == "入力" and ops["detection"]["description"], "名前・グループ・説明が違う")
        expect(ops["min_length"]["kind"] == "int" and (ops["min_length"]["min"], ops["min_length"]["max"]) == (1, 5) and ops["min_length"]["value"] == 2, f"数の項目が違う: {ops['min_length']}")
        expect(ops["live_default"] is True, "ライブ変換が既定で ON でない")
        for name in ("set_bool", "set_enum", "set_punct", "set_int", "set_continue", "set_shift_enter", "set_log"):
            expect(ops[name] is None, f"設定を保存できない ({name}): {ops[name]}")
        expect(ops["bad_unknown"] and "NoSuchKey" in ops["bad_unknown"], f"未知の項目を断らない: {ops['bad_unknown']}")
        expect(ops["bad_type"], "型の違う値を断らない")
        expect(ops["bad_range"] and "1〜5" in ops["bad_range"], f"範囲の外を断らない: {ops['bad_range']}")
        expect(ops["bad_enum"] and ops["bad_int_for_enum"] and ops["bad_json"], "未知の選択肢・型の違う値・壊れた JSON を断らない")
        expect(ops["after"] == {"LiveConversion": False, "DetectionLevel": "Conservative", "Punctuation": "CommaJapanese", "PredictionMinLength": 4,
                                "ContinueAfterConversion": True, "ShiftEnterNewline": False, "LogTypedText": True}, f"保存後の値が違う: {ops['after']}")
        expect(ops["flag_continue"] == 1 and ops["flag_shift_enter"] == 0, "入力メニュー用の getter に、設定タブの変更が見えない")

        data = os.path.join(home, "data")
        config = json.load(open(os.path.join(data, "config.json"), encoding="utf-8"))
        expect(config.get("DetectionLevel") == "Conservative" and config.get("Punctuation") == "CommaJapanese", f"列挙型が名前の文字列で保存されていない: {config.get('DetectionLevel')}")
        expect(config.get("LiveConversion") is False and config.get("PredictionMinLength") == 4 and config.get("FileLog") is False, "config.json の値が違う")
        expect(mode(os.path.join(data, "config.json")) == 0o600, "config.json が 0600 でない")

        # 別のプロセスで読み直す
        again = run(lib_path, home, "settings-read")
        expect(again["values"] == {**again["values"], **ops["after"]}, f"別のプロセスで読み直すと値が違う: {again['values']}")

        # 既定値に戻す: 一覧の項目だけ。一覧にない設定は残す
        saved = json.load(open(os.path.join(data, "config.json"), encoding="utf-8"))
        saved["PasteApps"] = "Foo.exe"
        saved["Mode"] = "AutoSwitch"
        json.dump(saved, open(os.path.join(data, "config.json"), "w", encoding="utf-8"), ensure_ascii=False)
        reset = run(lib_path, home, "settings-reset")
        expect(reset["reset"] is None, f"既定値に戻せない: {reset['reset']}")
        expect(reset["values"]["LiveConversion"] is True and reset["values"]["DetectionLevel"] == "Balanced" and reset["values"]["PredictionMinLength"] == 2
               and reset["values"]["ContinueAfterConversion"] is False and reset["values"]["ShiftEnterNewline"] is True, f"既定値に戻っていない: {reset['values']}")
        expect(reset["flag_continue"] == 0 and reset["flag_shift_enter"] == 1, "入力メニュー用の getter も既定値に戻らない")
        after_reset = json.load(open(os.path.join(data, "config.json"), encoding="utf-8"))
        expect(after_reset.get("PasteApps") == "Foo.exe" and after_reset.get("Mode") == "AutoSwitch", "既定値に戻すとき、一覧にない設定まで消した")

    # 読めない config.json は上書きしない
    with tempfile.TemporaryDirectory(prefix="meltype-aot-tab-broken-") as home:
        data = os.path.join(home, "data")
        os.makedirs(data)
        broken_text = "{ これは壊れた設定"
        with open(os.path.join(data, "config.json"), "w", encoding="utf-8") as f:
            f.write(broken_text)
        broken = run(lib_path, home, "settings-broken")
        expect(broken.get("get") is None, f"読めない設定で一覧を返す: {broken.get('get')}")
        expect(broken.get("set") and broken.get("reset"), f"読めない設定への変更を断らない: {broken}")
        expect(open(os.path.join(data, "config.json"), encoding="utf-8").read() == broken_text, "読めない config.json が書き換わった")

    # IME のつもりのプロセスが動いたまま、設定タブ (別のプロセス) の変更が反映される
    with tempfile.TemporaryDirectory(prefix="meltype-aot-tab-live-") as home:
        watcher = subprocess.Popen(command(lib_path, "settings-watch"), env=env_for(home), stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        ready = json.loads(watcher.stdout.readline())
        if not ready.get("ready"):
            failures.append(f"IME のつもりのプロセスが始まらない (設定): {ready}")
            watcher.kill()
            return
        changer = run(lib_path, home, "settings-ops")   # 設定タブのつもり (句読点 CommaJapanese・変換後も続けて入力 ON ほか)
        expect(changer["set_punct"] is None and changer["set_continue"] is None, "設定タブのつもりの変更ができない")
        stdout, stderr = watcher.communicate("go\n", timeout=120)
        if watcher.returncode != 0:
            failures.append(f"IME のつもりのプロセスが異常終了 (設定): {stderr.strip()[-500:]}")
            return
        live = json.loads(stdout.strip().splitlines()[-1])
        expect(live["session"], "セッションを作れない (設定)")
        expect(live["before"] == "あ、", f"変更前の句読点が既定の「、」でない: {live['before']}")
        expect(live["after"] == "あ，", f"作り直さなくても、設定タブの変更 (句読点「，。」) が入力に反映される: {live['after']}")
        expect(live["flag_continue_before"] == 0 and live["flag_continue_after"] == 1, f"変換後も続けて入力の変更が、動いている IME に見えない: {live}")


def check_user_terms(lib_path, failures):
    def expect(condition, message):
        if not condition:
            failures.append(message)

    with tempfile.TemporaryDirectory(prefix="meltype-aot-terms-") as home:
        ops = run(lib_path, home, "term-ops")
        if not ops["safe"]:
            failures.append(f"保存場所が一時フォルダーの外: {ops['data_directory']}")
            return
        data = ops["data_directory"]
        expect(ops["abi"] == 8, f"FFI の版数が 8 でない: {ops['abi']}")
        expect(len(ops["user_before"]) == 4, f"前提: ユーザー辞書に 4 語: {ops['user_before']}")
        expect(ops["create_bad"], "空の名前を断らない")
        expect(ops["create"] is None and ops["id"] and ops["id"].startswith("user-") and len(ops["id"]) == 13, f"自作の専門用語集を作れない: {ops['create']} / {ops['id']}")
        expect(ops["dup_name"] and ops["bundled_name"], "かぶる名前 (自作・同梱) を断らない")
        expect(ops["listed"] == ["自作の用語", 0, 1, 1], f"一覧に自作・有効で出ない: {ops['listed']}")
        expect(not ops["bundled_not_user"] or all(k.startswith("user-") for k in ops["bundled_not_user"]), "同梱の分野が自作に見える")
        expect(ops["move"] is None, f"移せない: {ops['move']}")
        expect(ops["move_skipped"] is None and ops["move_added"] and len(ops["move_added"].split("\n")) == 4, f"移した語が違う: {ops['move_added']} / {ops['move_skipped']}")
        expect(ops["user_after_move"] == [], f"移した語がユーザー辞書に残っている: {ops['user_after_move']}")
        expect(sorted(map(tuple, ops["words_after_move"])) == sorted([("きごう", "記号"), ("あいこん", "icon"), ("こうせい", "構成"), ("ぬるぽが", "nullpo")]), f"専門用語集の中身が違う: {ops['words_after_move']}")
        expect(ops["listed_after_move"] and ops["listed_after_move"][1] == 4, f"語数が変わらない: {ops['listed_after_move']}")
        expect(ops["undo_restore"] is None and ops["undo_remove"] is None, f"取り消せない: {ops['undo_restore']} / {ops['undo_remove']}")
        expect(ops["user_after_undo"] == ops["user_before"], f"取り消しで、ユーザー辞書が元の並びに戻らない: {ops['user_after_undo']}")
        expect(ops["words_after_undo"] == [], "取り消しで、専門用語集から消えない")
        expect(ops["redo_restore_terms"] is None and len(ops["words_after_redo"]) == 4, "消した専門用語を元に戻せない")
        expect(ops["check_ok"] is None and ops["check_dup"] and ops["check_short"], f"入力チェックが違う: {ops['check_ok']} / {ops['check_dup']} / {ops['check_short']}")
        expect(ops["add"] is None and ops["update"] is None and ops["update_missing"], f"語の追加・編集が違う: {ops['add']} / {ops['update']} / {ops['update_missing']}")
        expect(ops["rename"] is None and ops["rename_bundled"], "名前の変更 (同梱は断る) が違う")
        expect(ops["export"] is None and ops["export_count"] == 5 and "# 名称: 改名した用語" in ops["export_text"] and "# 出典: 自作" in ops["export_text"], f"書き出しが違う: {ops['export']} / {ops['export_count']}")
        expect(ops["export_bundled"], "同梱の専門用語集を書き出せてしまう")
        expect(ops["import"] is None and ops["imported"] and ops["imported"][0] == "改名した用語 (2)" and ops["imported"][1] == 5, f"取り込みが違う: {ops['import']} / {ops['imported']}")
        expect(ops["delete"] is None and ops["deleted_enabled"] == 1 and ops["after_delete"] is False and ops["delete_bundled"], "削除が違う")
        expect(ops["restore"] is None and ops["after_restore"] and ops["after_restore"][2] == 1 and ops["after_restore"][1] == 5, f"削除した専門用語集を、同じ中身・有効で戻せない: {ops['restore']} / {ops['after_restore']}")
        expect(len(ops["files"]) == 2 and all(f.startswith("terms-user-") for f in ops["files"]), f"terms/ のファイルが違う: {ops['files']}")
        terms_dir = os.path.join(data, "terms")
        expect(mode(terms_dir) == 0o700, "terms/ が 0700 でない")
        for name in ops["files"]:
            expect(mode(os.path.join(terms_dir, name)) == 0o600, f"{name} が 0600 でない")
        config = json.load(open(os.path.join(data, "config.json"), encoding="utf-8"))
        expect(ops["id"] in config.get("EnabledTermDomains", []), "config.json の有効な分野に入っていない")

        again = run(lib_path, home, "term-read")   # 別のプロセスで読み直す
        expect(set(again["domains"]) == set(user_ids(ops)), f"別のプロセスで読み直すと、専門用語集が違う: {again['domains']}")
        expect(len(again["words"].get(ops["id"], [])) == 5, "別のプロセスで読み直すと、語が違う")
        expect(again["user_words"] == ops["user_before"], "別のプロセスで読み直すと、ユーザー辞書が違う")

    # IME のつもりのプロセスが動いたまま、別のプロセスが作った専門用語集が変換に出る
    with tempfile.TemporaryDirectory(prefix="meltype-aot-terms-live-") as home:
        watcher = subprocess.Popen(command(lib_path, "term-watch"), env=env_for(home), stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        ready = json.loads(watcher.stdout.readline())
        if not ready.get("ready"):
            failures.append(f"IME のつもりのプロセスが始まらない (専門用語集): {ready}")
            watcher.kill()
            return
        created = run(lib_path, home, "term-create")
        expect(created.get("create") is None and created.get("add") is None, f"画面のつもりの専門用語集を作れない: {created}")
        stdout, stderr = watcher.communicate("go\n", timeout=120)
        if watcher.returncode != 0:
            failures.append(f"IME のつもりのプロセスが異常終了 (専門用語集): {stderr.strip()[-500:]}")
            return
        live = json.loads(stdout.strip().splitlines()[-1])
        expect(live["session"], "セッションを作れない (専門用語集)")
        expect("nullpo" not in (live["before"] or ""), f"作る前から出ている: {live['before']}")
        expect(live["after"] == "nullpo", f"別のプロセスが作った専門用語集 (英単語の語) が、動いている IME の変換に出ない: {live['after']}")
        expect(live["revision_after"] != live["revision_before"], "専門用語集の版が進まない")


def user_ids(ops):
    """term-ops が最後に持っている自作の専門用語集 (作った 1 つ + 取り込んで戻した 1 つ)。"""
    return [name[len("terms-"):-len(".txt")] for name in ops["files"]]


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
    check_settings_tab(lib_path, failures)
    check_user_terms(lib_path, failures)
    settings_failures = len(failures)
    check_dictionary(lib_path, failures)
    for message in failures:
        print("FAIL:", message)
    if not failures:
        print("PASS: AOT 版で設定を保存・読み直しできた")
        print("PASS: AOT 版で設定タブの操作・不正な値の拒否・別プロセスへの反映ができた")
        print("PASS: AOT 版で自作の専門用語集の作成・移動・取り消し・書き出し・取り込み・別プロセスへの反映ができた")
        print("PASS: AOT 版で辞書の管理画面の操作・別プロセスへの反映・同時の登録ができた")
    else:
        print(f"FAIL (設定 {settings_failures} 件・辞書 {len(failures) - settings_failures} 件)")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
