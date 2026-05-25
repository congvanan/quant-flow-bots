import { useEffect, useState } from 'react'
import { Trash2 } from 'lucide-react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AuthGuard } from '@/components/auth-guard'
import { NavBar } from '@/components/nav-bar'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { api, ApiError, type StrategyDto } from '@/lib/api'
import { qk } from '@/lib/queries'

const DEFAULTS: Record<string, string> = {
  sma_cross: JSON.stringify({ fast: 9, slow: 21 }, null, 2),
  rsi: JSON.stringify({ period: 14, oversold: 30, overbought: 70 }, null, 2),
  breakout: JSON.stringify({ lookback: 20, buffer: 0.001 }, null, 2),
}

export default function StrategiesPage() {
  return <AuthGuard><Inner /></AuthGuard>
}

function Inner() {
  const qc = useQueryClient()
  const [name, setName] = useState('')
  const [kind, setKind] = useState('sma_cross')
  const [params, setParams] = useState(DEFAULTS.sma_cross)
  const [err, setErr] = useState<string | null>(null)

  const { data: list = [] } = useQuery({
    queryKey: qk.strategies,
    queryFn: () => api<StrategyDto[]>('/api/strategies'),
  })
  const { data: kinds = [] } = useQuery({
    queryKey: ['strategy-kinds'],
    queryFn: () => api<string[]>('/api/strategies/kinds'),
    staleTime: Infinity,
  })

  useEffect(() => { setParams(DEFAULTS[kind] ?? '{}') }, [kind])

  const createMut = useMutation({
    mutationFn: (body: { name: string; kind: string; parametersJson: string }) =>
      api('/api/strategies', { method: 'POST', body: JSON.stringify(body) }),
    onSuccess: () => {
      setName('')
      qc.invalidateQueries({ queryKey: qk.strategies })
    },
    onError: (e) => setErr(e instanceof ApiError ? e.message : (e as Error).message),
  })

  const removeMut = useMutation({
    mutationFn: (id: string) => api(`/api/strategies/${id}`, { method: 'DELETE' }),
    onSuccess: () => qc.invalidateQueries({ queryKey: qk.strategies }),
  })

  function submit(e: React.FormEvent) {
    e.preventDefault()
    setErr(null)
    try {
      JSON.parse(params)
    } catch (e) {
      setErr('Invalid JSON: ' + (e as Error).message)
      return
    }
    createMut.mutate({ name, kind, parametersJson: params })
  }

  function remove(id: string) {
    if (!confirm('Delete strategy?')) return
    removeMut.mutate(id)
  }

  return (
    <main className="min-h-screen">
      <NavBar />
      <div className="container grid gap-5 py-5 lg:grid-cols-[1fr_380px]">
        <Card>
          <CardHeader><CardTitle>Strategies</CardTitle></CardHeader>
          <CardContent>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Name</TableHead><TableHead>Kind</TableHead><TableHead>Params</TableHead><TableHead></TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {list.map(s => (
                  <TableRow key={s.id}>
                    <TableCell className="font-medium">{s.name}</TableCell>
                    <TableCell>{s.kind}</TableCell>
                    <TableCell className="font-mono text-xs">{s.parametersJson}</TableCell>
                    <TableCell><Button size="sm" variant="ghost" onClick={() => remove(s.id)}><Trash2 className="h-4 w-4 text-destructive" /></Button></TableCell>
                  </TableRow>
                ))}
                {list.length === 0 && <TableRow><TableCell colSpan={4} className="text-muted-foreground">No strategies yet.</TableCell></TableRow>}
              </TableBody>
            </Table>
          </CardContent>
        </Card>

        <Card>
          <CardHeader><CardTitle>Create strategy</CardTitle></CardHeader>
          <CardContent>
            <form onSubmit={submit} className="space-y-3">
              <Field label="Name"><input className="w-full rounded-md border px-3 py-2 text-sm" value={name} onChange={e => setName(e.target.value)} required /></Field>
              <Field label="Kind">
                <select className="w-full rounded-sm border border-border bg-surface px-3 py-2 text-sm text-foreground" value={kind} onChange={e => setKind(e.target.value)}>
                  {kinds.map(k => <option key={k} value={k}>{k}</option>)}
                </select>
              </Field>
              <Field label="Parameters (JSON)">
                <textarea className="h-32 w-full rounded-md border px-3 py-2 font-mono text-xs" value={params} onChange={e => setParams(e.target.value)} />
              </Field>
              {err && <p className="text-sm text-destructive">{err}</p>}
              <Button type="submit" className="w-full" disabled={createMut.isPending}>{createMut.isPending ? 'Saving...' : 'Create'}</Button>
            </form>
          </CardContent>
        </Card>
      </div>
    </main>
  )
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return <label className="block"><span className="text-sm text-muted-foreground">{label}</span><div className="mt-1">{children}</div></label>
}
