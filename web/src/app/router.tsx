import { NavLink, Navigate, Route, Routes, useLocation } from 'react-router-dom'
import { LoginPage } from '../features/auth/LoginPage'
import { RegisterPage } from '../features/auth/RegisterPage'
import { HomePage } from '../features/home/HomePage'
import { CreatePostPage } from '../features/home/CreatePostPage'
import { ThreadsPage } from '../features/chats/ThreadsPage'
import { ChatPage } from '../features/chats/ChatPage'
import { ProfilePage } from '../features/profile/ProfilePage'
import { SettingsPage } from '../features/settings/SettingsPage'
import { getSession } from '../shared/session'

function RequireAuth({ children }: { children: React.ReactNode }) {
  const session = getSession()
  const location = useLocation()
  if (!session) {
    const next = encodeURIComponent(location.pathname + location.search)
    const loginPath = location.pathname.startsWith('/iphone') ? '/iphone/login' : '/login'
    return <Navigate to={`${loginPath}?next=${next}`} replace />
  }
  return <>{children}</>
}

function ShellLayout({ children, basePath = '', forceMobile = false }: { children: React.ReactNode; basePath?: string; forceMobile?: boolean }) {
  const normBase = basePath.endsWith('/') ? basePath.slice(0, -1) : basePath
  const to = (path: string) => `${normBase}${path}`
  return (
    <div className={`app-shell ${forceMobile ? 'force-mobile' : ''}`}>
      <header className="mobile-header">
        <div className="mobile-header-title">Steklo Portal</div>
      </header>
      <main className="mobile-content">{children}</main>
      <nav className="bottom-tabs">
        <NavLink to={to('/home')} className={({ isActive }) => `tab-link ${isActive ? 'active' : ''}`}>
          <span className="tab-icon">🏠</span>
          <span>Главная</span>
        </NavLink>
        <NavLink to={to('/chats')} className={({ isActive }) => `tab-link ${isActive ? 'active' : ''}`}>
          <span className="tab-icon">💬</span>
          <span>Чаты</span>
        </NavLink>
        <NavLink to={to('/settings')} className={({ isActive }) => `tab-link ${isActive ? 'active' : ''}`}>
          <span className="tab-icon">⚙️</span>
          <span>Настройки</span>
        </NavLink>
        <NavLink to={to('/profile')} className={({ isActive }) => `tab-link ${isActive ? 'active' : ''}`}>
          <span className="tab-icon">👤</span>
          <span>Профиль</span>
        </NavLink>
      </nav>
    </div>
  )
}

export function AppRouter() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/register" element={<RegisterPage />} />
      <Route path="/iphone/login" element={<LoginPage />} />
      <Route path="/iphone/register" element={<RegisterPage />} />
      <Route
        path="/home"
        element={
          <RequireAuth>
            <ShellLayout>
              <HomePage />
            </ShellLayout>
          </RequireAuth>
        }
      />
      <Route
        path="/create-post"
        element={
          <RequireAuth>
            <ShellLayout>
              <CreatePostPage />
            </ShellLayout>
          </RequireAuth>
        }
      />
      <Route
        path="/chats"
        element={
          <RequireAuth>
            <ShellLayout>
              <ThreadsPage />
            </ShellLayout>
          </RequireAuth>
        }
      />
      <Route
        path="/chats/:threadId"
        element={
          <RequireAuth>
            <ShellLayout>
              <ChatPage />
            </ShellLayout>
          </RequireAuth>
        }
      />
      <Route
        path="/settings"
        element={
          <RequireAuth>
            <ShellLayout>
              <SettingsPage />
            </ShellLayout>
          </RequireAuth>
        }
      />
      <Route
        path="/profile"
        element={
          <RequireAuth>
            <ShellLayout>
              <ProfilePage />
            </ShellLayout>
          </RequireAuth>
        }
      />
      <Route
        path="/iphone/home"
        element={
          <RequireAuth>
            <ShellLayout basePath="/iphone" forceMobile>
              <HomePage />
            </ShellLayout>
          </RequireAuth>
        }
      />
      <Route
        path="/iphone/create-post"
        element={
          <RequireAuth>
            <ShellLayout basePath="/iphone" forceMobile>
              <CreatePostPage />
            </ShellLayout>
          </RequireAuth>
        }
      />
      <Route
        path="/iphone/chats"
        element={
          <RequireAuth>
            <ShellLayout basePath="/iphone" forceMobile>
              <ThreadsPage />
            </ShellLayout>
          </RequireAuth>
        }
      />
      <Route
        path="/iphone/chats/:threadId"
        element={
          <RequireAuth>
            <ShellLayout basePath="/iphone" forceMobile>
              <ChatPage />
            </ShellLayout>
          </RequireAuth>
        }
      />
      <Route
        path="/iphone/settings"
        element={
          <RequireAuth>
            <ShellLayout basePath="/iphone" forceMobile>
              <SettingsPage />
            </ShellLayout>
          </RequireAuth>
        }
      />
      <Route
        path="/iphone/profile"
        element={
          <RequireAuth>
            <ShellLayout basePath="/iphone" forceMobile>
              <ProfilePage />
            </ShellLayout>
          </RequireAuth>
        }
      />
      <Route path="/iphone" element={<Navigate to="/iphone/login" replace />} />
      <Route path="*" element={<Navigate to="/chats" replace />} />
    </Routes>
  )
}
