import { useNavigate } from 'react-router-dom'
import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { useAuth } from '@/lib/auth-context'
import { ApiError } from '@/lib/api'

type AuthMode = 'login' | 'register' | 'forgot' | 'reset'

export default function LoginPage() {
  const navigate = useNavigate()
  const { login, register, forgotPassword, resetPassword } = useAuth()
  const [mode, setMode] = useState<AuthMode>('login')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [resetToken, setResetToken] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit(e: React.FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    setNotice(null)
    try {
      if (mode === 'login') {
        await login(email, password)
        navigate('/')
      } else if (mode === 'register') {
        await register(email, password, displayName || email)
        navigate('/')
      } else if (mode === 'forgot') {
        const res = await forgotPassword(email)
        if (res.resetToken) setResetToken(res.resetToken)
        setNotice(res.resetToken ? 'Development reset token generated. Paste it below to set a new password.' : res.message)
        setMode('reset')
      } else {
        if (password !== confirmPassword) {
          setError('Passwords do not match.')
          return
        }
        await resetPassword(email, resetToken, password)
        setPassword('')
        setConfirmPassword('')
        setResetToken('')
        setNotice('Password reset complete. Sign in with the new password.')
        setMode('login')
      }
    } catch (err) {
      setError(err instanceof ApiError ? `${err.status}: ${err.message}` : (err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  function go(next: AuthMode) {
    setMode(next)
    setError(null)
    setNotice(null)
  }

  return (
    <main className="flex min-h-screen items-center justify-center bg-background p-6">
      <Card className="w-full max-w-md">
        <CardHeader className="pb-2">
          <CardTitle className="text-base">{titleFor(mode)} - Quant Flow Bots</CardTitle>
        </CardHeader>
        <CardContent>
          <form onSubmit={submit} className="space-y-3">
            {mode === 'register' && (
              <Field label="Display name" type="text" value={displayName} onChange={setDisplayName} />
            )}
            <Field label="Email" type="email" value={email} onChange={setEmail} required />
            {(mode === 'login' || mode === 'register' || mode === 'reset') && (
              <Field label={mode === 'reset' ? 'New password' : 'Password'} type="password" value={password} onChange={setPassword} required />
            )}
            {mode === 'reset' && (
              <>
                <Field label="Confirm password" type="password" value={confirmPassword} onChange={setConfirmPassword} required />
                <label className="block space-y-1">
                  <span className="text-[11px] uppercase tracking-wider text-muted-foreground">Reset token</span>
                  <textarea
                    className="min-h-24 w-full resize-y rounded-sm border border-border bg-surface px-3 py-2 font-mono text-xs text-foreground outline-none focus:border-primary"
                    value={resetToken}
                    onChange={(e) => setResetToken(e.target.value)}
                    required
                  />
                </label>
              </>
            )}
            {notice && <p className="rounded-sm border border-success/30 bg-success/5 px-3 py-2 text-sm text-success">{notice}</p>}
            {error && <p className="text-sm text-destructive">{error}</p>}
            <Button type="submit" className="w-full bg-primary text-primary-foreground hover:bg-primary/90" disabled={busy}>
              {busy ? 'Working...' : submitLabelFor(mode)}
            </Button>
            <div className="space-y-2 text-center text-sm">
              {mode === 'login' && (
                <>
                  <button type="button" className="block w-full text-muted-foreground hover:text-foreground" onClick={() => go('forgot')}>
                    Forgot password?
                  </button>
                  <button type="button" className="block w-full text-muted-foreground hover:text-foreground" onClick={() => go('register')}>
                    No account? Register
                  </button>
                </>
              )}
              {mode !== 'login' && (
                <button type="button" className="w-full text-muted-foreground hover:text-foreground" onClick={() => go('login')}>
                  Back to sign in
                </button>
              )}
              {mode === 'forgot' && (
                <button type="button" className="w-full text-muted-foreground hover:text-foreground" onClick={() => go('reset')}>
                  Already have a reset token?
                </button>
              )}
            </div>
          </form>
        </CardContent>
      </Card>
    </main>
  )
}

function titleFor(mode: AuthMode) {
  if (mode === 'register') return 'Create account'
  if (mode === 'forgot') return 'Reset password'
  if (mode === 'reset') return 'Set new password'
  return 'Sign in'
}

function submitLabelFor(mode: AuthMode) {
  if (mode === 'register') return 'Register'
  if (mode === 'forgot') return 'Send reset token'
  if (mode === 'reset') return 'Reset password'
  return 'Sign in'
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
