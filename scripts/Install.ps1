[CmdletBinding(DefaultParameterSetName = 'Install')]
param(
    [Parameter(ParameterSetName = 'Install')]
    [ValidatePattern('^[^/\s]+/[^/\s]+$')]
    [string]$Repository = 'SeikoP/AutoSaveGame',

    [Parameter(ParameterSetName = 'Install')]
    [string]$Version,

    [Parameter(ParameterSetName = 'Install')]
    [switch]$NoLaunch,

    [Parameter(Mandatory, ParameterSetName = 'Verify')]
    [switch]$VerifyOnly,

    [Parameter(Mandatory, ParameterSetName = 'Verify')]
    [ValidateNotNullOrEmpty()]
    [string]$InstallerPath,

    [Parameter(Mandatory, ParameterSetName = 'Verify')]
    [ValidateNotNullOrEmpty()]
    [string]$ChecksumPath
)

$ErrorActionPreference = 'Stop'
$installerAssetName = 'AutoSaveGame-Setup.exe'
$checksumAssetName = 'SHA256SUMS.txt'
$temporaryDirectory = $null

function Get-FileSha256 {
    param([Parameter(Mandatory)][string]$Path)

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $sha256 = [System.Security.Cryptography.SHA256]::Create()
        try {
            return ([BitConverter]::ToString($sha256.ComputeHash($stream))).Replace('-', '').ToUpperInvariant()
        }
        finally {
            $sha256.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Test-FixedTimeTextEqual {
    param(
        [Parameter(Mandatory)][string]$Left,
        [Parameter(Mandatory)][string]$Right
    )

    $leftBytes = [System.Text.Encoding]::ASCII.GetBytes($Left)
    $rightBytes = [System.Text.Encoding]::ASCII.GetBytes($Right)
    if ($leftBytes.Length -ne $rightBytes.Length) {
        return $false
    }

    $difference = 0
    for ($index = 0; $index -lt $leftBytes.Length; $index++) {
        $difference = $difference -bor ($leftBytes[$index] -bxor $rightBytes[$index])
    }

    return $difference -eq 0
}

function Assert-InstallerChecksum {
    param(
        [Parameter(Mandatory)][string]$Installer,
        [Parameter(Mandatory)][string]$ChecksumFile
    )

    if (-not (Test-Path -LiteralPath $Installer -PathType Leaf)) {
        throw "Installer was not found: $Installer"
    }
    if (-not (Test-Path -LiteralPath $ChecksumFile -PathType Leaf)) {
        throw "Checksum file was not found: $ChecksumFile"
    }

    $escapedName = [Regex]::Escape([System.IO.Path]::GetFileName($Installer))
    $checksumText = [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $ChecksumFile).Path)
    $matches = [Regex]::Matches($checksumText, "(?im)^([a-f0-9]{64})\s+\*?$escapedName\s*$")
    if ($matches.Count -ne 1) {
        throw "Checksum file must contain exactly one SHA-256 entry for $([System.IO.Path]::GetFileName($Installer))."
    }

    $expected = $matches[0].Groups[1].Value.ToUpperInvariant()
    $actual = Get-FileSha256 -Path (Resolve-Path -LiteralPath $Installer).Path
    if (-not (Test-FixedTimeTextEqual -Left $expected -Right $actual)) {
        throw "SHA-256 verification failed for $([System.IO.Path]::GetFileName($Installer))."
    }

    Write-Output "Verified SHA-256: $actual"
}

function Remove-OwnTemporaryDirectory {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) {
        return
    }

    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    $leaf = [System.IO.Path]::GetFileName($resolvedPath)
    if (-not $resolvedPath.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
        -not $leaf.StartsWith('AutoSaveGame-Install-', [System.StringComparison]::Ordinal)) {
        throw "Refusing to remove an unexpected temporary directory: $resolvedPath"
    }

    Remove-Item -LiteralPath $resolvedPath -Recurse -Force
}

try {
    if ($VerifyOnly) {
        Assert-InstallerChecksum -Installer $InstallerPath -ChecksumFile $ChecksumPath
        return
    }

    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $headers = @{
        Accept = 'application/vnd.github+json'
        'User-Agent' = 'AutoSaveGame-Installer'
        'X-GitHub-Api-Version' = '2022-11-28'
    }

    if ([string]::IsNullOrWhiteSpace($Version)) {
        $releaseUri = "https://api.github.com/repos/$Repository/releases/latest"
    }
    else {
        $tag = if ($Version.StartsWith('v', [System.StringComparison]::OrdinalIgnoreCase)) { $Version } else { "v$Version" }
        $releaseUri = "https://api.github.com/repos/$Repository/releases/tags/$tag"
    }

    Write-Output "Resolving release from $Repository..."
    $release = Invoke-RestMethod -Uri $releaseUri -Headers $headers -TimeoutSec 30
    if ($release.draft -or ([string]::IsNullOrWhiteSpace($Version) -and $release.prerelease)) {
        throw "The selected release is not a stable published release."
    }

    $installerAssets = @($release.assets | Where-Object { $_.name -ceq $installerAssetName })
    $checksumAssets = @($release.assets | Where-Object { $_.name -ceq $checksumAssetName })
    if ($installerAssets.Count -ne 1 -or $checksumAssets.Count -ne 1) {
        throw "Release $($release.tag_name) must contain exactly one $installerAssetName and one $checksumAssetName."
    }

    $temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("AutoSaveGame-Install-{0}" -f [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $temporaryDirectory -Force | Out-Null
    $downloadedInstaller = Join-Path $temporaryDirectory $installerAssetName
    $downloadedChecksums = Join-Path $temporaryDirectory $checksumAssetName

    Write-Output "Downloading AutoSaveGame $($release.tag_name)..."
    Invoke-WebRequest -Uri $installerAssets[0].browser_download_url -Headers $headers -OutFile $downloadedInstaller -UseBasicParsing -TimeoutSec 120
    Invoke-WebRequest -Uri $checksumAssets[0].browser_download_url -Headers $headers -OutFile $downloadedChecksums -UseBasicParsing -TimeoutSec 30
    Assert-InstallerChecksum -Installer $downloadedInstaller -ChecksumFile $downloadedChecksums

    Write-Output 'Installing AutoSaveGame for the current Windows user...'
    $process = Start-Process -FilePath $downloadedInstaller `
        -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') `
        -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        throw "AutoSaveGame setup failed with exit code $($process.ExitCode)."
    }

    $installedExecutable = Join-Path $env:LOCALAPPDATA 'Programs\AutoSaveGame\AutoSaveGame.exe'
    if (-not (Test-Path -LiteralPath $installedExecutable -PathType Leaf)) {
        throw "Setup completed but the application was not found at $installedExecutable."
    }

    Write-Output "Installed: $installedExecutable"
    if (-not $NoLaunch) {
        Start-Process -FilePath $installedExecutable | Out-Null
    }
}
finally {
    Remove-OwnTemporaryDirectory -Path $temporaryDirectory
}
