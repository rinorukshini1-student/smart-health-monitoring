CREATE KEYSPACE IF NOT EXISTS smart_health
WITH replication = {
  'class': 'SimpleStrategy',
  'replication_factor': 1
};

USE smart_health;

-- Raw sensor readings (SensorReadings entity).
CREATE TABLE IF NOT EXISTS patient_vitals (
  room_number text,
  vital_day date,
  recorded_at timestamp,
  patient_id text,
  patient_name text,
  age int,
  heart_rate int,
  spo2 int,
  temperature double,
  systolic_bp int,
  diastolic_bp int,
  respiratory_rate int,
  PRIMARY KEY ((room_number, vital_day), recorded_at, patient_id)
) WITH CLUSTERING ORDER BY (recorded_at DESC, patient_id ASC);

-- For deployments created before the metric set was expanded (safe to run; ignore "already exists" errors).
ALTER TABLE patient_vitals ADD age int;
ALTER TABLE patient_vitals ADD systolic_bp int;
ALTER TABLE patient_vitals ADD diastolic_bp int;
ALTER TABLE patient_vitals ADD respiratory_rate int;

-- Alerts produced by the rule engine (Alerts entity).
CREATE TABLE IF NOT EXISTS alerts_log (
  room_number text,
  alert_day date,
  recorded_at timestamp,
  alert_id uuid,
  patient_id text,
  alert_type text,
  severity text,
  message text,
  value double,
  heart_rate int,
  spo2 int,
  temperature double,
  systolic_bp int,
  diastolic_bp int,
  respiratory_rate int,
  PRIMARY KEY ((room_number, alert_day), recorded_at, alert_id)
) WITH CLUSTERING ORDER BY (recorded_at DESC, alert_id ASC);

ALTER TABLE alerts_log ADD alert_type text;
ALTER TABLE alerts_log ADD value double;
ALTER TABLE alerts_log ADD systolic_bp int;
ALTER TABLE alerts_log ADD diastolic_bp int;
ALTER TABLE alerts_log ADD respiratory_rate int;

-- Patient registry (Patients entity) extended with the heart-attack dataset clinical profile.
CREATE TABLE IF NOT EXISTS patients (
  patient_id text PRIMARY KEY,
  patient_name text,
  room_number text,
  age int,
  sex text,
  cholesterol int,
  diabetes int,
  family_history int,
  smoking int,
  obesity int,
  alcohol_consumption int,
  exercise_hours_per_week double,
  diet text,
  previous_heart_problems int,
  medication_use int,
  stress_level int,
  sedentary_hours_per_day double,
  bmi double,
  triglycerides int,
  physical_activity_days_per_week int,
  sleep_hours_per_day int
);

-- For deployments created before the profile was added (ignore "already exists" errors).
ALTER TABLE patients ADD sex text;
ALTER TABLE patients ADD cholesterol int;
ALTER TABLE patients ADD diabetes int;
ALTER TABLE patients ADD family_history int;
ALTER TABLE patients ADD smoking int;
ALTER TABLE patients ADD obesity int;
ALTER TABLE patients ADD alcohol_consumption int;
ALTER TABLE patients ADD exercise_hours_per_week double;
ALTER TABLE patients ADD diet text;
ALTER TABLE patients ADD previous_heart_problems int;
ALTER TABLE patients ADD medication_use int;
ALTER TABLE patients ADD stress_level int;
ALTER TABLE patients ADD sedentary_hours_per_day double;
ALTER TABLE patients ADD bmi double;
ALTER TABLE patients ADD triglycerides int;
ALTER TABLE patients ADD physical_activity_days_per_week int;
ALTER TABLE patients ADD sleep_hours_per_day int;

-- Heart-attack ML prediction history (for trend visualisation).
CREATE TABLE IF NOT EXISTS ml_predictions (
  patient_id text,
  prediction_day date,
  recorded_at timestamp,
  room_number text,
  risk_probability double,
  risk_category text,
  top_factors text,
  PRIMARY KEY ((patient_id, prediction_day), recorded_at)
) WITH CLUSTERING ORDER BY (recorded_at DESC);

-- AI risk assessments (one row per scored reading).
CREATE TABLE IF NOT EXISTS ai_risk_scores (
  patient_id text,
  score_day date,
  recorded_at timestamp,
  room_number text,
  score int,
  category text,
  factors text,
  heart_rate int,
  spo2 int,
  temperature double,
  systolic_bp int,
  diastolic_bp int,
  respiratory_rate int,
  PRIMARY KEY ((patient_id, score_day), recorded_at)
) WITH CLUSTERING ORDER BY (recorded_at DESC);

-- Spark Streaming sliding-window aggregations (5-minute window, 1-minute slide).
CREATE TABLE IF NOT EXISTS vitals_window_agg (
  room_number text,
  window_day date,
  window_start timestamp,
  patient_id text,
  avg_heart_rate double,
  avg_spo2 double,
  avg_temperature double,
  avg_systolic double,
  avg_diastolic double,
  avg_respiratory double,
  sample_count int,
  PRIMARY KEY ((room_number, window_day), window_start, patient_id)
) WITH CLUSTERING ORDER BY (window_start DESC, patient_id ASC);
