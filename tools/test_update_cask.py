#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""tools/update-cask.py の自己テスト (一時ファイルで 1 回動かす)。  python3 tools/test_update_cask.py"""
import importlib.util
import pathlib
import subprocess
import sys
import tempfile

HERE = pathlib.Path(__file__).resolve().parent
CASK = HERE.parent / "mac" / "homebrew" / "Casks" / "meltype-for-mac.rb"
NEW_SHA = "a" * 64

with tempfile.TemporaryDirectory() as tmp:
    copy = pathlib.Path(tmp) / "meltype-for-mac.rb"
    original = CASK.read_text(encoding="utf-8")
    copy.write_text(original, encoding="utf-8")
    subprocess.run([sys.executable, str(HERE / "update-cask.py"), "9.8.7", NEW_SHA, str(copy)], check=True)
    updated = copy.read_text(encoding="utf-8")
    assert 'version "9.8.7"' in updated
    assert f'sha256 "{NEW_SHA}"' in updated
    # 2 行以外は変わらない
    diff = [(a, b) for a, b in zip(original.splitlines(), updated.splitlines()) if a != b]
    assert len(diff) == 2, diff
    assert len(original.splitlines()) == len(updated.splitlines())

    # 不正な値は拒否し、ファイルを変えない
    before = copy.read_text(encoding="utf-8")
    for args in (["x", NEW_SHA], ["1.2.3", "zz"]):
        result = subprocess.run([sys.executable, str(HERE / "update-cask.py"), *args, str(copy)], capture_output=True)
        assert result.returncode != 0, args
    assert copy.read_text(encoding="utf-8") == before

print("OK")
