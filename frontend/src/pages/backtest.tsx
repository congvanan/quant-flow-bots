import { useEffect, useState } from 'react'
import { Play, Trash2 } from 'lucide-react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AuthGuard } from '@/components/auth-guard'
import { NavBar } from '@/components/nav-bar'
import { EquityChart } from '@/components/equity-chart'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { api, ApiError, type BacktestDetail, type BacktestSummary, type StrategyDto } from '@/lib/api'
import { qk } from '@/lib/queries'

const INTERVALS = ['OneMinute', 'FiveMinutes', 'FifteenMinutes', 'OneHour', 'FourHours', 'OneDay']

export default function BacktestPage() {
  return <AuthGuard><Inner /></AuthGuard>
}

function Inner() {
  const qc = useQueryClient()
  const [strategyId, setStrategyId] = useState('')
  const [symbol, setSymbol] = useState('BTCUSDT')
  const [interval, setInterval] = useState('FifteenMinutes')
  const [from, setFrom] = useState(defaultFrom())
  const [to, setTo] = useState(defaultTo())
  const [capital, setCapital] = useState('10000')
  const [fee, setFee] = useState('0.1')
  const [err, setErr] = useState<string | null>(null)
  const [detail, setDetail] = useState<BacktestDetail | null>(null)
  const [openId, setOpenId] = useState<string | null>(null)

  const { data: strategies = [] } = useQuery({
    queryKey: qk.strategies,
    queryFn: () => api<StrategyDto[]>('/api/strategies'),
  })
  const { data: list = [] } = useQuery({
    queryKey: qk.backtests,
    queryFn: () => api<BacktestSummary[]>('/api/backtests'),
  })

  // Auto-pick first strategy.
  useEffect(() => {
    if (strategies.length > 0 && !strategyId) setStrategyId(strategies[0].id)
  }, [strategies, strategyId])

  // Lazy-load detail when user clicks "View".
  const { data: loadedDetail } = useQuery({
    queryKey: openId ? qk.backtest(openId) : ['backtest', 'none'],
    queryFn: () => api<BacktestDetail>(`/api/backtests/${openId}`),
    enabled: !!openId,
  })
  useEffect(() => { if (loadedDetail) setDetail(loadedDetail) }, [loadedDetail])

  const runMut = useMutation({
    mutationFn: (body: unknown) =>
      api<BacktestDetail>('/api/backtests', { method: 'POST', body: JSON.stringify(body) }),
    onMutate: () => { setErr(null); setDetail(null) },
    onSuccess: (res) => {
      setDetail(res)
      qc.invalidateQueries({ queryKey: qk.backtests })
    },
    onError: (e) => setErr(e instanceof ApiError ? e.message : (e as Error).message),
  })

  const deleteMut = useMutation({
    mutationFn: (id: string) => api(`/api/backtests/${id}`, { method: 'DELETE' }),
    onSuccess: (_data, id) => {
      // Nếu đang xem chi tiết của row vừa xóa → clear panel.
      if (detail?.summary.id === id) setDetail(null)
      qc.invalidateQueries({ queryKey: qk.backtests })
    },
  })

  const clearFailedMut = useMutation({
    mutationFn: () => api<{ deleted: number }>('/api/backtests/failed', { method: 'DELETE' }),
    onSuccess: () => qc.invalidateQueries({ queryKey: qk.backtests }),
  })

  function removeBacktest(id: string) {
    if (!confirm('Xóa backtest này?')) return
    deleteMut.mutate(id)
  }

  const failedCount = list.filter(b => b.status === 'Failed').length

  function run(e: React.FormEvent) {
    e.preventDefault()
    runMut.mutate({
      strategyId,
      symbolCode: symbol.toUpperCase(),
      interval,
      from: new Date(from).toISOString(),
      to: new Date(to).toISOString(),
      initialCapital: Number(capital),
      commissionPercent: Number(fee),
    })
  }

  const load = (id: string) => setOpenId(id)
  const running = runMut.isPending

  return (
    <main className="min-h-screen">
      <NavBar />
      <div className="container grid gap-5 py-5 lg:grid-cols-[1fr_360px]">
        <div className="space-y-5">
          {detail && <ResultPanel detail={detail} />}

          <Card>
            <CardHeader className="flex flex-row items-center justify-between pb-2">
              <CardTitle>Recent backtests</CardTitle>
              {failedCount > 0 && (
                <Button
                  size="sm"
                  variant="outline"
                  className="h-7 gap-1 text-[11px]"
                  disabled={clearFailedMut.isPending}
                  onClick={() => {
                    if (confirm(`Xóa ${failedCount} backtest Failed?`)) clearFailedMut.mutate()
                  }}
                >
                  <Trash2 className="h-3 w-3" /> Clear {failedCount} failed
                </Button>
              )}
            </CardHeader>
            <CardContent>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Strategy</TableHead><TableHead>Symbol</TableHead><TableHead>Interval</TableHead>
                    <TableHead>Return</TableHead><TableHead>MDD</TableHead><TableHead>Sharpe</TableHead>
                    <TableHead>Trades</TableHead><TableHead>Status</TableHead><TableHead></TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {list.map(b => (
                    <TableRow key={b.id}>
                      <TableCell>{b.strategyKind}</TableCell>
                      <TableCell>{b.symbolCode}</TableCell>
                      <TableCell>{b.interval}</TableCell>
                      <TableCell className={(b.returnPercent ?? 0) >= 0 ? 'text-up' : 'text-down'}>{fmtNum(b.returnPercent, '%')}</TableCell>
                      <TableCell className="text-down">{fmtNum(b.maxDrawdownPercent, '%')}</TableCell>
                      <TableCell>{fmtNum(b.sharpeRatio)}</TableCell>
                      <TableCell>{b.tradeCount ?? '—'}</TableCell>
                      <TableCell><Badge variant={b.status === 'Completed' ? 'default' : b.status === 'Failed' ? 'outline' : 'secondary'}>{b.status}</Badge></TableCell>
                      <TableCell>
                        <div className="flex items-center gap-1">
                          <Button size="sm" variant="ghost" onClick={() => load(b.id)}>View</Button>
                          <Button size="sm" variant="ghost" onClick={() => removeBacktest(b.id)} title="Delete">
                            <Trash2 className="h-4 w-4 text-destructive" />
                          </Button>
                        </div>
                      </TableCell>
                    </TableRow>
                  ))}
                  {list.length === 0 && <TableRow><TableCell colSpan={9} className="text-muted-foreground">No backtests yet.</TableCell></TableRow>}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
        </div>

        <Card>
          <CardHeader><CardTitle>Run backtest</CardTitle></CardHeader>
          <CardContent>
            <form onSubmit={run} className="space-y-3">
              <Field label="Strategy">
                <select className="w-full rounded-sm border border-border bg-surface px-3 py-2 text-sm text-foreground" value={strategyId} onChange={e => setStrategyId(e.target.value)} required>
                  <option value="">— pick —</option>
                  {strategies.map(s => <option key={s.id} value={s.id}>{s.name} ({s.kind})</option>)}
                </select>
                <Hint>Chiến lược + params đã setup ở trang Strategies. Backtest replay với chính params đó.</Hint>
              </Field>
              <Field label="Symbol">
                <input className="w-full rounded-md border px-3 py-2 font-mono text-sm uppercase" value={symbol} onChange={e => setSymbol(e.target.value)} required />
                <Hint>Cặp giao dịch, vd BTCUSDT / ETHUSDT. Phải có trong /symbols.</Hint>
              </Field>
              <Field label="Interval">
                <select className="w-full rounded-sm border border-border bg-surface px-3 py-2 text-sm text-foreground" value={interval} onChange={e => setInterval(e.target.value)}>
                  {INTERVALS.map(i => <option key={i} value={i}>{i}</option>)}
                </select>
                <Hint>Khung nến strategy chạy. 15m = mỗi 15 phút có 1 quyết định. Khung càng nhỏ → nhiều signal nhưng noise cao.</Hint>
              </Field>
              <div className="grid grid-cols-2 gap-3">
                <Field label="From">
                  <input className="w-full rounded-md border px-3 py-2 text-sm" type="datetime-local" value={from} onChange={e => setFrom(e.target.value)} required />
                  <Hint>Bắt đầu replay. Khuyến nghị ≥ 30 ngày cho stats có ý nghĩa.</Hint>
                </Field>
                <Field label="To">
                  <input className="w-full rounded-md border px-3 py-2 text-sm" type="datetime-local" value={to} onChange={e => setTo(e.target.value)} required />
                  <Hint>Kết thúc replay. Mặc định = hiện tại.</Hint>
                </Field>
              </div>
              <div className="grid grid-cols-2 gap-3">
                <Field label="Initial capital">
                  <input className="w-full rounded-md border px-3 py-2 text-sm" type="number" step="100" value={capital} onChange={e => setCapital(e.target.value)} required />
                  <Hint>Vốn USDT giả định lúc From. Equity curve khởi đầu từ số này.</Hint>
                </Field>
                <Field label="Commission %">
                  <input className="w-full rounded-md border px-3 py-2 text-sm" type="number" step="0.01" value={fee} onChange={e => setFee(e.target.value)} required />
                  <Hint>Phí mỗi lệnh, mô phỏng phí sàn. Binance Futures taker ~0.04%. Để 0.1% cho an toàn.</Hint>
                </Field>
              </div>
              {err && <p className="text-sm text-destructive">{err}</p>}
              <Button type="submit" className="w-full gap-2" disabled={running || !strategyId}>
                <Play className="h-4 w-4" /> {running ? 'Running...' : 'Run backtest'}
              </Button>
            </form>
          </CardContent>
        </Card>
      </div>
    </main>
  )
}

function ResultPanel({ detail }: { detail: BacktestDetail }) {
  const s = detail.summary
  return (
    <Card>
      <CardHeader>
        <div className="flex items-center justify-between">
          <CardTitle>Result · {s.strategyKind} · {s.symbolCode} · {s.interval}</CardTitle>
          <Badge variant={s.status === 'Completed' ? 'default' : 'outline'}>{s.status}</Badge>
        </div>
        <p className="text-xs text-muted-foreground">{new Date(s.fromTime).toLocaleString()} → {new Date(s.toTime).toLocaleString()}</p>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
          <Metric label="Final equity" value={fmtNum(s.finalEquity)} sub={`from ${s.initialCapital.toLocaleString()}`} />
          <Metric label="Total return" value={fmtNum(s.returnPercent, '%')} accent={(s.returnPercent ?? 0) >= 0 ? 'green' : 'red'} />
          <Metric label="Max drawdown" value={fmtNum(s.maxDrawdownPercent, '%')} accent="red" />
          <Metric label="Sharpe (annualized)" value={fmtNum(s.sharpeRatio)} />
          <Metric label="Trades" value={(s.tradeCount ?? 0).toString()} />
          <Metric label="Win rate" value={fmtNum(s.winRatePercent, '%')} />
        </div>
        {detail.equityCurve.length > 0
          ? <EquityChart data={detail.equityCurve} />
          : <p className="text-sm text-muted-foreground">No equity curve data.</p>}
      </CardContent>
    </Card>
  )
}

function Metric({ label, value, sub, accent }: { label: string; value: string; sub?: string; accent?: 'green' | 'red' }) {
  return (
    <div className="rounded-sm border border-border/40 bg-surface p-3">
      <p className="text-[10px] uppercase tracking-wider text-muted-foreground">{label}</p>
      <p className={`mt-1 num text-xl font-semibold ${accent === 'green' ? 'text-up' : accent === 'red' ? 'text-down' : ''}`}>{value}</p>
      {sub && <p className="text-xs text-muted-foreground">{sub}</p>}
    </div>
  )
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return <label className="block"><span className="text-sm text-muted-foreground">{label}</span><div className="mt-1">{children}</div></label>
}

function Hint({ children }: { children: React.ReactNode }) {
  return <p className="mt-1 text-[10px] leading-relaxed text-muted-foreground">{children}</p>
}

function fmtNum(v: number | null | undefined, suffix = ''): string {
  if (v == null) return '—'
  const n = Number(v)
  return `${n.toFixed(2)}${suffix}`
}

function defaultFrom(): string {
  const d = new Date(); d.setDate(d.getDate() - 7)
  return d.toISOString().slice(0, 16)
}
function defaultTo(): string {
  return new Date().toISOString().slice(0, 16)
}
