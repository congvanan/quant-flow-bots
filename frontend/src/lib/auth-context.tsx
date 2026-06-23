import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api, getToken, setToken, type AuthResponse, type ForgotPasswordResponse } from './api'

type AuthState = {
  userId: string | null
  email: string | null
  displayName: string | null
  token: string | null
  loading: boolean
  login: (email: string, password: string) => Promise<void>
  register: (email: string, password: string, displayName: string) => Promise<void>
  forgotPassword: (email: string) => Promise<ForgotPasswordResponse>
  resetPassword: (email: string, token: string, newPassword: string) => Promise<void>
  logout: () => void
}

const AuthContext = createContext<AuthState | null>(null)

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const navigate = useNavigate()
  const [token, setTokenState] = useState<string | null>(null)
  const [profile, setProfile] = useState<{ userId: string; email: string; displayName: string } | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    const t = getToken()
    if (t) {
      setTokenState(t)
      const payload = decodeJwt(t)
      if (payload) {
        setProfile({
          userId: payload.sub ?? '',
          email: payload.email ?? '',
          displayName: payload.display_name ?? '',
        })
      }
    }
    setLoading(false)
  }, [])

  const applyAuth = useCallback((res: AuthResponse) => {
    setToken(res.accessToken)
    setTokenState(res.accessToken)
    setProfile({ userId: res.userId, email: res.email, displayName: res.displayName })
  }, [])

  const login = useCallback(async (email: string, password: string) => {
    const res = await api<AuthResponse>('/api/auth/login', {
      method: 'POST',
      body: JSON.stringify({ email, password }),
    })
    applyAuth(res)
  }, [applyAuth])

  const register = useCallback(async (email: string, password: string, displayName: string) => {
    const res = await api<AuthResponse>('/api/auth/register', {
      method: 'POST',
      body: JSON.stringify({ email, password, displayName }),
    })
    applyAuth(res)
  }, [applyAuth])

  const forgotPassword = useCallback(async (email: string) => {
    return api<ForgotPasswordResponse>('/api/auth/forgot-password', {
      method: 'POST',
      body: JSON.stringify({ email }),
    })
  }, [])

  const resetPassword = useCallback(async (email: string, token: string, newPassword: string) => {
    await api('/api/auth/reset-password', {
      method: 'POST',
      body: JSON.stringify({ email, token, newPassword }),
    })
  }, [])

  const logout = useCallback(() => {
    setToken(null)
    setTokenState(null)
    setProfile(null)
    navigate('/login')
  }, [navigate])

  const value = useMemo<AuthState>(() => ({
    userId: profile?.userId ?? null,
    email: profile?.email ?? null,
    displayName: profile?.displayName ?? null,
    token,
    loading,
    login,
    register,
    forgotPassword,
    resetPassword,
    logout,
  }), [profile, token, loading, login, register, forgotPassword, resetPassword, logout])

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider')
  return ctx
}

function decodeJwt(token: string): { sub?: string; email?: string; display_name?: string } | null {
  try {
    const part = token.split('.')[1]
    const json = atob(part.replace(/-/g, '+').replace(/_/g, '/'))
    return JSON.parse(json)
  } catch {
    return null
  }
}
