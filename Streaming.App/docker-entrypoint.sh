#!/usr/bin/env bash
# Launches the streaming processor. Default is real Apache Spark (spark-submit);
# set STREAMING_MODE=direct for the lightweight direct-Kafka fallback.
set -euo pipefail

APP_DLL="/app/Streaming.App.dll"
SPARK_JAR="/app/${DOTNET_SPARK_JAR:-microsoft-spark-3-2_2.12-2.1.1.jar}"
MODE="${STREAMING_MODE:-spark}"
SPARK_MASTER="${SPARK_MASTER:-local[*]}"

if [ "${MODE}" = "direct" ]; then
  echo "[streaming] Mode=DIRECT (Confluent Kafka consumer, no JVM Spark)."
  exec dotnet "${APP_DLL}"
fi

if [ ! -f "${SPARK_JAR}" ]; then
  echo "[streaming] ERROR: Spark bridge jar not found at ${SPARK_JAR}." >&2
  echo "[streaming] Falling back to DIRECT mode." >&2
  exec dotnet "${APP_DLL}"
fi

echo "[streaming] Mode=SPARK | master=${SPARK_MASTER} | jar=${SPARK_JAR}"
exec "${SPARK_HOME}/bin/spark-submit" \
  --master "${SPARK_MASTER}" \
  --class org.apache.spark.deploy.dotnet.DotnetRunner \
  "${SPARK_JAR}" \
  dotnet "${APP_DLL}" --spark
