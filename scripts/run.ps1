# Starts the infrastructure (Docker) and the three services on the host.
#   ./scripts/run.ps1            # infra + build + services
#   ./scripts/run.ps1 -NoInfra   # services only (infra already running)
# Ctrl+C stops the services; the containers keep running (docker compose -f infra/docker-compose.yml down).
param([switch]$NoInfra)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

if (-not $NoInfra) {
    docker compose -f "$root/infra/docker-compose.yml" up -d --wait
    if ($LASTEXITCODE -ne 0) { throw 'docker compose failed' }
}

dotnet build "$root/Atlas.slnx"
if ($LASTEXITCODE -ne 0) { throw 'build failed' }

$projects = @(
    'src/ProviderStubs/Atlas.ProviderStubs',      # http://127.0.0.1:5300
    'src/Onboarding/Atlas.Onboarding.Api',        # http://127.0.0.1:5100
    'src/Accounts/Atlas.Accounts.Api'             # http://127.0.0.1:5200
)

$processes = foreach ($project in $projects) {
    Start-Process dotnet -ArgumentList @('run', '--no-build', '--project', "`"$root/$project`"") -NoNewWindow -PassThru
}

Write-Host "Onboarding http://127.0.0.1:5100 | Accounts http://127.0.0.1:5200 | Stubs http://127.0.0.1:5300 | Seq http://127.0.0.1:5341 | RabbitMQ http://127.0.0.1:15672"
try {
    Wait-Process -Id $processes.Id
}
finally {
    $processes | Stop-Process -Force -ErrorAction SilentlyContinue
}
