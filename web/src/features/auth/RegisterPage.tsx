import { useState } from 'react'
import type { FormEvent } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import phoneLogo from '../../../../app/src/main/res/drawable/logo.png'
import { registerByEmployee } from '../../api/employee'
import { getDeviceId, getDeviceName, saveSession } from '../../shared/session'

export function RegisterPage() {
  const navigate = useNavigate()
  const location = useLocation()
  const basePath = location.pathname.startsWith('/iphone') ? '/iphone' : ''
  const [employeeId, setEmployeeId] = useState('')
  const [phone, setPhone] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  async function handleRegister(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError('')
    try {
      const resp = await registerByEmployee({ employeeId: employeeId.trim(), phone: phone.trim() })
      if (!resp.success || !resp.result) {
        setError(resp.message || 'Не удалось зарегистрироваться')
        return
      }
      const r = resp.result
      saveSession({
        login: r.login,
        deviceId: getDeviceId(),
        deviceName: getDeviceName(),
        employee: {
          lastName: r.lastName,
          firstName: r.firstName,
          phone: r.phone,
          employeeId: r.employeeId,
          canCreatePosts: r.canCreatePosts || r.isTechAdmin,
          isTechAdmin: r.isTechAdmin,
          canUseDevConsole: r.canUseDevConsole,
        },
      })
      window.alert('Регистрация выполнена. Пароль отправлен в StekloSecurity.')
      navigate(`${basePath}/home`, { replace: true })
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="login-screen">
      <div className="login-brand">
        <img className="logo-image" src={phoneLogo} alt="Салават Стекло" />
      </div>
      <div className="login-card-phone">
        <form onSubmit={handleRegister} className="column">
          <label className="sr-only">
            Табельный номер
            <input
              placeholder="табельный номер"
              value={employeeId}
              onChange={(e) => setEmployeeId(e.target.value)}
              required
            />
          </label>
          <label className="sr-only">
            Телефон
            <input
              placeholder="телефон"
              value={phone}
              onChange={(e) => setPhone(e.target.value)}
              required
            />
          </label>
          {error ? <p className="error">{error}</p> : null}
          <button className="login-btn-main stable-cyr" type="submit" disabled={busy}>
            {busy ? 'Подождите...' : 'Зарегистрироваться'}
          </button>
          <button type="button" className="register-outline stable-cyr" onClick={() => navigate(`${basePath}/login`)}>
            Войти
          </button>
        </form>
      </div>
    </div>
  )
}
