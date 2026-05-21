CREATE KEYSPACE IF NOT EXISTS smart_health
WITH replication = {
  'class': 'SimpleStrategy',
  'replication_factor': 1
};

USE smart_health;

CREATE TABLE IF NOT EXISTS patient_vitals (
  room_number text,
  vital_day date,
  recorded_at timestamp,
  patient_id text,
  patient_name text,
  heart_rate int,
  spo2 int,
  temperature double,
  PRIMARY KEY ((room_number, vital_day), recorded_at, patient_id)
) WITH CLUSTERING ORDER BY (recorded_at DESC, patient_id ASC);

CREATE TABLE IF NOT EXISTS alerts_log (
  room_number text,
  alert_day date,
  recorded_at timestamp,
  alert_id uuid,
  patient_id text,
  severity text,
  message text,
  heart_rate int,
  spo2 int,
  temperature double,
  PRIMARY KEY ((room_number, alert_day), recorded_at, alert_id)
) WITH CLUSTERING ORDER BY (recorded_at DESC, alert_id ASC);
