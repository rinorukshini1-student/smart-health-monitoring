#!/bin/bash
set -euo pipefail

HOST="${CASSANDRA_HOST:-cassandra}"
MAX_ATTEMPTS=60

echo "Waiting for Cassandra at ${HOST}..."
for i in $(seq 1 "$MAX_ATTEMPTS"); do
  if cqlsh "$HOST" -e "DESCRIBE CLUSTER" >/dev/null 2>&1; then
    echo "Cassandra is ready."
    break
  fi
  if [ "$i" -eq "$MAX_ATTEMPTS" ]; then
    echo "Cassandra did not become ready in time." >&2
    exit 1
  fi
  sleep 5
done

echo "Creating keyspace..."
cqlsh "$HOST" -e "CREATE KEYSPACE IF NOT EXISTS smart_health WITH replication = {'class': 'SimpleStrategy', 'replication_factor': 1};"

echo "Applying tables..."
cqlsh "$HOST" -f /schema/cassandra-schema-init.cql

echo "Cassandra schema initialized successfully."
