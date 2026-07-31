param(
    [Parameter(Mandatory = $true)]
    [string] $Executable
)

$ErrorActionPreference = 'Stop'
$resolvedExecutable = (Resolve-Path -LiteralPath $Executable).Path
$tempRoot = [System.IO.Path]::GetFullPath($env:TEMP)
$smokeRoot = Join-Path $tempRoot ('AutoSaveGame-Smoke-' + [Guid]::NewGuid().ToString('N'))

try {
    New-Item -ItemType Directory -Path $smokeRoot | Out-Null
    $process = Start-Process -FilePath $resolvedExecutable -ArgumentList @('--smoke-test', $smokeRoot) -Wait -PassThru -WindowStyle Hidden

    if ($process.ExitCode -ne 0) {
        throw "Smoke executable exited with code $($process.ExitCode)."
    }

    $resultPath = Join-Path $smokeRoot 'smoke-result.txt'
    $savePath = Join-Path $smokeRoot 'save\slot.dat'
    if ((Get-Content -Raw -LiteralPath $resultPath) -ne 'PASS') {
        throw 'Smoke result marker is not PASS.'
    }

    if ((Get-Content -Raw -LiteralPath $savePath) -ne 'smoke-save-v1') {
        throw 'Restored smoke save does not match.'
    }

    $expectedHash = Get-Content -Raw -LiteralPath (Join-Path $smokeRoot 'expected-save.sha256')
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    $saveStream = [System.IO.File]::OpenRead($savePath)
    try {
        $restoredHash = -join ($sha256.ComputeHash($saveStream) | ForEach-Object { $_.ToString('X2') })
    }
    finally {
        $saveStream.Dispose()
        $sha256.Dispose()
    }

    if ($restoredHash -ne $expectedHash) {
        throw 'Restored smoke save SHA-256 does not match.'
    }

    Write-Output 'PASS'
}
finally {
    $resolvedSmokeRoot = [System.IO.Path]::GetFullPath($smokeRoot)
    $smokeLeaf = Split-Path -Leaf $resolvedSmokeRoot
    if ($resolvedSmokeRoot.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and $smokeLeaf.StartsWith('AutoSaveGame-Smoke-', [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedSmokeRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
