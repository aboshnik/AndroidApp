import { apiClient } from '../api/client'

export function resolveAssetUrl(raw?: string | null): string {
  const value = (raw ?? '').trim()
  if (!value) return ''
  try {
    const normalized = value.replace(/\\/g, '/')
    const lower = normalized.toLowerCase()
    const uploadsIndex = lower.indexOf('/uploads/')
    if (uploadsIndex >= 0) {
      // Keep /uploads relative so dev-server proxy handles it without CORS.
      return normalized.slice(uploadsIndex)
    }

    // Some legacy records can be like "uploads/file.jpg" without leading slash.
    if (lower.startsWith('uploads/')) {
      return `/${normalized}`
    }

    // Also support Windows absolute/relative file paths containing ".../wwwroot/uploads/...".
    const wwwrootUploadsIndex = lower.indexOf('wwwroot/uploads/')
    if (wwwrootUploadsIndex >= 0) {
      return `/${normalized.slice(wwwrootUploadsIndex + 'wwwroot/'.length)}`
    }

    if (/^https?:\/\//i.test(normalized)) return normalized

    const base = String(apiClient.defaults.baseURL ?? window.location.origin)
    // Most backend media paths are app-root relative even if they come without a leading slash.
    const absoluteLike = normalized.startsWith('/') ? normalized : `/${normalized}`
    return new URL(absoluteLike, base).toString()
  } catch {
    return value
  }
}
