$ErrorActionPreference = "Stop"

Write-Host "Checking .NET SDK..." -ForegroundColor Cyan
$sdks = & dotnet --list-sdks
$hasNet8 = $false

foreach ($sdk in $sdks) {
    if ($sdk -match "^8\.") {
        $hasNet8 = $true
        break
    }
}

if ($hasNet8) {
    Write-Host ".NET 8 SDK already installed." -ForegroundColor Green
    exit 0
}

if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
    Write-Host "winget is not available. Please install .NET 8 SDK manually from https://dotnet.microsoft.com/download" -ForegroundColor Yellow
    exit 1
}

Write-Host "Installing .NET 8 SDK via winget..." -ForegroundColor Cyan
winget install Microsoft.DotNet.SDK.8 --silent --accept-source-agreements --accept-package-agreements

Write-Host "Installation finished. Please restart terminal and run: dotnet --version" -ForegroundColor Green
