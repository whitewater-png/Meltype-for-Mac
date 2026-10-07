# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro

$ErrorActionPreference = 'Stop'

# Meltype のインストール。ビルド済みの app フォルダーを %LOCALAPPDATA%\Programs\Meltype にコピーし、
# スタートアップとスタートメニューに登録して起動する。管理者権限は不要。.NET は app の dotnet フォルダーに同梱しているので、インストール不要。

$source = Join-Path $PSScriptRoot 'app'
$target = Join-Path $env:LOCALAPPDATA 'Programs\Meltype'
$exe = Join-Path $target 'Meltype.exe'

# zip の中身がそろっているか (展開の途中で止まった・app フォルダーを差し替えた などで Meltype.exe が無いと、起動のところで分かりにくいエラーになる)
if (-not (Test-Path -LiteralPath (Join-Path $source 'Meltype.exe'))) {
    Write-Host "インストールするファイルが見つかりません: $(Join-Path $source 'Meltype.exe')"
    Write-Host 'zip をすべて展開し直してから、展開したフォルダーの Install.cmd を実行してください。'
    exit 1
}

# 動いている Meltype を止める。管理者として動いている Meltype は Stop-Process では止められないので、
# まず Meltype.exe --exit で終了の合図を送る (新しい版の Meltype なら、権限に関係なく終了する)。
function Stop-Meltype {
    $installed = Join-Path $env:LOCALAPPDATA 'Programs\Meltype\Meltype.exe'
    if ((Get-Process Meltype -ErrorAction SilentlyContinue) -and (Test-Path -LiteralPath $installed)) {
        Start-Process -FilePath $installed -ArgumentList '--exit' -Wait -ErrorAction SilentlyContinue
        for ($i = 0; $i -lt 30 -and (Get-Process Meltype -ErrorAction SilentlyContinue); $i++) { Start-Sleep -Milliseconds 100 }
    }
    foreach ($process in Get-Process Meltype, meltype_mozc_helper, AutoIME -ErrorAction SilentlyContinue) {
        try {
            $process | Stop-Process -Force -ErrorAction Stop
        }
        catch {
            Write-Host 'Meltype を終了できませんでした (管理者として動いているのかもしれません)。'
            Write-Host 'タスクトレイの Meltype のアイコンを右クリックして「終了」を選んでから、もう一度実行してください。'
            exit 1
        }
    }
    Start-Sleep -Milliseconds 300
}

Stop-Meltype

# 旧名 (AutoIME) のときのものがあれば片付ける (設定と学習データは Meltype の初回起動時に引き継ぐ)。
$oldShortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Startup\AutoIME.lnk'
if (Test-Path -LiteralPath $oldShortcut) { Remove-Item -LiteralPath $oldShortcut -Force }
$oldProgram = Join-Path $env:LOCALAPPDATA 'Programs\AutoIME'
if (Test-Path -LiteralPath $oldProgram) { Remove-Item -LiteralPath $oldProgram -Recurse -Force }
New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $target -Recurse -Force

# コピーした直後にウイルス対策ソフトが Meltype.exe を隔離することがある (キーボードの入力を扱うソフトなので誤検知されやすい)
if (-not (Test-Path -LiteralPath $exe)) {
    Write-Host "コピーした Meltype.exe が見つかりません: $exe"
    Write-Host 'ウイルス対策ソフトが Meltype を止めた可能性があります。'
    Write-Host 'Windows セキュリティ →「ウイルスと脅威の防止」→「保護の履歴」で Meltype を「許可」してから、もう一度 Install.cmd を実行してください。'
    exit 1
}

# インターネットから落とした印 (Zone.Identifier) はコピーにも付いてくるので外す (起動時の SmartScreen の確認を減らす)
Get-ChildItem -LiteralPath $target -Recurse -File | Unblock-File -ErrorAction SilentlyContinue

# Windows の「設定 → アプリ → インストールされているアプリ」からアンインストールできるようにする。
# 前は zip を展開したフォルダーの Uninstall.cmd しか無く、zip を消していると探し直す手間がかかった。
$uninstaller = Join-Path $target 'uninstall.ps1'
$uninstallSource = Join-Path $PSScriptRoot 'uninstall.ps1'
if (Test-Path -LiteralPath $uninstallSource) {
    Copy-Item -LiteralPath $uninstallSource -Destination $uninstaller -Force
    Unblock-File -LiteralPath $uninstaller -ErrorAction SilentlyContinue
    $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Meltype'
    New-Item -Path $key -Force | Out-Null
    $size = [int]((Get-ChildItem -LiteralPath $target -Recurse -File | Measure-Object Length -Sum).Sum / 1KB)
    $values = @{
        DisplayName     = 'Meltype'
        DisplayVersion  = (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion
        Publisher       = 'Yukishiro'
        DisplayIcon     = $exe
        InstallLocation = $target
        UninstallString = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$uninstaller`" -FromSettings"
        URLInfoAbout    = 'https://github.com/yksr-melt/Meltype'
    }
    foreach ($name in $values.Keys) { Set-ItemProperty -Path $key -Name $name -Value $values[$name] }
    foreach ($name in 'NoModify', 'NoRepair') { Set-ItemProperty -Path $key -Name $name -Value 1 -Type DWord }
    Set-ItemProperty -Path $key -Name EstimatedSize -Value $size -Type DWord
}

# 自動起動と、スタートメニュー・Windows 検索からの起動用 (現在のユーザー)
$shell = New-Object -ComObject WScript.Shell
foreach ($folderName in 'Startup', 'Programs') {
    $folder = [Environment]::GetFolderPath($folderName)
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    $shortcut = $shell.CreateShortcut((Join-Path $folder 'Meltype.lnk'))
    $shortcut.TargetPath = $exe
    $shortcut.WorkingDirectory = $target
    $shortcut.IconLocation = "$exe,0"
    $shortcut.Description = 'Meltype: 日本語と英語を自動で打ち分ける'
    $shortcut.Save()
}

# 管理者として実行していても、Meltype はふつうの権限で起動する (エクスプローラーから起動すると、ふつうの権限になる)。
# 管理者として動かすと、次のインストール・アンインストールでも管理者権限が必要になってしまう。
$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if ($admin) { Start-Process -FilePath explorer.exe -ArgumentList "`"$exe`"" }
else { Start-Process -FilePath $exe -WorkingDirectory $target }
Write-Host 'Meltype をインストールして起動しました。画面右下のタスクトレイに「あ」のアイコンが出ます。'
