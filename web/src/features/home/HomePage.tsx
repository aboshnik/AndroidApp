import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { useLocation, useNavigate } from 'react-router-dom'
import { deletePost, getFeed, registerEvent, votePost } from '../../api/post'
import { getSession } from '../../shared/session'
import type { PollItem, PostItem } from '../../api/types'
import { formatHm } from '../../shared/time'
import { resolveAssetUrl } from '../../shared/urls'

function detectMimeFromBytes(bytes: Uint8Array): string | null {
  if (bytes.length >= 3 && bytes[0] === 0xff && bytes[1] === 0xd8 && bytes[2] === 0xff) return 'image/jpeg'
  if (bytes.length >= 8 && bytes[0] === 0x89 && bytes[1] === 0x50 && bytes[2] === 0x4e && bytes[3] === 0x47) return 'image/png'
  if (bytes.length >= 6 && bytes[0] === 0x47 && bytes[1] === 0x49 && bytes[2] === 0x46) return 'image/gif'
  if (
    bytes.length >= 12 &&
    bytes[0] === 0x52 &&
    bytes[1] === 0x49 &&
    bytes[2] === 0x46 &&
    bytes[3] === 0x46 &&
    bytes[8] === 0x57 &&
    bytes[9] === 0x45 &&
    bytes[10] === 0x42 &&
    bytes[11] === 0x50
  ) {
    return 'image/webp'
  }
  if (bytes.length >= 12 && bytes[4] === 0x66 && bytes[5] === 0x74 && bytes[6] === 0x79 && bytes[7] === 0x70) {
    const brand = String.fromCharCode(bytes[8], bytes[9], bytes[10], bytes[11]).toLowerCase()
    if (brand.startsWith('heic') || brand.startsWith('heif') || brand.startsWith('mif1') || brand.startsWith('msf1')) {
      return 'image/heic'
    }
    if (brand.startsWith('qt')) return 'video/quicktime'
    return 'video/mp4'
  }
  return null
}

function MediaAsset({ url }: { url: string }) {
  const resolvedUrl = url.startsWith('/uploads/') ? `${window.location.origin}${url}` : url
  const normalized = resolvedUrl.split('?')[0].toLowerCase()
  const isBin = normalized.endsWith('.bin')
  const [src, setSrc] = useState(resolvedUrl)
  const [kind, setKind] = useState<'image' | 'video' | 'file'>(() => {
    return ['.mp4', '.mov', '.m4v', '.webm', '.ogg', '.ogv', '.avi', '.3gp', '.mkv'].some((ext) => normalized.endsWith(ext))
      ? 'video'
      : 'image'
  })
  const [visible, setVisible] = useState(true)

  useEffect(() => {
    let revokedUrl: string | null = null
    let cancelled = false
    setSrc(isBin ? '' : resolvedUrl)
    setVisible(true)
    if (!isBin) return () => {}

    void (async () => {
      try {
        const response = await fetch(resolvedUrl)
        if (!response.ok) return
        const buffer = await response.arrayBuffer()
        if (cancelled) return
        const bytes = new Uint8Array(buffer)
        const detected = detectMimeFromBytes(bytes)
        const mime = detected || 'application/octet-stream'
        const blob = new Blob([buffer], { type: mime })
        revokedUrl = URL.createObjectURL(blob)
        if (mime.startsWith('video/')) setKind('video')
        else if (mime.startsWith('image/')) setKind('image')
        else setKind('file')
        setSrc(revokedUrl)
      } catch {
        // Keep original URL fallback for .bin if blob decode fails.
        setSrc(resolvedUrl)
      }
    })()

    return () => {
      cancelled = true
      if (revokedUrl) URL.revokeObjectURL(revokedUrl)
    }
  }, [resolvedUrl, isBin])

  if (!visible) return null
  if (!src) return null
  if (kind === 'video') {
    return (
      <video className="post-media" controls playsInline preload="metadata">
        <source src={src} />
      </video>
    )
  }
  if (kind === 'file') {
    return (
      <a href={src} target="_blank" rel="noreferrer" className="muted">
        Открыть медиа
      </a>
    )
  }
  return <img src={src} alt="" className="post-media" onError={() => setVisible(false)} />
}

export function HomePage() {
  const navigate = useNavigate()
  const location = useLocation()
  const basePath = location.pathname.startsWith('/iphone') ? '/iphone' : ''
  const isIphonePreview = basePath === '/iphone'
  const session = getSession()
  if (!session) return null
  const login = session.login
  const canDeletePosts = !!session.employee.canCreatePosts

  const [detailsPost, setDetailsPost] = useState<PostItem | null>(null)
  const [pollPost, setPollPost] = useState<PostItem | null>(null)

  const feedQuery = useQuery({
    queryKey: ['feed', login],
    queryFn: () => getFeed(login),
    enabled: !!login,
    refetchInterval: 5000,
  })

  const deleteMutation = useMutation({
    mutationFn: async (postId: number) => {
      const resp = await deletePost(postId, login)
      if (!resp.success) throw new Error(resp.message || 'Не удалось удалить новость')
      return resp
    },
    onSuccess: () => {
      void feedQuery.refetch()
    },
  })

  const voteMutation = useMutation({
    mutationFn: async ({ postId, optionId }: { postId: number; optionId: number }) => {
      const resp = await votePost(postId, { login, optionId })
      if (!resp.success) throw new Error(resp.message || 'Не удалось проголосовать')
      return resp
    },
    onSuccess: () => {
      void feedQuery.refetch()
    },
  })
  const registerEventMutation = useMutation({
    mutationFn: async (postId: number) => {
      const resp = await registerEvent(postId, { login })
      if (!resp.success) throw new Error(resp.message || 'Не удалось зарегистрироваться')
      return resp
    },
    onSuccess: (resp) => {
      window.alert(resp.message || 'Регистрация выполнена')
      void feedQuery.refetch()
    },
    onError: (e) => {
      window.alert(e instanceof Error ? e.message : 'Ошибка регистрации')
    },
  })

  const posts = useMemo(() => feedQuery.data?.posts ?? [], [feedQuery.data?.posts])

  function mediaOf(post: PostItem): string[] {
    const list = post.mediaUrls?.map((v) => resolveAssetUrl(v)).filter(Boolean) ?? []
    if (list.length > 0) return list
    const single = resolveAssetUrl(post.imageUrl)
    return single ? [single] : []
  }

  function getDisplayPostText(post: PostItem): string {
    const source = String(post.content ?? '').trim()
    if (!source) return 'Без текста'

    const variants = [post.authorName, post.authorLogin]
      .map((v) => String(v ?? '').trim())
      .filter(Boolean)

    for (const author of variants) {
      const escaped = author.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
      const pattern = new RegExp(`^${escaped}(?:\\s*[-:,|]\\s*|\\s+)`, 'i')
      const trimmed = source.replace(pattern, '').trim()
      if (trimmed && trimmed.length < source.length) return trimmed
    }

    return source
  }

  function getPreviewPostText(post: PostItem): string {
    const source = getDisplayPostText(post)
    const collapsed = source.replace(/\s+/g, ' ').trim()
    if (!isIphonePreview || collapsed.length <= 230) return source
    return `${collapsed.slice(0, 230).trimEnd()}...`
  }

  function renderMedia(url: string) {
    return <MediaAsset key={url} url={url} />
  }

  function renderPoll(post: PostItem, poll: PollItem) {
    const total = Math.max(1, poll.totalVotes || 0)
    const canVote = !poll.hasVoted || poll.allowRevote
    return (
      <div className="poll-box">
        <div className="poll-title">{poll.question}</div>
        {poll.description ? <div className="muted">{poll.description}</div> : null}
        <div className="poll-options">
          {poll.options.map((opt) => {
            const pct = poll.canViewResults ? Math.round((opt.votesCount / total) * 100) : 0
            return (
              <button
                type="button"
                key={opt.id}
                className={`poll-option ${poll.selectedOptionId === opt.id ? 'selected' : ''}`}
                disabled={!canVote || voteMutation.isPending}
                onClick={() => voteMutation.mutate({ postId: post.id, optionId: opt.id })}
              >
                <span>
                  {opt.text}
                  {poll.selectedOptionId === opt.id ? ' ✓' : ''}
                </span>
                <span className="muted">{poll.canViewResults ? `${pct}% · ${opt.votesCount}` : 'скрыто'}</span>
              </button>
            )
          })}
        </div>
      </div>
    )
  }

  return (
    <section className="home-page">
      <div className="home-topbar">
        <h2>Главная</h2>
        <button type="button" onClick={() => navigate(`${basePath}/create-post`)}>
          + Новость
        </button>
      </div>

      {feedQuery.isLoading ? <p className="screen-state">Загрузка ленты...</p> : null}
      {feedQuery.error ? <p className="screen-state error">{(feedQuery.error as Error).message}</p> : null}
      {!feedQuery.isLoading && posts.length === 0 ? <p className="screen-state">Пока нет новостей</p> : null}

      <div className="home-feed">
        {posts.map((post) => (
          <article key={post.id} className="post-card">
            <div className="post-head">
              <span className="muted">{formatHm(post.createdAt)}</span>
            </div>
            {post.isImportant ? <span className="post-important">Важно</span> : null}
            <p className={`post-text ${isIphonePreview ? 'post-text-preview' : ''}`}>{getPreviewPostText(post)}</p>
            {mediaOf(post).slice(0, 1).map((url) => renderMedia(url))}
            {post.poll ? renderPoll(post, post.poll) : null}
            <div className="post-actions">
              {post.isEvent ? (
                <button
                  type="button"
                  disabled={!!post.isRegistered || registerEventMutation.isPending}
                  onClick={() => registerEventMutation.mutate(post.id)}
                >
                  {post.isRegistered ? 'Вы зарегистрированы' : registerEventMutation.isPending ? 'Регистрация...' : 'Зарегистрироваться'}
                </button>
              ) : null}
              <button type="button" onClick={() => setDetailsPost(post)}>
                Подробнее
              </button>
              {post.poll ? (
                <button type="button" onClick={() => setPollPost(post)}>
                  Опрос
                </button>
              ) : null}
              {canDeletePosts ? (
                <button
                  type="button"
                  onClick={() => {
                    if (!window.confirm('Удалить новость?')) return
                    deleteMutation.mutate(post.id)
                  }}
                >
                  Удалить
                </button>
              ) : null}
            </div>
          </article>
        ))}
      </div>

      {detailsPost ? (
        <div className="overlay" onClick={() => setDetailsPost(null)}>
          <div className="modal-card" onClick={(e) => e.stopPropagation()}>
            <h3>Подробнее</h3>
            <p className="post-text">{getDisplayPostText(detailsPost)}</p>
            {mediaOf(detailsPost).map((url) => renderMedia(url))}
            <button type="button" onClick={() => setDetailsPost(null)}>
              Закрыть
            </button>
          </div>
        </div>
      ) : null}

      {pollPost?.poll ? (
        <div className="overlay" onClick={() => setPollPost(null)}>
          <div className="modal-card" onClick={(e) => e.stopPropagation()}>
            <h3>Опрос</h3>
            {renderPoll(pollPost, pollPost.poll)}
            <button type="button" onClick={() => setPollPost(null)}>
              Закрыть
            </button>
          </div>
        </div>
      ) : null}
    </section>
  )
}
