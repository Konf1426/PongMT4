param(
  [string]$InstallDir = "$env:LOCALAPPDATA\CirclePong",
  [string]$SourceDir = "$PSScriptRoot\..\launcher"
)

$ErrorActionPreference = "Stop"

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item -Path (Join-Path $SourceDir "*") -Destination $InstallDir -Recurse -Force

$shortcutPath = Join-Path ([Environment]::GetFolderPath("Desktop")) "Circle Pong UDP.lnk"
$launcherPath = Join-Path $InstallDir "PongCircleLauncher.ps1"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = "powershell.exe"
$shortcut.Arguments = "-ExecutionPolicy Bypass -File `"$launcherPath`""
$shortcut.WorkingDirectory = $InstallDir
$shortcut.Save()

Write-Host "Circle Pong launcher installed in:"
Write-Host $InstallDir
Write-Host "Desktop shortcut:"
Write-Host $shortcutPath
