# Smart Health Monitoring — Struktura e Plotë e Projektit

> Dokument referencë për mbrojtjen e projektit IoT (bazuar në kërkesat e PDF-së).  
> **Përfshin:** Simulator → Kafka → Spark Streaming → Cassandra → Vue.Api → vue-client  
> **Nuk përfshin:** Web.Dashboard (version i vjetër Razor — i zëvendësuar nga Vue)

---

## 1. Çfarë është ky projekt?

**Smart Health Monitoring** është një sistem IoT për monitorimin e vitalëve të pacientëve në spital (dhoma 101–110, 10 pacientë). Sensorët fizikë zëvendësohen nga një **simulator** që gjeneron të dhëna realiste çdo 2 sekonda.

Pipeline-i i plotë:

```
┌─────────────┐    ┌─────────────┐    ┌──────────────────┐    ┌─────────────┐
│ Simulator   │───▶│   Kafka     │───▶│ Streaming.App    │───▶│  Cassandra  │
│ (sensorë)   │    │ health-vitals│    │ (Spark Streaming)│    │  (ruajtje)  │
└─────────────┘    └─────────────┘    └────────┬─────────┘    └──────┬──────┘
                                               │ SignalR live         │ REST read
                                               ▼                      ▼
                                        ┌─────────────┐        ┌─────────────┐
                                        │   Vue.Api   │◀───────│  vue-client │
                                        │  REST+Hub   │        │  (Vue 3 UI) │
                                        └─────────────┘        └─────────────┘
```

---

## 2. Pika e nisjes — nga fillon gjithçka?

### Nisja e sistemit (produksion / demo për mbrojtje)

```powershell
cd smart-health-monitoring
docker compose up -d --build
```

**Rendi logjik i nisjes** (nga `docker-compose.yml`):

| Rendi | Shërbimi | Roli |
|-------|----------|------|
| 1 | `zookeeper` + `kafka` + `kafka-init` | Krijon topic-in `health-vitals` |
| 2 | `cassandra` + `cassandra-init` | Krijon keyspace `smart_health` dhe tabelat |
| 3 | `vue-api` | API + SignalR hub (duhet gati para streaming) |
| 4 | `vue-client` | Ndërfaqja web (nginx :5173) |
| 5 | `streaming` | Konsumon Kafka, përpunon me Spark, shkruan në Cassandra |
| 6 | `simulator` | **Pika e nisjes së të dhënave** — fillon të dërgojë lexime |

### Pika e nisjes së të dhënave (sensorët)

**Skedari:** `Simulator.App/Worker.cs`  
**Container:** `smart-health-simulator`

Ky është **burimi i parë i të dhënave**. Nuk ka sensorë fizikë — PDF-ja lejon simulatorin.

Çfarë bën:

1. Mban listën e 10 pacientëve me profile klinike (mosha, kolesterol, diabet, BMI…)
2. Çdo ~2 sekonda, për çdo pacient thërret `CreateReading()`
3. Gjeneron: HR, SpO₂, temperaturë, tension arterial, frekuencë frymëmarrjeje
4. ~5–8% e leximeve janë **spike kritike** (për demo të alarmeve)
5. Serializon JSON dhe dërgon në Kafka me `Confluent.Kafka` producer

**Ku dërgon të dhënat:**  
→ Topic Kafka: **`health-vitals`**  
→ Bootstrap servers: `kafka:29092` (Docker) / `localhost:9092` (lokal)

**Shembull mesazhi JSON:**

```json
{
  "patientId": "P001",
  "patientName": "Arben Krasniqi",
  "roomNumber": "101",
  "heartRate": 88,
  "spo2": 97,
  "temperature": 36.8,
  "systolicBp": 128,
  "diastolicBp": 82,
  "respiratoryRate": 16,
  "recordedAt": "2026-06-11T10:30:00Z"
}
```

---

## 3. Apache Kafka — ku dhe pse përdoret?

### Ku përdoret

| Komponent | Roli | Skedar |
|-----------|------|--------|
| **Producer** | Dërgon lexime | `Simulator.App/Worker.cs` |
| **Consumer (Spark)** | Lexon stream | `Streaming.App/Program.cs` (Spark `ReadStream`) |
| **Consumer (fallback)** | Lexon direkt | `Streaming.App/Program.cs` (`RunDirectKafkaModeAsync`) |
| **Infrastruktura** | Broker + topic | `docker-compose.yml` (`kafka`, `kafka-init`) |

### Pse Kafka?

- **Decoupling:** simulatori dhe përpunuesi nuk varen njëri nga tjetri
- **Buffering:** të dhënat mbeten edhe nëse streaming ndalon përkohësisht
- **Skalabilitet:** topic me 3 partitions (`kafka-init`)
- **Real-time:** mesazhe të vazhdueshme (stream IoT)

### Konfigurimi

- **Topic:** `health-vitals`
- **Key:** `patientId` (p.sh. `P001`)
- **Value:** JSON i leximit vital

---

## 4. Apache Spark Streaming — ku dhe çfarë bën?

### Ku përdoret

**Skedari kryesor:** `Streaming.App/Program.cs`  
**Container:** `smart-health-streaming`  
**Docker:** `Streaming.App/Dockerfile` + `Streaming.App/docker-entrypoint.sh`  
**Mënyra default:** `STREAMING_MODE=spark` → `spark-submit` me Microsoft.Spark 3.2.1

### Dy mënyra pune

| Mënyra | Si niset | Kur përdoret |
|--------|----------|--------------|
| **Spark** (kryesore) | `spark-submit` + flag `--spark` | Docker, mbrojtje |
| **Direct** (fallback) | Pa `--spark`, Confluent consumer | Dev/test pa JVM |

### Çfarë bën Spark (kërkesa PDF)

#### A) Filtrim / validim

```csharp
.Filter("patientId IS NOT NULL AND heartRate > 0 AND heartRate < 260 AND spo2 > 0 AND spo2 <= 100")
```

Hedh lexime të pavlefshme ose të pamundshme klinikisht.

#### B) Përpunim për çdo lexim (`ProcessReadingAsync`)

Për **çdo** mesazh që kalon filtrin:

1. **Ruajtje** → Cassandra (`patient_vitals`, `patients`)
2. **Live push** → SignalR `PublishVitals` → vue-client
3. **Agregim dritare** → `WindowAggregator` (fallback) ose Spark window query
4. **AI risk scoring** → `RiskScoringEngine.Assess()` → `ai_risk_scores`
5. **Alarme smart** → `SmartAlertEngine.Evaluate()` → `alerts_log`

#### C) Dritare rrëshqitëse Spark (kërkesa PDF: agregime)

```csharp
.WithWatermark("recordedAt", "10 minutes")
.GroupBy(Window(Col("recordedAt"), "5 minutes", "1 minute"), roomNumber, patientId)
.Agg(Avg("heartRate"), Avg("spo2"), Avg("temperature"), ...)
```

- **Dritare:** 5 minuta
- **Slide:** 1 minutë
- **Ruajtje:** tabela `vitals_window_agg` në Cassandra

#### D) Checkpoint (optimizim / qëndrueshmëri)

- `Spark:CheckpointDir` → `/tmp/spark-checkpoints`
- Ruan offset-et Kafka — mos humb të dhëna pas restart-it

### Metrikat e performancës

`Streaming.App/MetricsCollector.cs` mat:

- mesazhe/sekondë
- madhësi batch, latencë përpunimi
- dritare të llogaritura, alarme të gjeneruara

Çdo 3 sekonda → `PublishMetrics` → faqja **Shëndeti i sistemit** në Vue.

---

## 5. Apache Cassandra — ku dhe çfarë ruan?

### Ku përdoret

| Operacion | Skedar |
|-----------|--------|
| **Shkrim** (nga streaming) | `Streaming.App/CassandraHealthRepository.cs` |
| **Lexim** (për dashboard) | `Vue.Api/Data/CassandraHealthStatsRepository.cs` |
| **Skema** | `cassandra-schema.cql` + `cassandra-schema-init.cql` |
| **Infrastruktura** | `docker-compose.yml` (`cassandra`, `cassandra-init`) |

**Keyspace:** `smart_health`

### Tabelat dhe roli i tyre

| Tabela | Çfarë ruan | Kush shkruan | Kush lexon |
|--------|------------|--------------|------------|
| `patient_vitals` | Lexime të papërpunuara (raw) | Streaming.App | Vue.Api |
| `patients` | Regjistri + profili klinik | Streaming.App | Vue.Api |
| `alerts_log` | Historiku i alarmeve | Streaming.App | Vue.Api |
| `vitals_window_agg` | Mesataret e dritares 5-min (Spark) | Streaming.App | Vue.Api |
| `ai_risk_scores` | Rezultati i rrezikut (rregulla) | Streaming.App | Vue.Api |
| `ml_predictions` | Parashikimi ML.NET i infarktit | Vue.Api (`MlPredictionWorker`) | Vue.Api |

### Pse Cassandra?

- Shkrim i shpejtë i volumit të lartë (shumë lexime/sekondë)
- Model partition-by-room/day — i përshtatshëm për IoT time-series
- Të dhënat historike për grafikë dhe analitikë

---

## 6. Vue.Api — shtresa e backend-it

**Skedari kryesor:** `Vue.Api/Program.cs`  
**Container:** `smart-health-vue-api` (:5099)

### Dy mënyra (e rëndësishme për ta kuptuar)

| `DataGeneration:Enabled` | Burimi i të dhënave | Kur përdoret |
|--------------------------|---------------------|--------------|
| `false` | **Cassandra** (pipeline real) | Docker, mbrojtje |
| `true` | Memorie (`DataGeneratorService`) | Dev pa Kafka/Spark |

**Për mbrojtje duhet `false`** — si në `docker-compose.yml`.

### REST API (`/api/*`)

| Endpoint | Përshkrimi |
|----------|------------|
| `GET /api/overview` | KPI, trende, ngjarje të fundit |
| `GET /api/live` | Snapshot i vitalëve live |
| `GET /api/patients` | Lista e pacientëve |
| `GET /api/patients/{id}` | Detaje + grafikë + statistika |
| `GET /api/alerts` | Alarmet me filtra |
| `GET /api/analytics` | Agregime, leaderboard rreziku |
| `GET /api/system` | Metrikat e performancës |
| `GET /api/ai/predictions` | Parashikimet ML.NET |
| `GET /api/ai/patient/{id}` | Parashikim për një pacient |
| `POST /api/push/register` | Regjistrim token Firebase |

### SignalR Hub

**Skedari:** `Vue.Api/Hubs/HealthHub.cs`  
**URL:** `/healthHub`

| Event | Burimi | Efekti në UI |
|-------|--------|--------------|
| `vitalsReceived` | Streaming.App | Tabela live, grafikë |
| `alertReceived` | Streaming.App | Qendra e alarmeve |
| `riskReceived` | Streaming.App | Paneli i rrezikut |
| `metricsReceived` | Streaming.App | Shëndeti i sistemit |
| `mlPredictionReceived` | MlPredictionWorker | Parashikimet AI |

**Lidhja:** `Streaming.App` lidhet si **klient** SignalR te `http://vue-api:5099/healthHub`.

---

## 7. vue-client — ndërfaqja e vizualizimit

**Folder:** `vue-client/`  
**Container:** `smart-health-vue-client`  
**URL:** http://localhost:5173

**Stack:** Vue 3, Pinia, Vue Router, ApexCharts, SignalR

### Faqet

| Rruga | Skedari | Funksioni |
|-------|---------|-----------|
| `/` | `Overview.vue` | Dashboard kryesor, KPI |
| `/live` | `LiveMonitoring.vue` | Monitorim live i vitalëve |
| `/rooms` | `Rooms.vue` | Pamje sipas dhomave |
| `/patients/:id` | `PatientDetails.vue` | Grafikë, min/max/avg, AI |
| `/alerts` | `Alerts.vue` | Qendra e alarmeve |
| `/analytics` | `Analytics.vue` | Analitika nga dritaret Spark |
| `/ai` | `AiPredictions.vue` | Parashikime ML.NET |
| `/system` | `SystemHealth.vue` | Kafka, Spark, Cassandra, API |

**Live data:** `vue-client/src/stores/realtime.js` — dëgjon SignalR  
**REST data:** thirrje `/api/*` për historik dhe faqe statike

---

## 8. Komponentët e avancuar (kërkesa PDF)

### A) Inteligjenca Artificiale

| Pjesa | Skedari | Logjika |
|-------|---------|---------|
| Trajnimi i modelit | `AI.Training/Program.cs` | Trajnon ML.NET nga dataseti i infarktit |
| Modeli | `Vue.Api/AiModels/heart_attack_model.zip` | Model i ngarkuar në runtime |
| Parashikimi | `Vue.Api/Ai/HeartAttackPredictionService.cs` | Probabilitet 0–1 + faktorët kryesorë |
| Worker | `Vue.Api/Services/MlPredictionWorker.cs` | Çdo ~15s parashikon për çdo pacient |
| Rrezik me rregulla | `Streaming.App/RiskScoringEngine.cs` | Score 0–100 (si NEWS) gjatë streaming |

**Dy shtresa AI:**

1. **Real-time (streaming):** `RiskScoringEngine` — bazuar në vitalë aktuale
2. **Periodik (API):** ML.NET — bazuar në profilin klinik + vitalët e fundit

### B) Sistemi i alarmeve (Smart Alert Engine)

**Skedari:** `Streaming.App/SmartAlertEngine.cs` (kopje edhe në `Vue.Api/` për demo mode)

**Logjika (jo alarm për çdo lexim):**

| Rregull | Vlera |
|---------|-------|
| Cooldown | 5 minuta për të njëjtin alarm |
| Persistencë HR | 15 sekonda mbi/nën prag |
| Persistencë SpO₂ | 10 sekonda |
| Persistencë temperaturë | 1 minutë |
| Persistencë tensioni | 5 minuta |
| Stabilizim | Mesazh INFO kur vlera kthehet në normale |

**Pragjet:** HR, SpO₂, temperaturë, BP sistolik/diastolik, frekuencë frymëmarrjeje.

### C) Analiza e performancës

| Komponent | Skedari |
|-----------|---------|
| Metrikat streaming | `Streaming.App/MetricsCollector.cs` |
| Metrikat API | `Vue.Api/Services/SystemMetricsService.cs` |
| UI | `vue-client/src/views/SystemHealth.vue` |

---

## 9. Cila është pjesa më e rëndësishme e projektit?

### Pjesa më kritike: **Streaming.App** (Spark Streaming)

Arsyet:

1. **Lidh të gjitha kërkesat teknike të PDF-së** — Kafka consumer, filtrim, agregime, dritare rrëshqitëse
2. **Orkestron pipeline-in** — lexon → përpunon → ruan në Cassandra → njofton live
3. **Përmban logjikën e biznesit** — alarme, AI risk, metrika
4. **Pa të, Kafka dhe Cassandra janë të shkëputura** — simulatori dërgon por asgjë nuk përpunohet

### Rrjedha e të dhënave (hap pas hapi)

```
1. Simulator.App/Worker.cs
   └─ CreateReading() → JSON

2. Kafka topic "health-vitals"
   └─ Mesazh me key=patientId

3. Streaming.App/Program.cs
   ├─ Spark ReadStream (ose Direct consumer)
   ├─ Filter (validim)
   ├─ ProcessReadingAsync():
   │    ├─ INSERT patient_vitals, patients
   │    ├─ SignalR PublishVitals
   │    ├─ RiskScoringEngine → ai_risk_scores
   │    └─ SmartAlertEngine → alerts_log
   └─ Spark Window 5min/1min → vitals_window_agg

4. Cassandra smart_health
   └─ 6 tabela (historik + agregime)

5. Vue.Api
   ├─ REST: lexon nga Cassandra
   └─ SignalR Hub: merr nga Streaming, dërgon te UI

6. vue-client
   └─ Grafikë, tabela, alarme — përditësohen live + nga REST
```

---

## 10. Struktura e folderëve të projektit

```
smart-health-monitoring/
│
├── Simulator.App/              ← PIKA E NISJES SË TË DHËNAVE (sensorë simuluar)
│   ├── Worker.cs               ← Producer Kafka
│   └── appsettings.json
│
├── Streaming.App/              ← PJESA MË E RËNDËSISHME (Spark Streaming)
│   ├── Program.cs              ← Kafka consumer + Spark + logjika
│   ├── CassandraHealthRepository.cs
│   ├── SmartAlertEngine.cs
│   ├── RiskScoringEngine.cs
│   ├── WindowAggregator.cs
│   ├── MetricsCollector.cs
│   ├── Dockerfile
│   └── docker-entrypoint.sh
│
├── Vue.Api/                    ← Backend API + SignalR + AI
│   ├── Program.cs
│   ├── Hubs/HealthHub.cs
│   ├── Data/CassandraHealthStatsRepository.cs
│   ├── Ai/HeartAttackPredictionService.cs
│   ├── Services/MlPredictionWorker.cs
│   └── AiModels/heart_attack_model.zip
│
├── vue-client/                 ← Ndërfaqja web (Vue 3)
│   ├── src/views/              ← 8 faqe dashboard
│   ├── src/stores/realtime.js  ← SignalR
│   └── Dockerfile
│
├── AI.Training/                ← Trajnimi i modelit ML.NET (offline)
│
├── cassandra-schema.cql        ← Skema e databazës
├── docker-compose.yml          ← Nisja e gjithë sistemit
└── README-DOCKER.md            ← Udhëzime Docker (në repo)
```

---

## 11. Si ta nisësh për mbrojtje

```powershell
# 1. Nis pipeline-in e plotë
docker compose up -d --build

# 2. Verifiko që containerët janë UP
docker compose ps

# 3. Hap dashboard-in
# Browser: http://localhost:5173

# 4. (Opsionale) Shiko logjet
docker compose logs -f simulator    # të dhënat po dërgohen?
docker compose logs -f streaming    # Spark po përpunon?
docker compose logs -f vue-api      # API gati?
```

### Çfarë të demonstrosh

1. **Simulator** → log me HR, SpO₂, temp për çdo dhomë
2. **Kafka** → topic `health-vitals` me mesazhe
3. **Spark** → agregime, filtrim, dritare 5-min
4. **Cassandra** → të dhëna të ruajtura (historik)
5. **Vue** → live monitoring, alarme, AI, performancë

---

## 12. Përmbledhje: kërkesa PDF vs implementimi

| # | Kërkesa PDF | Ku në projekt | Status |
|---|-------------|---------------|--------|
| 1 | Domeni IoT (Smart Health) | `Simulator.App`, 10 pacientë, dhoma 101–110 | ✅ |
| 2 | Mbledhja e të dhënave (simulator) | `Simulator.App/Worker.cs` | ✅ |
| 3 | Apache Kafka | `docker-compose.yml`, producer + consumer | ✅ |
| 4 | Spark Streaming (filtrim, agregime, dritare) | `Streaming.App/Program.cs` | ✅ |
| 5 | Apache Cassandra | `cassandra-schema.cql`, repositories | ✅ |
| 6 | Ndërfaqe vizualizimi web | `vue-client/` + `Vue.Api/` | ✅ |
| 7 | AI (avancuar) | `AI.Training`, `HeartAttackPredictionService`, `RiskScoringEngine` | ✅ |
| 8 | Alarme (avancuar) | `SmartAlertEngine.cs` | ✅ |
| 9 | Performancë (avancuar) | `MetricsCollector`, `SystemHealth.vue` | ✅ |
| 10 | Raporti final | Dokument i jashtëm (Word/PDF) | ⚠️ Shkruhet veçmas |

---

## 13. Fraza për mbrojtje (30 sekonda)

> "Të dhënat fillojnë nga **Simulator.App**, që simulon sensorët e 10 pacientëve dhe i dërgon në **Apache Kafka** (topic `health-vitals`). **Streaming.App** i konsumon me **Apache Spark Structured Streaming** — filtron, agregon në dritare 5-minutëshe dhe ruan në **Apache Cassandra**. Njëkohësisht vlerëson rrezikun AI, gjeneron alarme smart dhe dërgon përditësime live përmes SignalR te **Vue.Api**. **vue-client** (Vue 3) lexon historikun nga REST API dhe shfaq të dhënat live në 8 faqe: monitorim, dhomat, alarme, analitika, AI dhe performanca e sistemit."

---

*Smart Health Monitoring — Dokument strukture projekti*  
*Përditësuar: Qershor 2026*
