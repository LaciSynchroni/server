param(
    # Type
    [switch]$Standalone,
    [switch]$Sharded,
    # Mode
    $Mode
)

function Import-DotEnv {
    param(
        [string]$Path = ".env"
    )

    if (-not (Test-Path $Path)) {
        Write-Warning "Env file not found: $Path"
        return
    }

    Get-Content $Path | ForEach-Object {
        if ($_ -match '^\s*$' -or $_.TrimStart().StartsWith('#')) {
            return # skip empty or comment lines
        }

        $idx = $_.IndexOf('=')
        if ($idx -gt -1) {
            $name  = $_.Substring(0, $idx).Trim()
            $value = $_.Substring($idx + 1).Trim()

            # Remove wrapping quotes if present
            if (($value.StartsWith('"') -and $value.EndsWith('"')) -or
                ($value.StartsWith("'") -and $value.EndsWith("'"))) {
                $value = $value.Substring(1, $value.Length - 2)
            }

            Set-Content "env:\$name" $value
            Write-Host "+ $name"
        }
    }
}

Import-DotEnv "./compose/.env.local"

if (!(Test-Path config/standalone/base.appsettings.json ))
{
    Write-Error "Base appsettings not found!"
    exit 1
}
if (!(Test-Path config/standalone/authservice.appsettings.json ))
{
    Write-Error "Auth service appsettings not found!"
    exit 1
}
if (!(Test-Path config/standalone/files.appsettings.json ))
{
    Write-Error "Files appsettings not found!"
    exit 1
}
if (!(Test-Path config/standalone/server.appsettings.json ))
{
    Write-Error "Server appsettings not found!"
    exit 1
}
if (!(Test-Path config/standalone/services.appsettings.json ))
{
    Write-Error "Services appsettings not found!"
    exit 1
}

if ($Standalone -and $Sharded) {
    Write-Error "You cannot use -Standalone and -Sharded together."
    exit 1
}

if (-not $Standalone -and -not $Sharded) {
    Write-Error "You must specify either -Standalone or -Sharded."
    exit 1
}

if ($Mode -eq "start") {
    if ($Standalone) {
        Write-Host "Starting in Standalone mode..."
        docker compose -f "$PSScriptRoot/compose/standalone.yml" -p standalone up -d
    }
    elseif ($Sharded) {
        Write-Host "Starting in Sharded mode..."
        docker compose -f "$PSScriptRoot/compose/sharded.yml" -p sharded up -d
    }
}
elseif ($Mode -eq "stop") {
    if ($Standalone) {
        Write-Host "Stopping Standalone service..."
        docker compose -f "$PSScriptRoot/compose/standalone.yml" -p standalone stop
    }
    elseif ($Sharded) {
        Write-Host "Stopping Sharded service..."
        docker compose -f "$PSScriptRoot/compose/sharded.yml" -p sharded stop
    }
}
else {
    # neither Start nor Stop supplied
    if ($Standalone) {
        Write-Host "Running other Standalone action..."
        docker compose -f "$PSScriptRoot/compose/standalone.yml" -p standalone up
    }
    elseif ($Sharded) {
        Write-Host "Running other Sharded action..."
        docker compose -f "$PSScriptRoot/compose/sharded.yml" -p sharded up
    }
}
