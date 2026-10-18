param([ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

& dotnet run --project tests/UniLink.Tests/UniLink.Tests.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Tests failed. No package was produced.' }

$publishPath = Join-Path $PSScriptRoot "release/$Runtime"
& dotnet publish src/UniLink.Windows/UniLink.Windows.csproj -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false -o $publishPath
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

Copy-Item docs/README_FA.html $publishPath
Copy-Item LICENSE $publishPath
Copy-Item docs/CHANGELOG.md $publishPath
Copy-Item docs/NETWORK_BEHAVIOR.md $publishPath
Copy-Item notices $publishPath -Recurse -Force
$executable = Join-Path $publishPath 'UniLink.exe'
$digest = (Get-FileHash $executable -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath (Join-Path $publishPath 'SHA256SUMS.txt') -Value "$digest  UniLink.exe" -Encoding ASCII
Write-Output "Built $executable"
