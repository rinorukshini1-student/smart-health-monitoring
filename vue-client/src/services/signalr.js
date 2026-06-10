import { HubConnectionBuilder, HttpTransportType, LogLevel } from '@microsoft/signalr'
import { apiUrl } from '../config'

let connection = null

export function getConnection() {
  if (connection) return connection
  connection = new HubConnectionBuilder()
    .withUrl(apiUrl('/healthHub'), { transport: HttpTransportType.WebSockets | HttpTransportType.LongPolling })
    .withAutomaticReconnect([0, 1000, 3000, 5000, 10000])
    .configureLogging(LogLevel.Warning)
    .build()
  return connection
}
