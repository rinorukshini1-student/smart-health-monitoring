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
| `streaming` | — | Kafka → Cassandra → SignalR |
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

## Build manual (pa Docker)

```powershell
cd vue-client
npm run build:docker    # → dist/
npm run build           # → Vue.Api/wwwroot
```
