# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro

$ErrorActionPreference = 'Stop'

# Meltype はタスクトレイに常駐する独立プロセスとして動く。TSF/COM コンポーネントを他アプリに読み込ませることはない。
$project = Join-Path $PSScriptRoot 'src\Meltype\Meltype.csproj'
$output = Join-Path $PSScriptRoot 'app-build'

# 旧名 (AutoIME) のときのものがあれば片付ける (設定と学習データは Meltype の初回起動時に引き継ぐ)。
Get-Process AutoIME -ErrorAction SilentlyContinue | Stop-Process -Force
$oldShortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Startup\AutoIME.lnk'
if (Test-Path -LiteralPath $oldShortcut) { Remove-Item -LiteralPath $oldShortcut -Force }
Get-Process Meltype, meltype_mozc_helper -ErrorAction SilentlyContinue | Stop-Process -Force

dotnet build $project -c Release -o $output
if ($LASTEXITCODE -ne 0) { throw "Meltype のビルドに失敗しました (exit code $LASTEXITCODE)。" }

# Mozc の変換ヘルパー (native\mozc\Build-MozcHelper.ps1 で作ったもの) があれば一緒に置く。
$mozcBin = Join-Path $PSScriptRoot 'native\mozc\bin'
if (Test-Path -LiteralPath (Join-Path $mozcBin 'meltype_mozc_helper.exe')) {
    New-Item -ItemType Directory -Force -Path (Join-Path $output 'mozc') | Out-Null
    Copy-Item -Path (Join-Path $mozcBin '*') -Destination (Join-Path $output 'mozc') -Force
}

$exe = Join-Path $output 'Meltype.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "Meltype.exe が作成されませんでした: $exe" }

# 自動起動と、スタートメニュー・Windows 検索からの起動用 (現在のユーザー)
$shell = New-Object -ComObject WScript.Shell
foreach ($folderName in 'Startup', 'Programs') {
    $folder = [Environment]::GetFolderPath($folderName)
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    $shortcut = $shell.CreateShortcut((Join-Path $folder 'Meltype.lnk'))
    $shortcut.TargetPath = $exe
    $shortcut.WorkingDirectory = $output
    $shortcut.IconLocation = "$exe,0"
    $shortcut.Description = 'Meltype: 入力開始時に日本語入力を自動判定する'
    $shortcut.Save()
}

Start-Process -FilePath $exe -WorkingDirectory $output
Write-Host 'Meltype をインストールして起動しました。タスクトレイのアイコンから設定・ログ・一時停止 (Ctrl+半角/全角) ができます。'
