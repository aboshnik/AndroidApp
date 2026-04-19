import { useMemo, useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { useLocation, useNavigate } from 'react-router-dom'
import { deletePost, getFeed, votePost } from '../../api/post'
import { getSession } from '../../shared/session'
import type { PollItem, PostItem } from '../../api/types'
import { formatHm } from '../../shared/time'

export function HomePage() {
  const navigate = useNavigate()
  const location = useLocation()
  const basePath = location.pathname.startsWith('/iphone') ? '/iphone' : ''
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

  const posts = useMemo(() => feedQuery.data?.posts ?? [], [feedQuery.data?.posts])

  function mediaOf(post: PostItem): string[] {
    const list = post.mediaUrls?.filter(Boolean) ?? []
    if (list.length > 0) return list
    return post.imageUrl ? [post.imageUrl] : []
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
            <p className="post-text">{getDisplayPostText(post)}</p>
            {mediaOf(post).slice(0, 1).map((url) => (
              <img key={url} src={url} alt="post media" className="post-media" />
            ))}
            {post.poll ? renderPoll(post, post.poll) : null}
            <div className="post-actions">
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
            <p>{getDisplayPostText(detailsPost)}</p>
            {mediaOf(detailsPost).map((url) => (
              <img key={url} src={url} alt="post media" className="post-media" />
            ))}
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
