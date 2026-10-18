#!/usr/bin/env bash
# Starts the infrastructure (Docker) and the three services on the host.
#   ./scripts/run.sh            # infra + build + services
#   ./scripts/run.sh --no-infra # services only (infra already running)
# Ctrl+C stops the services; the containers keep running (docker compose -f infra/docker-compose.yml down).
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"

if [[ "${1:-}" != "--no-infra" ]]; then
  docker compose -f "$root/infra/docker-compose.yml" up -d --wait
fi

dotnet build "$root/Atlas.slnx"

pids=()
for project in src/ProviderStubs/Atlas.ProviderStubs src/Onboarding/Atlas.Onboarding.Api src/Accounts/Atlas.Accounts.Api; do
  dotnet run --no-build --project "$root/$project" &
  pids+=($!)
done

trap 'kill "${pids[@]}" 2>/dev/null' INT TERM EXIT
echo "Onboarding http://127.0.0.1:5100 | Accounts http://127.0.0.1:5200 | Stubs http://127.0.0.1:5300 | Seq http://127.0.0.1:5341 | RabbitMQ http://127.0.0.1:15672"
wait
