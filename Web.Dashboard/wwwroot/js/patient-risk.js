// Patient Details - AI heart-attack risk panel. Vue 3 + ApexCharts.
// Exposes window.updateRiskPanel(patientId) so the existing page can switch patients.
const { createApp, ref, onMounted } = Vue;

createApp({
  setup() {
    const data = ref(null);
    const patientId = ref(new URLSearchParams(location.search).get("patientId") || "P001");
    let historyChart;

    const pct = p => Math.round((p || 0) * 100);
    const catClass = c => c === "HIGH" ? "risk-high" : c === "MEDIUM" ? "risk-medium" : "risk-low";
    const tone = c => c === "HIGH" ? "tone-red" : c === "MEDIUM" ? "tone-amber" : "tone-green";

    function renderHistory(points) {
      const series = [{ name: "Risk %", data: points.map(p => [new Date(p.timestamp).getTime(), Math.round(p.riskProbability * 100)]) }];
      const opts = {
        chart: { type: "area", height: 220 },
        colors: ["#dc2626"],
        stroke: { curve: "smooth", width: 2 },
        fill: { type: "gradient", gradient: { opacityFrom: .4, opacityTo: .05 } },
        dataLabels: { enabled: false },
        xaxis: { type: "datetime" },
        yaxis: { min: 0, max: 100 }
      };
      if (!historyChart) { historyChart = new ApexCharts(document.querySelector("#riskHistoryChart"), { ...opts, series }); historyChart.render(); }
      else historyChart.updateSeries(series);
    }

    async function load(id) {
      patientId.value = id;
      try {
        data.value = await (await fetch(`/api/ai/patient/${id}`)).json();
        renderHistory(await (await fetch(`/api/ai/history/${id}`)).json());
      } catch (e) { }
    }

    onMounted(() => {
      load(patientId.value);
      window.updateRiskPanel = load;
      window.SmartHealth.onMlPrediction(p => { if (p.patientId === patientId.value) data.value = p; });
      setInterval(() => load(patientId.value), 15000);
    });

    return { data, patientId, pct, catClass, tone };
  },
  template: `
    <div class="kpi-grid" style="grid-template-columns: 1.2fr 2fr;">
      <div class="kpi" :class="tone(data ? data.riskCategory : 'LOW')">
        <div class="kpi__icon">❤</div>
        <div class="kpi__label">Heart Attack Risk Score</div>
        <div class="kpi__value">{{ data ? pct(data.riskProbability) : '--' }}%</div>
        <div class="kpi__hint">
          <span class="badge-pill" :class="catClass(data ? data.riskCategory : 'LOW')">{{ data ? data.riskCategory : '—' }}</span>
        </div>
      </div>
      <div class="panel">
        <div class="panel-head"><h3>Current Risk Assessment</h3></div>
        <p class="sub" style="margin:0 0 .6rem;">Prediction: <strong>{{ data ? data.prediction : '—' }}</strong> · model probability {{ data ? pct(data.riskProbability) : 0 }}%</p>
        <div>
          <span v-for="f in (data ? data.topFactors : [])" :key="f" class="factor-chip">{{ f }}</span>
        </div>
      </div>
    </div>
    <div class="panel" style="margin-bottom:1.2rem;">
      <div class="panel-head"><h3>AI Prediction History</h3></div>
      <div id="riskHistoryChart"></div>
    </div>
  `
}).mount("#riskApp");
