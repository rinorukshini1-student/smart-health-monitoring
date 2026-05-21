# Smart Health Monitoring

IoT demo solution with three .NET 8 modules:

- `Simulator.App`: Worker Service that publishes vitals for 10 patients to Kafka topic `health-vitals`.
- `Streaming.App`: Spark for .NET streaming processor that reads Kafka, stores vitals and alerts in Cassandra, and publishes SignalR messages.
- `Web.Dashboard`: ASP.NET Core Razor Pages dashboard with live room cards and Chart.js statistics.

## Infrastructure

Start Kafka, Zookeeper, and Cassandra:

```powershell
docker compose up -d
```

Initialize Cassandra after the container is ready:

```powershell
docker exec -it smart-health-cassandra cqlsh -f /schema/cassandra-schema.cql
```

## Run

Run the web dashboard:

```powershell
dotnet run --project Web.Dashboard
```

Run the simulator:

```powershell
dotnet run --project Simulator.App
```

Run the processor locally in direct Kafka mode:

```powershell
dotnet run --project Streaming.App
```

Run the Spark streaming app with a Spark for .NET runtime configured:

```powershell
spark-submit --class org.apache.spark.deploy.dotnet.DotnetRunner `
  --master local `
  microsoft-spark-*.jar `
  dotnet Streaming.App.dll
```

Pass `--spark` only when launching through `spark-submit`.

Open `http://localhost:5084/Rooms` for the live monitor and `http://localhost:5084/Statistics` for Cassandra-backed analytics.
