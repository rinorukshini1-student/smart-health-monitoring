# Smart Health — Docker

Nis **vue-client + Vue.Api + pipeline** me një komandë.

## Parakushtet

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) i nisur
- `Vue.Api/firebase-service-account.json` (për push notifications)

## Nisja e plotë

```powershell
cd smart-health-monitoring
docker compose up -d --build
```

Hap dashboard-in:

| Shërbimi | URL |
|----------|-----|
| **vue-client** (UI) | **http://localhost:5173** |
| vue-api (REST/SignalR) | http://localhost:5099 |

Në server (p.sh. `178.105.181.143`):

- UI: **http://178.105.181.143:5173**
- API: http://178.105.181.143:5099

## Arkitektura

```
Browser → vue-client (nginx :5173)
              ↓ proxy /api, /healthHub
          vue-api (:5099)
              ↑
          streaming ← simulator
              ↓
          kafka + cassandra
```

## Shërbimet

| Container | Porti | Përshkrimi |
|-----------|-------|------------|
| `vue-client` | **5173** | Vue 3 SPA (nginx) |
| `vue-api` | 5099 | .NET API + SignalR hub |
| `streaming` | — | **Apache Spark Structured Streaming** (spark-submit) → Kafka → Cassandra → SignalR |
| `simulator` | — | Gjeneron vitals |
| `kafka` | 9092 | Message broker |
| `cassandra` | 9042 | Database |

## Komanda

```powershell
docker compose logs -f vue-client
docker compose logs -f vue-api
docker compose up -d --build vue-client vue-api
docker compose down
docker compose down -v
```

## Vetëm frontend + API (pa pipeline)

```powershell
docker compose up -d --build zookeeper kafka kafka-init cassandra cassandra-init vue-api vue-client
```

Për të dhëna live, nis edhe `streaming` dhe `simulator`.

## Streaming: Spark vs Direct

Shërbimi `streaming` ekzekutohet si **Apache Spark real** përmes `spark-submit` (JVM bridge i
Microsoft.Spark / .NET for Apache Spark). Imazhi përmban Java 11, Apache Spark 3.2.1 dhe konektorin
`spark-sql-kafka-0-10`.

| Variabël | Vlera | Përshkrimi |
|----------|-------|------------|
| `STREAMING_MODE` | `spark` (default) | Spark Structured Streaming me `spark-submit` |
| `STREAMING_MODE` | `direct` | Fallback i lehtë: konsumues Confluent Kafka pa JVM |
| `SPARK_MASTER` | `local[*]` | Master i Spark (local me të gjitha bërthamat) |
| `Spark__CheckpointDir` | `/tmp/spark-checkpoints` | Checkpoint për offset-et e Kafka (exactly-once) |

Spark dritaret rrëshqitëse (5 min, slide 1 min) dhe filtrimi ekzekutohen brenda Spark; rezultatet
ruhen në Cassandra (`vitals_window_agg`). Ndryshimi i modës:

```powershell
# Përkohësisht në direct mode
docker compose run -e STREAMING_MODE=direct streaming
```

> Shënim: ndërtimi i parë i imazhit `streaming` shkarkon Apache Spark + jar-et e Kafka (~300 MB),
> prandaj kërkon internet dhe pak më shumë kohë.

## Build manual (pa Docker)

```powershell
cd vue-client
npm run build:docker    # → dist/
npm run build           # → Vue.Api/wwwroot
```
