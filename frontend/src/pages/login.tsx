import { useNavigate } from 'react-router-dom'
import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { useAuth } from '@/lib/auth-context'
import { ApiError } from '@/lib/api'

export default function LoginPage() {
  const navigate = useNavigate()
  const { login, register } = useAuth()
  const [mode, setMode] = useState<'login' | 'register'>('login')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit(e: React.FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      if (mode === 'login') await login(email, password)
      else await register(email, password, displayName || email)
      navigate('/')
    } catch (err) {
      setError(err instanceof ApiError ? `${err.status}: ${err.message}` : (err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <main className="flex min-h-screen items-center justify-center bg-background p-6">
      <Card className="w-full max-w-md">
        <CardHeader className="pb-2">
          <CardTitle className="text-base">{mode === 'login' ? 'Sign in' : 'Create account'} — Quant Flow Bots</CardTitle>
        </CardHeader>
        <CardContent>
          <form onSubmit={submit} className="space-y-3">
            {mode === 'register' && (
              <Field label="Display name" type="text" value={displayName} onChange={setDisplayName} />
            )}
            <Field label="Email" type="email" value={email} onChange={setEmail} required />
            <Field label="Password" type="password" value={password} onChange={setPassword} required />
            {error && <p className="text-sm text-destructive">{error}</p>}
            <Button type="submit" className="w-full bg-primary text-primary-foreground hover:bg-primary/90" disabled={busy}>
              {busy ? 'Working...' : mode === 'login' ? 'Sign in' : 'Register'}
            </Button>
            <button type="button" className="w-full text-sm text-muted-foreground hover:text-foreground"
              onClick={() => { setMode(mode === 'login' ? 'register' : 'login'); setError(null) }}>
              {mode === 'login' ? 'No account? Register' : 'Already have an account? Sign in'}
            </button>
          </form>
        </CardContent>
      </Card>
    </main>
  )
}

function Field(props: { label: string; type: string; value: string; onChange: (v: string) => void; required?: boolean }) {
  return (
    <label className="block space-y-1">
      <span className="text-[11px] uppercase tracking-wider text-muted-foreground">{props.label}</span>
      <input
        className="h-9 w-full rounded-sm border border-border bg-surface px-3 text-sm text-foreground outline-none focus:border-primary"
        type={props.type}
        value={props.value}
        onChange={(e) => props.onChange(e.target.value)}
        required={props.required}
      />
    </label>
  )
}
