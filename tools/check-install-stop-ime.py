#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
#
# mac/install.sh の「動いている Meltype を止める行」が、更新機能 (入力メニューの「更新する」) から起動されたときにも
# 動いている IME を止められるか確かめる。リリースの前 (docs/RELEASE.md) と、install.sh を触ったあとに実行する。
#   python3 tools/check-install-stop-ime.py [install.sh のパス]      (既定は、このスクリプトから見た mac/install.sh)
#
# 背景: 更新機能は、動いている IME が切り離した bash -c を起動し、その中で install.sh を実行する
#   (Updater.swift の launchDetachedInstall。IME → bash -c → bash install.sh)。macOS の pkill は、既定で「自分と、その祖先」を
#   対象から外す (man pkill の -a)。install.sh から見て IME は祖先なので、`pkill -x Meltype` では IME が止まらず、
#   入れ替えのあとも古い版の IME が動き続けて、入力メニューに「新しい版があります」が残っていた (docs/IMPROVEMENT_PLAN.md の Q13)。
#   ターミナルから手で実行する場合 (祖先に Meltype がいない) は問題にならないので、手で試しただけでは気づけない。
#
# 確かめ方: install.sh から Meltype を止める行 (pkill / killall のうち Meltype という名前だけを指すもの) を取り出し、
#   名前だけを身代わりの名前 (MeltypeD<このスクリプトの pid>) に置き換えて、身代わりの実行ファイル (このスクリプトが C で作る) に対して実行する。
#   本物の Meltype は名前が違うので止まらず、辞書の管理画面 (MeltypeDictionary) の行も対象にしない。止めるのは、自分が起動した
#   子プロセスだけ (pid を指定する。名前での後始末はしない)。取り出す行は、想定した形 (pkill / killall + オプション (-a・-x・番号か
#   HUP・INT・QUIT・KILL・TERM の名前だけ) + 名前 + 任意の 2>/dev/null || true) の
#   ものだけを実行し、想定外の形は実行せずに FAIL にする。
#   1. 祖先: 身代わりが、更新機能と同じ起動のしかた (POSIX_SPAWN_SETSID | POSIX_SPAWN_CLOEXEC_DEFAULT・標準入力は /dev/null・固定の PATH)
#      で bash を起動し、その bash が止める行を実行する。身代わりがシグナル (SIGTERM・SIGKILL など) で止まり、止まったあとも bash が最後まで動くこと。
#      (本物の bash -c の中の bash install.sh の 1 段は省いている。祖先なので段数は関係しない)
#      (本物の更新は、環境変数に HOME・LANG も渡し、標準出力を update.log に向ける。祖先かどうかには関係しない)
#   2. 祖先ではない: ターミナルから手で実行した形。止まること
#   3. 名前は完全一致: 名前の先頭が同じ別の実行ファイル (<身代わり>X) は止まらないこと
#   4. 止める相手がいない: install.sh は set -euo pipefail なので、止める行が 0 以外で終わると install.sh が rsync のあとで中断する。
#      `set -euo pipefail` の下で止める行を実行して、そのあとの行に届くこと
#   5. 位置 (静的): 止める行が、入れ直しの分岐の rsync の行より後・`FIRST_INSTALL=0` の行より前にあり、rsync の行と同じ字下げ
#      (if や関数の中に入れていない)で、その間に exit / return がないこと
#      (入れ替えより前に止めると新旧が混ざる。初回インストールの分岐に置くと更新で動かない)
# 限界: install.sh 全体は実行しない (lsregister で本物の登録に触れる処理があるため)。止める行の位置は、字下げと exit / return の有無で見る粗い検査で、
#   字下げをそろえたまま呼ばれない形にしたもの (呼ばれない関数の定義・here-document や複数行の文字列の中・`{ exit 0; }` や `true; exit 0`
#   で抜ける書き方など) は見つからない。SHAPE は、bash の解釈と食い違う形 (Unicode の空白・対でない引用符・killall の -a など) も
#   受理することがあるが、実行すると赤になるだけで、本物を巻き込む経路はない。
#   検査に使う C のコンパイラは `xcrun cc` (Xcode の Command Line Tools が要る)。
# 終了コード: 0 = 通った、1 = 失敗、2 = 確かめられなかった (コンパイラが無い、ファイルが無い、身代わりの名前が先に使われている など)。
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_INSTALL = os.path.join(HERE, "..", "mac", "install.sh")
# 名前は pid 入り (同時に 2 つ実行しても、お互いの身代わりを止めない)。macOS の p_comm は古い版で 16 文字までなので、短く保つ。
DECOY = "MeltypeD%d" % os.getpid()
OTHER = DECOY + "X"
ENV = {"PATH": "/usr/bin:/bin:/usr/sbin:/sbin", "HOME": os.path.expanduser("~")}


def stopped(code):
    """身代わりがシグナルで終わったか (SIGTERM・SIGKILL・SIGHUP など。subprocess の終了コードは、シグナルのとき負)。"""
    return code is not None and code < 0

# 身代わり。parent: 更新機能 (Updater.swift) と同じ起動のしかたで bash を起動し、終わるのを待つ。idle: 何もせず待つ。
C_SOURCE = r'''
#include <fcntl.h>
#include <spawn.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include <sys/wait.h>
int main(int argc, char **argv) {
    if (argc >= 2 && strcmp(argv[1], "idle") == 0) { sleep(120); return 0; }
    if (argc < 3 || strcmp(argv[1], "parent") != 0) return 2;
    posix_spawnattr_t attr;
    posix_spawn_file_actions_t act;
    posix_spawnattr_init(&attr);
    posix_spawnattr_setflags(&attr, POSIX_SPAWN_SETSID | POSIX_SPAWN_CLOEXEC_DEFAULT);
    posix_spawn_file_actions_init(&act);
    posix_spawn_file_actions_addopen(&act, 0, "/dev/null", O_RDONLY, 0);
    posix_spawn_file_actions_addopen(&act, 1, "/dev/null", O_WRONLY, 0);
    posix_spawn_file_actions_adddup2(&act, 1, 2);
    char *args[] = { "/bin/bash", "-c", argv[2], NULL };
    char *env[] = { "PATH=/usr/bin:/bin:/usr/sbin:/sbin", NULL };
    pid_t child = 0;
    if (posix_spawn(&child, "/bin/bash", &act, &attr, args, env) != 0) return 3;
    int status = 0;
    waitpid(child, &status, 0);
    usleep(300000);
    return 0;
}
'''

# 名前が Meltype だけの語 (Meltype.app や MeltypeDictionary、パスの一部は含めない)
NAME = re.compile(r"(?<![\w./-])Meltype(?![\w./-])")
KILLER = re.compile(r"\b(pkill|killall)\b")
STOP_LINE = re.compile(r"^(pkill|killall)\b")
# 実行してよい形: pkill / killall + オプション + Meltype (引用符は可) + 任意の 2>/dev/null || true。
# オプションは許可リスト (-a・-x・シグナルの番号か HUP・INT・QUIT・KILL・TERM の名前) だけ。pkill -v は名前が一致しない自分のプロセスすべてにシグナルを送るので、
# 検査の実行で本物の Meltype を含む自分のプロセスのほとんどに飛ぶ。killall -s は何も送らない。どちらも実行せずに FAIL にする
SHAPE = re.compile(r"""^(pkill|killall)(\s+(-[ax]+|-[0-9]+|-(HUP|INT|QUIT|KILL|TERM)))*(\s+--)?\s+['"]?Meltype['"]?(\s+2>\s*/dev/null)?(\s+\|\|\s+true)?\s*$""")
EXIT_LINE = re.compile(r"^(exit|return)(\s|;|$)")
RSYNC_LINE = re.compile(r"^\s*rsync -a --delete Meltype\.app/ ")
FIRST_INSTALL_ZERO = re.compile(r"^\s*FIRST_INSTALL=0\s*$")


def read_lines(path):
    with open(path, encoding="utf-8", newline="") as f:   # newline="": CRLF を LF に直さず、CR を検査できるようにする
        return f.read().split("\n")


def continues(text):
    """bash が次の行へ続けるか: 行末 (空白なし) のバックスラッシュが奇数個で、コメント行ではない。"""
    trailing = len(text) - len(text.rstrip("\\"))
    return trailing % 2 == 1 and not text.lstrip().startswith("#")


def indent_of(line):
    """字下げの幅 (タブは空白 4 つ分)。"""
    return len(line[:len(line) - len(line.lstrip())].expandtabs(4))


def logical_lines(lines):
    """行末のバックスラッシュで続く行を 1 行にまとめる ((開始行の番号, 文字列) の一覧)。コメント行のバックスラッシュは続きにならない。"""
    out, i = [], 0
    while i < len(lines):
        start, text = i + 1, lines[i]
        while continues(text) and i + 1 < len(lines):
            i += 1
            text = text[:-1] + " " + lines[i].strip()
        out.append((start, text.strip()))
        i += 1
    return out


def find_stop_lines(lines):
    """止める行 (行番号, 文字列) の一覧と、取り出せない書き方の行 (行番号, 文字列) の一覧を返す。"""
    found, unextractable = [], []
    for number, text in logical_lines(lines):
        if text.startswith("#") or not (KILLER.search(text) and NAME.search(text)):
            continue
        (found if STOP_LINE.match(text) else unextractable).append((number, text))
    return found, unextractable


def check_placement(lines, number):
    """止める行の位置 (静的)。問題の説明の一覧を返す。"""
    rsync = [i for i, l in enumerate(lines, 1) if RSYNC_LINE.match(l)]
    zero = [i for i, l in enumerate(lines, 1) if FIRST_INSTALL_ZERO.match(l)]
    if len(rsync) != 1 or len(zero) != 1:
        return ["位置: 目印 (入れ直しの rsync の行・FIRST_INSTALL=0 の行) が 1 つずつ見つからない (%d 個・%d 個)。検査を直すこと" % (len(rsync), len(zero))]
    if not rsync[0] < number < zero[0]:
        return ["位置: 止める行 (%d 行) が、入れ直しの rsync (%d 行) と FIRST_INSTALL=0 (%d 行) の間にない" % (number, rsync[0], zero[0])]
    indent = indent_of(lines[number - 1])
    rsync_indent = indent_of(lines[rsync[0] - 1])
    if indent != rsync_indent:
        return ["位置: 止める行 (%d 行) の字下げが、入れ直しの rsync の行 (%d 行) と違う。if や関数の中に入れていないか" % (number, rsync[0])]
    for i in range(rsync[0], number - 1):
        line = lines[i]
        if EXIT_LINE.match(line.strip()) and indent_of(line) == indent:
            return ["位置: 止める行の前に、同じ字下げの exit / return がある (%d 行)" % (i + 1)]
    for i in range(number, zero[0] - 1):
        if EXIT_LINE.match(lines[i].strip()):
            return ["位置: 止める行のあと、FIRST_INSTALL=0 の前に exit / return がある (%d 行)" % (i + 1)]
    return []


def compiler():
    # Xcode の Command Line Tools が無い Mac では /usr/bin/cc が導入ダイアログを出す偽物なので、xcrun で実在を確かめる
    found = subprocess.run(["/usr/bin/xcrun", "--find", "cc"], stdin=subprocess.DEVNULL, capture_output=True, text=True)
    if found.returncode == 0 and found.stdout.strip():
        return found.stdout.strip()
    return None


def build_decoys(workdir):
    cc = compiler()
    if not cc:
        return None
    source = os.path.join(workdir, "decoy.c")
    with open(source, "w", encoding="utf-8") as f:
        f.write(C_SOURCE)
    paths = {}
    for name in (DECOY, OTHER):
        out = os.path.join(workdir, name)
        # xcrun cc (cc の実体を直接呼ばない): SDKROOT が無い環境 (Homebrew の python など) でも SDK のヘッダーが見つかる
        build = subprocess.run(["/usr/bin/xcrun", "cc", "-O0", "-o", out, source], stdin=subprocess.DEVNULL, capture_output=True, text=True)
        if build.returncode != 0:
            print(build.stderr)
            return None
        paths[name] = out
    return paths


def running(name):
    return subprocess.run(["/usr/bin/pgrep", "-x", name], stdin=subprocess.DEVNULL, capture_output=True).returncode == 0


def wait_exit(proc, seconds):
    try:
        return proc.wait(timeout=seconds)
    except subprocess.TimeoutExpired:
        return None


def check_line(number, text, decoys, workdir):
    """止める行 1 つについて、動かして確かめる 4 つの場面を試す。失敗の説明の一覧を返す (空なら通った)。"""
    if not SHAPE.match(text):
        return ["想定外の形なので実行しない (pkill / killall + オプション + Meltype + 任意の 2>/dev/null || true だけを実行する)。検査の SHAPE を見直すこと"]
    stop = NAME.sub(DECOY, text)
    if NAME.search(stop):
        return ["名前の置き換えが済んでいない (%s)" % stop]
    problems = []
    marker = os.path.join(workdir, "tail-%d" % number)

    # 1. 祖先: 身代わりが起動した bash から実行する
    script = stop + "\nsleep 1\necho ran > '" + marker + "'\n"
    parent = subprocess.Popen([decoys[DECOY], "parent", script], stdin=subprocess.DEVNULL)
    try:
        code = wait_exit(parent, 15)
        if code is None:
            problems.append("祖先: 15 秒たっても身代わりが終わらない")
        elif code == 0:
            problems.append("祖先: 起動した側 (IME のつもり) が止まらなかった。pkill は祖先を対象から外す")
        elif not stopped(code):
            problems.append("祖先: 起動した側がシグナル以外で終わった (終了コード %s)" % code)
        deadline = time.time() + 6
        while not os.path.exists(marker) and time.time() < deadline:
            time.sleep(0.1)
        if stopped(code) and not os.path.exists(marker):
            problems.append("祖先: 起動した側が止まったあと、bash が最後まで動かなかった")
    finally:
        if parent.poll() is None:
            parent.kill()
            parent.wait()

    # 2 と 3. ターミナルから手で実行した形 (止める行を実行する bash は、身代わりの子ではない)
    target = subprocess.Popen([decoys[DECOY], "idle"], stdin=subprocess.DEVNULL)
    keep = subprocess.Popen([decoys[OTHER], "idle"], stdin=subprocess.DEVNULL)
    try:
        time.sleep(0.5)
        subprocess.run(["/bin/bash", "-c", stop], stdin=subprocess.DEVNULL, capture_output=True, env=ENV, timeout=20)
        code = wait_exit(target, 5)
        if not stopped(code):
            problems.append("祖先ではない場合 (手で実行): 止まらなかった (終了コード %s)" % code)
        time.sleep(0.5)
        if keep.poll() is not None:
            problems.append("名前の完全一致: 名前の先頭が同じ別の実行ファイル (%s) まで止めた" % OTHER)
    finally:
        for proc in (target, keep):
            if proc.poll() is None:
                proc.kill()
                proc.wait()

    # 4. 止める相手がいない (target・keep は上で終わっている): set -euo pipefail の下でも、次の行に届くこと
    if running(DECOY):
        problems.append("止める相手がいない場合: 身代わりがまだ動いていて確かめられない")
    else:
        result = subprocess.run(["/bin/bash", "-c", "set -euo pipefail\n" + stop + "\necho reached"], stdin=subprocess.DEVNULL,
                                capture_output=True, text=True, env=ENV, timeout=20)
        if "reached" not in result.stdout:
            problems.append("止める相手がいない場合: set -euo pipefail の下で、止める行のあとに進めない (終了コード %d)。`|| true` が要る" % result.returncode)
    return problems


def main():
    path = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else DEFAULT_INSTALL)
    if not os.path.isfile(path):
        print("確かめられません: %s がありません" % path)
        return 2
    try:
        lines = read_lines(path)
    except (UnicodeDecodeError, OSError) as error:
        print("確かめられません: %s を UTF-8 の文字として読めません (%s)" % (path, error))
        return 2
    if any("\r" in line for line in lines):
        print("FAIL: %s に CR (CRLF の改行) があります。bash はそのままでは動きません" % path)
        return 1
    found, unextractable = find_stop_lines(lines)
    if unextractable:
        for number, text in unextractable:
            print("FAIL 行 %d: 取り出せない書き方 (行頭が pkill / killall ではない): %s" % (number, text))
        print("      Meltype を止める行は、行頭に pkill / killall を置く書き方にそろえるか、この検査の取り出しを直すこと")
        return 1
    if not found:
        print("FAIL: %s に、Meltype を止める行 (pkill / killall) が見つかりません" % path)
        return 1
    for name in (DECOY, OTHER):
        if running(name):
            print("確かめられません: 身代わりの名前 %s のプロセスが、すでに動いています" % name)
            return 2
    workdir = tempfile.mkdtemp(prefix="meltype-stop-check-")
    try:
        decoys = build_decoys(workdir)
        if decoys is None:
            print("確かめられません: C のコンパイラが使えません (xcode-select --install で Command Line Tools を入れる)")
            return 2
        failed = False
        for number, text in found:
            problems = check_placement(lines, number) + check_line(number, text, decoys, workdir)
            print("%s 行 %d: %s" % ("FAIL" if problems else "OK  ", number, text))
            for problem in problems:
                print("      - " + problem)
            failed = failed or bool(problems)
        return 1 if failed else 0
    finally:
        shutil.rmtree(workdir, ignore_errors=True)


if __name__ == "__main__":
    sys.exit(main())
