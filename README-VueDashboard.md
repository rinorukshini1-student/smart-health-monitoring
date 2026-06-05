# Smart Health Monitoring — Vue.js Dashboard

A **complete, self-contained** rewrite of the dashboard in **Vue 3**, kept fully separate from the
existing Razor `Web.Dashboard` project. Patient telemetry **starts generating automatically the
moment the backend starts** — no Kafka, Spark, or Cassandra are required to run it.

```
vue-client (Vue 3 SPA)  ──REST + SignalR──▶  Vue.Api (.NET 8)
                                                 │
                          DataGeneratorService (auto-start) ── in-memory pipeline:
                          vitals → alert rules → AI risk → 5-min windows → ML.NET heart-attack model
```

## Projects

| Folder        | What it is                                                                                  |
| ------------- | ------------------------------------------------------------------------------------------- |
| `Vue.Api/`    | .NET 8 backend: REST API + SignalR hub + auto-starting in-memory data generator + ML model. |
| `vue-client/` | Vue 3 SPA (Composition API, Pinia, Vue Router, ApexCharts).                                 |

The original `Web.Dashboard`, `Simulator.App`, and `Streaming.App` projects are **untouched**.

## Pages (all in Vue)

1. **Overview** — KPI cards, messages/min, alerts/hour, HR & temperature trends, recent events.
2. **Live Monitoring** — auto-updating vitals table with search, status filters, sorting.
3. **Patient Details** — info card, HR/Temp/SpO₂/BP charts, time ranges (hour/24h/7d), min/max/avg, AI risk panel.
4. **Alert Center** — live alert log with severity/type filters, search, counters.
5. **Analytics** — windowed averages, alert frequency, highest-risk leaderboard.
6. **AI Predictions** — heart-attack risk (ML.NET): risk distribution, top factors, high-risk table, prediction history.
7. **System Health** — Kafka / Spark / Cassandra / Backend metrics + performance summary.

## Run it

### Option A — one process (production-style)

The Vue build output is already compiled into `Vue.Api/wwwroot`, so the API serves the SPA directly:

```powershell
dotnet run --project Vue.Api/Vue.Api.csproj
```

Open **http://localhost:5099** — data is already flowing.

To rebuild the SPA after changing Vue code:

```powershell
cd vue-client
npm install      # first time only
npm run build    # outputs to ../Vue.Api/wwwroot
```

### Option B — hot-reload development (two terminals)

```powershell
# Terminal 1 — backend API (port 5099)
dotnet run --project Vue.Api/Vue.Api.csproj

# Terminal 2 — Vite dev server (port 5173, proxies /api and /healthHub to 5099)
cd vue-client
npm install
npm run dev
```

Open **http://localhost:5173**.

## How "data starts on dashboard start" works

`DataGeneratorService` is a hosted `BackgroundService` registered in `Program.cs`. On startup it:

1. **Backfills ~20 minutes of history** for 10 patients so charts are populated immediately.
2. Every 2 s, generates realistic vitals (with random critical spikes), runs the **alert rules**,
   the **rule-based AI risk score**, and **5-minute window aggregation**.
3. Every ~14 s, runs the **ML.NET heart-attack model** (`AiModels/heart_attack_model.zip`) per patient.
4. Pushes everything to the SPA over **SignalR** (`vitalsReceived`, `alertReceived`, `riskReceived`,
   `metricsReceived`, `mlPredictionReceived`).

## Notes

- The build toolchain here runs Node 16, so `vue-client` is pinned to **Vite 4** / **ApexCharts 3**.
- The in-memory store keeps ~3 h of readings and ~24 h of alerts; it resets when the backend restarts.
- The heart-attack dataset is statistically weak (AUC ≈ 0.50), so the model is paired with an
  explainable rule-based score and per-patient risk factors for a meaningful demo.
```
