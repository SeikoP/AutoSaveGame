[CmdletBinding()]
param(
    [string]$Version,

    [ValidateRange(1, 20)]
    [int]$KeepLatest = 1,

    [switch]$CleanAll,

    [switch]$SkipInstaller,

    [switch]$SkipSmoke,

    [string]$IsccPath
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$artifactsRoot = Join-Path $repositoryRoot 'artifacts'
$envFile = Join-Path $repositoryRoot '.env'
$latestTag = $null

if ([string]::IsNullOrWhiteSpace($Version)) {
    $tag = git describe --tags --abbrev=0 2>$null
    if ($LASTEXITCODE -eq 0 -and $tag) {
        $latestTag = $tag.TrimStart('v')
    }
    $Version = if ($latestTag) { $latestTag } else { '0.3.0' }
}

if ($Version -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$') {
    throw "Version must be MAJOR.MINOR.PATCH: $Version"
}

function Get-OAuthConfigPath {
    if (-not (Test-Path -LiteralPath $envFile -PathType Leaf)) {
        throw "Missing OAuth credentials file: $envFile"
    }

    $values = @{}
    Get-Content -LiteralPath $envFile | ForEach-Object {
        if ($_ -match '^\s*([A-Z0-9_]+)\s*=\s*(.*?)\s*$') {
            $values[$Matches[1]] = $Matches[2].Trim('"', "'")
        }
    }

    if ([string]::IsNullOrWhiteSpace($values['AUTOSAVEGAME_GOOGLE_CLIENT_ID']) -or
        [string]::IsNullOrWhiteSpace($values['AUTOSAVEGAME_GOOGLE_CLIENT_SECRET'])) {
        throw "AUTOSAVEGAME_GOOGLE_CLIENT_ID and AUTOSAVEGAME_GOOGLE_CLIENT_SECRET are required in $envFile"
    }

    $config = [ordered]@{
        clientId = $values['AUTOSAVEGAME_GOOGLE_CLIENT_ID']
        clientSecret = $values['AUTOSAVEGAME_GOOGLE_CLIENT_SECRET']
    }
    $configPath = Join-Path $env:TEMP 'autosavegame-local-oauth.json'
    $config | ConvertTo-Json -Compress | Set-Content `
        -LiteralPath $configPath `
        -Encoding utf8 -NoNewline
    return $configPath
}

function Get-OldVersionDirectories {
    param([string]$Root)

    if (-not (Test-Path -LiteralPath $Root)) {
        return @()
    }

    $builds = Get-ChildItem -LiteralPath $Root -Directory -ErrorAction SilentlyContinue
    return @($builds | Where-Object { $_.Name -match '^\d+\.\d+\.\d+(?:\.\d+)?$' })
}

function Remove-OwnBuildDirectory {
    param([string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $resolvedArtifacts = [System.IO.Path]::GetFullPath($artifactsRoot)
    if (-not $fullPath.StartsWith($resolvedArtifacts, [System.StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $fullPath)) {
        return
    }

    Write-Output "Removing old build: $fullPath"
    Remove-Item -LiteralPath $fullPath -Recurse -Force
}

if ($CleanAll -and (Test-Path -LiteralPath $artifactsRoot)) {
    Write-Output "CleanAll: removing entire artifacts directory: $artifactsRoot"
    Remove-Item -LiteralPath $artifactsRoot -Recurse -Force
}

$publishDirectory = Join-Path $artifactsRoot "win-x64\$Version"
$installerDirectory = Join-Path $artifactsRoot "installer\$Version"

Get-OldVersionDirectories -Root (Join-Path $artifactsRoot 'win-x64') |
    Sort-Object { [version]$_.Name } -Descending |
    Select-Object -Skip $KeepLatest |
    ForEach-Object { Remove-OwnBuildDirectory -Path $_.FullName }

Get-OldVersionDirectories -Root (Join-Path $artifactsRoot 'installer') |
    Sort-Object { [version]$_.Name } -Descending |
    Select-Object -Skip $KeepLatest |
    ForEach-Object { Remove-OwnBuildDirectory -Path $_.FullName }

$oauthConfigPath = Get-OAuthConfigPath

Write-Output "Publishing AutoSaveGame $Version to $publishDirectory ..."
& dotnet publish (Join-Path $repositoryRoot 'src\AutoSaveGame.App\AutoSaveGame.App.csproj') `
    -c Release -r win-x64 --self-contained true `
    -p:AutoSaveGameOAuthConfig="$oauthConfigPath" `
    -p:Version="$Version" `
    -o $publishDirectory
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$publishedExecutable = Join-Path $publishDirectory 'AutoSaveGame.exe'
if (-not (Test-Path -LiteralPath $publishedExecutable -PathType Leaf)) {
    throw "Published executable was not found at: $publishedExecutable"
}

if (-not $SkipSmoke) {
    Write-Output "Running smoke test against $publishedExecutable ..."
    & (Join-Path $PSScriptRoot 'SmokeTest.ps1') -Executable $publishedExecutable
}
else {
    Write-Output 'Skipping smoke test (SkipSmoke).'
}

if ($SkipInstaller) {
    Write-Output "Local build complete: $publishedExecutable"
    exit 0
}

Write-Output "Building installer for $Version ..."
$installerOutput = & (Join-Path $PSScriptRoot 'Build-Installer.ps1') `
    -PublishDirectory $publishDirectory `
    -Version $Version `
    -OutputDirectory $installerDirectory `
    -IsccPath $IsccPath
$installerPath = ($installerOutput | Select-Object -Last 1)

if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    throw "Installer was not produced: $installerPath"
}

Write-Output "Installer produced: $installerPath"
Write-Output "Local build complete: $Version"
