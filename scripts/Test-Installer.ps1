param(
    [Parameter(Mandatory = $true)]
    [string]$Installer,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedVersion,

    [switch]$UseDefaultInstallPath
)

$ErrorActionPreference = 'Stop'
$resolvedInstaller = (Resolve-Path -LiteralPath $Installer).Path
$tempRoot = [System.IO.Path]::GetFullPath($env:TEMP)
$testRoot = Join-Path $tempRoot ('AutoSaveGame-InstallerTest-' + [Guid]::NewGuid().ToString('N'))
$installPath = if ($UseDefaultInstallPath) {
    Join-Path $env:LOCALAPPDATA 'Programs\AutoSaveGame'
} else {
    Join-Path $testRoot 'installed'
}

try {
    [System.IO.Directory]::CreateDirectory($testRoot) | Out-Null
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
    if (-not $UseDefaultInstallPath) {
        $arguments += '/NOICONS'
        $arguments += "/DIR=$installPath"
    }

    $install = Start-Process -FilePath $resolvedInstaller `
        -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
    if ($install.ExitCode -ne 0) {
        throw "Installer exited with code $($install.ExitCode)."
    }

    $executable = Join-Path $installPath 'AutoSaveGame.exe'
    if (-not (Test-Path -LiteralPath $executable)) {
        throw "Installed executable was not found: $executable"
    }

    $fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($executable).FileVersion
    if (-not $fileVersion.StartsWith($ExpectedVersion, [StringComparison]::Ordinal)) {
        throw "Expected installed version $ExpectedVersion but found $fileVersion."
    }

    $smokeScript = Join-Path $PSScriptRoot 'SmokeTest.ps1'
    & powershell -NoProfile -ExecutionPolicy Bypass -File $smokeScript -Executable $executable
    if ($LASTEXITCODE -ne 0) {
        throw "Installed executable smoke test exited with code $LASTEXITCODE."
    }

    $uninstaller = Join-Path $installPath 'unins000.exe'
    if (-not (Test-Path -LiteralPath $uninstaller)) {
        throw "Uninstaller was not found: $uninstaller"
    }

    $uninstall = Start-Process -FilePath $uninstaller `
        -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') `
        -Wait -PassThru -WindowStyle Hidden
    if ($uninstall.ExitCode -ne 0) {
        throw "Uninstaller exited with code $($uninstall.ExitCode)."
    }

    if (Test-Path -LiteralPath $executable) {
        throw "Uninstall left the application executable behind: $executable"
    }

    Write-Output 'PASS'
}
finally {
    if (-not $UseDefaultInstallPath) {
        $resolvedTestRoot = [System.IO.Path]::GetFullPath($testRoot)
        $leaf = Split-Path -Leaf $resolvedTestRoot
        if ($resolvedTestRoot.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and
            $leaf.StartsWith('AutoSaveGame-InstallerTest-', [StringComparison]::Ordinal)) {
            Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
