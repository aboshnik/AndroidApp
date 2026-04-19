import type { EmployeeLoginResult } from '../api/types'

const SESSION_KEY = 'steklo.web.session'
const DEVICE_KEY = 'steklo.web.deviceId'

export type Session = {
  login: string
  password?: string
  deviceId: string
  deviceName: string
  employee: EmployeeLoginResult
}

export function getDeviceId() {
  const oldValue = localStorage.getItem(DEVICE_KEY)
  if (oldValue) return oldValue
  const next = crypto.randomUUID()
  localStorage.setItem(DEVICE_KEY, next)
  return next
}

export function getDeviceName() {
  return navigator.userAgent.slice(0, 120)
}

export function saveSession(session: Session) {
  localStorage.setItem(SESSION_KEY, JSON.stringify(session))
}

export function getSession(): Session | null {
  try {
    const raw = localStorage.getItem(SESSION_KEY)
    if (!raw) return null
    return JSON.parse(raw) as Session
  } catch {
    return null
  }
}

export function clearSession() {
  localStorage.removeItem(SESSION_KEY)
}
