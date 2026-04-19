import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { clearThreadHistory, getThreads, openDirectThread, searchColleagues } from '../../api/chat'
import { subscribeChatUpdates } from '../../api/chatRealtime'
import { getSession } from '../../shared/session'
import { formatHm } from '../../shared/time'
import { BotAvatar } from '../../shared/BotAvatar'

export function ThreadsPage() {
  const navigate = useNavigate()
  const location = useLocation()
  const session = getSession()
  const login = session?.login ?? ''
  const basePath = location.pathname.startsWith('/iphone') ? '/iphone' : ''
  const isIphone = basePath === '/iphone'
  const [searchOpen, setSearchOpen] = useState(false)
  const [searchValue, setSearchValue] = useState('')
  const [menuThreadId, setMenuThreadId] = useState<number | null>(null)
  const query = useQuery({
    queryKey: ['threads', login],
    queryFn: () => getThreads(login),
    enabled: !!login,
    refetchInterval: 10000,
  })
  const refetchThreads = query.refetch
  const colleaguesQuery = useQuery({
    queryKey: ['colleagues', login, searchValue],
    queryFn: () => searchColleagues(login, searchValue),
    enabled: !!login && searchOpen,
  })

  const openDirectMutation = useMutation({
    mutationFn: async (colleagueLogin: string) => {
      const resp = await openDirectThread({ login, colleagueLogin })
      if (!resp.success || !resp.thread) throw new Error(resp.message || 'Не удалось открыть чат')
      return resp.thread
    },
    onSuccess: (thread) => {
      setSearchOpen(false)
      navigate(`${basePath}/chats/${thread.id}`)
    },
  })

  const clearHistoryMutation = useMutation({
    mutationFn: async (threadId: number) => {
      const resp = await clearThreadHistory(threadId, login)
      if (!resp.success) throw new Error(resp.message || 'Не удалось очистить историю')
      return resp
    },
    onSuccess: () => void refetchThreads(),
  })

  useEffect(() => {
    if (!login) return
    return subscribeChatUpdates(login, () => {
      void refetchThreads()
    })
  }, [login, refetchThreads])
  const colleagues = useMemo(() => colleaguesQuery.data?.colleagues ?? [], [colleaguesQuery.data?.colleagues])

  if (!session) return null
  if (query.isLoading) return <p className="screen-state">Загрузка чатов...</p>
  if (query.error) return <p className="screen-state error">{(query.error as Error).message}</p>
  if (!query.data?.success) return <p className="screen-state error">{query.data?.message ?? 'Ошибка загрузки чатов'}</p>

  const threads = query.data.threads ?? []
  return (
    <section className="desktop-chat-layout">
      <aside className="desktop-sidebar">
        {isIphone ? (
          <div className="iphone-chats-head">
            <h2>Чаты</h2>
            <div className="iphone-chats-head-actions">
              <button type="button" className="icon-btn" onClick={() => setSearchOpen(true)} aria-label="Поиск">
                🔍
              </button>
              <button type="button" className="icon-btn" aria-label="Меню">
                ⋮
              </button>
            </div>
          </div>
        ) : (
          <div className="desktop-search" role="button" onClick={() => setSearchOpen(true)}>
            Поиск коллег
          </div>
        )}
        <div className="threads">
          {threads.map((thread) => (
            <article key={thread.id} className={`thread-card ${isIphone ? 'iphone-thread-card' : ''}`}>
              <Link to={`${basePath}/chats/${thread.id}`} className="thread-avatar" style={{ textDecoration: 'none' }}>
                <BotAvatar
                  botId={thread.type.toLowerCase() === 'bot' ? thread.botId : null}
                  avatarUrl={thread.avatarUrl}
                  title={thread.title}
                  className="thread-avatar-img"
                />
              </Link>
              <Link to={`${basePath}/chats/${thread.id}`} className="thread-main" style={{ textDecoration: 'none', color: 'inherit' }}>
                <div className="thread-title-row">
                  <strong>
                    {thread.title}
                    {thread.type.toLowerCase() === 'bot' ? <span className="role-chip bot" title="Бот" aria-label="Бот">🤖</span> : null}
                    {thread.isTechAdmin ? <span className="role-chip tech" title="Техадмин" aria-label="Техадмин">🔧</span> : null}
                  </strong>
                  {thread.isOnline && <span className="dot online" />}
                  <span className="muted">{formatHm(thread.lastMessageAtUtc)}</span>
                </div>
                <div className="thread-meta-row">
                  <p className="muted ellipsis">{thread.lastMessageText || 'Нет сообщений'}</p>
                  {thread.lastMessageFromSelf ? <span className="muted">{thread.lastMessageIsRead ? '✓✓' : '✓'}</span> : null}
                  {thread.unreadCount > 0 ? <span className="badge">{thread.unreadCount}</span> : null}
                </div>
              </Link>
              <div className="thread-tools">
                <button type="button" className="message-menu-btn" onClick={() => setMenuThreadId((v) => (v === thread.id ? null : thread.id))}>
                  ⋯
                </button>
                {menuThreadId === thread.id ? (
                  <div className="message-menu">
                    <button
                      type="button"
                      onClick={() => {
                        if (!window.confirm('Удалить всю историю этого чата на вашей стороне?')) return
                        clearHistoryMutation.mutate(thread.id)
                        setMenuThreadId(null)
                      }}
                    >
                      Очистить историю
                    </button>
                  </div>
                ) : null}
              </div>
            </article>
          ))}
        </div>
      </aside>
      <div className="desktop-empty">Выберите чат слева</div>
      {isIphone ? (
        <button type="button" className="iphone-fab" onClick={() => setSearchOpen(true)} aria-label="Новый чат">
          ✏️
        </button>
      ) : null}
      {searchOpen ? (
        <div className="overlay" onClick={() => setSearchOpen(false)}>
          <div className={`modal-card ${isIphone ? 'iphone-search-modal' : ''}`} onClick={(e) => e.stopPropagation()}>
            <h3>Найти коллегу</h3>
            <input value={searchValue} onChange={(e) => setSearchValue(e.target.value)} placeholder="Поиск..." />
            <div className="threads">
              {colleagues.map((c) => (
                <button
                  type="button"
                  key={c.login}
                  className="thread-card"
                  onClick={() => openDirectMutation.mutate(c.login)}
                  style={{ textAlign: 'left' }}
                >
                  <div className="thread-avatar">{(c.fullName || c.login).trim().slice(0, 1).toUpperCase()}</div>
                  <div className="thread-main">
                    <div className="thread-title-row">
                      <strong>{c.fullName || c.login}</strong>
                      {c.isOnline ? <span className="dot online" /> : null}
                    </div>
                    <div className="thread-meta-row">
                      <p className="muted ellipsis">{c.position || c.employeeId || c.login}</p>
                      {c.isTechAdmin ? <span className="role-chip tech">🔧</span> : null}
                    </div>
                  </div>
                </button>
              ))}
            </div>
            <button type="button" onClick={() => setSearchOpen(false)}>
              Закрыть
            </button>
          </div>
        </div>
      ) : null}
    </section>
  )
}
