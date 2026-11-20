# Builds a Release copy into %LOCALAPPDATA%\Programs\Cells and starts it.
# -SelfContained produces a single exe that runs without .NET installed (the one to share).
param(
    [string]$Target = (Join-Path $env:LOCALAPPDATA 'Programs\Cells'),
    [switch]$SelfContained
)
$ErrorActionPreference = 'Stop'

$exe = Join-Path $Target 'Cells.exe'
if (Get-Process Cells -ErrorAction SilentlyContinue) {
    $any = if (Test-Path $exe) { $exe } else { (Get-Process Cells | Select-Object -First 1).Path }
    Start-Process $any -ArgumentList '--exit' -Wait
    Get-Process Cells -ErrorAction SilentlyContinue | Wait-Process -Timeout 15 -ErrorAction SilentlyContinue
}

$extra = if ($SelfContained) { @('--self-contained', 'true', '-p:EnableCompressionInSingleFile=true') } else { @('--self-contained', 'false') }
dotnet publish "$PSScriptRoot\src\Cells" -c Release -r win-x64 @extra `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $Target
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Start-Process $exe
"Installed to $Target"
