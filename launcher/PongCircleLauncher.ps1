param(
  [string]$GameExe = "$PSScriptRoot\Game\PongCircle.exe",
  [string]$ServerHost = "pong.becop.fr",
  [int]$ServerPort = 41234
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $GameExe)) {
  Write-Host "Game executable not found:"
  Write-Host $GameExe
  Write-Host ""
  Write-Host "Place your Unity standalone build in the Game folder next to this launcher."
  exit 1
}

$env:PONG_UDP_HOST = $ServerHost
$env:PONG_UDP_PORT = "$ServerPort"
$env:PONG_UDP_DEVICE_ID = [Guid]::NewGuid().ToString("N")
$env:PONG_UDP_DEVICE_NAME = "$env:COMPUTERNAME-$PID"

Write-Host "Starting Circle Pong UDP..."
Write-Host "Server: $ServerHost`:$ServerPort"
Write-Host "Device: $env:PONG_UDP_DEVICE_NAME"
Start-Process -FilePath $GameExe -WorkingDirectory (Split-Path -Parent $GameExe)
