param(
    [string]$Configuration = "Release",
    [string]$OutputRoot = "artifacts/phase-1/win-x64"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedOutput = Join-Path $repoRoot $OutputRoot
$hostOutput = Join-Path $resolvedOutput "MapleProduct"
$brokerStage = Join-Path $resolvedOutput "broker-stage"

if (Test-Path -LiteralPath $resolvedOutput) {
    Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}
New-Item -ItemType Directory -Path $hostOutput | Out-Null
New-Item -ItemType Directory -Path $brokerStage | Out-Null

npm --prefix (Join-Path $repoRoot "client") run build
dotnet publish (Join-Path $repoRoot "src/Maple.WindowsHost/Maple.WindowsHost.csproj") `
    -c $Configuration -r win-x64 --self-contained true -o $hostOutput
dotnet publish (Join-Path $repoRoot "src/Maple.InputBroker/Maple.InputBroker.csproj") `
    -c $Configuration -r win-x64 --self-contained true -o $brokerStage

Get-ChildItem -LiteralPath $brokerStage -File -Filter "Maple.InputBroker.*" |
    Copy-Item -Destination $hostOutput -Force
Remove-Item -LiteralPath $brokerStage -Recurse -Force

$zipPath = Join-Path $resolvedOutput "MapleProduct-phase-1-win-x64.zip"
Compress-Archive -Path (Join-Path $hostOutput "*") -DestinationPath $zipPath -Force
Write-Output "Published directory: $hostOutput"
Write-Output "Published ZIP: $zipPath"
