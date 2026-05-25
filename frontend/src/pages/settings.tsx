import { useEffect, useMemo, useState } from 'react'
import { KeyRound, Power, PowerOff, ShieldCheck, Trash2 } from 'lucide-react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AuthGuard } from '@/components/auth-guard'
import { NavBar } from '@/components/nav-bar'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Bell, Send } from 'lucide-react'
import { api, ApiError, type ApiKeyDto, type ExchangeDto, type UserSettingsDto } from '@/lib/api'
import { qk } from '@/lib/queries'

export default function SettingsPage() {
  return <AuthGuard><Inner /></AuthGuard>
}

function Inner() {
  const qc = useQueryClient()
  const [label, setLabel] = useState('Binance main')
  const [exchangeCode, setExchangeCode] = useState('binance')
  const [mode, setMode] = useState('Paper')
  const [apiKeyValue, setApiKeyValue] = useState('')
  const [apiSecret, setApiSecret] = useState('')
  const [permissionsJson, setPermissionsJson] = useState('{"spot":true,"trade":false,"withdraw":false}')
  const [err, setErr] = useState<string | null>(null)

  const { data: exchanges = [], error: exchangesErr } = useQuery({
    queryKey: qk.exchanges,
    queryFn: () => api<ExchangeDto[]>('/api/settings/exchanges'),
    staleTime: 5 * 60_000,
  })
  const { data: keys = [], isFetching: reloading, refetch: refetchKeys } = useQuery({
    queryKey: qk.apiKeys,
    queryFn: () => api<ApiKeyDto[]>('/api/settings/api-keys'),
  })
  const reloadErr = exchangesErr ? (exchangesErr as Error).message : null
  const activeLiveKey = useMemo(() => keys.find(k => k.mode === 'Live' && k.isActive), [keys])

  // Auto-pick first exchange code once loaded.
  useEffect(() => {
    if (exchanges[0] && exchangeCode === 'binance' && !exchanges.find(e => e.code === 'binance')) {
      setExchangeCode(exchanges[0].code)
    }
  }, [exchanges, exchangeCode])

  useEffect(() => {
    if (exchangeCode === 'binance-futures-testnet') {
      setMode('Live')
      setPermissionsJson('{"futures":true,"trade":true,"withdraw":false}')
    }
  }, [exchangeCode])

  const invalidateKeys = () => qc.invalidateQueries({ queryKey: qk.apiKeys })

  const submitMut = useMutation({
    mutationFn: (body: unknown) => api<ApiKeyDto>('/api/settings/api-keys', { method: 'POST', body: JSON.stringify(body) }),
    onSuccess: () => { setApiKeyValue(''); setApiSecret(''); invalidateKeys() },
    onError: (e) => setErr(e instanceof ApiError ? e.message : (e as Error).message),
  })
  const toggleMut = useMutation({
    mutationFn: (row: ApiKeyDto) =>
      api<ApiKeyDto>(`/api/settings/api-keys/${row.id}/${row.isActive ? 'deactivate' : 'activate'}`, { method: 'POST' }),
    onSuccess: invalidateKeys,
  })
  const removeMut = useMutation({
    mutationFn: (id: string) => api(`/api/settings/api-keys/${id}`, { method: 'DELETE' }),
    onSuccess: invalidateKeys,
  })
  const validateMut = useMutation({
    mutationFn: (id: string) =>
      api<{ validatedAt: string; canTrade: boolean; canWithdraw: boolean; totalWalletBalance: number; availableBalance: number }>(
        `/api/settings/api-keys/${id}/validate`, { method: 'POST' }),
    onSuccess: (res) => { setErr(null); alert(`Validated. canTrade=${res.canTrade} canWithdraw=${res.canWithdraw} balance=${res.availableBalance}`); invalidateKeys() },
    onError: (e) => setErr(e instanceof ApiError ? e.message : (e as Error).message),
  })

  function submit(e: React.FormEvent) {
    e.preventDefault()
    setErr(null)
    try { JSON.parse(permissionsJson) }
    catch (e) { setErr('Invalid JSON: ' + (e as Error).message); return }
    submitMut.mutate({
      exchangeCode,
      label,
      apiKey: apiKeyValue,
      apiSecret,
      mode,
      isActive: true,
      permissionsJson,
    })
  }
  const reload = () => { void refetchKeys() }
  const toggle = (row: ApiKeyDto) => toggleMut.mutate(row)
  const remove = (id: string) => { if (confirm('Delete API key?')) removeMut.mutate(id) }
  const busy = submitMut.isPending

  return (
    <main className="min-h-screen">
      <NavBar />
      <div className="container grid gap-5 py-5 lg:grid-cols-[1fr_400px]">
        <Card>
          <CardHeader className="flex flex-row items-center justify-between">
            <CardTitle>API keys</CardTitle>
            <Button size="sm" variant="outline" onClick={() => void reload()} disabled={reloading}>
              {reloading ? 'Reloading...' : 'Reload'}
            </Button>
          </CardHeader>
          <CardContent className="space-y-4">
            {reloadErr && (
              <div className="rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
                Reload failed: {reloadErr}. API might be restarting — try again in a moment.
              </div>
            )}
            {activeLiveKey && (
              <div className="rounded-md border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-800">
                Active live key: {activeLiveKey.label} ({activeLiveKey.keyPreview})
              </div>
            )}
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Label</TableHead>
                  <TableHead>Exchange</TableHead>
                  <TableHead>Mode</TableHead>
                  <TableHead>Key</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead></TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {keys.map(k => (
                  <TableRow key={k.id}>
                    <TableCell className="font-medium">{k.label}</TableCell>
                    <TableCell>{k.exchangeCode}</TableCell>
                    <TableCell><Badge variant={k.mode === 'Live' ? 'default' : 'outline'}>{k.mode}</Badge></TableCell>
                    <TableCell className="font-mono text-xs">{k.keyPreview}</TableCell>
                    <TableCell>
                      <Badge variant={k.isActive ? 'default' : 'outline'}>{k.isActive ? 'Active' : 'Inactive'}</Badge>
                    </TableCell>
                    <TableCell className="flex justify-end gap-1">
                      {k.exchangeCode === 'binance-futures-testnet' && (
                        <Button size="sm" variant="outline" title="Validate against Binance Futures testnet" disabled={validateMut.isPending} onClick={() => validateMut.mutate(k.id)}>
                          <ShieldCheck className="h-3.5 w-3.5" />
                        </Button>
                      )}
                      <Button size="sm" variant="outline" onClick={() => toggle(k)}>
                        {k.isActive ? <PowerOff className="h-3.5 w-3.5" /> : <Power className="h-3.5 w-3.5" />}
                      </Button>
                      <Button size="sm" variant="ghost" onClick={() => remove(k.id)}>
                        <Trash2 className="h-4 w-4 text-destructive" />
                      </Button>
                    </TableCell>
                  </TableRow>
                ))}
                {keys.length === 0 && (
                  <TableRow>
                    <TableCell colSpan={6} className="text-muted-foreground">No API keys configured.</TableCell>
                  </TableRow>
                )}
              </TableBody>
            </Table>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2"><KeyRound className="h-5 w-5" /> Add API key</CardTitle>
          </CardHeader>
          <CardContent>
            <form onSubmit={submit} className="space-y-3">
              <Field label="Exchange">
                <select className="w-full rounded-sm border border-border bg-surface px-3 py-2 text-sm text-foreground" value={exchangeCode} onChange={e => setExchangeCode(e.target.value)}>
                  {exchanges.map(e => <option key={e.id} value={e.code}>{e.name}</option>)}
                </select>
              </Field>
              <Field label="Label">
                <input className="w-full rounded-md border px-3 py-2 text-sm" value={label} onChange={e => setLabel(e.target.value)} required />
              </Field>
              <Field label="Mode">
                <select className="w-full rounded-sm border border-border bg-surface px-3 py-2 text-sm text-foreground" value={mode} onChange={e => setMode(e.target.value)}>
                  <option value="Paper">Paper</option>
                  <option value="Live">Live</option>
                </select>
              </Field>
              <Field label="API key">
                <input className="w-full rounded-md border px-3 py-2 font-mono text-sm" value={apiKeyValue} onChange={e => setApiKeyValue(e.target.value)} autoComplete="off" required />
              </Field>
              <Field label="API secret">
                <input className="w-full rounded-md border px-3 py-2 font-mono text-sm" type="password" value={apiSecret} onChange={e => setApiSecret(e.target.value)} autoComplete="new-password" required />
              </Field>
              <Field label="Permissions JSON">
                <textarea className="h-24 w-full rounded-md border px-3 py-2 font-mono text-xs" value={permissionsJson} onChange={e => setPermissionsJson(e.target.value)} />
              </Field>
              {err && <p className="text-sm text-destructive">{err}</p>}
              <Button type="submit" className="w-full" disabled={busy}>{busy ? 'Saving...' : 'Save key'}</Button>
            </form>
          </CardContent>
        </Card>

        <TelegramSection />
      </div>
    </main>
  )
}

function TelegramSection() {
  const qc = useQueryClient()
  const [token, setToken] = useState('')
  const [chatId, setChatId] = useState('')
  const [enabled, setEnabled] = useState(false)
  const [msg, setMsg] = useState<{ kind: 'ok' | 'err'; text: string } | null>(null)

  const { data: settings } = useQuery({
    queryKey: qk.userSettings,
    queryFn: () => api<UserSettingsDto>('/api/me/settings'),
  })

  // Sync local form state from server payload (don't override while user is typing token).
  useEffect(() => {
    if (settings) {
      setEnabled(settings.telegramAlertsEnabled)
      setChatId(settings.telegramChatId ?? '')
    }
  }, [settings])

  const saveMut = useMutation({
    mutationFn: (body: Record<string, unknown>) =>
      api<UserSettingsDto>('/api/me/settings', { method: 'PUT', body: JSON.stringify(body) }),
    onMutate: () => setMsg(null),
    onSuccess: () => { setToken(''); setMsg({ kind: 'ok', text: 'Saved.' }); qc.invalidateQueries({ queryKey: qk.userSettings }) },
    onError: (e) => setMsg({ kind: 'err', text: e instanceof ApiError ? e.message : (e as Error).message }),
  })
  const testMut = useMutation({
    mutationFn: () => api('/api/me/settings/telegram/test', { method: 'POST' }),
    onMutate: () => setMsg(null),
    onSuccess: () => setMsg({ kind: 'ok', text: 'Test message sent — check Telegram.' }),
    onError: (e) => setMsg({ kind: 'err', text: e instanceof ApiError ? e.message : (e as Error).message }),
  })

  function save(e: React.FormEvent) {
    e.preventDefault()
    const body: Record<string, unknown> = {
      telegramAlertsEnabled: enabled,
      telegramChatId: chatId.trim(),
    }
    if (token.trim()) body.telegramBotToken = token.trim()
    saveMut.mutate(body)
  }
  const test = () => testMut.mutate()
  const saving = saveMut.isPending
  const testing = testMut.isPending

  return (
    <Card className="lg:col-span-2">
      <CardHeader className="pb-2">
        <CardTitle className="flex items-center gap-2 text-sm">
          <Bell className="h-4 w-4 text-primary" /> Telegram alerts
        </CardTitle>
        <p className="text-[11px] text-muted-foreground">
          Forwarded events: <code className="font-mono">risk</code>, <code className="font-mono">auto_close</code> (kill switch, SL/TP hit, break-even, blocked orders).
          {' '}<a href="https://core.telegram.org/bots#how-do-i-create-a-bot" target="_blank" rel="noopener" className="text-primary hover:underline">How to create a bot</a>
        </p>
      </CardHeader>
      <CardContent className="px-4 pb-4">
        <form onSubmit={save} className="grid gap-3 md:grid-cols-[1fr_240px]">
          <div className="space-y-3">
            <Field label="Bot token">
              <input
                className="h-9 w-full rounded-sm border border-border bg-surface px-3 font-mono text-sm"
                type="password"
                placeholder={settings?.telegramBotTokenConfigured ? '••••••••• (configured, leave blank to keep)' : '123456:ABC-DEF...'}
                value={token}
                onChange={e => setToken(e.target.value)}
                autoComplete="off"
              />
            </Field>
            <Field label="Chat ID">
              <input
                className="h-9 w-full rounded-sm border border-border bg-surface px-3 font-mono text-sm"
                placeholder="e.g. 123456789 or -100123…"
                value={chatId}
                onChange={e => setChatId(e.target.value)}
                autoComplete="off"
              />
            </Field>
            <label className="flex items-center gap-2 rounded-sm border border-border bg-surface px-2.5 py-2 text-xs">
              <input type="checkbox" checked={enabled} onChange={e => setEnabled(e.target.checked)} className="accent-primary" />
              <span>Enable Telegram alerts</span>
            </label>
            {msg && (
              <p className={`text-xs ${msg.kind === 'err' ? 'text-destructive' : 'text-up'}`}>{msg.text}</p>
            )}
          </div>
          <div className="flex flex-col gap-2">
            <Button type="submit" className="w-full" disabled={saving}>{saving ? 'Saving…' : 'Save'}</Button>
            <Button
              type="button"
              variant="outline"
              className="w-full gap-2"
              onClick={test}
              disabled={testing || !settings?.telegramBotTokenConfigured || !chatId}
            >
              <Send className="h-3.5 w-3.5" /> {testing ? 'Sending…' : 'Send test'}
            </Button>
            <div className="rounded-sm border border-border/40 bg-surface px-2.5 py-2 text-[11px] text-muted-foreground">
              <p>Status: {settings?.telegramBotTokenConfigured ? <span className="text-up">Token configured</span> : <span className="text-warning">No token</span>}</p>
              <p>Alerts: {settings?.telegramAlertsEnabled ? <span className="text-up">On</span> : <span>Off</span>}</p>
            </div>
          </div>
        </form>
      </CardContent>
    </Card>
  )
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return <label className="block"><span className="text-sm text-muted-foreground">{label}</span><div className="mt-1">{children}</div></label>
}
