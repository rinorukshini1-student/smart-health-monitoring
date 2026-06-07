// Përkthime shqip për vlerat që vijnë nga backend-i ose metadata e modelit.

export function translateStatus(status) {
  const map = { Normal: 'Normal', Warning: 'Paralajmërim', Critical: 'Kritik' }
  return map[status] ?? status
}

export function translateSeverity(severity) {
  const map = { CRITICAL: 'KRITIK', WARNING: 'PARALAJMËRIM', INFO: 'INFORMACION' }
  return map[(severity || '').toUpperCase()] ?? severity
}

export function translateRiskCategory(category) {
  const c = (category || '')
  if (c === 'Rrezik i Lartë' || c.toUpperCase() === 'HIGH') return 'I LARTË'
  if (c === 'Rrezik Mesatar' || c.toUpperCase() === 'MEDIUM') return 'MESATAR'
  if (c === 'Rrezik i Ulët' || c.toUpperCase() === 'LOW') return 'I ULËT'
  const u = c.toUpperCase()
  if (u.includes('HIGH')) return 'I LARTË'
  if (u.includes('MEDIUM') || u.includes('MED')) return 'MESATAR'
  if (u.includes('LOW')) return 'I ULËT'
  return category
}

export function translateRiskCategoryLong(category) {
  const map = {
    'Rrezik i Lartë': 'Rrezik i Lartë',
    'Rrezik Mesatar': 'Rrezik Mesatar',
    'Rrezik i Ulët': 'Rrezik i Ulët',
    'High Risk': 'Rrezik i Lartë',
    'Medium Risk': 'Rrezik Mesatar',
    'Low Risk': 'Rrezik i Ulët',
    HIGH: 'Rrezik i Lartë',
    MEDIUM: 'Rrezik Mesatar',
    LOW: 'Rrezik i Ulët'
  }
  return map[category] ?? translateRiskCategory(category)
}

export function translateAlertType(type) {
  const map = {
    HeartRate: 'Pulsi',
    Temperature: 'Temperatura',
    SpO2: 'SpO₂',
    BloodPressure: 'Tensioni i Gjakut',
    RespiratoryRate: 'Frymëmarrja',
    Other: 'Tjetër'
  }
  return map[type] ?? type
}

export function translateModelFactor(factor) {
  const map = {
    BMI: 'BMI',
    'Sedentary Hours/Day': 'Orë sedentare/ditë',
    Cholesterol: 'Kolesterol',
    'Exercise Hours/Week': 'Orë ushtrimi/javë',
    'Blood Pressure (Systolic)': 'Tensioni i gjakut (sistolik)',
    Triglycerides: 'Trigliceridë',
    'Heart Rate': 'Pulsi',
    Age: 'Mosha',
    'Blood Pressure (Diastolic)': 'Tensioni i gjakut (diastolik)',
    'Physical Activity Days/Week': 'Ditë aktiviteti fizik/javë'
  }
  return map[factor] ?? factor
}

export const filterLabels = {
  all: 'Të gjitha',
  normal: 'Normal',
  warning: 'Paralajmërim',
  critical: 'Kritik',
  CRITICAL: 'KRITIK',
  WARNING: 'PARALAJMËRIM',
  INFO: 'INFORMACION'
}
