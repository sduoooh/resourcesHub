param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Output = "artifacts/publish",
    [bool]$PublishSingleFile = $true,
    [bool]$SelfContained = $true,
    [bool]$PublishReadyToRun = $true
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Push-Location $repoRoot

try {
    Write-Host "Publishing ResourceRouter.App with settings:" -ForegroundColor Cyan
    Write-Host "  Configuration   : $Configuration"
    Write-Host "  Runtime         : $Runtime"
    Write-Host "  Output          : $Output"
    Write-Host "  SingleFile      : $PublishSingleFile"
    Write-Host "  SelfContained   : $SelfContained"
    Write-Host "  ReadyToRun      : $PublishReadyToRun"

    dotnet publish src/ResourceRouter.App/ResourceRouter.App.csproj `
        -c $Configuration `
        -r $Runtime `
        -o $Output `
        -p:PublishSingleFile=$PublishSingleFile `
        -p:SelfContained=$SelfContained `
        -p:PublishReadyToRun=$PublishReadyToRun
}
finally {
    Pop-Location
}
