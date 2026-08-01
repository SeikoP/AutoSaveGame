param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:\.\d+)?$')]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [string]$IsccPath
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$definitionPath = Join-Path $repositoryRoot 'installer\AutoSaveGame.iss'
$resolvedPublish = (Resolve-Path -LiteralPath $PublishDirectory).Path

if (-not (Test-Path -LiteralPath (Join-Path $resolvedPublish 'AutoSaveGame.exe'))) {
    throw "Published AutoSaveGame.exe was not found in: $resolvedPublish"
}

if ([string]::IsNullOrWhiteSpace($IsccPath)) {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    )
    $IsccPath = $candidates | Where-Object {
        $_ -and (Test-Path -LiteralPath $_)
    } | Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($IsccPath) -or
    -not (Test-Path -LiteralPath $IsccPath)) {
    throw 'Inno Setup 6 compiler was not found. Install Inno Setup 6 or pass -IsccPath.'
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null
$arguments = @(
    "/DAppVersion=$Version",
    "/DPublishDir=$resolvedPublish",
    "/DOutputDir=$resolvedOutput",
    $definitionPath
)

& $IsccPath @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compiler exited with code $LASTEXITCODE."
}

$installerPath = Join-Path $resolvedOutput 'AutoSaveGame-Setup.exe'
if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "Installer was not produced at: $installerPath"
}

Write-Output $installerPath
