#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
#
# Claude Code のクラウド セッションの開始時に、Core のテストと eval を流せるように .NET SDK を用意する。
# 手元の Mac など、dotnet が既にある環境では何もしない。クラウド以外 (CLAUDE_CODE_REMOTE が true でない) でも何もしない。
# 取得に失敗しても止めない (警告だけ出す。dotnet が要らない作業は続けられる)。
# 取得先: https://dot.net/v1/dotnet-install.sh → builds.dotnet.microsoft.com (2026-10-24 にクラウドから到達できることを確認。builds.dot.net は拒否される)。
# `--jsonfile global.json` は、このリポジトリの 1 行形式の global.json を読めないので使わず、チャンネル 10.0 を取る
# (global.json の rollForward: latestFeature で 10.0.401 以上なら整合する)。
set -uo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

INSTALL_DIR="$HOME/.dotnet"
if command -v dotnet >/dev/null 2>&1 || [ -x "$INSTALL_DIR/dotnet" ]; then
  :
else
  echo "dotnet が無いので .NET SDK 10.0 を $INSTALL_DIR に入れます"
  tmp="$(mktemp -d)"
  if curl -sSL --max-time 60 -o "$tmp/dotnet-install.sh" https://dot.net/v1/dotnet-install.sh \
     && bash "$tmp/dotnet-install.sh" --channel 10.0 --install-dir "$INSTALL_DIR" --no-path >/dev/null; then
    echo "入れました: $("$INSTALL_DIR/dotnet" --version)"
  else
    echo "警告: .NET SDK を取得できませんでした。dotnet を使う作業 (テスト・eval) はできません (ネットワークの許可を確認)" >&2
  fi
  rm -rf "$tmp"
fi

# このセッションの以後のコマンドで dotnet が見えるようにする
if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
  {
    echo "export PATH=\"$INSTALL_DIR:\$PATH\""
    echo 'export DOTNET_CLI_TELEMETRY_OPTOUT=1'
    echo 'export DOTNET_NOLOGO=1'
  } >> "$CLAUDE_ENV_FILE"
fi
