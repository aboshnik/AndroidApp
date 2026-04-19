import { useEffect, useMemo, useState } from 'react'
import { resolveAssetUrl } from './urls'

type BotAvatarProps = {
  botId?: string | null
  avatarUrl?: string | null
  title: string
  className?: string
}

function unique(items: string[]) {
  return Array.from(new Set(items.filter(Boolean)))
}

export function BotAvatar({ avatarUrl, title, className }: BotAvatarProps) {
  const candidates = useMemo(() => {
    const list: string[] = []
    if (avatarUrl) list.push(resolveAssetUrl(avatarUrl))
    return unique(list)
  }, [avatarUrl])

  const [idx, setIdx] = useState(0)
  const [displaySrc, setDisplaySrc] = useState('')

  useEffect(() => {
    let cancelled = false
    let objectUrl: string | null = null
    const src = candidates[idx]
    if (!src) {
      setDisplaySrc('')
      return
    }

    ;(async () => {
      try {
        const response = await fetch(src, {
          headers: {
            'ngrok-skip-browser-warning': '1',
          },
        })
        if (!response.ok) throw new Error(`HTTP ${response.status}`)
        const ct = (response.headers.get('content-type') ?? '').toLowerCase()
        if (!ct.startsWith('image/')) throw new Error('Not image content')
        const blob = await response.blob()
        objectUrl = URL.createObjectURL(blob)
        if (!cancelled) setDisplaySrc(objectUrl)
      } catch {
        if (!cancelled) {
          setDisplaySrc('')
          setIdx((i) => (i + 1 < candidates.length ? i + 1 : i))
        }
      }
    })()

    return () => {
      cancelled = true
      if (objectUrl) URL.revokeObjectURL(objectUrl)
    }
  }, [candidates, idx])

  if (!displaySrc) return <>{title.slice(0, 1).toUpperCase()}</>
  return (
    <img
      src={displaySrc}
      alt={title}
      className={className}
      onError={() => setIdx((i) => (i + 1 < candidates.length ? i + 1 : i))}
    />
  )
}
