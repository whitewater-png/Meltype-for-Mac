#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""Homebrew cask (mac/homebrew/Casks/meltype-for-mac.rb) の version と sha256 を書き換える。

    python3 tools/update-cask.py <version> <sha256> [cask のパス] [--force]

version は 1.1.7 の形、sha256 は 64 桁の 16 進数。それぞれ cask にちょうど 1 か所あることを確かめてから書く。
版が変わらないのに sha256 だけを変える書き換えは拒否する (同じ版の ZIP を差し替えると、Homebrew の
キャッシュと食い違い、SHA-256 不一致が再発するため)。どうしても同じ版で直すときだけ --force を付ける。
"""
import pathlib
import re
import sys

DEFAULT_CASK = pathlib.Path(__file__).resolve().parent.parent / "mac" / "homebrew" / "Casks" / "meltype-for-mac.rb"
VERSION_RE = re.compile(r'^(\s*version\s+)"[^"]*"', re.MULTILINE)
SHA_RE = re.compile(r'^(\s*sha256\s+)"[^"]*"', re.MULTILINE)
VERSION_VALUE_RE = re.compile(r'(?m)^\s*version\s+"([^"]*)"')
SHA_VALUE_RE = re.compile(r'(?m)^\s*sha256\s+"([^"]*)"')


def update(text, version, sha256, force=False):
    if not re.fullmatch(r"\d+(\.\d+)+", version):
        raise ValueError(f"version の形が違います: {version}")
    if not re.fullmatch(r"[0-9a-f]{64}", sha256):
        raise ValueError("sha256 は小文字 16 進 64 桁で指定してください")
    for name, pattern in (("version", VERSION_RE), ("sha256", SHA_RE)):
        count = len(pattern.findall(text))
        assert count == 1, f"cask の {name} の行が {count} 件あります (1 件のはずです)"
    old_version_match = VERSION_VALUE_RE.search(text)
    old_sha_match = SHA_VALUE_RE.search(text)
    if old_version_match is None or old_sha_match is None:
        raise ValueError("cask の version または sha256 の行が読めません")
    old_version = old_version_match.group(1)
    old_sha = old_sha_match.group(1)
    if old_version == version and old_sha != sha256 and not force:
        raise ValueError(
            f"版 {version} の sha256 を {old_sha[:12]}… から {sha256[:12]}… へ変えようとしています。"
            "同じ版の ZIP を差し替えると Homebrew のキャッシュと食い違い、SHA-256 不一致が再発します。"
            "版を上げてください (どうしても同じ版で直すときは --force)。"
        )
    text = VERSION_RE.sub(lambda m: f'{m.group(1)}"{version}"', text)
    return SHA_RE.sub(lambda m: f'{m.group(1)}"{sha256}"', text)


def main(argv):
    args = [a for a in argv[1:] if a != "--force"]
    force = len(args) < len(argv) - 1
    if len(args) not in (2, 3):
        print(__doc__, file=sys.stderr)
        return 2
    path = pathlib.Path(args[2]) if len(args) == 3 else DEFAULT_CASK
    try:
        text = path.read_text(encoding="utf-8")
        path.write_text(update(text, args[0], args[1], force=force), encoding="utf-8")
    except ValueError as e:
        print(f"エラー: {e}", file=sys.stderr)
        return 1
    print(f"更新しました: {path} (version {args[0]})")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
