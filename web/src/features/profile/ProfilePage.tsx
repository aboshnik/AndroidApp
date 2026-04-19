import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import { getProfile, getWorkSchedule, uploadAvatar } from '../../api/employee'
import { clearSession, getSession } from '../../shared/session'
import { resolveAssetUrl } from '../../shared/urls'

const WEEKDAYS_RU = ['пн', 'вт', 'ср', 'чт', 'пт', 'сб', 'вс']

function ymd(date: Date): string {
  const y = date.getFullYear()
  const m = String(date.getMonth() + 1).padStart(2, '0')
  const d = String(date.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}

function parseIsoDate(raw?: string | null): Date | null {
  const value = String(raw ?? '').trim()
  if (!value) return null
  const parsed = new Date(`${value}T00:00:00`)
  return Number.isNaN(parsed.getTime()) ? null : parsed
}

export function ProfilePage() {
  const navigate = useNavigate()
  const session = getSession()
  const login = session?.login
  const employeeId = session?.employee.employeeId

  const profileQuery = useQuery({
    queryKey: ['profile', login, employeeId],
    queryFn: () => getProfile({ login, employeeId }),
    enabled: !!login,
    refetchInterval: 5000,
  })
  const [scheduleOpen, setScheduleOpen] = useState(false)
  const [calendarMonth, setCalendarMonth] = useState(() => {
    const now = new Date()
    return new Date(now.getFullYear(), now.getMonth(), 1)
  })
  const [selectedDayInfo, setSelectedDayInfo] = useState<{ title: string; message: string } | null>(null)
  const scheduleQuery = useQuery({
    queryKey: ['work-schedule', login, employeeId],
    queryFn: () => getWorkSchedule({ login, employeeId }),
    enabled: !!login && scheduleOpen,
  })
  const uploadMutation = useMutation({
    mutationFn: async (file: File) => {
      const resp = await uploadAvatar(login ?? '', file)
      if (!resp.success) throw new Error(resp.message || 'Не удалось загрузить фото')
      return resp
    },
    onSuccess: () => void profileQuery.refetch(),
  })
  const data = profileQuery.data
  const progressPct = useMemo(() => {
    if (!data?.profile) return 0
    const total = Math.max(1, (data.profile.experience ?? 0) + (data.profile.xpToNext ?? 0))
    return Math.max(0, Math.min(100, Math.round(((data.profile.experience ?? 0) / total) * 100)))
  }, [data?.profile])
  const avatarCandidates = useMemo(() => {
    const raw = data?.profile?.avatarUrl
    const rawValue = String(raw ?? '').trim()
    const fileLike = /^[^/\\]+\.(jpg|jpeg|png|webp|gif)$/i.test(rawValue)
    const uploadsPathMatch = rawValue.match(/\/uploads\/avatars\/[^?#]+/i)
    const uploadsPathCandidate = uploadsPathMatch?.[0] ?? ''
    const fileFromPath = rawValue.split(/[\\/]/).filter(Boolean).pop() ?? ''
    const fileName = fileLike
      ? rawValue
      : /^[^/\\]+\.(jpg|jpeg|png|webp|gif)$/i.test(fileFromPath)
        ? fileFromPath
        : ''
    const avatarPathLike = /(^|[\\/])avatars([\\/]|$)/i.test(rawValue)
    const normalizedRaw = fileLike
      ? `/uploads/avatars/${rawValue}`
      : avatarPathLike && !/uploads[\\/]/i.test(rawValue)
        ? `/uploads/${rawValue.replace(/^[\\/]+/, '')}`
        : rawValue
    const extraFromFile = fileName ? resolveAssetUrl(`/uploads/avatars/${fileName}`) : ''
    const localDirect =
      fileName &&
      (window.location.hostname === 'localhost' ||
        window.location.hostname === '127.0.0.1' ||
        window.location.hostname === '::1')
        ? `http://localhost:5000/uploads/avatars/${encodeURIComponent(fileName)}`
        : ''
    const candidates = [localDirect, uploadsPathCandidate, resolveAssetUrl(normalizedRaw), extraFromFile].filter(Boolean)
    const unique = Array.from(new Set(candidates))
    const rewritten = unique.map((src) => {
      try {
        const parsed = new URL(src, window.location.origin)
        const isLocalHost =
          parsed.hostname === 'localhost' ||
          parsed.hostname === '127.0.0.1' ||
          parsed.hostname === '::1'
        if (parsed.protocol === 'http:' && !isLocalHost) {
          parsed.protocol = 'https:'
        }
        return parsed.toString()
      } catch {
        return src
      }
    })
    return rewritten.length > 0 ? rewritten : ['/favicon.svg']
  }, [data?.profile?.avatarUrl])
  const [avatarIdx, setAvatarIdx] = useState(0)
  useEffect(() => {
    setAvatarIdx(0)
  }, [avatarCandidates])
  const avatarSrc = avatarCandidates[avatarIdx] ?? '/favicon.svg'
  const schedule = scheduleQuery.data?.schedule
  const vacationFrom = parseIsoDate(schedule?.vacationStart)
  const vacationTo = parseIsoDate(schedule?.vacationEnd)
  const vacationRange =
    vacationFrom && vacationTo && vacationTo.getTime() >= vacationFrom.getTime()
      ? { from: vacationFrom, to: vacationTo }
      : null

  const calendarTitle = useMemo(() => {
    const title = calendarMonth.toLocaleDateString('ru-RU', { month: 'long', year: 'numeric' })
    return title.charAt(0).toUpperCase() + title.slice(1)
  }, [calendarMonth])

  const calendarCells = useMemo(() => {
    const first = new Date(calendarMonth.getFullYear(), calendarMonth.getMonth(), 1)
    const startOffset = (first.getDay() + 6) % 7 // Monday=0
    const daysInMonth = new Date(calendarMonth.getFullYear(), calendarMonth.getMonth() + 1, 0).getDate()
    const todayKey = ymd(new Date())
    const cells: Array<{
      key: string
      day: number | null
      date: Date | null
      isToday: boolean
      isWeekend: boolean
      isVacation: boolean
    }> = []

    for (let index = 0; index < 42; index += 1) {
      const dayNum = index - startOffset + 1
      if (dayNum >= 1 && dayNum <= daysInMonth) {
        const date = new Date(calendarMonth.getFullYear(), calendarMonth.getMonth(), dayNum)
        const dateKey = ymd(date)
        const weekDay = date.getDay() // 0 Sunday, 6 Saturday
        const isWeekend = weekDay === 0 || weekDay === 6
        const isVacation = vacationRange
          ? date.getTime() >= vacationRange.from.getTime() && date.getTime() <= vacationRange.to.getTime()
          : false
        cells.push({
          key: dateKey,
          day: dayNum,
          date,
          isToday: dateKey === todayKey,
          isWeekend,
          isVacation,
        })
      } else {
        cells.push({
          key: `empty-${index}`,
          day: null,
          date: null,
          isToday: false,
          isWeekend: false,
          isVacation: false,
        })
      }
    }
    return cells
  }, [calendarMonth, vacationRange])

  if (!session) return null
  if (profileQuery.isLoading) return <p className="screen-state">Загрузка профиля...</p>
  if (profileQuery.error) return <p className="screen-state error">{(profileQuery.error as Error).message}</p>

  if (!data?.success || !data.profile) {
    return (
      <div className="card">
        <p className="error">{data?.message ?? 'Профиль недоступен'}</p>
        <button
          onClick={() => {
            clearSession()
            navigate('/login', { replace: true })
          }}
        >
          Выйти в авторизацию
        </button>
      </div>
    )
  }

  return (
    <section className="iphone-profile-page">
      <h2>Профиль</h2>
      <div className="iphone-group profile-main-card">
        <div className="profile-head">
          <img
            src={avatarSrc}
            alt="avatar"
            className="profile-avatar-lg"
            referrerPolicy="no-referrer"
            onError={(e) => {
              const img = e.currentTarget
              const next = avatarIdx + 1
              if (next < avatarCandidates.length) {
                setAvatarIdx(next)
                return
              }
              if (!img.src.endsWith('/favicon.svg')) {
                img.src = '/favicon.svg'
              }
            }}
          />
          <div>
            <h2>{data.profile.lastName} {data.profile.firstName}</h2>
            <p className="muted">{data.profile.phone}</p>
          </div>
        </div>
        <div className="level-box">
          <div className="level-row">
            <strong>Уровень {data.profile.level ?? 1}</strong>
          </div>
          <div className="progress">
            <div className="progress-fill" style={{ width: `${progressPct}%` }} />
          </div>
          <span className="muted">
            {data.profile.experience ?? 0} / {(data.profile.experience ?? 0) + (data.profile.xpToNext ?? 0)} опыта
          </span>
        </div>
      </div>
      <label className="profile-upload-label">
        Фото профиля
        <input type="file" accept="image/*" onChange={(e) => {
          const f = e.target.files?.[0]
          if (f) uploadMutation.mutate(f)
        }} />
      </label>
      {uploadMutation.error ? <p className="error">{(uploadMutation.error as Error).message}</p> : null}
      <h3>Данные</h3>
      <div className="iphone-group">
        <div className="iphone-info-card">
          <span className="muted">Должность</span>
          <strong>{data.profile.position}</strong>
        </div>
        <div className="iphone-info-card">
          <span className="muted">Табельный номер</span>
          <strong>{data.profile.employeeId}</strong>
        </div>
        <div className="iphone-info-card">
          <span className="muted">Подразделение</span>
          <strong>{data.profile.subdivision}</strong>
        </div>
      </div>
      <h3>Аккаунт</h3>
      <div className="iphone-group">
        <button
          type="button"
          className="iphone-row iphone-row-btn"
          onClick={() => {
            window.alert('в разработке')
          }}
        >
          <span>Календарь</span>
          <span>›</span>
        </button>
        <button
          type="button"
          className="iphone-row iphone-row-btn danger"
          onClick={() => {
            if (!window.confirm('Выйти из аккаунта?')) return
            clearSession()
            navigate('/login', { replace: true })
          }}
        >
          <span>Выйти из аккаунта</span>
          <span>›</span>
        </button>
      </div>
      {scheduleOpen ? (
        <div className="overlay" onClick={() => setScheduleOpen(false)}>
          <div className="modal-card" onClick={(e) => e.stopPropagation()}>
            <h3>График работы</h3>
            {scheduleQuery.isLoading ? <p className="muted">Загрузка...</p> : null}
            {scheduleQuery.error ? <p className="error">{(scheduleQuery.error as Error).message}</p> : null}
            {!scheduleQuery.isLoading && !scheduleQuery.error ? (
              (() => {
                const resp = scheduleQuery.data
                const days = (resp?.days ?? []).slice(0, 20)
                if (days.length > 0) {
                  return (
                    <>
                      {days.map((d) => (
                        <div key={d.date} className="thread-meta-row">
                          <span>{d.date}</span>
                          <span className="muted">{d.dayType}{d.shiftStart ? ` · ${d.shiftStart}-${d.shiftEnd ?? ''}` : ''}</span>
                        </div>
                      ))}
                    </>
                  )
                }

                const sch = resp?.schedule
                if (sch) {
                  const shiftText =
                    sch.shiftStart && sch.shiftEnd
                      ? `${sch.shiftStart} - ${sch.shiftEnd}`
                      : '--:-- - --:--'
                  const patternText = String(sch.workPattern ?? '').trim() || '-'
                  return (
                    <>
                      <div className="calendar-toolbar">
                        <button
                          type="button"
                          className="calendar-nav-btn"
                          onClick={() =>
                            setCalendarMonth((prev) => new Date(prev.getFullYear(), prev.getMonth() - 1, 1))
                          }
                        >
                          ‹
                        </button>
                        <strong>{calendarTitle}</strong>
                        <button
                          type="button"
                          className="calendar-nav-btn"
                          onClick={() =>
                            setCalendarMonth((prev) => new Date(prev.getFullYear(), prev.getMonth() + 1, 1))
                          }
                        >
                          ›
                        </button>
                      </div>
                      <div className="calendar-actions">
                        <button
                          type="button"
                          className="calendar-today-btn"
                          onClick={() => {
                            const now = new Date()
                            setCalendarMonth(new Date(now.getFullYear(), now.getMonth(), 1))
                          }}
                        >
                          Сегодня
                        </button>
                      </div>
                      <div className="calendar-weekdays">
                        {WEEKDAYS_RU.map((w) => (
                          <span key={w}>{w}</span>
                        ))}
                      </div>
                      <div className="calendar-grid">
                        {calendarCells.map((cell) => (
                          <button
                            key={cell.key}
                            type="button"
                            className={[
                              'calendar-day',
                              cell.day == null ? 'empty' : '',
                              cell.isWeekend ? 'weekend' : '',
                              cell.isVacation ? 'vacation' : '',
                              cell.isToday ? 'today' : '',
                            ]
                              .filter(Boolean)
                              .join(' ')}
                            disabled={cell.day == null}
                            onClick={() => {
                              if (!cell.date) return
                              const title = cell.date.toLocaleDateString('ru-RU', {
                                day: '2-digit',
                                month: 'long',
                                year: 'numeric',
                              })
                              const message = cell.isVacation
                                ? 'Отпуск'
                                : cell.isWeekend
                                  ? 'Выходной день'
                                  : `Рабочий день\nГрафик: ${patternText}, смена ${shiftText}`
                              setSelectedDayInfo({ title, message })
                            }}
                          >
                            {cell.day ?? ''}
                          </button>
                        ))}
                      </div>
                      <div className="calendar-legend">
                        <span>Сегодня: рамка</span>
                        <span><i className="legend-dot weekend" /> Выходной</span>
                        <span><i className="legend-dot vacation" /> Отпуск</span>
                      </div>
                    </>
                  )
                }

                return <p className="muted">{resp?.message || 'Нет данных графика'}</p>
              })()
            ) : null}
            <button type="button" onClick={() => setScheduleOpen(false)}>Закрыть</button>
            {selectedDayInfo ? (
              <div className="overlay" onClick={() => setSelectedDayInfo(null)}>
                <div className="modal-card" onClick={(e) => e.stopPropagation()}>
                  <h3>{selectedDayInfo.title}</h3>
                  <p style={{ whiteSpace: 'pre-line' }}>{selectedDayInfo.message}</p>
                  <button type="button" onClick={() => setSelectedDayInfo(null)}>OK</button>
                </div>
              </div>
            ) : null}
          </div>
        </div>
      ) : null}
    </section>
  )
}
