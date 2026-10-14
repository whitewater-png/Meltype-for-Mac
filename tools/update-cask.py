#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""Homebrew cask (mac/homebrew/Casks/meltype-for-mac.rb) の version と sha256 を書き換える。

    python3 tools/update-cask.py <version> <sha256> [cask のパス]

version は 1.1.7 の形、sha256 は 64 桁の 16 進数。それぞれ cask にちょうど 1 か所あることを確かめてから書く。
"""
import pathlib
import re
import sys

DEFAULT_CASK = pathlib.Path(__file__).resolve().parent.parent / "mac" / "homebrew" / "Casks" / "meltype-for-mac.rb"
VERSION_RE = re.compile(r'^(\s*version\s+)"[^"]*"', re.MULTILINE)
SHA_RE = re.compile(r'^(\s*sha256\s+)"[^"]*"', re.MULTILINE)


def update(text, version, sha256):
    if not re.fullmatch(r"\d+(\.\d+)+", version):
        raise ValueError(f"version の形が違います: {version}")
    if not re.fullmatch(r"[0-9a-f]{64}", sha256):
        raise ValueError("sha256 は小文字 16 進 64 桁で指定してください")
    for name, pattern in (("version", VERSION_RE), ("sha256", SHA_RE)):
        count = len(pattern.findall(text))
        assert count == 1, f"cask の {name} の行が {count} 件あります (1 件のはずです)"
    text = VERSION_RE.sub(lambda m: f'{m.group(1)}"{version}"', text)
    return SHA_RE.sub(lambda m: f'{m.group(1)}"{sha256}"', text)


def main(argv):
    if len(argv) not in (3, 4):
        print(__doc__, file=sys.stderr)
        return 2
    path = pathlib.Path(argv[3]) if len(argv) == 4 else DEFAULT_CASK
    path.write_text(update(path.read_text(encoding="utf-8"), argv[1], argv[2]), encoding="utf-8")
    print(f"更新しました: {path} (version {argv[1]})")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
