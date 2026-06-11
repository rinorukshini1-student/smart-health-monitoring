/** ML.NET heart-risk alerts — shown only on Patient Details, not on live monitoring pages. */
export const ML_ALERT_TYPE = 'AI_HEART_RISK'

export function isMlAlert(alert) {
  return alert?.alertType === ML_ALERT_TYPE
}

export function excludeMlAlerts(alerts) {
  return (alerts ?? []).filter(a => !isMlAlert(a))
}
