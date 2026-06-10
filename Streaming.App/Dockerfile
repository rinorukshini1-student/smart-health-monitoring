# ============================================================================
#  Streaming.App — Apache Spark Structured Streaming processor
#  Runs the .NET app through `spark-submit` on a real Apache Spark runtime
#  (Microsoft.Spark / .NET for Apache Spark JVM bridge), per project requirements.
# ============================================================================

# ---- Stage 1: build & publish the .NET app ---------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Streaming.App/Streaming.App.csproj Streaming.App/
RUN dotnet restore Streaming.App/Streaming.App.csproj
COPY Streaming.App/ Streaming.App/
RUN dotnet publish Streaming.App/Streaming.App.csproj -c Release -o /app/publish /p:UseAppHost=false

# Microsoft.Spark ships the JVM bridge jars (microsoft-spark-*.jar) inside the NuGet
# package; copy them next to the published app so spark-submit can load the DotnetRunner.
# Fails the build loudly if the expected jar is missing.
RUN cp /root/.nuget/packages/microsoft.spark/2.1.1/jars/*.jar /app/publish/ && \
    ls -l /app/publish/microsoft-spark-3-2_2.12-2.1.1.jar

# ---- Stage 2: Apache Spark + JVM + .NET 8 runtime --------------------------
FROM eclipse-temurin:11-jre-jammy

# Microsoft.Spark 2.1.1 is compatible with Apache Spark 3.2.x -> microsoft-spark-3-2_2.12-2.1.1.jar
ENV SPARK_VERSION=3.2.1 \
    HADOOP_PROFILE=3.2 \
    SCALA_PROFILE=2.12 \
    DOTNET_SPARK_VERSION=2.1.1 \
    SPARK_HOME=/opt/spark \
    DEBIAN_FRONTEND=noninteractive
ENV DOTNET_SPARK_JAR=microsoft-spark-3-2_${SCALA_PROFILE}-${DOTNET_SPARK_VERSION}.jar
ENV PATH="${SPARK_HOME}/bin:${PATH}"

# .NET 8 runtime (to execute the published Streaming.App.dll) + tooling.
RUN apt-get update && \
    apt-get install -y --no-install-recommends curl ca-certificates libicu70 procps bash && \
    rm -rf /var/lib/apt/lists/* && \
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh && \
    bash /tmp/dotnet-install.sh --channel 8.0 --runtime dotnet --install-dir /usr/share/dotnet && \
    ln -s /usr/share/dotnet/dotnet /usr/local/bin/dotnet && \
    rm /tmp/dotnet-install.sh

# Apache Spark.
RUN curl -fsSL "https://archive.apache.org/dist/spark/spark-${SPARK_VERSION}/spark-${SPARK_VERSION}-bin-hadoop${HADOOP_PROFILE}.tgz" -o /tmp/spark.tgz && \
    tar -xzf /tmp/spark.tgz -C /opt && \
    mv "/opt/spark-${SPARK_VERSION}-bin-hadoop${HADOOP_PROFILE}" "${SPARK_HOME}" && \
    rm /tmp/spark.tgz

# Kafka structured-streaming connector jars (pre-baked so no Maven download at runtime).
RUN cd "${SPARK_HOME}/jars" && \
    BASE=https://repo1.maven.org/maven2 && \
    curl -fsSLO "${BASE}/org/apache/spark/spark-sql-kafka-0-10_${SCALA_PROFILE}/${SPARK_VERSION}/spark-sql-kafka-0-10_${SCALA_PROFILE}-${SPARK_VERSION}.jar" && \
    curl -fsSLO "${BASE}/org/apache/spark/spark-token-provider-kafka-0-10_${SCALA_PROFILE}/${SPARK_VERSION}/spark-token-provider-kafka-0-10_${SCALA_PROFILE}-${SPARK_VERSION}.jar" && \
    curl -fsSLO "${BASE}/org/apache/kafka/kafka-clients/2.8.0/kafka-clients-2.8.0.jar" && \
    curl -fsSLO "${BASE}/org/apache/commons/commons-pool2/2.6.2/commons-pool2-2.6.2.jar"

WORKDIR /app
COPY --from=build /app/publish/ ./
COPY Streaming.App/docker-entrypoint.sh /usr/local/bin/streaming-entrypoint.sh
# Normalize potential CRLF line endings (script may be authored on Windows) and make executable.
RUN sed -i 's/\r$//' /usr/local/bin/streaming-entrypoint.sh && \
    chmod +x /usr/local/bin/streaming-entrypoint.sh

ENTRYPOINT ["/usr/local/bin/streaming-entrypoint.sh"]
