// AI Predictions page - Vue 3 (Composition API) + ApexCharts.
// Consumes the heart-attack ML APIs and live SignalR predictions.
const { createApp, ref, computed, onMounted } = Vue;

createApp({
  setup() {
    const predictions = ref([]);
    const factors = ref([]);
    const meta = ref(null);
    const selected = ref(null);
    let distChart, factorChart, historyChart;

    const high = computed(() => predictions.value.filter(p => p.riskCategory === "HIGH").length);
    const medium = computed(() => predictions.value.filter(p => p.riskCategory === "MEDIUM").length);
    const low = computed(() => predictions.value.filter(p => p.riskCategory === "LOW").length);
    const avg = computed(() => predictions.value.length
      ? Math.round(predictions.value.reduce((s, p) => s + p.riskProbability, 0) / predictions.value.length * 100)
      : 0);
    const sorted = computed(() => [...predictions.value].sort((a, b) => b.riskProbability - a.riskProbability));

    const catClass = c => c === "HIGH" ? "risk-high" : c === "MEDIUM" ? "risk-medium" : "risk-low";
    const pct = p => Math.round(p * 100);
    const fmt = t => new Date(t).toLocaleTimeString();

    function upsert(p) {
      const i = predictions.value.findIndex(x => x.patientId === p.patientId);
      if (i >= 0) predictions.value.splice(i, 1, p); else predictions.value.push(p);
    }

    function renderDistribution() {
      const opts = {
        chart: { type: "donut", height: 260 },
        labels: ["Low", "Medium", "High"],
        colors: ["#16a34a", "#d97706", "#dc2626"],
        legend: { position: "bottom" },
        dataLabels: { enabled: true }
      };
      const series = [low.value, medium.value, high.value];
      if (!distChart) { distChart = new ApexCharts(document.querySelector("#distChart"), { ...opts, series }); distChart.render(); }
      else distChart.updateSeries(series);
    }

    function renderFactors() {
      const cats = factors.value.map(f => f.factor);
      const data = factors.value.map(f => Math.round(f.importance * 100000) / 1000);
      const opts = {
        chart: { type: "bar", height: 300 },
        plotOptions: { bar: { horizontal: true, borderRadius: 4 } },
        colors: ["#7c3aed"],
        xaxis: { categories: cats, title: { text: "Importance (AUC drop ×100)" } },
        series: [{ name: "Importance", data }]
      };
      if (!factorChart) { factorChart = new ApexCharts(document.querySelector("#factorChart"), opts); factorChart.render(); }
      else factorChart.updateOptions({ xaxis: { categories: cats }, series: [{ name: "Importance", data }] });
    }

    function renderHistory(points) {
      const series = [{ name: "Risk %", data: points.map(p => [new Date(p.timestamp).getTime(), Math.round(p.riskProbability * 100)]) }];
      const opts = {
        chart: { type: "area", height: 260, animations: { enabled: true } },
        colors: ["#dc2626"],
        stroke: { curve: "smooth", width: 2 },
        fill: { type: "gradient", gradient: { opacityFrom: .4, opacityTo: .05 } },
        dataLabels: { enabled: false },
        xaxis: { type: "datetime" },
        yaxis: { min: 0, max: 100, title: { text: "Risk probability %" } }
      };
      if (!historyChart) { historyChart = new ApexCharts(document.querySelector("#historyChart"), { ...opts, series }); historyChart.render(); }
      else historyChart.updateSeries(series);
    }

    async function loadHistory(id) {
      selected.value = id;
      try { renderHistory(await (await fetch(`/api/ai/history/${id}`)).json()); } catch (e) { }
    }

    async function loadAll() {
      try {
        predictions.value = await (await fetch("/api/ai/predictions")).json();
        renderDistribution();
        if (!selected.value && sorted.value.length) loadHistory(sorted.value[0].patientId);
      } catch (e) { }
    }

    onMounted(async () => {
      renderDistribution();
      try { factors.value = await (await fetch("/api/ai/factors")).json(); renderFactors(); } catch (e) { }
      try { meta.value = await (await fetch("/api/ai/metrics")).json(); } catch (e) { }
      await loadAll();

      window.SmartHealth.onMlPrediction(p => { upsert(p); renderDistribution(); if (selected.value === p.patientId) loadHistory(p.patientId); });
      setInterval(loadAll, 8000);
    });

    return { predictions, sorted, factors, meta, selected, high, medium, low, avg, catClass, pct, fmt, loadHistory };
  },
  template: `
    <div class="page-head">
      <div>
        <p class="eyebrow">Artificial Intelligence · Heart Attack Risk</p>
        <h2>AI Predictions</h2>
        <p class="sub" v-if="meta">Model: <strong>{{ meta.chosenModel }}</strong> · trained on {{ meta.rows }} records</p>
      </div>
    </div>

    <div class="kpi-grid">
      <div class="kpi tone-red"><div class="kpi__icon">🧠</div><div class="kpi__label">High Risk</div><div class="kpi__value">{{ high }}</div><div class="kpi__hint">≥ 70%</div></div>
      <div class="kpi tone-amber"><div class="kpi__icon">▲</div><div class="kpi__label">Medium Risk</div><div class="kpi__value">{{ medium }}</div><div class="kpi__hint">30–70%</div></div>
      <div class="kpi tone-green"><div class="kpi__icon">✓</div><div class="kpi__label">Low Risk</div><div class="kpi__value">{{ low }}</div><div class="kpi__hint">&lt; 30%</div></div>
      <div class="kpi tone-purple"><div class="kpi__icon">Ø</div><div class="kpi__label">Average Risk</div><div class="kpi__value">{{ avg }}%</div><div class="kpi__hint">All patients</div></div>
    </div>

    <div class="grid-2">
      <div class="panel"><div class="panel-head"><h3>Risk Distribution</h3></div><div id="distChart"></div></div>
      <div class="panel"><div class="panel-head"><h3>Top Risk Factors (model importance)</h3></div><div id="factorChart"></div></div>
    </div>

    <div class="panel" style="margin-bottom:1.2rem;">
      <div class="panel-head"><h3>High Risk Patients</h3><span class="kpi__hint">Click a row to view its prediction history</span></div>
      <div class="table-wrap">
        <table class="data">
          <thead><tr><th>Patient</th><th>Risk Score</th><th>Risk Category</th><th>Heart Rate</th><th>Blood Pressure</th><th>Top Factors</th><th>Last Updated</th></tr></thead>
          <tbody>
            <tr v-for="p in sorted" :key="p.patientId" v-on:click="loadHistory(p.patientId)" :class="{ 'row-critical': p.riskCategory === 'HIGH' }" style="cursor:pointer;">
              <td class="mono">{{ p.patientId }} · {{ p.patientName }}</td>
              <td class="mono">{{ pct(p.riskProbability) }}%</td>
              <td><span class="badge-pill" :class="catClass(p.riskCategory)">{{ p.riskCategory }}</span></td>
              <td class="mono">{{ p.heartRate }} bpm</td>
              <td class="mono">{{ p.systolicBp }}/{{ p.diastolicBp }}</td>
              <td>{{ (p.topFactors || []).slice(0,2).join(', ') }}</td>
              <td>{{ fmt(p.recordedAt) }}</td>
            </tr>
            <tr v-if="!sorted.length"><td colspan="7" class="empty">Waiting for AI predictions…</td></tr>
          </tbody>
        </table>
      </div>
    </div>

    <div class="panel">
      <div class="panel-head"><h3>Prediction History <span v-if="selected">· {{ selected }}</span></h3></div>
      <div id="historyChart"></div>
    </div>
  `
}).mount("#aiApp");
