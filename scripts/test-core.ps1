$Stress = $false
if ($args -contains "-Stress") {
    $Stress = $true
}

$ErrorActionPreference = "Stop"

Push-Location (Join-Path $PSScriptRoot "..")
try {
    if ($Stress) {
        dotnet test tests/ResourceRouter.Core.Tests/ResourceRouter.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~PipelineEngineConcurrencyTests"
    }
    else {
        dotnet test tests/ResourceRouter.Core.Tests/ResourceRouter.Core.Tests.csproj -c Debug
    }
}
finally {
    Pop-Location
}
