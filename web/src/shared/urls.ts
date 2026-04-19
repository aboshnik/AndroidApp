import { apiClient } from '../api/client'

export function resolveAssetUrl(raw?: string | null): string {
  const value = (raw ?? '').trim()
  if (!value) return ''
  try {
    if (/^https?:\/\//i.test(value)) return value
    const base = String(apiClient.defaults.baseURL ?? window.location.origin)
    const normalized = value.replace(/\\/g, '/')
    // Most backend media paths are app-root relative even if they come without a leading slash.
    const absoluteLike = normalized.startsWith('/') ? normalized : `/${normalized}`
    return new URL(absoluteLike, base).toString()
  } catch {
    return value
  }
}
