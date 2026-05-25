import { useEffect } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '@/lib/auth-context'

export function AuthGuard({ children }: { children: React.ReactNode }) {
  const navigate = useNavigate()
  const { token, loading } = useAuth()

  useEffect(() => {
    if (!loading && !token) navigate('/login', { replace: true })
  }, [loading, token, navigate])

  if (loading || !token) {
    return <div className="flex h-screen items-center justify-center text-sm text-muted-foreground">Loading...</div>
  }
  return <>{children}</>
}
