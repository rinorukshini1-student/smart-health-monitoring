import { HubConnectionBuilder, HttpTransportType, LogLevel } from '@microsoft/signalr'

// Single shared SignalR connection to the backend hub.
let connection = null

export function getConnection() {
  if (connection) return connection
  connection = new HubConnectionBuilder()
    .withUrl('/healthHub', { transport: HttpTransportType.WebSockets | HttpTransportType.LongPolling })
    .withAutomaticReconnect([0, 1000, 3000, 5000, 10000])
    .configureLogging(LogLevel.Warning)
    .build()
  return connection
}
