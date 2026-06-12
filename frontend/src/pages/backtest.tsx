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
  // Futures mode: nến fapi + long/short + leverage. Spot: long-only (behavior cũ).
  const [market, setMarket] = useState<'Spot' | 'Futures'>('Spot')
  const [leverage, setLeverage] = useState('1')
  // Scan mode: chạy strategy trên top-N coins theo 24h volume thay vì nhập 1 symbol.
  const [mode, setMode] = useState<'single' | 'scan'>('single')
  const [topN, setTopN] = useState('50')
  const [scanResult, setScanResult] = useState<ScanResponse | null>(null)
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

  const scanMut = useMutation({
    mutationFn: (body: unknown) =>
      api<ScanResponse>('/api/backtests/scan', { method: 'POST', body: JSON.stringify(body) }),
    onMutate: () => { setErr(null); setScanResult(null) },
    onSuccess: (res) => setScanResult(res),
    onError: (e) => setErr(e instanceof ApiError ? e.message : (e as Error).message),
  })

  function removeBacktest(id: string) {
    if (!confirm('Xóa backtest này?')) return
    deleteMut.mutate(id)
  }

  const failedCount = list.filter(b => b.status === 'Failed').length

  function run(e: React.FormEvent) {
    e.preventDefault()
    const common = {
      strategyId,
      interval,
      from: new Date(from).toISOString(),
      to: new Date(to).toISOString(),
      initialCapital: Number(capital),
      commissionPercent: Number(fee),
      market,
      leverage: market === 'Futures' ? Math.max(1, Math.min(125, Number(leverage) || 1)) : 1,
    }
    if (mode === 'scan') {
      scanMut.mutate({ ...common, topN: Number(topN) })
    } else {
      runMut.mutate({ ...common, symbolCode: symbol.toUpperCase() })
    }
  }

  // Click row trong scan result → chạy single backtest cho symbol đó để xem equity curve chi tiết.
  function drillDown(sym: string) {
    setMode('single')
    setSymbol(sym)
    runMut.mutate({
      strategyId,
      symbolCode: sym,
      interval,
      from: new Date(from).toISOString(),
      to: new Date(to).toISOString(),
      initialCapital: Number(capital),
      commissionPercent: Number(fee),
      market,
      leverage: market === 'Futures' ? Math.max(1, Math.min(125, Number(leverage) || 1)) : 1,
    })
  }

  const load = (id: string) => setOpenId(id)
  const running = runMut.isPending || scanMut.isPending

  return (
    <main className="min-h-screen">
      <NavBar />
      <div className="container grid gap-5 py-5 lg:grid-cols-[1fr_360px]">
        <div className="space-y-5">
          {detail && <ResultPanel detail={detail} />}

          {scanResult && (
            <Card>
              <CardHeader className="flex flex-row items-center justify-between pb-2">
                <CardTitle>
                  Scan results — {scanResult.okCount}/{scanResult.universe} coins
                  {scanResult.failedCount > 0 && <span className="ml-2 text-[11px] font-normal text-muted-foreground">({scanResult.failedCount} lỗi/thiếu data)</span>}
                </CardTitle>
                <Button size="sm" variant="ghost" className="h-7 text-[11px]" onClick={() => setScanResult(null)}>Đóng</Button>
              </CardHeader>
              <CardContent>
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>#</TableHead><TableHead>Symbol</TableHead>
                      <TableHead>Return</TableHead><TableHead>MDD</TableHead>
                      <TableHead>Sharpe</TableHead><TableHead>Trades</TableHead>
                      <TableHead>Win rate</TableHead><TableHead>Final equity</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {scanResult.results.map((r, i) => (
                      <TableRow key={r.symbol} className="cursor-pointer hover:bg-surface/60" onClick={() => drillDown(r.symbol)}>
                        <TableCell className="text-muted-foreground">{i + 1}</TableCell>
                        <TableCell className="font-mono font-medium">{r.symbol}</TableCell>
                        <TableCell className={(r.returnPercent ?? 0) >= 0 ? 'text-up' : 'text-down'}>
                          {(r.returnPercent ?? 0) >= 0 ? '+' : ''}{r.returnPercent?.toFixed(2)}%
                        </TableCell>
                        <TableCell className="text-down">{r.maxDrawdownPercent?.toFixed(2)}%</TableCell>
                        <TableCell>{r.sharpeRatio?.toFixed(2)}</TableCell>
                        <TableCell>{r.tradeCount}</TableCell>
                        <TableCell>{r.winRatePercent?.toFixed(1)}%</TableCell>
                        <TableCell className="font-mono">{r.finalEquity?.toFixed(0)}</TableCell>
                      </TableRow>
                    ))}
                    {scanResult.results.length === 0 && (
                      <TableRow><TableCell colSpan={8} className="text-muted-foreground">Không coin nào chạy thành công — kiểm tra khoảng thời gian / interval.</TableCell></TableRow>
                    )}
                  </TableBody>
                </Table>
                <p className="mt-2 text-[10px] text-muted-foreground">Click 1 row để chạy single backtest chi tiết (equity curve) cho symbol đó. Kết quả scan không lưu vào Recent.</p>
              </CardContent>
            </Card>
          )}

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
              {/* Mode: 1 symbol thủ công vs scan top-N coins theo 24h volume. */}
              <div className="flex h-8 items-center rounded-sm border border-border bg-surface text-[12px] font-medium">
                <button type="button" onClick={() => setMode('single')}
                  className={`h-full flex-1 ${mode === 'single' ? 'bg-primary/15 text-primary' : 'text-muted-foreground hover:text-foreground'}`}>
                  Single symbol
                </button>
                <button type="button" onClick={() => setMode('scan')}
                  className={`h-full flex-1 border-l border-border ${mode === 'scan' ? 'bg-primary/15 text-primary' : 'text-muted-foreground hover:text-foreground'}`}>
                  Scan top coins
                </button>
              </div>

              {mode === 'single' ? (
                <Field label="Symbol">
                  <input className="w-full rounded-md border px-3 py-2 font-mono text-sm uppercase" value={symbol} onChange={e => setSymbol(e.target.value)} required />
                  <Hint>Cặp giao dịch, vd BTCUSDT / ETHUSDT. Phải có trong /symbols.</Hint>
                </Field>
              ) : (
                <Field label="Universe">
                  <select className="w-full rounded-sm border border-border bg-surface px-3 py-2 text-sm text-foreground" value={topN} onChange={e => setTopN(e.target.value)}>
                    <option value="20">Top 20 coins (24h volume)</option>
                    <option value="50">Top 50 coins</option>
                    <option value="100">Top 100 coins</option>
                  </select>
                  <Hint>Chạy strategy trên toàn bộ universe, trả bảng so sánh. Top 50 × 30 ngày 15m ≈ 1-2 phút. Click symbol trong kết quả để xem equity curve chi tiết.</Hint>
                </Field>
              )}
              <div className="grid grid-cols-2 gap-3">
                <Field label="Market">
                  <select className="w-full rounded-sm border border-border bg-surface px-3 py-2 text-sm text-foreground" value={market} onChange={e => setMarket(e.target.value as 'Spot' | 'Futures')}>
                    <option value="Spot">Spot — long only</option>
                    <option value="Futures">Futures — long + short</option>
                  </select>
                  <Hint>Futures: nến fapi, mở được cả short (Sell khi không có position), margin model.</Hint>
                </Field>
                <Field label="Leverage">
                  <input
                    className="w-full rounded-md border px-3 py-2 text-sm disabled:opacity-50"
                    type="number" min={1} max={125} step={1}
                    value={market === 'Futures' ? leverage : '1'}
                    onChange={e => setLeverage(e.target.value)}
                    disabled={market !== 'Futures'}
                  />
                  <Hint>{market === 'Futures' ? 'Notional = vốn × leverage. Lev cao → lãi/lỗ khuếch đại, dễ cháy (equity về 0 = liquidation).' : 'Chỉ dùng được ở Futures.'}</Hint>
                </Field>
              </div>
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
                <Play className="h-4 w-4" />
                {running
                  ? (mode === 'scan' ? `Scanning top ${topN}…` : 'Running...')
                  : (mode === 'scan' ? `Scan top ${topN} coins` : 'Run backtest')}
              </Button>
              {mode === 'scan' && running && (
                <p className="text-[11px] text-muted-foreground">Đang chạy backtest trên {topN} coins (4 song song) — có thể mất 1-3 phút tùy khoảng thời gian…</p>
              )}
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

type ScanRow = {
  symbol: string
  ok: boolean
  error?: string | null
  returnPercent?: number | null
  maxDrawdownPercent?: number | null
  sharpeRatio?: number | null
  tradeCount?: number | null
  winRatePercent?: number | null
  finalEquity?: number | null
}

type ScanResponse = {
  scannedAt: string
  universe: number
  okCount: number
  failedCount: number
  results: ScanRow[]
  failures: ScanRow[]
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
