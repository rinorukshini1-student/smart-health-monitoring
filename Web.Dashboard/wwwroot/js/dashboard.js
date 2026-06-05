// Shared dashboard runtime: a single SignalR connection that pages subscribe to,
// plus small formatting/charting helpers reused across the six dashboard pages.
window.SmartHealth = (function () {
  const handlers = { vitals: [], alert: [], risk: [], metrics: [], ml: [] };

  const connection = new signalR.HubConnectionBuilder()
    .withUrl("/healthHub")
    .withAutomaticReconnect()
    .build();

  connection.on("vitalsReceived", r => handlers.vitals.forEach(h => h(r)));
  connection.on("alertReceived", a => handlers.alert.forEach(h => h(a)));
  connection.on("riskReceived", r => handlers.risk.forEach(h => h(r)));
  connection.on("metricsReceived", m => handlers.metrics.forEach(h => h(m)));
  connection.on("mlPredictionReceived", p => handlers.ml.forEach(h => h(p)));

  function setConn(online) {
    const dot = document.getElementById("globalConn");
    const text = document.getElementById("globalConnText");
    const pill = document.getElementById("livePill");
    if (dot) dot.className = "dot " + (online ? "online" : "offline");
    if (text) text.textContent = online ? "Connected" : "Disconnected";
    if (pill) pill.classList.toggle("offline", !online);
  }

  connection.onreconnecting(() => setConn(false));
  connection.onreconnected(() => setConn(true));
  connection.onclose(() => setConn(false));

  connection.start().then(() => setConn(true)).catch(() => setConn(false));

  return {
    onVitals: h => handlers.vitals.push(h),
    onAlert: h => handlers.alert.push(h),
    onRisk: h => handlers.risk.push(h),
    onMetrics: h => handlers.metrics.push(h),
    onMlPrediction: h => handlers.ml.push(h),

    fmtTime: ts => new Date(ts).toLocaleTimeString(),
    fmtDateTime: ts => new Date(ts).toLocaleString(),

    statusClass: s => "s-" + (s || "").toLowerCase(),
    sevClass: s => "sev-" + (s || "").toLowerCase(),
    riskClass: c => (c || "").toLowerCase().includes("high") ? "risk-high"
      : (c || "").toLowerCase().includes("medium") ? "risk-medium" : "risk-low",

    // Standard line chart factory.
    lineChart: (ctx, label, color, fill) => new Chart(ctx, {
      type: "line",
      data: { labels: [], datasets: [{ label, data: [], borderColor: color, backgroundColor: fill || (color + "22"), fill: !!fill, tension: .35, pointRadius: 0, borderWidth: 2 }] },
      options: { responsive: true, maintainAspectRatio: false, plugins: { legend: { display: false } }, scales: { x: { ticks: { maxTicksLimit: 8 } } } }
    }),

    barChart: (ctx, label, color) => new Chart(ctx, {
      type: "bar",
      data: { labels: [], datasets: [{ label, data: [], backgroundColor: color, borderRadius: 6 }] },
      options: { responsive: true, maintainAspectRatio: false, plugins: { legend: { display: false } }, scales: { y: { beginAtZero: true, ticks: { precision: 0 } } } }
    }),

    setSeries: (chart, labels, values) => {
      chart.data.labels = labels;
      chart.data.datasets[0].data = values;
      chart.update();
    },

    pushPoint: (chart, label, value, max) => {
      chart.data.labels.push(label);
      chart.data.datasets[0].data.push(value);
      if (chart.data.labels.length > (max || 40)) {
        chart.data.labels.shift();
        chart.data.datasets[0].data.shift();
      }
      chart.update();
    }
  };
})();
