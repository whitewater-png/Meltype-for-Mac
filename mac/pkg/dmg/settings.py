# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
#
# dmgbuild の設定 (make-dmg.sh から -D pkg=... -D readme=... -D background=... で値を受け取る)。
# Finder を AppleScript で操作せず、.DS_Store を直接書く (macOS 26 の Finder は AppleScript での背景の設定を保存しないため)。
import os.path

defines = globals()["defines"]  # dmgbuild が渡す
pkg = defines["pkg"]
readme = defines["readme"]
background = defines["background"]

format = "UDZO"
compression_level = 9
filesystem = "HFS+"
size = None

files = [pkg, readme]
# background は上の defines から (dmgbuild は同名の変数を読む)
icon_locations = {
    os.path.basename(pkg): (135, 270),
    os.path.basename(readme): (135, 460),
}

# 窓の幅は make-background.swift の W と合わせる。高さは H (540) より大きくする
# (macOS 26 の Finder はツールバー等を消す設定に従わず、その分だけ中身の高さが減るため。余りは背景と同じ白)
window_rect = ((200, 120), (720, 640))
default_view = "icon-view"
show_status_bar = False
show_tab_view = False
show_toolbar = False
show_pathbar = False
show_sidebar = False
arrange_by = None
icon_size = 96
text_size = 12
show_icon_preview = False
include_icon_view_settings = True
include_list_view_settings = False
