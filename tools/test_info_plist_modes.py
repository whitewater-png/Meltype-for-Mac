#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""mac/Resources/Info.plist の入力モードの形を確かめる (要件 052、fork Issue #21)。Linux でも動く。

- 日本語モードに、モードごとの tsInputModeCharacterRepertoireKey が無いこと
  (ことえり・Google・ATOK の日本語モードと同じ形。ASCII 対応でない入力ソースにするため)。
- 日本語モードの辞書のキーと TISInputSourceID は今までどおり (変えると、更新した人の登録が外れるおそれがある)。
- 英数モード・入力メソッド全体の指定・モードの並びは今までどおり。
- ID が mac/Sources/MeltypeIME/Registration.swift の定数と合うこと。

使い方: python3 tools/test_info_plist_modes.py [Info.plist のパス]
"""
import os
import plistlib
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
JA_ID = "io.github.yksr-melt.inputmethod.Meltype.Japanese"
ROMAN_KEY = "com.apple.inputmethod.Roman"
ROMAN_ID = "io.github.yksr-melt.inputmethod.Meltype.Roman"
REPERTOIRE = "tsInputModeCharacterRepertoireKey"

failures = []


def check(cond, name, detail=""):
    print(("PASS " if cond else "FAIL ") + name + ("" if cond else ": " + detail))
    if not cond:
        failures.append(name)


def main():
    path = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "mac", "Resources", "Info.plist")
    with open(path, "rb") as f:
        d = plistlib.load(f)

    modes = d.get("ComponentInputModeDict", {}).get("tsInputModeListKey", {})
    check(sorted(modes) == sorted([JA_ID, ROMAN_KEY]), "モードは日本語と英数の 2 つ (辞書のキーは今までどおり)", repr(sorted(modes)))

    ja = modes.get(JA_ID, {})
    check(ja.get("TISInputSourceID") == JA_ID, "日本語モードの TISInputSourceID は今までどおり", repr(ja.get("TISInputSourceID")))
    check(REPERTOIRE not in ja, "日本語モードに、モードごとの " + REPERTOIRE + " が無い (Issue #21)", repr(ja.get(REPERTOIRE)))
    check(ja.get("TISIntendedLanguage") == "ja", "日本語モードの TISIntendedLanguage は ja", repr(ja.get("TISIntendedLanguage")))
    check(ja.get("tsInputModeScriptKey") == "smJapanese", "日本語モードの tsInputModeScriptKey は smJapanese", repr(ja.get("tsInputModeScriptKey")))

    roman = modes.get(ROMAN_KEY, {})
    check(roman.get("TISInputSourceID") == ROMAN_ID, "英数モードの TISInputSourceID は今までどおり", repr(roman.get("TISInputSourceID")))
    check(roman.get(REPERTOIRE) == ["Latn"], "英数モードの " + REPERTOIRE + " は [Latn] のまま", repr(roman.get(REPERTOIRE)))

    method = d.get("tsInputMethodCharacterRepertoireKey")
    check(method == ["Hira", "Kana", "Hani", "Latn"], "入力メソッド全体の tsInputMethodCharacterRepertoireKey は今までどおり", repr(method))

    order = d.get("ComponentInputModeDict", {}).get("tsVisibleInputModeOrderedArrayKey")
    check(order == [JA_ID, ROMAN_KEY], "モードの並びは今までどおり", repr(order))

    reg = os.path.join(ROOT, "mac", "Sources", "MeltypeIME", "Registration.swift")
    if len(sys.argv) <= 1 and os.path.exists(reg):
        src = open(reg, encoding="utf-8").read()
        def const(name):
            m = re.search(r"static let " + name + r'\s*=\s*"([^"]+)"', src)
            return m.group(1) if m else None
        check(const("modeID") == JA_ID, "Registration.swift の modeID が日本語モードの ID と同じ", repr(const("modeID")))
        check(const("romanModeID") == ROMAN_ID, "Registration.swift の romanModeID が英数モードの ID と同じ", repr(const("romanModeID")))
        check(const("romanModeKey") == ROMAN_KEY, "Registration.swift の romanModeKey が英数モードの辞書のキーと同じ", repr(const("romanModeKey")))

    print("%d 件失敗" % len(failures) if failures else "OK")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
