$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$sdkDirectory = Join-Path $env:LOCALAPPDATA 'PhasmoEditor/dotnet'
$dotnet = Join-Path $sdkDirectory 'dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet) -or -not ((& $dotnet --list-sdks) -match '^10\.')) {
    Write-Host 'Installing .NET 10 SDK from Microsoft (no administrator required)...'
    $installer = Join-Path $env:TEMP ('dotnet-install-' + [Guid]::NewGuid().ToString('N') + '.ps1')
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -UseBasicParsing -OutFile $installer
        & $installer -Channel 10.0 -InstallDir $sdkDirectory
        if (-not (Test-Path -LiteralPath $dotnet)) { throw 'SDK installation failed.' }
    }
    finally { if (Test-Path -LiteralPath $installer) { Remove-Item -LiteralPath $installer } }
}
$env:DOTNET_ROOT = $sdkDirectory
$env:PATH = "$sdkDirectory;$env:PATH"
Write-Host 'Restoring packages, testing and creating the standalone EXE...'
& (Join-Path $PSScriptRoot 'prepare-release.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
Write-Host 'Ready. Open artifacts/release for the standalone EXE.'
