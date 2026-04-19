import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { apiClient } from './client'

type ChatUpdatedCallback = () => void

let connection: HubConnection | null = null
let activeLogin = ''
const listeners = new Set<ChatUpdatedCallback>()
let realtimeDisabled = false

function resolveHubUrl() {
  const base = String(apiClient.defaults.baseURL ?? window.location.origin)
  return new URL('/hubs/chat', base).toString()
}

async function ensureConnection(login: string) {
  const normalizedLogin = login.trim()
  if (!normalizedLogin || realtimeDisabled) return

  if (!connection) {
    connection = new HubConnectionBuilder()
      .withUrl(resolveHubUrl(), {
        // Backend may reply with wildcard CORS; credentials must stay disabled for browser SignalR.
        withCredentials: false,
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    connection.on('chat:updated', () => {
      for (const cb of listeners) cb()
    })

    connection.onreconnected(async () => {
      if (!activeLogin) return
      try {
        await connection?.invoke('Join', activeLogin)
      } catch {
        // ignore transient reconnect race
      }
    })
  }

  if (connection.state === 'Disconnected') {
    try {
      await connection.start()
    } catch {
      // Don't break the app if realtime is unavailable (CORS/ngrok/etc). Polling continues to work.
      realtimeDisabled = true
      return
    }
  }
  if (connection.state !== 'Connected') return
  if (activeLogin !== normalizedLogin) {
    if (activeLogin) {
      await connection.invoke('Leave', activeLogin).catch(() => undefined)
    }
    activeLogin = normalizedLogin
    await connection.invoke('Join', activeLogin)
  }
}

export function subscribeChatUpdates(login: string, callback: ChatUpdatedCallback) {
  listeners.add(callback)
  void ensureConnection(login).catch(() => undefined)
  return () => {
    listeners.delete(callback)
    if (listeners.size === 0 && connection && connection.state === 'Connected') {
      if (activeLogin) {
        void connection.invoke('Leave', activeLogin).catch(() => undefined)
      }
    }
  }
}
