import { ref } from 'vue'
import { api } from '../services/api'

// Loads consolidated AI model information (GET /api/ai/model-info).
export function useAiModel() {
  const info = ref(null)
  const loading = ref(false)
  const error = ref('')

  async function load() {
    loading.value = true
    error.value = ''
    try {
      info.value = await api.aiModelInfo()
    } catch {
      // Fallback for older API builds that only expose /api/ai/metrics + /api/ai/factors.
      try {
        const [metrics, factors] = await Promise.all([
          api.aiMetrics(),
          api.aiFactors().catch(() => [])
        ])
        info.value = {
          ...metrics,
          topFactors: Array.isArray(factors) && factors.length
            ? factors
            : (metrics.topFactors ?? [])
        }
      } catch (e) {
        error.value = e?.message || 'Informacioni i modelit AI nuk u ngarkua.'
        info.value = null
      }
    } finally {
      loading.value = false
    }
  }

  return { info, loading, error, load }
}

// Runs an on-demand heart-attack risk prediction (POST /api/ai/predict).
export function usePatientRisk() {
  const result = ref(null)
  const loading = ref(false)
  const error = ref('')

  async function predict(record) {
    loading.value = true
    error.value = ''
    try {
      result.value = await api.aiPredict(record)
    } catch (e) {
      error.value = e?.message || 'Parashikimi AI dështoi.'
      result.value = null
    } finally {
      loading.value = false
    }
  }

  return { result, loading, error, predict }
}

// Maps the backend profile + latest vitals into the HeartRecord shape the model expects.
export function buildHeartRecord(profile, vitals, detail) {
  const lastOf = (arr) => (Array.isArray(arr) && arr.length ? arr[arr.length - 1] : 0)
  return {
    age: profile?.age ?? detail?.age ?? 0,
    sex: profile?.sex || 'Unknown',
    cholesterol: profile?.cholesterol ?? 0,
    systolic: vitals?.systolicBp ?? lastOf(detail?.systolics),
    diastolic: vitals?.diastolicBp ?? lastOf(detail?.diastolics),
    heartRate: vitals?.heartRate ?? lastOf(detail?.heartRates),
    diabetes: profile?.diabetes ?? 0,
    familyHistory: profile?.familyHistory ?? 0,
    smoking: profile?.smoking ?? 0,
    obesity: profile?.obesity ?? 0,
    alcoholConsumption: profile?.alcoholConsumption ?? 0,
    exerciseHoursPerWeek: profile?.exerciseHoursPerWeek ?? 0,
    diet: profile?.diet || 'Average',
    previousHeartProblems: profile?.previousHeartProblems ?? 0,
    medicationUse: profile?.medicationUse ?? 0,
    stressLevel: profile?.stressLevel ?? 0,
    sedentaryHoursPerDay: profile?.sedentaryHoursPerDay ?? 0,
    bmi: profile?.bmi ?? 0,
    triglycerides: profile?.triglycerides ?? 0,
    physicalActivityDaysPerWeek: profile?.physicalActivityDaysPerWeek ?? 0,
    sleepHoursPerDay: profile?.sleepHoursPerDay ?? 0
  }
}
