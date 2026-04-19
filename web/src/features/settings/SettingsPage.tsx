import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { clearSession } from '../../shared/session'

export function SettingsPage() {
  const navigate = useNavigate()
  const [notificationsEnabled, setNotificationsEnabled] = useState(
    localStorage.getItem('steklo.notifications.enabled') !== '0',
  )
  const [theme, setTheme] = useState<'light' | 'dark'>(
    (localStorage.getItem('steklo.theme') as 'light' | 'dark' | null) ?? 'dark',
  )

  useEffect(() => {
    localStorage.setItem('steklo.notifications.enabled', notificationsEnabled ? '1' : '0')
  }, [notificationsEnabled])

  useEffect(() => {
    localStorage.setItem('steklo.theme', theme)
    document.documentElement.setAttribute('data-theme', theme)
  }, [theme])

  async function requestBrowserNotifications() {
    if (typeof Notification === 'undefined') {
      window.alert('Браузер не поддерживает уведомления')
      return
    }
    const permission = await Notification.requestPermission()
    window.alert(`Статус уведомлений: ${permission}`)
  }

  return (
    <section className="iphone-settings-page">
      <h2>Настройки</h2>
      <h3>Уведомления</h3>
      <div className="iphone-group">
        <label className="iphone-row">
          <span>Включить уведомления в приложении</span>
          <input
            type="checkbox"
            checked={notificationsEnabled}
            onChange={(e) => setNotificationsEnabled(e.target.checked)}
          />
        </label>
        <button type="button" className="iphone-row iphone-row-btn" onClick={() => void requestBrowserNotifications()}>
          <span>Системные настройки уведомлений</span>
          <span>›</span>
        </button>
      </div>
      <h3>Тема</h3>
      <div className="iphone-group">
        <label className="iphone-row">
          <input type="radio" name="theme" checked={theme === 'light'} onChange={() => setTheme('light')} />
          <span>Светлая</span>
        </label>
        <label className="iphone-row">
          <input type="radio" name="theme" checked={theme === 'dark'} onChange={() => setTheme('dark')} />
          <span>Темная</span>
        </label>
      </div>
      <button
        type="button"
        className="iphone-logout"
        onClick={() => {
          clearSession()
          navigate('/login', { replace: true })
        }}
      >
        Выйти из аккаунта
      </button>
    </section>
  )
}
