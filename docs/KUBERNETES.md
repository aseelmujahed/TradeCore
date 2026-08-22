# Kubernetes deployment

TradeCore has Kubernetes manifests for a **local development/demo cluster**. This is not a production-ready deployment. The manifest set preserves the existing API, PostgreSQL, RabbitMQ, migration-job, and worker architecture; it does not make the system horizontally scalable.

**## Current validation status

The Kubernetes deployment has been manually validated on Docker Desktop Kubernetes using the docker-desktop context.

Validated successfully:

Kubernetes manifests render and pass kubectl apply --dry-run=server -k .\k8s.

The migration Job completed successfully and applied EF Core migrations and stock seed data.

PostgreSQL, RabbitMQ, the API, and the Trading Engine reached a stable 1/1 Running state with 0 restarts.

GET /health returned Healthy through the API port-forward.

A real sell/buy trade completed end-to-end through API -> RabbitMQ -> Trading Engine -> PostgreSQL, with both orders reaching Filled.

SignalR was verified with a real client receiving both TradeExecuted and StockPriceUpdated.

API and Trading Engine pod self-healing was verified by deleting their pods and confirming automatic recreation and recovery.

PostgreSQL persistence was verified by deleting postgres-0 and confirming the same PVC remained bound and persisted data remained available.

RabbitMQ outage recovery was verified by scaling the broker to zero, submitting an order, observing a pending API outbox row, restoring RabbitMQ, and confirming automatic publication/processing recovery.

Retry/dead-letter behavior was verified with a valid submitted-order message referencing a nonexistent order: attempts 1, 2, and 3 occurred, then the message was moved to the dead-letter queue.

PostgreSQL and RabbitMQ probe configuration was hardened for Docker Desktop Kubernetes by replacing heavyweight exec probes with TCP socket probes.

The deployment is therefore validated for its intended local development/demo use case.

Layout and design**

`k8s/` contains a namespace, non-secret ConfigMap, example Secret, PostgreSQL StatefulSet and headless Service, RabbitMQ StatefulSet and Service, migration Job, API Deployment and Service, Trading Engine Deployment, and `kustomization.yaml` for offline rendering/validation.

All resources use namespace `tradecore`. Application pods resolve the database as `postgres` and the broker as `rabbitmq` through Services; no pod-to-pod `localhost` address is used.

`tradecore-config` contains the exact .NET configuration environment variables for the selected local behavior: `ASPNETCORE_ENVIRONMENT=Development`, `RabbitMq__*` topology values, and retry configuration. Development is intentional so Swagger/OpenAPI is available locally; it is not a production assertion. The database connection string and both sets of credentials remain in the `tradecore-credentials` Secret.

The API image is intentionally also the migration image. `tradecore-migrations` sets `Database__MigrateOnStartup=true` and `Database__ExitAfterMigration=true`, invoking the existing EF Core migration and stock-seeding behavior without duplicating it in a script. Its `backoffLimit` is 3.

PostgreSQL uses `postgres:17-alpine`, a one-replica StatefulSet, a 2 GiB `ReadWriteOnce` claim, and `pg_isready` startup/readiness/liveness probes. RabbitMQ uses `rabbitmq:4-management`, a one-replica StatefulSet, a 2 GiB `ReadWriteOnce` claim, AMQP port 5672, management port 15672, and RabbitMQ-native diagnostics. Its readiness probe verifies that the AMQP listener is accepting connections; liveness only checks the broker process.

The API has `/health` startup, readiness, and liveness probes. This endpoint is deliberately a process-health signal, not a RabbitMQ dependency gate: API outbox publishing and event consumption already reconnect after broker outages. The worker has no HTTP endpoint, so it deliberately has no synthetic probe. Its process remains running while its existing RabbitMQ reconnect loop waits for the broker.

Both the API and worker use `replicas: 1` and a `Recreate` strategy. The worker's per-stock `SemaphoreSlim` lock is process-local, so simultaneous worker pods are unsafe. The API also has in-memory SignalR notification deduplication, so preventing temporary overlap avoids duplicate notifications. Neither workload has an HPA. Services are only created for PostgreSQL, RabbitMQ, and the API; the worker accepts no inbound connections.

Current local resource settings are: PostgreSQL 100m/128Mi request and 500m/256Mi limit; RabbitMQ 200m/512Mi request and 1 CPU/2Gi limit; API and worker each 100m/128Mi request and 500m/512Mi limit. Workloads do not need Kubernetes API access and disable service-account-token mounting.

## Prerequisites and images

Require a running Kubernetes cluster with a default dynamic `StorageClass`, `kubectl`, and Docker. First inspect the actual runtime:

```powershell

kubectl config current-context

kubectl cluster-info

kubectl get nodes

kubectl get storageclass

docker context show

```

For Docker Desktop Kubernetes, build the explicit local tags with the same Docker daemon used by the enabled Kubernetes cluster:

```powershell

docker build -f TradeCore.Api/Dockerfile -t tradecore-api:local .

docker build -f TradeCore.TradingEngine/Dockerfile -t tradecore-trading-engine:local .

docker image inspect tradecore-api:local tradecore-trading-engine:local

```

The manifests use `imagePullPolicy: IfNotPresent`, so a Docker Desktop cluster that shares this Docker daemon uses those images without an image registry. Verify that assumption on the target cluster before deployment: if its runtime cannot see the images, load them by that runtime's supported method or change the image references to a registry that it can pull from. Do not claim local image sharing merely because Docker is installed.

## Secret creation

Never apply `k8s/secret.example.yaml` unchanged. Either copy it to ignored `k8s/secret.local.yaml`, replace all placeholders, and apply it, or use PowerShell variables that are not committed:

```powershell

$pgUser = Read-Host 'PostgreSQL user'

$pgPassword = Read-Host 'PostgreSQL password' -AsSecureString

$rabbitUser = Read-Host 'RabbitMQ user'

$rabbitPassword = Read-Host 'RabbitMQ password' -AsSecureString

$pgPasswordText = [System.Net.NetworkCredential]::new('', $pgPassword).Password

$rabbitPasswordText = [System.Net.NetworkCredential]::new('', $rabbitPassword).Password

$connection = "Host=postgres;Port=5432;Database=tradecore;Username=$pgUser;Password=$pgPasswordText"

kubectl create namespace tradecore

kubectl -n tradecore create secret generic tradecore-credentials --from-literal=postgres-user=$pgUser --from-literal=postgres-password=$pgPasswordText --from-literal=rabbitmq-user=$rabbitUser --from-literal=rabbitmq-password=$rabbitPasswordText --from-literal=connection-string=$connection

```

Use a password manager or your normal local-secret workflow instead if it is preferable. Do not paste the resulting Secret into source control.

## Deploy in dependency order

Kubernetes Services/DNS and readiness replace Compose `depends_on`; the migration Job must be created only after PostgreSQL is ready. Apply the namespace once, then the common config. If the namespace was already created during secret creation, the first command is harmless.

```powershell

kubectl apply -f k8s/namespace.yaml

kubectl apply -f k8s/configmap.yaml -n tradecore

kubectl apply -f k8s/postgres/ -n tradecore

kubectl apply -f k8s/rabbitmq/ -n tradecore

kubectl rollout status statefulset/postgres -n tradecore --timeout=5m

kubectl rollout status statefulset/rabbitmq -n tradecore --timeout=8m

kubectl apply -f k8s/migrations/job.yaml -n tradecore

kubectl wait --for=condition=complete job/tradecore-migrations -n tradecore --timeout=5m

kubectl apply -f k8s/api/ -n tradecore

kubectl apply -f k8s/trading-engine/ -n tradecore

kubectl rollout status deployment/tradecore-api -n tradecore --timeout=5m

kubectl rollout status deployment/tradecore-trading-engine -n tradecore --timeout=5m

```

For a deliberate migration re-run, inspect its logs, delete the completed Job, then apply it again:

```powershell

kubectl logs job/tradecore-migrations -n tradecore

kubectl delete job tradecore-migrations -n tradecore

kubectl apply -f k8s/migrations/job.yaml -n tradecore

```

## Access and routine inspection

```powershell

kubectl port-forward service/tradecore-api 5021:8080 -n tradecore

kubectl port-forward service/rabbitmq 15672:15672 -n tradecore

```

Then visit `http://localhost:5021/health`, `http://localhost:5021/swagger`, and `http://localhost:15672`. The management login uses the RabbitMQ values stored in the Secret.

```powershell

kubectl get all,pvc -n tradecore

kubectl logs -f deployment/tradecore-api -n tradecore

kubectl logs -f deployment/tradecore-trading-engine -n tradecore

kubectl logs statefulset/postgres -n tradecore

kubectl logs statefulset/rabbitmq -n tradecore

kubectl describe pod <pod-name> -n tradecore

kubectl get events -n tradecore --sort-by=.lastTimestamp

kubectl exec -it statefulset/postgres -n tradecore -- psql -U <postgres-user> -d tradecore

```

## Required verification before calling this tested

After deployment, record the actual output of the following checks.

1. Confirm the migration Job is `Complete`, all pods are ready/running, both claims are `Bound`, and the API health endpoint responds through the port-forward.

2. Use the real API to create users/accounts, deposit funds, arrange a seller portfolio with the supported portfolio-transfer workflow, submit matching sell/buy orders, then verify one persisted trade, settlement balances, portfolio positions, updated stock price, both Trading Engine outbox records, RabbitMQ publication, and API consumption.

3. Connect a real SignalR client to `http://localhost:5021/hubs/trading` before submitting the matching orders. Confirm it receives both `TradeExecuted` and `StockPriceUpdated`; broker publication alone is not sufficient.

4. Delete the API and worker pods one at a time. Confirm their one-replica Deployments recreate ready pods and that the worker resumes consumption without a manual restart.

5. Create data through the API, delete `postgres-0`, wait for replacement, and confirm `data-postgres-0` remains `Bound`, the database starts, and previously created data remains.

6. Delete `rabbitmq-0`, verify its claim remains `Bound`, the AMQP listener becomes ready, durable topology is restored/redeclared, and both long-running application processes reconnect without manual restarts.

7. Scale RabbitMQ to zero, submit an order, confirm the API returns `201` and an API outbox row stays pending, then restore one replica and confirm publication and worker processing resume. Restore the broker to one replica even if the test fails.

```powershell

kubectl delete pod -l app.kubernetes.io/name=tradecore-api -n tradecore

kubectl delete pod -l app.kubernetes.io/name=tradecore-trading-engine -n tradecore

kubectl delete pod postgres-0 -n tradecore

kubectl delete pod rabbitmq-0 -n tradecore

kubectl scale statefulset rabbitmq --replicas=0 -n tradecore

kubectl scale statefulset rabbitmq --replicas=1 -n tradecore

```

The existing automated suite covers duplicate-order idempotency. Pair it with one Kubernetes duplicate-delivery sanity check and confirm no duplicate trade, settlement, position, or Trading Engine outbox record is created.

## Manifest validation and cleanup

Render all committed resources without a cluster:

```powershell

kubectl kustomize k8s

kubectl apply --dry-run=client --validate=false -k k8s

```

The `kustomization.yaml` intentionally omits the Secret template: placeholders are not deployable credentials. Server-side dry-run confirms that the running cluster accepts the rendered resources, but it does not replace runtime verification of storage, image availability, connectivity, recovery, or business behavior.

To remove the stack and, if desired, the persistent local data:

```powershell

kubectl delete -k k8s

kubectl delete namespace tradecore

```

Deleting the namespace deletes its PVCs according to the storage provisioner's reclaim policy, so export data first if it matters.