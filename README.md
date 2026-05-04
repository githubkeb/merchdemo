# MerchantDemo Docker Setup

This setup runs all solution services plus RabbitMQ and PostgreSQL with replication:

- `Aggregator` -> http://localhost:8081
- `EventsPublisher` -> http://localhost:8082
- `EventsConsumer` -> http://localhost:8083
- `ResultApi` -> http://localhost:8084
- RabbitMQ UI -> http://localhost:15672 (`guest` / `guest`)
- PostgreSQL primary -> `localhost:5432` (`merchant` / `merchant`, DB: `merchantdemo`)
- PostgreSQL follower replica (delayed) -> `localhost:5433` (apply delay: ~1 minute)

## Prerequisites

- Docker Desktop with Docker Compose support.

## Run

```powershell
cd C:\Src\MerchantDemo
docker compose -f Docker/docker-compose.yml up -d --build
```

## Verify

```powershell
docker compose -f Docker/docker-compose.yml ps
docker compose -f Docker/docker-compose.yml logs --tail 100 aggregator
docker compose -f Docker/docker-compose.yml logs --tail 100 eventspublisher
docker compose -f Docker/docker-compose.yml logs --tail 100 eventsconsumer
docker compose -f Docker/docker-compose.yml logs --tail 100 resultapi
```

```powershell
Invoke-WebRequest http://localhost:8081/ | Select-Object -ExpandProperty Content
Invoke-WebRequest http://localhost:8082/ | Select-Object -ExpandProperty Content
Invoke-WebRequest http://localhost:8083/ | Select-Object -ExpandProperty Content
Invoke-WebRequest http://localhost:8084/ | Select-Object -ExpandProperty Content
```

Expected response from each API (current template): `Hello World!`

### Verify Postgres replication

```powershell
docker compose -f Docker/docker-compose.yml exec postgres psql -U merchant -d merchantdemo -c "SELECT pg_current_wal_lsn();"
docker compose -f Docker/docker-compose.yml exec postgres-replica psql -U merchant -d merchantdemo -c "SELECT pg_is_in_recovery(), now() - pg_last_xact_replay_timestamp() AS replay_lag;"
```

`pg_is_in_recovery` should be `t` on replica, and replay lag should trend around 1 minute after writes.

### Verify logical replication is enabled

```powershell
docker compose -f Docker/docker-compose.yml exec postgres psql -U merchant -d merchantdemo -c "SHOW wal_level;"
docker compose -f Docker/docker-compose.yml exec postgres psql -U merchant -d merchantdemo -c "SELECT pubname FROM pg_publication;"
docker compose -f Docker/docker-compose.yml exec postgres psql -U merchant -d merchantaggregates -c "SELECT pubname FROM pg_publication;"
```

`wal_level` should be `logical`, and publications should include `merchantdemo_all` / `merchantaggregates_all`.

## Stop

```powershell
cd C:\Src\MerchantDemo
docker compose -f Docker/docker-compose.yml down
```

To also remove PostgreSQL data volume:

```powershell
docker compose -f Docker/docker-compose.yml down -v
```

