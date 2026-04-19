import { useEffect, useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import { useLocation, useNavigate, useSearchParams } from 'react-router-dom'
import { confirmDeviceLogin, login } from '../../api/employee'
import { getDeviceId, getDeviceName, getSession, saveSession } from '../../shared/session'
import phoneLogo from '../../../../app/src/main/res/drawable/logo.png'

export function LoginPage() {
  const navigate = useNavigate()
  const location = useLocation()
  const [params] = useSearchParams()
  const basePath = location.pathname.startsWith('/iphone') ? '/iphone' : ''
  const [loginValue, setLoginValue] = useState(params.get('login') ?? '')
  const [password, setPassword] = useState(params.get('password') ?? '')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [attemptId, setAttemptId] = useState<number | null>(null)
  const [code, setCode] = useState('')
  const [rememberMe, setRememberMe] = useState(true)
  const [showPassword, setShowPassword] = useState(false)

  const autoSeconds = useMemo(() => Number(params.get('auto') ?? '0') || 0, [params])
  const reloginBypass = params.get('reloginBypass') === '1'

  useEffect(() => {
    if (getSession()) navigate(`${basePath}/chats`, { replace: true })
  }, [basePath, navigate])

  useEffect(() => {
    if (autoSeconds <= 0 || !loginValue || !password) return
    const id = window.setTimeout(() => {
      void handleLogin()
    }, autoSeconds * 1000)
    return () => window.clearTimeout(id)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [autoSeconds, loginValue, password])

  async function handleLogin(e?: FormEvent) {
    e?.preventDefault()
    setBusy(true)
    setError('')
    try {
      const result = await login({
        login: loginValue.trim(),
        password: password.trim(),
        deviceId: getDeviceId(),
        deviceName: getDeviceName(),
        reloginBypass,
      })
      if (result.requiresDeviceCode && result.pendingAttemptId) {
        setAttemptId(result.pendingAttemptId)
        return
      }
      if (!result.success || !result.result) {
        setError(result.message)
        return
      }
      saveSession({
        login: loginValue.trim(),
        password: rememberMe ? password.trim() : undefined,
        deviceId: getDeviceId(),
        deviceName: getDeviceName(),
        employee: result.result,
      })
      const next = params.get('next')
      navigate(next ? decodeURIComponent(next) : `${basePath}/chats`, { replace: true })
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  async function handleConfirmCode(e: FormEvent) {
    e.preventDefault()
    if (!attemptId) return
    setBusy(true)
    setError('')
    try {
      const result = await confirmDeviceLogin({
        login: loginValue.trim(),
        password: password.trim(),
        deviceId: getDeviceId(),
        deviceName: getDeviceName(),
        attemptId,
        code: code.trim(),
      })
      if (!result.success || !result.result) {
        setError(result.message)
        return
      }
      saveSession({
        login: loginValue.trim(),
        password: rememberMe ? password.trim() : undefined,
        deviceId: getDeviceId(),
        deviceName: getDeviceName(),
        employee: result.result,
      })
      navigate(`${basePath}/chats`, { replace: true })
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
      <form onSubmit={attemptId ? handleConfirmCode : handleLogin} className="column">
        <label className="sr-only">
          Логин
          <input placeholder="логин" value={loginValue} onChange={(e) => setLoginValue(e.target.value)} required />
        </label>
        <label className="password-wrap">
          Пароль
          <div className="password-input-wrap">
            <input
              type={showPassword ? 'text' : 'password'}
              placeholder="пароль"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
            />
            <button type="button" className="ghost-eye" onClick={() => setShowPassword((x) => !x)}>
              {showPassword ? '🙈' : '👁'}
            </button>
          </div>
        </label>
        {attemptId && (
          <label className="sr-only">
            Код устройства
            <input placeholder="код устройства" value={code} onChange={(e) => setCode(e.target.value)} required minLength={6} maxLength={6} />
          </label>
        )}
        <label className="remember-row">
          <input type="checkbox" checked={rememberMe} onChange={(e) => setRememberMe(e.target.checked)} />
          <span>Запомнить меня</span>
        </label>
        {error && <p className="error">{error}</p>}
        <button className="login-btn-main stable-cyr" type="submit" disabled={busy}>
          {busy ? 'Подождите...' : attemptId ? 'Подтвердить код' : 'Войти'}
        </button>
        <div className="or-row">
          <span />
          <b>или</b>
          <span />
        </div>
        <button type="button" className="register-outline stable-cyr" onClick={() => navigate(`${basePath}/register`)}>
          Регистрация
        </button>
      </form>
      </div>
    </div>
  )
}
