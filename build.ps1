# Builds DDL.
#   .\build.ps1            -> .\app\DDL.exe (needs the .NET 10 Desktop Runtime to run)
#   .\build.ps1 -Release   -> .\dist\DDL-<version>-win-x64.zip (self-contained, runs anywhere)
# A running DDL locks .\app, so it is asked to quit first (DDL.exe --quit).
param([switch]$Release)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'DDL\DDL.csproj'

# Use a portable SDK next to the repo if there is one, else the dotnet on PATH.
$portable = Join-Path $root '..\.tools\dotnet\dotnet.exe'
$dotnet = if (Test-Path $portable) { (Resolve-Path $portable).Path } else { 'dotnet' }
if (Test-Path $portable) {
    $env:DOTNET_ROOT = Split-Path $dotnet
    $packages = Join-Path $root '..\.tools\nuget-packages'
    if (Test-Path $packages) { $env:NUGET_PACKAGES = (Resolve-Path $packages).Path }
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

if ($Release) {
    $version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    $out = Join-Path $root 'dist\publish'
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    & $dotnet publish $project -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none `
        -o $out
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $zip = Join-Path $root "dist\DDL-$version-win-x64.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip
    Write-Host "Release: $zip"
    return
}

$exe = Join-Path $root 'app\DDL.exe'
if ((Test-Path $exe) -and (Get-Process DDL -ErrorAction SilentlyContinue)) {
    Write-Host "Asking the running DDL to quit..."
    Start-Process $exe -ArgumentList '--quit' -Wait
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Process DDL -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 200 }
    if (Get-Process DDL -ErrorAction SilentlyContinue) { throw "DDL is still running. Quit it from the tray menu and try again." }
}

& $dotnet publish $project -c Release -o (Join-Path $root 'app')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Published: $exe"
