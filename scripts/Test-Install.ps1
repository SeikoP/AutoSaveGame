[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$installScript = Join-Path $PSScriptRoot 'Install.ps1'

if (-not (Test-Path -LiteralPath $installScript -PathType Leaf)) {
    throw "Install script was not found: $installScript"
}

$testDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("AutoSaveGame-InstallTest-{0}" -f [Guid]::NewGuid().ToString('N'))

function Get-Sha256 {
    param([Parameter(Mandatory)][string]$Path)

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $sha256 = [System.Security.Cryptography.SHA256]::Create()
        try {
            return ([BitConverter]::ToString($sha256.ComputeHash($stream))).Replace('-', '')
        }
        finally {
            $sha256.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

try {
    New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null
    $installerPath = Join-Path $testDirectory 'AutoSaveGame-Setup.exe'
    $checksumPath = Join-Path $testDirectory 'SHA256SUMS.txt'

    [System.IO.File]::WriteAllBytes($installerPath, [byte[]](1, 2, 3, 4, 5))
    $expectedHash = Get-Sha256 -Path $installerPath
    [System.IO.File]::WriteAllText($checksumPath, "$expectedHash  AutoSaveGame-Setup.exe`r`n", [System.Text.Encoding]::ASCII)

    & powershell -NoProfile -ExecutionPolicy Bypass -File $installScript `
        -VerifyOnly -InstallerPath $installerPath -ChecksumPath $checksumPath
    if ($LASTEXITCODE -ne 0) {
        throw "Matching checksum was rejected with exit code $LASTEXITCODE."
    }

    [System.IO.File]::WriteAllBytes($installerPath, [byte[]](1, 2, 3, 4, 6))
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & powershell -NoProfile -ExecutionPolicy Bypass -File $installScript `
        -VerifyOnly -InstallerPath $installerPath -ChecksumPath $checksumPath 2>$null
    $mismatchExitCode = $LASTEXITCODE
    $ErrorActionPreference = $previousErrorActionPreference
    if ($mismatchExitCode -eq 0) {
        throw 'A mismatched checksum was accepted.'
    }

    Write-Output 'PASS'
}
finally {
    if (Test-Path -LiteralPath $testDirectory) {
        $resolvedTestDirectory = [System.IO.Path]::GetFullPath($testDirectory)
        $resolvedTempDirectory = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
        if ($resolvedTestDirectory.StartsWith($resolvedTempDirectory, [System.StringComparison]::OrdinalIgnoreCase) -and
            [System.IO.Path]::GetFileName($resolvedTestDirectory).StartsWith('AutoSaveGame-InstallTest-', [System.StringComparison]::Ordinal)) {
            Remove-Item -LiteralPath $resolvedTestDirectory -Recurse -Force
        }
    }
}
