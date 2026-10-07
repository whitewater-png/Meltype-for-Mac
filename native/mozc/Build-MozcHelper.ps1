# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
#
# Mozc の変換ヘルパー (meltype_mozc_helper.exe) をビルドして native\mozc\bin に置く。
# Build-Package.ps1 / Install-Meltype.ps1 は、bin にヘルパーがあれば Meltype に同梱する (無ければ Microsoft IME だけで動く)。
# GitHub Actions (.github/workflows/build.yml の mozc) でも同じスクリプトでビルドする。
#
# 必要なもの (Mozc の docs/build_mozc_in_windows.md と同じ):
#   Visual Studio 2022 (「C++ によるデスクトップ開発」、Windows 11 SDK、C++ ATL)、Python 3.12 以降、Bazelisk、Git
#
#   .\native\mozc\Build-MozcHelper.ps1 -Python C:\tools\python\python.exe -Bazelisk C:\tools\bazelisk.exe
param(
    # Mozc のソースを置く場所 (無ければ取得する)。Windows のパスの長さの制限があるので短い場所がよい。
    [string]$MozcSource = (Join-Path $env:USERPROFILE 'mozc'),
    [string]$Python = 'python',
    [string]$Bazelisk = 'bazelisk',
    # Visual Studio の VC フォルダー。省略すると vswhere で探す (Mozc の自動検出は一部の構成で失敗するため)。
    [string]$VcPath = '',
    # Bazel の結果を保存しておく場所 (GitHub Actions でビルドを速くするため)。省略すると使わない。
    [string]$DiskCache = ''
)
$ErrorActionPreference = 'Stop'

# 外部のプログラムを実行する。進み具合を標準エラーに出すもの (git・Bazel) を、Windows PowerShell 5.1 が
# エラーとして扱って止まらないようにし、終了コードで成否を見る。
function Invoke-Native([string]$FilePath, [string[]]$Arguments, [string]$Failure) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $FilePath @Arguments 2>&1 | ForEach-Object { Write-Host $_ } }
    finally { $ErrorActionPreference = $previous }
    if ($LASTEXITCODE -ne 0) { throw $Failure }
}
$here = $PSScriptRoot
$bin = Join-Path $here 'bin'
# ビルドする Mozc の版 (動作を確かめた commit に固定する)
$commit = (Get-Content -Raw -LiteralPath (Join-Path $here 'MOZC_COMMIT')).Trim()

# src の有無ではなく .git で見る (GitHub Actions のキャッシュが src\third_party_cache だけを先に戻すため)。
if (-not (Test-Path (Join-Path $MozcSource '.git'))) {
    New-Item -ItemType Directory -Force -Path $MozcSource | Out-Null
    Invoke-Native git @('-C', $MozcSource, 'init', '-q') 'git init に失敗しました。'
    Invoke-Native git @('-C', $MozcSource, 'remote', 'add', 'origin', 'https://github.com/google/mozc.git') 'git remote に失敗しました。'
    Invoke-Native git @('-C', $MozcSource, 'fetch', '-q', '--depth', '1', 'origin', $commit) "Mozc ($commit) を取得できませんでした。"
    Invoke-Native git @('-C', $MozcSource, 'checkout', '-q', 'FETCH_HEAD') 'git checkout に失敗しました。'
    Invoke-Native git @('-C', $MozcSource, 'submodule', 'update', '-q', '--init', '--recursive', '--depth', '1') 'Mozc のサブモジュールを取得できませんでした。'
}
$src = Join-Path $MozcSource 'src'

# ヘルパーのソースと BUILD の定義を Mozc の converter パッケージに入れる (エンジンを使えるのがこのパッケージのため)。
Copy-Item -LiteralPath (Join-Path $here 'meltype_mozc_helper.cc') -Destination (Join-Path $src 'converter') -Force
$build = Join-Path $src 'converter\BUILD.bazel'
if (-not (Select-String -LiteralPath $build -Pattern 'name = "meltype_mozc_helper"' -Quiet)) {
    Add-Content -LiteralPath $build -Value ("`n" + (Get-Content -Raw -LiteralPath (Join-Path $here 'BUILD.fragment'))) -Encoding utf8
}

if (-not $VcPath) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $vs) { throw 'Visual Studio (C++) が見つかりません。' }
    $VcPath = Join-Path $vs 'VC'
}

Push-Location $src
try {
    # LLVM・MSYS2・Ninja (Qt・WiX・Android NDK は使わない)
    Invoke-Native $Python @('build_tools/update_deps.py', '--noqt', '--nowix', '--nondk') '依存関係を取得できませんでした。'
    $env:BAZEL_VC = $VcPath
    $options = @('build', '//converter:meltype_mozc_helper', '--config', 'release_build')
    if ($DiskCache) { $options += "--disk_cache=$DiskCache" }
    # シンボリックリンクは Windows の開発者モードか管理者権限が要るので使わない。
    Invoke-Native $Bazelisk (@('--nowindows_enable_symlinks') + $options) 'ビルドに失敗しました。'
}
finally {
    Pop-Location
}

New-Item -ItemType Directory -Force -Path $bin | Out-Null
Copy-Item -LiteralPath (Join-Path $src 'bazel-bin\converter\meltype_mozc_helper.exe') -Destination $bin -Force
# Bazel の出力は読み取り専用なので、上書きやアンインストールで困らないように外す。
(Get-Item -LiteralPath (Join-Path $bin 'meltype_mozc_helper.exe')).IsReadOnly = $false
# Mozc と辞書 (IPAdic など)・ライブラリのライセンス
Copy-Item -LiteralPath (Join-Path $src 'data\installer\credits_en.html') -Destination (Join-Path $bin 'MOZC-CREDITS.html') -Force
Copy-Item -LiteralPath (Join-Path $MozcSource 'LICENSE') -Destination (Join-Path $bin 'MOZC-LICENSE.txt') -Force
Write-Host "作成しました: $bin"
