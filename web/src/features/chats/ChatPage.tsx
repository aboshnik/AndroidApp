import { useEffect, useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import type { KeyboardEvent } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom'
import { deleteMessage, editMessage, getBotProfile, getMessages, getThreads, sendMessage, updateBotProfile, uploadBotAvatar, uploadChatMedia } from '../../api/chat'
import { subscribeChatUpdates } from '../../api/chatRealtime'
import { getSession } from '../../shared/session'
import { formatHm } from '../../shared/time'
import { BotAvatar } from '../../shared/BotAvatar'
import type { BotProfileItem, MessageItem } from '../../api/types'

type ChatMedia = { url: string; kind: string }
type ReplyMeta = { replyToId: number; replyText: string; replySender: string }

function parseMessageMedia(metaJson?: string | null): ChatMedia[] {
  if (!metaJson) return []
  try {
    const obj = JSON.parse(metaJson) as { media?: Array<{ url?: string; kind?: string }>; mediaUrl?: string; mediaKind?: string }
    const list: ChatMedia[] = []
    if (Array.isArray(obj.media)) {
      for (const item of obj.media) {
        if (item?.url) list.push({ url: item.url, kind: item.kind ?? 'image' })
      }
    }
    if (list.length === 0 && obj.mediaUrl) list.push({ url: obj.mediaUrl, kind: obj.mediaKind ?? 'image' })
    return list
  } catch {
    return []
  }
}

function parseReplyMeta(metaJson?: string | null): ReplyMeta | null {
  if (!metaJson) return null
  try {
    const obj = JSON.parse(metaJson) as { replyToId?: number; replyText?: string; replySender?: string }
    const replyToId = Number(obj.replyToId ?? 0)
    const replyText = String(obj.replyText ?? '').trim()
    const replySender = String(obj.replySender ?? '').trim()
    if (replyToId <= 0 || !replyText) return null
    return { replyToId, replyText, replySender }
  } catch {
    return null
  }
}

export function ChatPage() {
  const { threadId } = useParams()
  const location = useLocation()
  const navigate = useNavigate()
  const session = getSession()
  const login = session?.login ?? ''
  const employeeId = session?.employee?.employeeId ?? ''
  const selfAliases = useMemo(
    () =>
      new Set(
        [login, employeeId]
          .map((v) => v.trim().toLowerCase())
          .filter(Boolean),
      ),
    [employeeId, login],
  )
  const currentThreadId = Number(threadId ?? '0')
  const basePath = location.pathname.startsWith('/iphone') ? '/iphone' : ''
  const isIphone = basePath === '/iphone'
  const [text, setText] = useState('')
  const [file, setFile] = useState<File | null>(null)
  const [menuForId, setMenuForId] = useState<number | null>(null)
  const [replyTo, setReplyTo] = useState<ReplyMeta | null>(null)
  const [jumpHighlightId, setJumpHighlightId] = useState<number | null>(null)
  const [botProfile, setBotProfile] = useState<BotProfileItem | null>(null)
  const [botProfileOpen, setBotProfileOpen] = useState(false)

  const threadsQuery = useQuery({
    queryKey: ['threads', login],
    queryFn: () => getThreads(login),
    enabled: !!login,
    refetchInterval: 12000,
  })

  const query = useQuery({
    queryKey: ['messages', currentThreadId, login],
    queryFn: () => getMessages(currentThreadId, login),
    enabled: currentThreadId > 0 && !!login,
    refetchInterval: 10000,
  })
  const refetchThreads = threadsQuery.refetch
  const refetchMessages = query.refetch

  useEffect(() => {
    if (!login || currentThreadId <= 0) return
    return subscribeChatUpdates(login, () => {
      void refetchThreads()
      void refetchMessages()
    })
  }, [currentThreadId, login, refetchMessages, refetchThreads])

  const sendMutation = useMutation({
    mutationFn: async () => {
      let media: Array<{ url: string; kind: string }> = []
      if (file) {
        const up = await uploadChatMedia(currentThreadId, login, file)
        if (!up.success || !up.url) throw new Error(up.message || 'Ошибка загрузки файла')
        media = [{ url: up.url, kind: up.kind ?? 'image' }]
      }
      const metaObj: Record<string, unknown> = {}
      if (replyTo) {
        metaObj.replyToId = replyTo.replyToId
        metaObj.replyText = replyTo.replyText
        metaObj.replySender = replyTo.replySender
      }
      if (media.length > 0) {
        metaObj.mediaUrl = media[0].url
        metaObj.mediaKind = media[0].kind
        metaObj.media = media
      }
      const meta = Object.keys(metaObj).length > 0 ? JSON.stringify(metaObj) : null
      const resp = await sendMessage(currentThreadId, { login, text: text.trim(), metaJson: meta })
      if (!resp.success) throw new Error(resp.message || 'Не удалось отправить')
      return resp
    },
    onSuccess: () => {
      setText('')
      setFile(null)
      setReplyTo(null)
      void query.refetch()
    },
  })

  const messages = useMemo(() => query.data?.messages ?? [], [query.data?.messages])
  const currentThread = (threadsQuery.data?.threads ?? []).find((t) => t.id === currentThreadId)

  function handleAction(item: MessageItem, action: string) {
    if (!action) return
    if (action.startsWith('open_apk:', 0)) {
      const url = action.replace('open_apk:', '').trim()
      if (url) window.open(url, '_blank', 'noopener,noreferrer')
      return
    }
    if (action === 'relogin_account' && item.metaJson) {
      try {
        const obj = JSON.parse(item.metaJson) as {
          actionLogin?: string
          actionPassword?: string
          actionAutoSeconds?: number
        }
        const params = new URLSearchParams()
        if (obj.actionLogin) params.set('login', obj.actionLogin)
        if (obj.actionPassword) params.set('password', obj.actionPassword)
        params.set('reloginBypass', '1')
        params.set('auto', String(obj.actionAutoSeconds ?? 0))
        navigate(`/login?${params.toString()}`, { replace: true })
      } catch {
        // ignore malformed action payload
      }
    }
  }

  async function handleEditMessage(msg: MessageItem) {
    const current = (msg.text ?? '').trim()
    const next = window.prompt('Редактировать текст', current)
    if (next == null) return
    const value = next.trim()
    if (!value || value === current) return
    try {
      const resp = await editMessage(currentThreadId, msg.id, { login, text: value })
      if (!resp.success) throw new Error(resp.message || 'Не удалось редактировать')
      await Promise.all([refetchMessages(), refetchThreads()])
    } catch (e) {
      const message = e instanceof Error ? e.message : 'Ошибка редактирования'
      window.alert(message)
    }
  }

  async function handleDeleteMessage(msg: MessageItem) {
    if (!window.confirm('Удалить сообщение?')) return
    try {
      const resp = await deleteMessage(currentThreadId, msg.id, login)
      if (!resp.success) throw new Error(resp.message || 'Не удалось удалить')
      await Promise.all([refetchMessages(), refetchThreads()])
    } catch (e) {
      window.alert(e instanceof Error ? e.message : 'Ошибка удаления')
    }
  }

  async function openBotProfile() {
    if (!currentThread?.botId) return
    try {
      const resp = await getBotProfile(currentThread.botId, login)
      if (!resp.success || !resp.profile) throw new Error(resp.message || 'Не удалось загрузить профиль бота')
      setBotProfile(resp.profile)
      setBotProfileOpen(true)
    } catch (e) {
      window.alert(e instanceof Error ? e.message : 'Ошибка загрузки')
    }
  }

  async function saveBotProfile() {
    if (!botProfile) return
    try {
      const resp = await updateBotProfile(botProfile.botId, {
        login,
        description: botProfile.description ?? '',
        isOfficial: botProfile.isOfficial,
        displayName: botProfile.displayName,
      })
      if (!resp.success) throw new Error(resp.message || 'Не удалось обновить профиль')
      setBotProfile(resp.profile ?? botProfile)
      await refetchThreads()
    } catch (e) {
      window.alert(e instanceof Error ? e.message : 'Ошибка сохранения')
    }
  }

  async function onBotAvatarPicked(file: File | null) {
    if (!file || !botProfile) return
    try {
      const resp = await uploadBotAvatar(botProfile.botId, login, file)
      if (!resp.success) throw new Error(resp.message || 'Не удалось загрузить фото')
      setBotProfile(resp.profile ?? botProfile)
      await refetchThreads()
    } catch (e) {
      window.alert(e instanceof Error ? e.message : 'Ошибка загрузки фото')
    }
  }

  function handleCopyText(msg: MessageItem) {
    const value = (msg.text ?? '').trim()
    if (!value) return
    void navigator.clipboard?.writeText(value)
  }

  function handleSelectText(msgId: number) {
    const el = document.getElementById(`msg-text-${msgId}`)
    if (!el) return
    const selection = window.getSelection()
    if (!selection) return
    const range = document.createRange()
    range.selectNodeContents(el)
    selection.removeAllRanges()
    selection.addRange(range)
  }

  function handleReplyMessage(msg: MessageItem) {
    const safeText = (msg.text ?? '').trim()
    const media = parseMessageMedia(msg.metaJson)
    const fallback =
      safeText ||
      (media.some((m) => m.kind.toLowerCase() === 'video')
        ? 'Видео'
        : media.some((m) => m.kind.toLowerCase() === 'apk')
          ? 'APK файл'
          : media.length > 0
            ? 'Фото'
            : 'Сообщение')
    setReplyTo({
      replyToId: msg.id,
      replyText: fallback.slice(0, 120),
      replySender: (msg.senderName || msg.senderId || msg.senderType || 'Пользователь').trim(),
    })
    setMenuForId(null)
  }

  function handleReplyJump(replyId: number) {
    const el = document.getElementById(`msg-${replyId}`)
    if (!el) return
    el.scrollIntoView({ behavior: 'smooth', block: 'center' })
    setJumpHighlightId(replyId)
    window.setTimeout(() => {
      setJumpHighlightId((prev) => (prev === replyId ? null : prev))
    }, 2200)
  }

  function onSubmit(e: FormEvent) {
    e.preventDefault()
    if (!text.trim() && !file) return
    sendMutation.mutate()
  }

  function onComposerKeyDown(e: KeyboardEvent<HTMLTextAreaElement>) {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault()
      if (!sendMutation.isPending && (text.trim() || file)) {
        sendMutation.mutate()
      }
    }
  }

  if (!session) return null
  if (query.isLoading) return <p className="screen-state">Загрузка диалога...</p>

  return (
    <section className="desktop-chat-layout">
      {!isIphone ? (
        <aside className="desktop-sidebar">
          <div className="desktop-search">Search</div>
          <div className="threads">
            {(threadsQuery.data?.threads ?? []).map((thread) => (
              <Link key={thread.id} to={`${basePath}/chats/${thread.id}`} className={`thread-card ${thread.id === currentThreadId ? 'active' : ''}`}>
                <div className="thread-avatar">
                  <BotAvatar
                    botId={thread.type.toLowerCase() === 'bot' ? thread.botId : null}
                    avatarUrl={thread.avatarUrl}
                    title={thread.title}
                    className="thread-avatar-img"
                  />
                </div>
                <div className="thread-main">
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
                    {thread.unreadCount > 0 ? <span className="badge">{thread.unreadCount}</span> : null}
                  </div>
                </div>
              </Link>
            ))}
          </div>
        </aside>
      ) : null}

      <div className="desktop-chat-pane">
        <div className="chat-page">
          {isIphone ? (
            <div className="chat-back-row">
              <Link to={`${basePath}/chats`} className="chat-back-link">← Чаты</Link>
            </div>
          ) : null}
          <div className="chat-header-desktop" onClick={() => { if (currentThread?.type?.toLowerCase() === 'bot') void openBotProfile() }}>
            <div className="thread-avatar">
              <BotAvatar
                botId={currentThread?.type?.toLowerCase() === 'bot' ? currentThread?.botId : null}
                avatarUrl={currentThread?.avatarUrl}
                title={currentThread?.title ?? 'Чат'}
                className="thread-avatar-img"
              />
            </div>
            <div>
              <strong>
                {currentThread?.title ?? 'Чат'}
                {currentThread?.type?.toLowerCase() === 'bot' ? <span className="role-chip bot" title="Бот" aria-label="Бот">🤖</span> : null}
                {currentThread?.isTechAdmin ? <span className="role-chip tech" title="Техадмин" aria-label="Техадмин">🔧</span> : null}
              </strong>
              <div className={`muted ${currentThread?.isOnline ? 'online' : 'offline'}`}>
                {currentThread?.isOnline ? 'в сети' : 'не в сети'}
              </div>
            </div>
          </div>
          <div className="messages-list">
            {messages.map((msg) => {
              const safeText = (msg.text ?? '').trim()
              const isSelf =
                msg.senderType.toLowerCase() === 'user' &&
                selfAliases.has((msg.senderId ?? '').trim().toLowerCase())
              const media = parseMessageMedia(msg.metaJson)
              const replyMeta = parseReplyMeta(msg.metaJson)
              let action = ''
              let label = ''
              if (msg.metaJson) {
                try {
                  const obj = JSON.parse(msg.metaJson) as { action?: string; actionLabel?: string }
                  action = obj.action ?? ''
                  label = obj.actionLabel ?? ''
                } catch {
                  // ignore
                }
              }
              const apk = media.find((m) => m.kind.toLowerCase() === 'apk')
              if (!action && apk) {
                action = `open_apk:${apk.url}`
                label = 'Скачать APK'
              }
              return (
                <article id={`msg-${msg.id}`} className={`message-card ${isSelf ? 'out' : 'in'} ${jumpHighlightId === msg.id ? 'jump-highlight' : ''}`} key={msg.id}>
                  <div className="message-head muted">
                    <strong>
                      {msg.senderName || msg.senderId || msg.senderType}
                      {msg.senderType.toLowerCase() === 'bot' ? <span className="role-chip bot" title="Бот" aria-label="Бот">🤖</span> : null}
                      {msg.senderIsTechAdmin ? <span className="role-chip tech" title="Техадмин" aria-label="Техадмин">🔧</span> : null}
                    </strong>
                  </div>
                  {replyMeta ? (
                    <button
                      type="button"
                      className="reply-block"
                      onClick={() => handleReplyJump(replyMeta.replyToId)}
                      title="Перейти к сообщению"
                    >
                      <div className="reply-label">
                        Ответ {replyMeta.replySender || ''}
                      </div>
                      <div className="reply-text">{replyMeta.replyText}</div>
                    </button>
                  ) : null}
                  {safeText ? <p id={`msg-text-${msg.id}`} className="message-text">{safeText}</p> : null}
                  {media.map((m) => (
                    <div key={m.url}>
                      {m.kind.toLowerCase() === 'image' ? <img src={m.url} alt="attachment" className="media" /> : null}
                      {m.kind.toLowerCase() === 'video' ? <video src={m.url} className="media" controls playsInline /> : null}
                      {m.kind.toLowerCase() === 'apk' ? <p className="muted">APK файл</p> : null}
                    </div>
                  ))}
                  {action && label ? (
                    <button type="button" onClick={() => handleAction(msg, action)}>
                      {label}
                    </button>
                  ) : null}
                  <div className="message-meta muted">
                    <span>{formatHm(msg.createdAtUtc)}</span>
                    {msg.isEdited ? <span>редакт.</span> : null}
                    {isSelf ? <span>{msg.isRead ? '✓✓' : '✓'}</span> : null}
                  </div>
                  <div className="message-tools">
                    <button type="button" className="message-menu-btn" onClick={() => setMenuForId((v) => (v === msg.id ? null : msg.id))}>
                      ⋯
                    </button>
                    {menuForId === msg.id ? (
                      <div className="message-menu">
                        <button type="button" onClick={() => handleReplyMessage(msg)}>Ответить</button>
                        <button type="button" onClick={() => { handleSelectText(msg.id); setMenuForId(null) }}>Выделить текст</button>
                        <button type="button" onClick={() => { handleCopyText(msg); setMenuForId(null) }}>Копировать текст</button>
                        {isSelf ? (
                          <button type="button" onClick={() => { void handleDeleteMessage(msg); setMenuForId(null) }}>Удалить</button>
                        ) : null}
                        {isSelf && safeText ? (
                          <button type="button" onClick={() => { void handleEditMessage(msg); setMenuForId(null) }}>Редактировать текст</button>
                        ) : null}
                      </div>
                    ) : null}
                  </div>
                </article>
              )
            })}
          </div>

          <form className="composer" onSubmit={onSubmit}>
            {replyTo ? (
              <div className="reply-preview">
                <div className="reply-preview-main">
                  <div className="reply-label">Ответ {replyTo.replySender || 'Пользователь'}</div>
                  <div className="reply-text">{replyTo.replyText}</div>
                </div>
                <button type="button" className="reply-cancel" onClick={() => setReplyTo(null)}>
                  ✕
                </button>
              </div>
            ) : null}
            {file ? <p className="muted">Вложение: {file.name}</p> : null}
            <div className="composer-row">
              <label className="attach-btn">
                +
                <input
                  type="file"
                  accept="image/*,video/*,.apk,application/vnd.android.package-archive"
                  onChange={(e) => setFile(e.target.files?.[0] ?? null)}
                />
              </label>
              <textarea
                value={text}
                onChange={(e) => setText(e.target.value)}
                onKeyDown={onComposerKeyDown}
                placeholder="Сообщение"
                rows={2}
              />
              <button disabled={sendMutation.isPending} type="submit">
                {sendMutation.isPending ? '...' : '➤'}
              </button>
            </div>
            {sendMutation.error ? <p className="error">{(sendMutation.error as Error).message}</p> : null}
          </form>
        </div>
      </div>
      {botProfileOpen && botProfile ? (
        <div className="overlay" onClick={() => setBotProfileOpen(false)}>
          <div className="modal-card" onClick={(e) => e.stopPropagation()}>
            <h3>Профиль бота</h3>
            <label>
              Название
              <input
                value={botProfile.displayName}
                onChange={(e) => setBotProfile((prev) => (prev ? { ...prev, displayName: e.target.value } : prev))}
              />
            </label>
            <label>
              Описание
              <textarea
                rows={4}
                value={botProfile.description ?? ''}
                onChange={(e) => setBotProfile((prev) => (prev ? { ...prev, description: e.target.value } : prev))}
              />
            </label>
            <label className="remember-row">
              <input
                type="checkbox"
                checked={!!botProfile.isOfficial}
                onChange={(e) => setBotProfile((prev) => (prev ? { ...prev, isOfficial: e.target.checked } : prev))}
              />
              <span>Официальный бот</span>
            </label>
            <label>
              Фото бота
              <input type="file" accept="image/*" onChange={(e) => void onBotAvatarPicked(e.target.files?.[0] ?? null)} />
            </label>
            <div className="post-actions">
              <button type="button" onClick={() => setBotProfileOpen(false)}>
                Закрыть
              </button>
              <button type="button" onClick={() => void saveBotProfile()}>
                Сохранить
              </button>
            </div>
          </div>
        </div>
      ) : null}
    </section>
  )
}
