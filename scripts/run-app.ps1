$ErrorActionPreference = "Stop"

Push-Location (Join-Path $PSScriptRoot "..")
try {
    dotnet restore ResourceRouter.sln
    dotnet build ResourceRouter.sln -c Debug
    dotnet run --project src/ResourceRouter.App/ResourceRouter.App.csproj
}
finally {
    Pop-Location
}
