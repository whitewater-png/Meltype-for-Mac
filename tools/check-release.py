#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
#
# 配布する前に、Meltype.app (と zip・pkg) の中身を読み取り専用で検査する (Mac 専用。Windows は対象外)。
#   cd mac && ./build.sh --no-install                 # 先にビルド (インストールはしない)
#   python3 tools/check-release.py                    # mac/build/Meltype.app を検査
#   python3 tools/check-release.py mac/dist/Meltype-mac-1.1.6.zip mac/dist/Meltype-mac-1.1.6.pkg
#   python3 tools/check-release.py --help
# 背景: 配布物のできを手で確かめていたため、署名の状態・ビルド時の絶対パス・arch・版の食い違い・pkg の中身を、
# 公開してから (または査読で) 見つけることがあった (docs/PKG_REVIEW_2026-10.md の §7 が検査項目の案。本家 #78・#87 の考え方)。
#
# 完全に読み取り専用: アプリ・zip・pkg・リポジトリ・mac/dist・導入済みの Meltype に書き込まない。zip と pkg は
# $TMPDIR の下の新しい一時フォルダーへ展開して調べ、終わると (中断しても) 消す。何もインストールせず、pkg の postinstall も
# 配布物の実行ファイルも実行しない。署名を変えない。ネットワークを使わない (公開済みの Release の digest は --zip-digest などで渡す)。
# xattr -l と mdls は署名つきのダウンロード URL を出しうるので使わない。
# 出力は項目ごとの PASS / FAIL / SKIP / INFO と件数だけ。絶対パスの文字列は出さない (1 行ずつ、念のためのマスクもかけている)。
#
# ビルド時の絶対パスの数え方は grep -a -o -E '/Users/[A-Za-z0-9._-]+/' <ファイル> | wc -l と同じ (strings では拾えない)。
# 検査項目 (番号は docs/PKG_REVIEW_2026-10.md の §7):
#   Meltype.app ................ §7-6 構造・arch・minos・RPATH・依存・ビルド時の絶対パス・ファイルの衛生、§7-7 署名
#   zip ........................ 中身の一覧・実行権限・シェルの構文・展開した Meltype.app (上と同じ)、§7-1 digest
#   pkg ........................ §7-1 digest、§7-2 署名・Installer への問い合わせ、§7-3 展開、§7-4 Distribution・PackageInfo・Scripts が postinstall の 1 つだけ、
#                                §7-5 postinstall・Resources・Distribution がリポジトリと同じか、§7-6 ペイロード (生のモードで setuid なども見る)
#   版 ......................... §7-8 Info.plist・Core の csproj・Converter.swift・zip/pkg の名前と中身・(任意で) --expect-version
#   cask ....................... mac/homebrew/Casks/meltype-for-mac.rb の version が built app と、sha256 が mac/dist/Meltype-mac-<版>.zip と一致するか
#   案内文 ..................... §7-9 「右クリック → 開く」の近くに「このまま開く」があるか
#   導入済みの Meltype ......... §7-10 (--compare-installed。参考の表示だけで、失敗にはしない)
# 終了コード: 0 = FAIL なし、1 = FAIL あり、2 = 使い方の誤り (明示した --ref が読めない場合や、想定外の例外で検査を続けられない場合を含む)。SKIP (検査できなかった) と INFO (参考) は失敗にしない。
# リポジトリ側の検査は --ref (既定 HEAD) のコミットの内容と比べる。未コミットの変更は見ないので、コミットしてから実行する。
import argparse
import atexit
import hashlib
import os
import plistlib
import re
import shutil
import signal
import stat
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, ".."))
DEFAULT_APP = os.path.join(REPO, "mac", "build", "Meltype.app")

# --- 期待値 (方針を変えたら、ここと docs/PKG_REVIEW_2026-10.md を直す) -------------------------------------------------
IME_ID = "io.github.yksr-melt.inputmethod.Meltype"
DICT_ID = "io.github.yksr-melt.Meltype.Dictionary"
PKG_ID = "io.github.yksr-melt.inputmethod.Meltype.pkg"
INSTALL_LOCATION = "/Library/Input Methods"
POSTINSTALL_TIMEOUT = "600"  # mac/make-pkg.sh に明示は無い。pkgbuild が postinstall に付ける既定値 (pkgbuild の既定値が変われば、ここを直す)
PKG_AUTH = "root"  # 同上。pkgbuild の既定値 (make-pkg.sh に明示は無い。インストール先がホーム限定でも root で走る)
# pkg の中身 (mac/make-pkg.sh: --scripts pkg/scripts / --resources pkg/resources)。root で走るスクリプトは postinstall の 1 つだけ。
PKG_SCRIPTS_EXPECTED = {"postinstall"}
PKG_TOP_EXPECTED = {"Distribution", "Resources", "Meltype-component.pkg"}
PKG_COMP_EXPECTED = {"Bom", "PackageInfo", "Payload", "Scripts"}
REPO_PKG_RESOURCES = "mac/pkg/resources"
REPO_PKG_SCRIPTS = "mac/pkg/scripts"
REPO_CASK = "mac/homebrew/Casks/meltype-for-mac.rb"
MAIN_MACHOS = [  # app の中に必ず要る Mach-O (件数ではなく名前で見る。Swift の互換 dylib などは、ツールチェーンによって有無が変わる)
    "Contents/MacOS/Meltype",
    "Contents/Helpers/MeltypeDictionary.app/Contents/MacOS/MeltypeDictionary",
    "Contents/Frameworks/libMeltypeNative.dylib",
]
HOST_ARCH = "arm64"
ZIP_TOP = "Meltype-mac"
ZIP_EXPECTED = {  # zip の中の Meltype-mac/ の直下 (mac/make-zip.sh が置くもの)。True = 実行権限が要る
    "Meltype.app": None,
    "install.sh": True,
    "uninstall.sh": True,
    "Install Meltype.command": True,
    "Uninstall Meltype.command": True,
    "INSTALL.txt": False,
    "LICENSE.txt": False,
    "THIRD-PARTY-NOTICES.md": False,
}
# postinstall に現れてはいけないもの (コメント行を除いて探す。簡単な目安であって、監査の代わりではない)
POSTINSTALL_DENY = [r"\bcurl\b", r"\bwget\b", r"\bnc\b", r"\bssh\b", r"\bscp\b", r"\bosascript\b", r"\bbase64\b", r"\beval\b", r"\brm\s+-[a-zA-Z]*r"]
MACHO_MAGICS = {bytes.fromhex(h) for h in ("cffaedfe", "feedfacf", "cafebabe", "bebafeca", "cafebabf")}
USERS_RE = re.compile(rb"/Users/[A-Za-z0-9._-]+/")  # 数え方は grep -a -o -E '/Users/[A-Za-z0-9._-]+/' | wc -l と同じ
LLAMA_FRAMEWORK_PREFIX = "Contents/Frameworks/llama.framework/"
THIRD_PARTY_BUILD_USER = b"/Users/miwa/"
OPEN_RE = r"右クリック[^→]*→ *「開く」"


# --- 出力 ----------------------------------------------------------------------------------------------------------
class Report:
    def __init__(self):
        self.counts = {"PASS": 0, "FAIL": 0, "SKIP": 0, "INFO": 0}
        self.masks = []

    def mask(self, text):
        for m in sorted(set(self.masks), key=len, reverse=True):  # 長いパスから先に伏せる (短い ~ だけを先に置き換えると、後ろの部分が残る)
            if m and len(m) > 3:
                text = text.replace(m, "<path>")
        # 念のため: ホームフォルダー・一時フォルダー配下とみられる絶対パスは伏せる
        text = re.sub(r"/(?:Users|private|var|Volumes|tmp)/[^\s'\"),;]+", "<path>", text)
        text = re.sub(r"<path>(?:/[A-Za-z0-9._@%+=~-]+)+", "<path>", text)  # 置き換えたパスの続き (/<フォルダー>/... など。ASCII のところまで) も伏せる
        return text

    def section(self, title):
        print("\n== " + self.mask(title), flush=True)

    def item(self, status, name, detail=""):
        self.counts[status] += 1
        line = "[%s] %s" % (status, name)
        if detail:
            line += " — " + detail
        print(self.mask(line), flush=True)
        return status == "PASS"


R = Report()


def ok(name, detail=""):
    return R.item("PASS", name, detail)


def fail(name, detail=""):
    return R.item("FAIL", name, detail)


def skip(name, detail=""):
    return R.item("SKIP", name, detail)


def info(name, detail=""):
    return R.item("INFO", name, detail)


def check(cond, name, good="", bad=""):
    return ok(name, good) if cond else fail(name, bad)


# --- 道具 ----------------------------------------------------------------------------------------------------------
class Tmp:
    """$TMPDIR の下の一時フォルダー。終了時 (中断を含む) に消す。"""

    def __init__(self):
        self.path = None

    def get(self):
        if self.path is None:
            self.path = tempfile.mkdtemp(prefix="check-release-")
            R.masks.append(self.path)
            R.masks.append(os.path.realpath(self.path))
        return self.path

    def cleanup(self):
        if self.path and os.path.isdir(self.path):
            def onerror(func, path, _exc):
                try:
                    os.chmod(path, 0o700)
                    func(path)
                except OSError:
                    pass
            shutil.rmtree(self.path, onerror=onerror)
        self.path = None


TMP = Tmp()
atexit.register(TMP.cleanup)


CHILD = [None]  # 実行中の子プロセス (中断のとき、展開の途中のファイルを書かせたまま消さないよう、先に止める)


def _on_signal(signum, _frame):
    child = CHILD[0]
    if child is not None and child.poll() is None:
        child.kill()
        child.wait()
    TMP.cleanup()
    sys.exit(130)


signal.signal(signal.SIGINT, _on_signal)
signal.signal(signal.SIGTERM, _on_signal)


def run(cmd, timeout=300, stdin=None):
    """(終了コード, stdout のバイト列, stderr のバイト列)。コマンドが無い・時間切れは rc=127 / 124。stdin にバイト列を渡せる。"""
    try:
        p = subprocess.Popen(cmd, stdin=subprocess.PIPE if stdin is not None else subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    except FileNotFoundError:
        return 127, b"", b"not found"
    CHILD[0] = p
    try:
        out, err = p.communicate(input=stdin, timeout=timeout)
        return p.returncode, out, err
    except subprocess.TimeoutExpired:
        p.kill()
        p.communicate()
        return 124, b"", b"timeout"
    finally:
        CHILD[0] = None


def text(b):
    return b.decode("utf-8", "replace")


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def vtuple(s):
    """版の文字列を数の組にする。末尾の 0 は無視する ("13" と "13.0" と "13.0.0" は同じ)。"""
    parts = [int(x) for x in re.findall(r"\d+", s or "")]
    while len(parts) > 1 and parts[-1] == 0:
        parts.pop()
    return tuple(parts) if parts else None


def git(*args):
    return run(["git", "-c", "core.quotePath=false", "-C", REPO] + list(args))


def git_show(ref, path):
    rc, out, _ = git("show", "%s:%s" % (ref, path))
    return out if rc == 0 else None


def is_macho(path):
    try:
        with open(path, "rb") as f:
            return f.read(4) in MACHO_MAGICS
    except OSError:
        return False


def archs_of(path):
    rc, out, _ = run(["lipo", "-archs", path])
    return text(out).split() if rc == 0 else None


def plist_of(path):
    try:
        with open(path, "rb") as f:
            return plistlib.load(f)
    except Exception:
        return None


REF = ["HEAD"]  # --ref の値
REF_EXPLICIT = [False]  # --ref を自分で指定したか。指定したのに読めないのは、typo などの誤りなので FAIL にする (指定しないときの HEAD は、リポジトリの外だと SKIP)


def ref_unreadable(name, detail="git でその版を読めない"):
    """リポジトリ側の基準 (--ref) を読めなかった項目。--ref を明示したなら FAIL、既定の HEAD なら SKIP。"""
    return fail(name, "指定した --ref を読めない (" + detail + ")") if REF_EXPLICIT[0] else skip(name, detail)


def real_names(d):
    """フォルダー直下の名前 (AppleDouble の ._* を除く)。"""
    try:
        return {n for n in os.listdir(d) if not n.startswith("._")}
    except OSError:
        return set()


def walk_files(root):
    """({root からの相対パス: 絶対パス} (._* を除く), ._* の数)。"""
    files, dots = {}, 0
    for dirpath, _dirs, names in os.walk(root, followlinks=False):
        for n in names:
            if n.startswith("._"):
                dots += 1
                continue
            p = os.path.join(dirpath, n)
            files[os.path.relpath(p, root)] = p
    return files, dots


PAYLOAD_LINE = re.compile(r"^(?P<t>[-dlbcps])(?P<p>[-rwxsStT]{9})\S?\s+\d+\s+\S+\s+\S+\s+.*?\s[A-Z][a-z]{2}\s+\d+\s+(?:\d{2}:\d{2}|\d{4})\s(?P<name>.*)$")


def read_payload_modes(payload):
    """ペイロード (gzip した cpio) を展開せずに一覧だけ読む。pkgutil --expand-full は展開時に setuid を落とす (04755 → 0755) ので、
    モードは生の Payload から読む。(行の一覧, None) か (None, 読めなかった理由)。"""
    tries = [("bsdtar", ["bsdtar", "-tvf", payload])]
    for label, cmd in tries:
        rc, out, _ = run(cmd)
        if rc == 127:
            continue
        lines = [l for l in text(out).splitlines() if l.strip()]
        parsed = [PAYLOAD_LINE.match(l) for l in lines]
        if rc == 0 and lines and all(parsed):
            return [m.groupdict() for m in parsed], None
    rc, gz, _ = run(["gzip", "-dc", payload])
    if rc == 0 and gz:
        rc, out, _ = run(["cpio", "-it", "-v"], stdin=gz)
        lines = [l for l in text(out).splitlines() if l.strip()]
        parsed = [PAYLOAD_LINE.match(l) for l in lines]
        if rc == 0 and lines and all(parsed):
            return [m.groupdict() for m in parsed], None
    return None, "bsdtar・cpio のどちらでも Payload の一覧を読めなかった"


def payload_problems(entries):
    """(特殊権限, 特殊ファイル, 外へ出る symlink) の名前の一覧。"""
    special, odd, links = [], [], []
    for e in entries:
        name, t, p = e["name"], e["t"], e["p"]
        target = None
        if t == "l" and " -> " in name:
            name, target = name.split(" -> ", 1)
        if t in "bcps":
            odd.append(name)
        if t == "l":
            if target is not None:
                base = os.path.normpath(os.path.join(os.path.dirname(name), target))
                if target.startswith("/") or base == ".." or base.startswith("../"):
                    links.append(name)
            continue
        if p[2] in "sS" or p[5] in "sS" or p[8] in "tT" or p[7] == "w":
            special.append(name)
    return special, odd, links


# --- Meltype.app の検査 (build/Meltype.app・zip の中・pkg のペイロードで共通) --------------------------------------------
def walk_bundle(app):
    """(ファイルの相対パス一覧, symlink のうち外へ出るもの, symlink の数, 特殊権限, .DS_Store, ._ の数)"""
    files, bad_links, n_links, special, ds_store, dot_underscore = [], [], 0, [], 0, 0
    root = os.path.realpath(app)
    for dirpath, dirnames, filenames in os.walk(app, followlinks=False):
        for name in dirnames + filenames:
            p = os.path.join(dirpath, name)
            rel = os.path.relpath(p, app)
            try:
                st = os.lstat(p)
            except OSError:
                continue
            if stat.S_ISLNK(st.st_mode):
                n_links += 1
                target = os.readlink(p)
                resolved = os.path.realpath(os.path.join(dirpath, target))
                if os.path.isabs(target) or not (resolved == root or resolved.startswith(root + os.sep)):
                    bad_links.append(rel)
                continue
            if stat.S_ISREG(st.st_mode):
                files.append(rel)
                if name == ".DS_Store":
                    ds_store += 1
                if name.startswith("._"):
                    dot_underscore += 1
            if st.st_mode & (stat.S_ISUID | stat.S_ISGID | stat.S_ISVTX | stat.S_IWOTH):
                special.append(rel)
    return files, bad_links, n_links, special, ds_store, dot_underscore


def parse_otool_l(out, want_macos=True):
    """otool -l の出力から (macOS 向けの minos 一覧, RPATH の一覧)。"""
    minos, rpaths = [], []
    blocks = re.split(r"(?m)^Load command \d+\s*$", out)
    for b in blocks:
        m = re.search(r"cmd (LC_\w+)", b)
        if not m:
            continue
        cmd = m.group(1)
        if cmd == "LC_BUILD_VERSION":
            plat = re.search(r"platform (\S+)", b)
            mo = re.search(r"minos (\S+)", b)
            if plat and mo and plat.group(1).lower() in ("1", "macos"):
                minos.append(mo.group(1))
        elif cmd == "LC_VERSION_MIN_MACOSX":
            mo = re.search(r"version (\S+)", b)
            if mo:
                minos.append(mo.group(1))
        elif cmd == "LC_RPATH":
            mo = re.search(r"path (.+?) \(offset \d+\)", b)
            if mo:
                rpaths.append(mo.group(1))
    return minos, rpaths


def parse_otool_L(out):
    deps = []
    for line in out.splitlines():
        if line.startswith("\t") or line.startswith("    "):
            deps.append(line.strip().split(" (")[0])
    return deps


def codesign_fields(path):
    """codesign -dv の stderr から必要な行だけを取る (Executable= は絶対パスなので捨てる)。"""
    rc, _, err = run(["codesign", "-dv", "--verbose=2", path])
    fields = {}
    for line in text(err).splitlines():
        for key in ("Signature", "TeamIdentifier", "Format"):
            if line.startswith(key + "="):
                fields[key] = line.split("=", 1)[1]
    return rc, fields


def check_app(app, label, signed):
    """Meltype.app 1 つを検査して、{'version': ..., 'exe': {相対パス: sha256}} を返す。"""
    R.section("Meltype.app: " + label)
    if not os.path.isdir(app):
        fail("Meltype.app がある", "見つからない")
        return None
    R.masks.append(os.path.realpath(app))
    R.masks.append(app)
    main_plist = plist_of(os.path.join(app, "Contents", "Info.plist"))
    helper = os.path.join(app, "Contents", "Helpers", "MeltypeDictionary.app")
    helper_plist = plist_of(os.path.join(helper, "Contents", "Info.plist"))
    frameworks = os.path.join(app, "Contents", "Frameworks")
    result = {"version": None, "exe": {}}

    # §7-6 構造
    need = [
        "Contents/Info.plist",
        "Contents/MacOS/Meltype",
        "Contents/Helpers/MeltypeDictionary.app/Contents/Info.plist",
        "Contents/Helpers/MeltypeDictionary.app/Contents/MacOS/MeltypeDictionary",
        "Contents/Frameworks/libMeltypeNative.dylib",
    ]
    missing = [n for n in need if not os.path.exists(os.path.join(app, n))]
    check(not missing, "必要なファイルがある (§7-6)", "Meltype・MeltypeDictionary・libMeltypeNative.dylib・Info.plist 2 つ", "足りない: " + ", ".join(missing))
    exe_main = os.path.join(app, "Contents", "MacOS", "Meltype")
    check(os.access(exe_main, os.X_OK), "Meltype に実行権限がある", "", "実行権限が無い")

    # §7-8 Info.plist
    if main_plist is None:
        fail("Info.plist を読める", "読めない")
        return None
    short, build = main_plist.get("CFBundleShortVersionString"), main_plist.get("CFBundleVersion")
    result["version"] = (short, build)
    check(main_plist.get("CFBundleIdentifier") == IME_ID, "Meltype の CFBundleIdentifier", "想定どおり", "想定と違う")
    lsmin = main_plist.get("LSMinimumSystemVersion")
    check(bool(vtuple(lsmin)), "LSMinimumSystemVersion がある (§7-4/8)", str(lsmin), "無い・読めない")
    if helper_plist is None:
        fail("辞書の管理画面の Info.plist を読める", "読めない")
    else:
        check(helper_plist.get("CFBundleIdentifier") == DICT_ID, "辞書の管理画面の CFBundleIdentifier", "想定どおり", "想定と違う")
        same = (helper_plist.get("CFBundleShortVersionString"), helper_plist.get("CFBundleVersion")) == (short, build)
        check(same, "辞書の管理画面の版が Meltype.app と同じ", "%s (%s)" % (short, build), "Meltype.app は %s (%s)、管理画面は %s (%s)" % (short, build, helper_plist.get("CFBundleShortVersionString"), helper_plist.get("CFBundleVersion")))

    # §7-7 署名
    rc, _, _ = run(["codesign", "--verify", "--deep", "--strict", app])
    check(rc == 0, "codesign --verify --deep --strict (§7-7)", "通る", "通らない (rc=%d)。署名の後に中身を変えた・strip した可能性" % rc)
    targets = [("Meltype.app", app), ("MeltypeDictionary.app", helper)]
    if os.path.isdir(frameworks):
        for n in sorted(os.listdir(frameworks)):
            if n.endswith((".dylib", ".framework")):
                targets.append(("Frameworks/" + n, os.path.join(frameworks, n)))
    bad = []
    for name, path in targets:
        rc, f = codesign_fields(path)
        if rc != 0:
            bad.append("%s: 署名なし/読めない" % name)
        elif signed:
            if f.get("Signature") == "adhoc" or f.get("TeamIdentifier", "not set") == "not set":
                bad.append("%s: Developer ID の署名ではない" % name)
        elif f.get("Signature") != "adhoc" or f.get("TeamIdentifier", "not set") != "not set":
            bad.append("%s: ad-hoc でない (Signature=%s, TeamIdentifier=%s)" % (name, f.get("Signature"), f.get("TeamIdentifier")))
    kind = "Developer ID" if signed else "ad-hoc・TeamIdentifier なし"
    check(not bad, "署名の種類 (§7-7)", "%d 個とも %s" % (len(targets), kind), "; ".join(bad))
    ent = []
    for name, path in targets[:2]:
        _, out, _ = run(["codesign", "-d", "--entitlements", ":-", path])
        if out.strip():
            ent.append(name)
    if signed:
        info("エンタイトルメント (§7-7)", "配布用署名では検査しない (持つ: %d 個)" % len(ent))
    else:
        check(not ent, "エンタイトルメントが無い (§7-7)", "Meltype.app・管理画面とも無し", "ある: " + ", ".join(ent))

    # ファイルの衛生
    files, bad_links, n_links, special, ds_store, dot_us = walk_bundle(app)
    check(not bad_links, "symlink が bundle の外へ出ない (§7-6)", "symlink %d 本、外へ出るもの 0" % n_links, "外へ出る・絶対パスの symlink が %d 本" % len(bad_links))
    check(not special, "setuid/setgid/sticky/他者書き込みが 0 (§7-6)", "0 件", "%d 件: %s" % (len(special), ", ".join(special[:5])))
    check(ds_store == 0, ".DS_Store が 0 (§7-6)", "0 件", "%d 件" % ds_store)
    check(dot_us == 0, "AppleDouble (._*) が 0 (§7-6)", "0 件", "%d 件" % dot_us)

    # Mach-O: arch / minos / RPATH / 依存 / ビルド時のパス
    machos = [f for f in files if is_macho(os.path.join(app, f))]
    non_macho = [f for f in files if f not in set(machos)]
    lost = [n for n in MAIN_MACHOS if n not in set(machos)]
    check(not lost, "必須の Mach-O 3 本がある (§7-6。Meltype・MeltypeDictionary・libMeltypeNative.dylib)", "3 本とも Mach-O (全部で %d 本)" % len(machos), "Mach-O でない・無い: " + ", ".join(os.path.basename(n) for n in lost))
    min_allowed = vtuple(lsmin)
    arch_bad, minos_bad, rpath_bad, dep_bad, paths = [], [], [], [], {}
    rpath_stat = {"@": 0, "OS 標準": 0, "その他": 0}
    n_deps = 0
    third_party_paths = 0
    for rel in machos:
        path = os.path.join(app, rel)
        with open(path, "rb") as f:
            found_paths = USERS_RE.findall(f.read())
        # 例外: azooKey の出来合いの llama.framework の中の /Users/miwa/ だけは、第三者の作者のビルド機のパスで、利用者 (自分) の情報ではない。
        # SwiftPM が checksum で中身を固定している。FAIL にせず件数だけ INFO に出す (件数が変わったら気づけるように)。
        # 自前の Mach-O (Meltype・MeltypeDictionary・libMeltypeNative.dylib) と、llama.framework の中でも /Users/miwa/ 以外は、0 件のまま FAIL で守る。
        if rel.startswith(LLAMA_FRAMEWORK_PREFIX):
            third_party_paths += sum(1 for p in found_paths if p == THIRD_PARTY_BUILD_USER)
            found_paths = [p for p in found_paths if p != THIRD_PARTY_BUILD_USER]
        paths[rel] = len(found_paths)
        result["exe"][rel] = sha256(path)
        archs = archs_of(path)
        if archs is None or "arm64" not in archs:
            arch_bad.append("%s (%s)" % (rel, " ".join(archs) if archs else "lipo 不可"))
            continue
        arch_flag = ["-arch", "arm64"] if len(archs) > 1 else []
        rc, out, _ = run(["otool"] + arch_flag + ["-l", path])
        minos, rpaths = parse_otool_l(text(out)) if rc == 0 else ([], [])
        if not minos:
            minos_bad.append("%s (macOS 向けの minos が読めない)" % rel)
        elif min_allowed and any((vtuple(m) or ()) > min_allowed for m in minos):
            minos_bad.append("%s (minos %s > %s)" % (rel, max(minos, key=lambda m: vtuple(m) or ()), lsmin))
        for rp in rpaths:  # mac/build.sh と同じ分類: OS 標準の場所と @ で始まる同梱側だけ許す
            if rp.startswith("@"):
                rpath_stat["@"] += 1
            elif rp.startswith(("/usr/lib/", "/System/")):
                rpath_stat["OS 標準"] += 1
            else:
                rpath_stat["その他"] += 1
                rpath_bad.append(rel)
        rc, out, _ = run(["otool"] + arch_flag + ["-L", path])
        deps = parse_otool_L(text(out)) if rc == 0 else []
        # dylib の -L の先頭は自分自身の install name (LC_ID_DYLIB)。依存ではないので外す
        # (libMeltypeNative.dylib の名前は NativeAOT の出力名のまま MeltypeNative.dylib。パスで dlopen するので実害は無い)
        rc2, out2, _ = run(["otool"] + arch_flag + ["-D", path])
        own = [l for l in text(out2).splitlines() if l.strip() and not l.rstrip().endswith(":")]
        if rc2 == 0 and own and deps and deps[0] == own[-1].strip():
            deps = deps[1:]
        for dep in deps:
            n_deps += 1
            if dep.startswith(("/usr/lib/", "/System/Library/")):
                continue
            if dep.startswith("@rpath/"):
                if not os.path.exists(os.path.join(frameworks, dep[len("@rpath/"):])):
                    dep_bad.append("%s: 同梱されていない @rpath の依存" % rel)
                continue
            if dep.startswith("@loader_path/") and os.path.exists(os.path.join(os.path.dirname(path), dep[len("@loader_path/"):])):
                continue
            if dep.startswith("@executable_path/") and os.path.exists(os.path.join(os.path.dirname(exe_main), dep[len("@executable_path/"):])):
                continue
            dep_bad.append("%s: OS 標準でも同梱でもない依存" % rel)
    check(not arch_bad, "Mach-O が arm64 を含む (§7-6)", "%d 本とも arm64" % len(machos), "arm64 でない: " + ", ".join(arch_bad))
    check(not minos_bad, "minos が LSMinimumSystemVersion 以下 (§7-6)", "最大 %s 以下 (platform 1 の行だけを見た)" % lsmin, "; ".join(minos_bad))
    check(not rpath_bad, "RPATH にビルド機の絶対パスが無い (§7-6)", "@ で始まる %d / OS 標準 %d / その他 0 (文字列は出さない)" % (rpath_stat["@"], rpath_stat["OS 標準"]), "OS 標準でも @ でもない (相対・絶対パスの) RPATH が %d 本 (文字列は出さない): %s" % (rpath_stat["その他"], ", ".join(sorted(set(rpath_bad)))))
    check(not dep_bad, "リンクされた依存が OS 標準か同梱 (§7-6)", "依存 %d 件" % n_deps, "; ".join(dep_bad[:5]))
    # Contents/Frameworks の *.framework が、2 つの実行ファイルの otool -L (@rpath/<名前>.framework/...) と一致するか (mac/build.sh の B1)。
    # 参照されないものが入っていれば無駄な同梱 (サイズ増)、参照されるのに無ければ起動できない (dependencies の検査と別に、余分も見る)。
    referenced, why = set(), None
    for exe in (exe_main, os.path.join(helper, "Contents", "MacOS", "MeltypeDictionary")):
        arch_flag = ["-arch", "arm64"] if "arm64" in (archs_of(exe) or []) and len(archs_of(exe) or []) > 1 else []
        rc, out, _ = run(["otool"] + arch_flag + ["-L", exe])
        if rc != 0:
            why = "otool -L が読めない"
            break
        referenced |= set(re.findall(r"@rpath/([^/\s]+)\.framework/", text(out)))
    if why:
        skip("Contents/Frameworks の *.framework が otool -L の参照と一致 (B1)", why)
    else:
        bundled = {n[:-len(".framework")] for n in real_names(frameworks) if n.endswith(".framework")}
        check(bundled == referenced, "Contents/Frameworks の *.framework が otool -L の参照と一致 (B1)", "%d 個 (%s)" % (len(bundled), ", ".join(sorted(bundled)) or "なし"), "足りない: %s / 余分: %s" % (", ".join(sorted(referenced - bundled)) or "-", ", ".join(sorted(bundled - referenced)) or "-"))
    total = sum(paths.values())
    detail = ", ".join("%s %d" % (os.path.basename(k), v) for k, v in sorted(paths.items()))
    check(total == 0, "実行ファイルに残るビルド時の絶対パス (ホームフォルダー配下) が 0 件 (§7-6)", "Mach-O %d 本で 0 件" % len(paths), "合計 %d 件 (%s)。mac/build.sh の strip -S (S2) が入る前のビルド、または効いていない" % (total, detail))
    info("llama.framework (azooKey の出来合い) に残る第三者のビルド機のパス %s (参考。自分のパスではない)" % THIRD_PARTY_BUILD_USER.decode(), "%d 件" % third_party_paths)
    others = 0
    for rel in non_macho:
        try:
            with open(os.path.join(app, rel), "rb") as f:
                others += len(USERS_RE.findall(f.read()))
        except OSError:
            pass
    info("Mach-O 以外のファイルに残る同じ種類の絶対パス (参考。ライセンス文などは正当な場合がある)", "%d 件" % others)
    return result


# --- zip ----------------------------------------------------------------------------------------------------------
def check_zip(path, digest, signed):
    name = os.path.basename(path)
    R.section("zip: " + name)
    R.masks.append(os.path.abspath(path))
    if not os.path.isfile(path):
        fail("zip がある", "見つからない")
        return None
    h = sha256(path)
    info("SHA-256 (§7-1)", h)
    if digest:
        check(h == digest.lower(), "SHA-256 が渡された digest と一致 (§7-1)", "一致", "一致しない")
    else:
        skip("SHA-256 の突き合わせ (§7-1)", "--zip-digest が無い (公開後は gh release view の digest を渡す)")
    try:
        zf = zipfile.ZipFile(path)
    except zipfile.BadZipFile:
        fail("zip を開ける", "壊れている")
        return None
    names = zf.namelist()
    unsafe = [n for n in names if n.startswith("/") or ".." in n.split("/")]
    junk = [n for n in names if "__MACOSX" in n or "/._" in n or n.startswith("._") or n.endswith(".DS_Store")]
    check(not unsafe, "名前に絶対パス・.. が無い", "%d 項目" % len(names), "%d 件" % len(unsafe))
    check(not junk, "__MACOSX・._*・.DS_Store が 0 (§7-6)", "0 件", "%d 件" % len(junk))
    tops = {n.split("/")[0] for n in names}
    check(tops == {ZIP_TOP}, "最上位が %s/ だけ" % ZIP_TOP, "そのとおり", "最上位: %s" % ", ".join(sorted(tops)))
    direct = {}
    for n in names:
        parts = n.split("/")
        if parts[0] == ZIP_TOP and len(parts) >= 2 and parts[1]:
            direct.setdefault(parts[1], []).append(n)
    miss = [k for k in ZIP_EXPECTED if k not in direct]
    extra = [k for k in direct if k not in ZIP_EXPECTED]
    check(not miss and not extra, "直下の構成が make-zip.sh と同じ", "%d 個" % len(ZIP_EXPECTED), "足りない: %s / 余分: %s" % (", ".join(miss) or "-", ", ".join(extra) or "-"))
    bad_exec = []
    for k, need_x in ZIP_EXPECTED.items():
        if need_x is None or k not in direct:
            continue
        mode = zf.getinfo("%s/%s" % (ZIP_TOP, k)).external_attr >> 16
        if need_x and not (mode & 0o111):
            bad_exec.append(k)
    check(not bad_exec, "シェルスクリプトに実行権限がある", "install.sh など 4 つ", "実行権限なし: " + ", ".join(bad_exec))
    tmp = tempfile.mkdtemp(prefix="zip-", dir=TMP.get())
    rc, _, err = run(["unzip", "-q", path, "-d", tmp])
    if rc != 0:
        fail("zip を一時フォルダーへ展開できる", "rc=%d" % rc)
        return None
    top = os.path.join(tmp, ZIP_TOP)
    scripts = [f for f in ("install.sh", "uninstall.sh", "Install Meltype.command", "Uninstall Meltype.command") if os.path.isfile(os.path.join(top, f))]
    syn = [f for f in scripts if run(["bash", "-n", os.path.join(top, f)])[0] != 0]
    check(not syn, "bash -n (構文だけ。実行しない)", "%d 本とも通る" % len(scripts), "構文エラー: " + ", ".join(syn))
    try:
        with open(os.path.join(top, "INSTALL.txt"), encoding="utf-8") as f:
            check("このまま開く" in f.read(), "INSTALL.txt に「このまま開く」がある (§7-9)", "ある", "無い")
    except OSError:
        skip("INSTALL.txt の案内 (§7-9)", "読めない")
    return check_app(os.path.join(top, "Meltype.app"), "zip の中 (%s)" % name, signed)


# --- pkg ----------------------------------------------------------------------------------------------------------
def tag_free(tree):
    """Distribution を比べやすい形にする (コメントと空白は無視)。"""
    return [tree.tag, dict(tree.attrib), (tree.text or "").strip(), [tag_free(c) for c in tree]]


def normalize_pkg_distribution(root):
    """productbuild が書き換える部分を、リポジトリの版に合わせて戻す。"""
    kids = []
    for c in list(root):
        if c.tag == "pkg-ref" and not (c.text or "").strip() and c.find("bundle-version") is not None:
            continue  # productbuild が足した bundle-version のブロック
        if c.tag == "pkg-ref":
            c.attrib.pop("installKBytes", None)
            c.attrib.pop("updateKBytes", None)
            if (c.text or "").startswith("#"):
                c.text = c.text[1:]
        kids.append(c)
    root[:] = kids
    return root


def check_pkg(path, digest, signed, ref, app_min):
    name = os.path.basename(path)
    R.section("pkg: " + name)
    R.masks.append(os.path.abspath(path))
    out_info = {"version": None, "app_result": None, "dist_min": None}
    if not os.path.isfile(path):
        fail("pkg がある", "見つからない")
        return out_info
    h = sha256(path)
    info("SHA-256 (§7-1)", h)
    if digest:
        check(h == digest.lower(), "SHA-256 が渡された digest と一致 (§7-1)", "一致", "一致しない")
    else:
        skip("SHA-256 の突き合わせ (§7-1)", "--pkg-digest が無い (公開後は gh release view の digest を渡す)")

    # §7-2 署名・Installer への問い合わせ (どれも読み取り専用。installer に -target は渡さない)
    rc, out, err = run(["pkgutil", "--check-signature", path])
    st = text(out + err)
    is_signed = rc == 0 and "no signature" not in st.lower()
    if signed:
        check(is_signed, "pkg が署名されている (§7-2)", "署名あり", "署名が無い")
    else:
        check(not is_signed, "pkg が署名なし (§7-2。署名しない方針のあいだの期待値)", "no signature", "署名ありに変わっている。方針を変えたなら --signed を付ける")
    rc, _, _ = run(["spctl", "--assess", "--type", "install", path])
    info("spctl --assess --type install (§7-2)", "受理 (rc=0)" if rc == 0 else "拒否 (rc=%d。未署名ならこれが普通で、Gatekeeper の手動許可が要る)" % rc)
    rc, out, _ = run(["installer", "-dominfo", "-pkg", path])
    doms = text(out).split()
    check(rc == 0 and doms == ["CurrentUserHomeDirectory"], "installer -dominfo がホームフォルダー限定 (§7-2)", "CurrentUserHomeDirectory だけ", "想定と違う: " + " ".join(doms))
    rc, out, _ = run(["pkgutil", "--payload-files", path])
    plist = [l for l in text(out).splitlines() if l.strip()]
    outside = [l for l in plist if not (l == "." or (l == "./Meltype.app" or l.startswith("./Meltype.app/")) or l == "./._Meltype.app")]  # ._Meltype.app は AppleDouble (下で数える)
    dot_us = sum(1 for l in plist if "/._" in l)
    check(rc == 0 and not outside, "payload の一覧が全部 ./Meltype.app の下 (§7-6)", "%d 項目" % len(plist), "外にある項目 %d 件 (rc=%d)" % (len(outside), rc))
    info("payload の AppleDouble (._*) (§7-6。参考値。1.1.5 は 3,470 件で無害と見ている)", "%d 件" % dot_us)

    # §7-3 展開
    dest = os.path.join(tempfile.mkdtemp(prefix="pkg-", dir=TMP.get()), "expanded")  # pkgutil --expand-full は存在しないパスにしか展開しない
    rc, _, err = run(["pkgutil", "--expand-full", path, dest])
    if not check(rc == 0, "pkgutil --expand-full (§7-3)", "展開できた (一時フォルダー。終わると消す)", "失敗 (rc=%d)" % rc):
        return out_info
    comp = os.path.join(dest, "Meltype-component.pkg")
    dist_path, pinfo_path = os.path.join(dest, "Distribution"), os.path.join(comp, "PackageInfo")
    try:
        dist = ET.parse(dist_path).getroot()
        pinfo = ET.parse(pinfo_path).getroot()
    except (ET.ParseError, OSError):
        fail("Distribution・PackageInfo を読める (§7-4)", "読めない")
        return out_info

    # §7-4 構造
    top_names = real_names(dest)
    comp_names = real_names(comp)
    check(top_names == PKG_TOP_EXPECTED, "pkg の最上位の構成が make-pkg.sh と同じ (§7-4)", "Distribution・Resources・Meltype-component.pkg だけ", "足りない: %s / 余分: %s" % (", ".join(sorted(PKG_TOP_EXPECTED - top_names)) or "-", ", ".join(sorted(top_names - PKG_TOP_EXPECTED)) or "-"))
    check(comp_names == PKG_COMP_EXPECTED, "Meltype-component.pkg の構成 (§7-4)", "Bom・PackageInfo・Payload・Scripts だけ", "足りない: %s / 余分: %s" % (", ".join(sorted(PKG_COMP_EXPECTED - comp_names)) or "-", ", ".join(sorted(comp_names - PKG_COMP_EXPECTED)) or "-"))
    # root で走るスクリプトは postinstall の 1 つだけ。Scripts の中身と PackageInfo の <scripts> の両方を固定する (preinstall などの追加を見逃さない)
    script_files, script_dots = walk_files(os.path.join(comp, "Scripts"))
    check(set(script_files) == PKG_SCRIPTS_EXPECTED, "Scripts の中身が postinstall の 1 つだけ (§7-4。root で走る)", "postinstall だけ", "足りない: %s / 余分: %s" % (", ".join(sorted(PKG_SCRIPTS_EXPECTED - set(script_files))) or "-", ", ".join(sorted(set(script_files) - PKG_SCRIPTS_EXPECTED)) or "-"))
    scripts_el = pinfo.find("scripts")
    declared = sorted((c.tag, c.get("file")) for c in (scripts_el if scripts_el is not None else []))
    check(declared == [("postinstall", "./postinstall")], "PackageInfo の <scripts> が postinstall の 1 つだけ (§7-4。preinstall などを足していない)", "postinstall だけ", "宣言されているもの: " + (", ".join("%s(%s)" % (t, f) for t, f in declared) or "なし"))
    if script_dots:
        info("Scripts の AppleDouble (._*)", "%d 件 (数に入れていない。スクリプトとしては走らない)" % script_dots)
    opt = dist.find("options")
    dom = dist.find("domains")
    osv = dist.find("./volume-check/allowed-os-versions/os-version")
    dist_min = osv.get("min") if osv is not None else None
    out_info["dist_min"] = dist_min
    check(opt is not None and opt.get("hostArchitectures") == HOST_ARCH, "Distribution の hostArchitectures (§7-4)", HOST_ARCH, "想定は %s" % HOST_ARCH)
    dok = dom is not None and dom.get("enable_currentUserHome") == "true" and dom.get("enable_localSystem") == "false" and dom.get("enable_anywhere") == "false"
    check(dok, "Distribution の domains (§7-4)", "ホームフォルダーのみ (localSystem・anywhere は false)", "想定と違う")
    check(dist_min is not None and (app_min is None or vtuple(dist_min) == vtuple(app_min)), "Distribution の os-version min が LSMinimumSystemVersion と同じ (§7-4)", str(dist_min), "pkg は %s、Info.plist は %s" % (dist_min, app_min))
    pi = pinfo.attrib
    check(pi.get("install-location") == INSTALL_LOCATION, "PackageInfo の install-location (§7-4)", "想定どおり", "想定と違う")
    check(pi.get("relocatable") == "false", "PackageInfo の relocatable=false (§7-4)", "false", "%s" % pi.get("relocatable"))
    check(pi.get("auth") == PKG_AUTH, "PackageInfo の auth (§7-4。pkgbuild の既定値で make-pkg.sh に明示は無い。ホーム限定でも root)", PKG_AUTH, "%s" % pi.get("auth"))
    check(pi.get("identifier") == PKG_ID, "PackageInfo の identifier (§7-4)", "想定どおり", "想定と違う")
    up = {b.get("id") for b in pinfo.findall("./upgrade-bundle/bundle")}
    check(up == {IME_ID}, "PackageInfo の upgrade-bundle (§7-4)", "Meltype だけ", "想定と違う (%d 個)" % len(up))
    strict = {b.get("id") for b in pinfo.findall("./strict-identifier/bundle")}
    check(strict == {IME_ID, DICT_ID}, "PackageInfo の strict-identifier (§7-4)", "Meltype と辞書の管理画面", "想定と違う (%d 個)" % len(strict))
    pin = pinfo.find("./scripts/postinstall")
    check(pin is not None and pin.get("timeout") == POSTINSTALL_TIMEOUT, "postinstall の timeout (§7-4)", POSTINSTALL_TIMEOUT, "想定と違う")

    # postinstall の中身
    post = os.path.join(comp, "Scripts", "postinstall")
    try:
        with open(post, "rb") as f:
            post_bytes = f.read()
    except OSError:
        fail("postinstall がある", "無い")
        post_bytes = None
    if post_bytes is not None:
        check(os.access(post, os.X_OK), "postinstall に実行権限がある", "", "実行権限が無い")
        check(post_bytes.startswith(b"#!/bin/bash"), "postinstall の先頭が #!/bin/bash", "", "別の先頭行")
        check(run(["bash", "-n", post])[0] == 0, "postinstall の bash -n (構文だけ。実行しない)", "通る", "構文エラー")
        body = "\n".join(l for l in text(post_bytes).splitlines() if not l.lstrip().startswith("#"))
        hits = [pat for pat in POSTINSTALL_DENY if re.search(pat, body)]
        check(not hits, "postinstall に通信・難読化・再帰削除のコマンドが無い (目安。監査の代わりではない)", "コメント行を除いて 0 件", "%d パターンに該当" % len(hits))
        # §7-5 リポジトリの同じ版と一致するか
        src = git_show(ref, REPO_PKG_SCRIPTS + "/postinstall")
        if src is None:
            ref_unreadable("postinstall が %s のものとバイト一致 (§7-5)" % ref)
        else:
            check(src == post_bytes, "postinstall が %s のものとバイト一致 (§7-5)" % ref, "一致", "違う (配布物の postinstall と %s の mac/pkg/scripts/postinstall が別物。公開済みのタグと比べるなら --ref v<版>-mac)" % ref)

    # §7-5 Resources (welcome.html・conclusion.html など。make-pkg.sh の --resources pkg/resources が、そのまま pkg の Resources になる)
    got_res, res_dots = walk_files(os.path.join(dest, "Resources"))
    rc, out, _ = git("ls-tree", "-r", "--name-only", ref, "--", REPO_PKG_RESOURCES)
    want_paths = [l for l in text(out).splitlines() if l.strip()] if rc == 0 else []
    if not want_paths:
        ref_unreadable("pkg の Resources が %s の mac/pkg/resources とバイト一致 (§7-5)" % ref)
    else:
        want_res = {p[len(REPO_PKG_RESOURCES) + 1:]: git_show(ref, p) for p in want_paths}
        diff, miss, extra = [], sorted(set(want_res) - set(got_res)), sorted(set(got_res) - set(want_res))
        for k in sorted(set(want_res) & set(got_res)):
            with open(got_res[k], "rb") as f:
                if f.read() != want_res[k]:
                    diff.append(k)
        check(not (diff or miss or extra), "pkg の Resources が %s の mac/pkg/resources とバイト一致 (§7-5)" % ref, "%d 個とも一致" % len(want_res), "違う: %s / 足りない: %s / 余分: %s" % (", ".join(diff) or "-", ", ".join(miss) or "-", ", ".join(extra) or "-"))
    if res_dots:
        info("Resources の AppleDouble (._*)", "%d 件 (数に入れていない)" % res_dots)

    # §7-6 Payload の生のモード (pkgutil --expand-full は展開時に setuid/setgid を落とすので、展開したものでは見えない)
    raw = os.path.join(tempfile.mkdtemp(prefix="pkgraw-", dir=TMP.get()), "raw")
    rc, _, _ = run(["pkgutil", "--expand", path, raw])
    entries, why = (None, "pkgutil --expand に失敗した (rc=%d)" % rc) if rc != 0 else read_payload_modes(os.path.join(raw, "Meltype-component.pkg", "Payload"))
    if entries is None:
        skip("Payload の生のモード (setuid/setgid・特殊ファイル・symlink) (§7-6)", why + "。展開後の app の検査 (下) では setuid は見えない")
    else:
        special, odd, links = payload_problems(entries)
        check(not (special or odd or links), "Payload の生のモードに setuid/setgid/sticky/他者書き込み・特殊ファイル・外へ出る symlink が無い (§7-6)", "%d 項目を読んで 0 件" % len(entries), "特殊権限 %d 件 (%s) / 特殊ファイル %d 件 (%s) / 外へ出る symlink %d 件 (%s)" % (len(special), ", ".join(special[:3]), len(odd), ", ".join(odd[:3]), len(links), ", ".join(links[:3])))

    # §7-5 Distribution
    srcd = git_show(ref, "mac/pkg/Distribution.xml")
    ver = pi.get("version")
    if srcd is None:
        ref_unreadable("Distribution が %s のものと一致 (§7-5)" % ref)
    else:
        try:
            replaced = "\n".join(re.sub("VERSION", ver or "", l, count=1) for l in text(srcd).split("\n"))  # make-pkg.sh の sed と同じ (1 行に 1 回)
            a = tag_free(ET.fromstring(replaced.encode("utf-8")))
            b = tag_free(normalize_pkg_distribution(ET.fromstring(open(dist_path, "rb").read())))
            check(a == b, "Distribution が %s のものと一致 (§7-5。productbuild の書き換えを除く)" % ref, "一致", "違う")
        except ET.ParseError:
            fail("Distribution が %s のものと一致 (§7-5)" % ref, "XML を読めない")

    # §7-8 版 (pkg の中)
    versions = {}
    pr = dist.find("pkg-ref[@version]")
    if pr is not None:
        versions["Distribution の pkg-ref"] = (pr.get("version"), None)
    versions["PackageInfo"] = (pi.get("version"), None)
    for b in pinfo.findall("bundle"):
        if b.get("CFBundleShortVersionString"):
            versions["PackageInfo の bundle " + ("管理画面" if b.get("id") == DICT_ID else "Meltype")] = (b.get("CFBundleShortVersionString"), b.get("CFBundleVersion"))
    out_info["versions"] = versions

    # §7-6 ペイロードの Meltype.app (zip・build と同じ検査)
    payload_plist = plist_of(os.path.join(comp, "Payload", "Meltype.app", "Contents", "Info.plist")) or {}
    if app_min is None and payload_plist.get("LSMinimumSystemVersion") and dist_min:
        check(vtuple(dist_min) == vtuple(payload_plist["LSMinimumSystemVersion"]), "Distribution の os-version min がペイロードの LSMinimumSystemVersion と同じ (§7-4)", str(dist_min), "pkg は %s、ペイロードは %s" % (dist_min, payload_plist["LSMinimumSystemVersion"]))
    out_info["app_result"] = check_app(os.path.join(comp, "Payload", "Meltype.app"), "pkg のペイロード (%s)" % name, signed)
    return out_info


# --- リポジトリ側 (版・案内文) ----------------------------------------------------------------------------------------
def repo_versions(ref):
    """リポジトリの ref 時点の版 {場所: (短い版, ビルド番号)}。読めなければ None。"""
    rc, _, _ = git("rev-parse", "--verify", "--quiet", ref + "^{commit}")
    if rc != 0:
        return None
    out = {}
    pl = git_show(ref, "mac/Resources/Info.plist")
    if pl is not None:
        try:
            d = plistlib.loads(pl)
            out["リポジトリの mac/Resources/Info.plist"] = (d.get("CFBundleShortVersionString"), d.get("CFBundleVersion"))
        except Exception:
            pass
    cs = git_show(ref, "src/Meltype.Core/Meltype.Core.csproj")
    if cs is not None:
        m = re.search(r"<Version>([^<]+)</Version>", text(cs))
        if m:
            out["Meltype.Core.csproj の <Version>"] = (m.group(1).strip(), None)
    sw = git_show(ref, "mac/Sources/MeltypeIME/Converter.swift")
    if sw is not None:
        m = re.search(r'versionString:\s*"Meltype\s+([0-9][0-9.]*)"', text(sw))
        if m:
            out["Converter.swift の versionString"] = (m.group(1), None)
    bug = git_show(ref, ".github/ISSUE_TEMPLATE/1-bug.yml")
    if bug is not None:
        ms = re.findall(r'placeholder:\s*"(\d+\.\d+\.\d+)"', text(bug))
        if len(ms) == 1:
            out["1-bug.yml の版の例"] = (ms[0], None)
    return out


def check_versions(sources, expect):
    R.section("版の一致 (§7-8)")
    shorts = {k: v[0] for k, v in sources.items() if v[0]}
    builds = {k: v[1] for k, v in sources.items() if v[1]}
    if expect:
        shorts["--expect-version"] = expect
    if len(shorts) < 2:
        skip("版が全部同じ", "比べる場所が足りない (%d 個)" % len(shorts))
        return
    distinct = sorted(set(shorts.values()))
    if len(distinct) == 1:
        ok("CFBundleShortVersionString と各所の版", "%d 箇所とも %s" % (len(shorts), distinct[0]))
    else:
        by = {}
        for k, v in shorts.items():
            by.setdefault(v, []).append(k)
        fail("CFBundleShortVersionString と各所の版", "食い違い: " + " / ".join("%s = %s" % (v, ", ".join(ks)) for v, ks in sorted(by.items())))
    if builds:
        bd = sorted(set(builds.values()))
        if len(bd) == 1:
            ok("CFBundleVersion (ビルド番号)", "%d 箇所とも %s" % (len(builds), bd[0]))
        else:
            by = {}
            for k, v in builds.items():
                by.setdefault(v, []).append(k)
            fail("CFBundleVersion (ビルド番号)", "食い違い: " + " / ".join("%s = %s" % (v, ", ".join(ks)) for v, ks in sorted(by.items())))


def check_cask(ref, app_versions):
    """Homebrew cask の version が built app の CFBundleShortVersionString と、sha256 が mac/dist の zip と一致するか。cask は --ref のコミットから読む。"""
    R.section("Homebrew cask (%s)" % REPO_CASK)
    src = git_show(ref, REPO_CASK)
    if src is None:
        ref_unreadable("cask の version と sha256", "git で %s の %s を読めない" % (ref, REPO_CASK))
        return
    body = text(src)
    mv = re.search(r'(?m)^\s*version\s+"([^"]+)"', body)
    ms = re.search(r'(?m)^\s*sha256\s+"([0-9a-fA-F]{64})"', body)
    if not mv or not ms:
        fail("cask に version と sha256 (64 桁) がある", "読めない")
        return
    cask_ver, cask_sha = mv.group(1), ms.group(1).lower()
    shorts = sorted({v[0] for v in app_versions if v and v[0]})
    if not shorts:
        skip("cask の version が built app と同じ", "built app の版を読めていない (app の検査を省いた)")
    else:
        check(shorts == [cask_ver], "cask の version が built app の CFBundleShortVersionString と同じ", cask_ver, "cask は %s、app は %s" % (cask_ver, ", ".join(shorts)))
    zpath = os.path.join(REPO, "mac", "dist", "Meltype-mac-%s.zip" % cask_ver)
    if not os.path.isfile(zpath):
        skip("cask の sha256 が mac/dist/Meltype-mac-%s.zip と同じ" % cask_ver, "zip が無い (make-zip.sh のあとに実行する)")
    else:
        h = sha256(zpath)
        check(h == cask_sha, "cask の sha256 が mac/dist/Meltype-mac-%s.zip と同じ" % cask_ver, "一致", "cask は %s…、zip は %s… (版を上げるか、同じ版で直すなら python3 tools/update-cask.py <版> <zip の sha256> --force)" % (cask_sha[:12], h[:12]))


def check_open_text(ref):
    R.section("案内文の整合 (§7-9)")
    rc, out, _ = git("grep", "-n", "-E", OPEN_RE, ref, "--", "README.md", "mac")
    if rc not in (0, 1):
        ref_unreadable("「右クリック → 開く」の近くに「このまま開く」がある", "git で読めない")
        return
    hits, missing = 0, []
    cache = {}
    for line in text(out).splitlines():
        m = re.match(r"^[^:]+:(.+?):(\d+):", line)
        if not m:
            continue
        path, n = m.group(1), int(m.group(2))
        if path not in cache:
            b = git_show(ref, path)
            cache[path] = text(b).split("\n") if b is not None else []
        lines = cache[path]
        hits += 1
        window = lines[max(0, n - 4):n + 3]
        if not any("このまま開く" in w for w in window):
            missing.append("%s:%d" % (path, n))
    check(not missing, "「右クリック → 開く」の前後 3 行に「このまま開く」がある", "%d 行すべて" % hits, "%d 行中 %d 行に無い: %s" % (hits, len(missing), ", ".join(missing[:5])))


def compare_installed(results):
    R.section("導入済みの Meltype との比較 (§7-10。参考。失敗にはしない)")
    inst = os.path.join(os.path.expanduser("~"), "Library", "Input Methods", "Meltype.app")
    R.masks.append(inst)
    exe = os.path.join(inst, "Contents", "MacOS", "Meltype")
    pl = plist_of(os.path.join(inst, "Contents", "Info.plist"))
    if not os.path.isfile(exe) or pl is None:
        info("導入済みの Meltype", "見つからない")
        return
    h = sha256(exe)
    info("導入済みの版", "%s (%s)" % (pl.get("CFBundleShortVersionString"), pl.get("CFBundleVersion")))
    for label, res in results:
        if res and res.get("exe"):
            theirs = res["exe"].get("Contents/MacOS/Meltype")
            ver = res["version"]
            same_ver = ver and ver[0] == pl.get("CFBundleShortVersionString")
            info("導入済みの Meltype と %s の実行ファイル" % label, ("同じ" if theirs == h else "違う") + ("・版番号は同じ" if same_ver else "・版番号も違う") + (" (同じ版番号で中身が違う: 開発ビルドが入っている)" if same_ver and theirs != h else ""))


# --- 本体 ----------------------------------------------------------------------------------------------------------
def main():
    ap = argparse.ArgumentParser(
        description="配布前の検査 (読み取り専用・Mac 専用)。既定で mac/build/Meltype.app を検査し、引数の zip・pkg も検査する。"
        " 何もインストールせず、署名を変えず、mac/dist に書かない。ネットワークも使わない。",
        epilog="終了コード: 0 = FAIL なし / 1 = FAIL あり / 2 = 使い方の誤り。検査項目は docs/PKG_REVIEW_2026-10.md の §7。",
    )
    ap.add_argument("files", nargs="*", help="検査する .zip / .pkg / .app (複数可)。省略すると mac/build/Meltype.app だけ")
    ap.add_argument("--no-app", action="store_true", help="既定の mac/build/Meltype.app を検査しない (zip・pkg だけを調べるとき)")
    ap.add_argument("--zip-digest", metavar="SHA256", help="zip の SHA-256 (公開済みの Release の digest。gh release view v<版>-mac --json assets で読み、ここへ渡す)")
    ap.add_argument("--pkg-digest", metavar="SHA256", help="pkg の SHA-256 (同上)")
    ap.add_argument("--digest", metavar="SHA256", help="zip・pkg のどちらか 1 つだけを渡したときの SHA-256 (--zip-digest / --pkg-digest の短縮)")
    ap.add_argument("--ref", default=None, metavar="REF", help="リポジトリ側の基準 (既定 HEAD。公開済みの版は v<版>-mac)。postinstall・Resources・Distribution・版・案内文を git で読み、作業ツリーの未コミットの変更は見ない (コミットしてから実行する)。明示した REF を読めなければ終了コード 2")
    ap.add_argument("--expect-version", metavar="X.Y.Z", help="全部がこの版であること (タグにする版)")
    ap.add_argument("--signed", action="store_true", help="Developer ID で署名した配布物を想定する (既定は ad-hoc 署名の app と署名なしの pkg)")
    ap.add_argument("--compare-installed", action="store_true", help="導入済みの Meltype と実行ファイルを比べて表示する (参考。開発用の Mac 向け)")
    args = ap.parse_args()

    apps, zips, pkgs = [], [], []
    for f in args.files:
        low = f.rstrip("/").lower()
        if low.endswith(".zip"):
            zips.append(f)
        elif low.endswith(".pkg"):
            pkgs.append(f)
        elif low.endswith(".app"):
            apps.append(f)
        else:
            print("引数の種類が分かりません (.zip / .pkg / .app): " + os.path.basename(f), file=sys.stderr)
            return 2
    if not args.no_app and not apps:
        apps.append(DEFAULT_APP)
    if not (apps or zips or pkgs):
        print("検査するものがありません", file=sys.stderr)
        return 2
    if args.digest and (len(zips) + len(pkgs) != 1):
        print("--digest は zip・pkg のどちらか 1 つだけを渡したときに使います。--zip-digest / --pkg-digest を使ってください", file=sys.stderr)
        return 2
    zip_digest = args.zip_digest or (args.digest if zips and not pkgs else None)
    pkg_digest = args.pkg_digest or (args.digest if pkgs and not zips else None)
    for d in (zip_digest, pkg_digest, args.digest):
        if d and not re.fullmatch(r"[0-9a-fA-F]{64}", d):
            print("SHA-256 は 64 桁の 16 進数で渡してください", file=sys.stderr)
            return 2
    R.masks.extend([REPO, os.path.expanduser("~"), os.environ.get("TMPDIR", "").rstrip("/")])
    REF_EXPLICIT[0] = args.ref is not None
    args.ref = args.ref or "HEAD"
    REF[0] = args.ref
    if REF_EXPLICIT[0] and (args.ref.startswith("-") or git("rev-parse", "--verify", "--quiet", args.ref + "^{commit}")[0] != 0):
        print("--ref で指定した %s を、このリポジトリのコミットとして読めません (名前の打ち間違いか、タグ・ブランチがありません)" % R.mask(args.ref), file=sys.stderr)
        return 2

    print("Meltype リリース前の検査 (読み取り専用・Mac 専用)")
    sources = {}
    results = []
    exe_hashes = []  # (表示名, {相対パス: sha256})

    for a in apps:
        label = "mac/build/Meltype.app" if os.path.abspath(a) == DEFAULT_APP else os.path.basename(a.rstrip("/"))
        res = check_app(a, label, args.signed)
        results.append((label, res))
        if res:
            sources["Meltype.app の Info.plist (%s)" % label] = res["version"]
            exe_hashes.append((label, res["exe"]))
    app_min = None
    for a in apps:
        p = plist_of(os.path.join(a, "Contents", "Info.plist"))
        if p and p.get("LSMinimumSystemVersion"):
            app_min = p["LSMinimumSystemVersion"]
            break
    for z in zips:
        res = check_zip(z, zip_digest, args.signed)
        results.append(("zip", res))
        if res:
            sources["zip の中の Info.plist"] = res["version"]
            exe_hashes.append(("zip の中", res["exe"]))
        m = re.match(r"Meltype-mac-(\d+\.\d+\.\d+)\.zip$", os.path.basename(z))
        if m:
            sources["zip のファイル名"] = (m.group(1), None)
    for pk in pkgs:
        info_out = check_pkg(pk, pkg_digest, args.signed, args.ref, app_min)
        res = info_out.get("app_result")
        results.append(("pkg", res))
        if res:
            sources["pkg のペイロードの Info.plist"] = res["version"]
            exe_hashes.append(("pkg の中", res["exe"]))
        for k, v in (info_out.get("versions") or {}).items():
            sources["pkg の " + k] = v
        m = re.match(r"Meltype-mac-(\d+\.\d+\.\d+)\.pkg$", os.path.basename(pk))
        if m:
            sources["pkg のファイル名"] = (m.group(1), None)

    # 同じビルドか (zip・pkg・app を同時に渡したとき)
    if len(exe_hashes) >= 2:
        R.section("同じビルドか (§7-6 の補足)")
        base_label, base = exe_hashes[0]
        for label, h in exe_hashes[1:]:
            diff = [k for k in base if k in h and base[k] != h[k]]
            only = [k for k in set(base) ^ set(h)]
            check(not diff and not only, "%s と %s の Mach-O が同一" % (base_label, label), "%d 本とも SHA-256 が同じ" % len(base), "違うもの %d 本 / 片方にしか無いもの %d 本 (別のビルドの成果物が混ざっている)" % (len(diff), len(only)))

    rv = repo_versions(args.ref)
    if rv is None:
        R.section("リポジトリ側 (--ref %s)" % args.ref)
        ref_unreadable("リポジトリの版・postinstall・案内文", "git で %s を読めない (リポジトリの外、または ref が無い)" % args.ref)
    else:
        sources.update(rv)
        check_versions(sources, args.expect_version)
        check_open_text(args.ref)
        check_cask(args.ref, [res["version"] for _l, res in results if res and _l not in ("zip", "pkg")])
    if rv is None and sources:
        check_versions(sources, args.expect_version)

    if args.compare_installed:
        compare_installed(results)

    c = R.counts
    print("\n== 結果: PASS %d / FAIL %d / SKIP %d / INFO %d" % (c["PASS"], c["FAIL"], c["SKIP"], c["INFO"]))
    if c["FAIL"]:
        print("FAIL があります。配布しないでください (原因を直して再実行)。")
        return 1
    print("FAIL はありません。SKIP は検査できなかった項目です (digest の突き合わせなど)。実機での確認は別です。")
    return 0


def guarded_main():
    """想定外の例外でも Traceback (絶対パスが混ざる) を出さず、パスを伏せた 1 行の FAIL にして終了コード 2 にする。"""
    try:
        return main()
    except Exception as e:  # 読めない zip・pkg、壊れたファイルなど。SystemExit・KeyboardInterrupt は握らない
        reason = getattr(e, "strerror", None) or ""
        line = "[FAIL] 検査を続けられませんでした (内部エラー: %s%s)。引数のファイルが読めるか確かめてください" % (type(e).__name__, ": " + reason if reason else "")
        try:
            print(R.mask(line), flush=True)
        except Exception:
            print("[FAIL] 検査を続けられませんでした (内部エラー)", flush=True)
        return 2


if __name__ == "__main__":
    try:
        code = guarded_main()
    finally:
        TMP.cleanup()
    sys.exit(code)
