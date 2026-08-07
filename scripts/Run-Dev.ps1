[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$envFile = Join-Path $repositoryRoot '.env'
$legacyEnvFile = Join-Path $repositoryRoot 'env'
$project = Join-Path $repositoryRoot 'src\AutoSaveGame.App\AutoSaveGame.App.csproj'

if (-not (Test-Path -LiteralPath $envFile -PathType Leaf)) {
    if (Test-Path -LiteralPath $legacyEnvFile -PathType Leaf) {
        $envFile = $legacyEnvFile
    }
    else {
        throw "Missing OAuth credentials file: $envFile"
    }
}

$values = @{}
Get-Content -LiteralPath $envFile | ForEach-Object {
    if ($_ -match '^\s*([A-Z0-9_]+)\s*=\s*(.*?)\s*$') {
        $values[$Matches[1]] = $Matches[2].Trim('"', "'")
    }
}

if ([string]::IsNullOrWhiteSpace($values['AUTOSAVEGAME_GOOGLE_CLIENT_ID']) -or
    [string]::IsNullOrWhiteSpace($values['AUTOSAVEGAME_GOOGLE_CLIENT_SECRET'])) {
    throw 'AUTOSAVEGAME_GOOGLE_CLIENT_ID and AUTOSAVEGAME_GOOGLE_CLIENT_SECRET are required in .env'
}

$oauthConfigPath = Join-Path $env:TEMP 'autosavegame-dev-oauth.json'
@{
    clientId = $values['AUTOSAVEGAME_GOOGLE_CLIENT_ID']
    clientSecret = $values['AUTOSAVEGAME_GOOGLE_CLIENT_SECRET']
} | ConvertTo-Json -Compress | Set-Content -LiteralPath $oauthConfigPath -Encoding utf8 -NoNewline

& dotnet watch --project $project run "--property:AutoSaveGameOAuthConfig=$oauthConfigPath"
exit $LASTEXITCODE
