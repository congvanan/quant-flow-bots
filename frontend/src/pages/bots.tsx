import { Link } from 'react-router-dom'
import { useEffect, useState } from 'react'
import { Play, Square, Trash2, AlertOctagon } from 'lucide-react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AuthGuard } from '@/components/auth-guard'
import { NavBar } from '@/components/nav-bar'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { api, ApiError, type ApiKeyDto, type BotDto, type BotKind, type BotRunMode, type BotStatsRowDto, type StrategyDto } from '@/lib/api'
import { qk } from '@/lib/queries'

export default function BotsPage() {
  return <AuthGuard><Inner /></AuthGuard>
}

function Inner() {
  const qc = useQueryClient()
  const { data: bots = [] } = useQuery({
    queryKey: qk.bots,
    queryFn: () => api<BotDto[]>('/api/bots'),
  })
  const { data: strategies = [] } = useQuery({
    queryKey: qk.strategies,
    queryFn: () => api<StrategyDto[]>('/api/strategies'),
  })
  const { data: stats = [] } = useQuery({
    queryKey: qk.botsStatsSummary,
    queryFn: () => api<BotStatsRowDto[]>('/api/bots/stats/summary'),
    refetchInterval: 30_000,
  })
  const statsByBot = new Map(stats.map(s => [s.botId, s]))
  const [name, setName] = useState('')
  const [strategyId, setStrategyId] = useState('')
  const [symbol, setSymbol] = useState('BTCUSDT')
  const [baseEquity, setBaseEquity] = useState('1000')
  const [riskPct, setRiskPct] = useState('1')
  const [maxQty, setMaxQty] = useState('0.01')
  const [slPct, setSlPct] = useState('2')
  const [tpPct, setTpPct] = useState('4')
  const [trailingPct, setTrailingPct] = useState('')
  const [dailyLoss, setDailyLoss] = useState('5')
  const [maxOpen, setMaxOpen] = useState('1')
  const [maxConsec, setMaxConsec] = useState('3')
  const [cooldown, setCooldown] = useState('15')
  const [killEnabled, setKillEnabled] = useState(true)
  const [runMode, setRunMode] = useState<BotRunMode>('PaperTrading')
  const [kind, setKind] = useState<BotKind>('Signal')
  const [kindConfigJson, setKindConfigJson] = useState<string>('')
  const [apiKeyId, setApiKeyId] = useState<string>('')
  const [leverage, setLeverage] = useState<string>('1')

  const { data: apiKeys = [] } = useQuery({
    queryKey: qk.apiKeys,
    queryFn: () => api<ApiKeyDto[]>('/api/settings/api-keys'),
  })
  const liveKeys = apiKeys.filter(k =>
    (k.exchangeCode === 'binance-futures-testnet' || k.exchangeCode === 'binance-spot-testnet')
    && k.mode === 'Live' && k.isActive)
  // Advanced SL/TP
  const [slKind, setSlKind] = useState<'FixedPercent' | 'Atr'>('FixedPercent')
  const [atrPeriod, setAtrPeriod] = useState('14')
  const [atrMult, setAtrMult] = useState('1.5')
  const [tpLevels, setTpLevels] = useState<{ profitPercent: string; closePercent: string }[]>([
    { profitPercent: '2', closePercent: '30' },
    { profitPercent: '4', closePercent: '30' },
    { profitPercent: '7', closePercent: '40' },
  ])
  const [beEnabled, setBeEnabled] = useState(true)
  const [beTrigger, setBeTrigger] = useState('2')
  const [beOffset, setBeOffset] = useState('0.1')
  const [showAdvanced, setShowAdvanced] = useState(false)
  const [err, setErr] = useState<string | null>(null)

  // Auto-pick the first available strategy once loaded.
  useEffect(() => {
    if (strategies.length > 0 && !strategyId) setStrategyId(strategies[0].id)
  }, [strategies, strategyId])

  useEffect(() => {
    setKindConfigJson(defaultKindConfig(kind))
  }, [kind])

  const invalidateBots = () => qc.invalidateQueries({ queryKey: qk.bots })

  const createMut = useMutation({
    mutationFn: (body: unknown) => api('/api/bots', { method: 'POST', body: JSON.stringify(body) }),
    onSuccess: () => { setName(''); invalidateBots() },
    onError: (e) => setErr(e instanceof ApiError ? e.message : (e as Error).message),
  })

  const startMut = useMutation({
    mutationFn: (id: string) => api(`/api/bots/${id}/start`, { method: 'POST' }),
    onSuccess: invalidateBots,
  })
  const stopMut = useMutation({
    mutationFn: (id: string) => api(`/api/bots/${id}/stop`, { method: 'POST' }),
    onSuccess: invalidateBots,
  })
  const removeMut = useMutation({
    mutationFn: (id: string) => api(`/api/bots/${id}`, { method: 'DELETE' }),
    onSuccess: invalidateBots,
  })

  function create(e: React.FormEvent) {
    e.preventDefault()
    setErr(null)
    createMut.mutate({
      name,
      strategyId,
      symbolCode: symbol.toUpperCase(),
      maxPositionSize: Number(maxQty),
      baseEquityUsdt: Number(baseEquity),
      riskPerTradePercent: riskPct ? Number(riskPct) : null,
      dailyLossStopPercent: dailyLoss ? Number(dailyLoss) : null,
      maxOpenPositions: maxOpen ? Number(maxOpen) : null,
      maxConsecutiveLosses: maxConsec ? Number(maxConsec) : null,
      cooldownAfterLossMinutes: cooldown ? Number(cooldown) : null,
      killSwitchEnabled: killEnabled,
      runMode,
      kind,
      kindConfigJson: kindConfigJson.trim() ? kindConfigJson : null,
      apiKeyId: apiKeyId || null,
      leverage: leverage ? Math.max(1, Math.min(125, Number(leverage))) : 1,
      stopLossKind: slKind,
      defaultStopLossPercent: slPct ? Number(slPct) : null,
      atrPeriod: atrPeriod ? Number(atrPeriod) : null,
      atrMultiplier: atrMult ? Number(atrMult) : null,
      defaultTakeProfitPercent: tpPct ? Number(tpPct) : null,
      takeProfitLevels: tpLevels
        .map(l => ({ profitPercent: Number(l.profitPercent), closePercent: Number(l.closePercent) }))
        .filter(l => l.profitPercent > 0 && l.closePercent > 0),
      defaultTrailingStopPercent: trailingPct ? Number(trailingPct) : null,
      breakEvenEnabled: beEnabled,
      breakEvenTriggerPercent: beTrigger ? Number(beTrigger) : null,
      breakEvenOffsetPercent: beOffset ? Number(beOffset) : null,
    })
  }

  const start = (id: string) => startMut.mutate(id)
  const stop = (id: string) => stopMut.mutate(id)
  const remove = (id: string) => { if (confirm('Delete bot?')) removeMut.mutate(id) }

  return (
    <main className="min-h-screen">
      <NavBar />
      <div className="container grid gap-4 py-4 lg:grid-cols-[1fr_400px]">
        <Card>
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <div>
              <CardTitle className="text-sm">Bots</CardTitle>
              <p className="text-[11px] text-muted-foreground">
                {bots.length} configured · total realized:{' '}
                <span className={pnlCls(stats.reduce((acc, s) => acc + s.totalRealizedPnl, 0))}>
                  {stats.reduce((acc, s) => acc + s.totalRealizedPnl, 0) >= 0 ? '+' : ''}
                  {stats.reduce((acc, s) => acc + s.totalRealizedPnl, 0).toFixed(2)} USDT
                </span>
                {' · '}
                {stats.reduce((acc, s) => acc + s.totalTrades, 0)} trades
              </p>
            </div>
          </CardHeader>
          <CardContent className="px-3 pt-0 pb-2">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead className="pl-3">Name</TableHead>
                  <TableHead>Symbol</TableHead>
                  <TableHead>Strategy</TableHead>
                  <TableHead>State</TableHead>
                  <TableHead className="text-right">P&L</TableHead>
                  <TableHead className="text-right">Today</TableHead>
                  <TableHead className="text-right">Win%</TableHead>
                  <TableHead className="text-right">Trades</TableHead>
                  <TableHead className="text-right">MDD</TableHead>
                  <TableHead className="text-right pr-3"></TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {bots.map(b => {
                  const tripped = !!b.killSwitchTrippedAt
                  const s = statsByBot.get(b.id)
                  return (
                    <TableRow key={b.id}>
                      <TableCell className="pl-3 font-medium">
                        <Link to={`/bots/${b.id}`} className="hover:text-primary">{b.name}</Link>
                        {tripped && (
                          <span className="ml-2 inline-flex items-center gap-1 text-[10px] text-destructive">
                            <AlertOctagon className="h-3 w-3" /> KILL
                          </span>
                        )}
                      </TableCell>
                      <TableCell className="font-mono text-[12px]">{b.symbolCode}</TableCell>
                      <TableCell className="text-[12px] text-muted-foreground">
                        <span className="mr-1 rounded-sm bg-surface-2 px-1 text-[10px] font-mono uppercase">{b.kind ?? 'Signal'}</span>
                        {b.strategyKind}
                      </TableCell>
                      <TableCell>
                        <div className="flex items-center gap-1">
                          <Badge
                            variant="outline"
                            className={`h-5 border-0 px-2 text-[10px] font-mono ${
                              b.state === 'Running' ? 'bg-success/15 text-up' : 'bg-surface-2 text-muted-foreground'
                            }`}
                          >
                            {b.state.toUpperCase()}
                          </Badge>
                          <Badge variant="outline" className={`h-5 border-0 px-2 text-[10px] font-mono ${runModeBadgeCls(b.runMode)}`}>
                            {runModeShort(b.runMode)}
                          </Badge>
                        </div>
                      </TableCell>
                      <TableCell className="text-right num text-[12px]">
                        <span className={pnlCls(s?.totalRealizedPnl)}>
                          {s ? `${s.totalRealizedPnl >= 0 ? '+' : ''}${s.totalRealizedPnl.toFixed(2)}` : '—'}
                        </span>
                        {s && Math.abs(s.totalReturnPercent) > 0.005 && (
                          <span className={`ml-1 text-[10px] ${pnlCls(s.totalReturnPercent)}`}>
                            ({s.totalReturnPercent >= 0 ? '+' : ''}{s.totalReturnPercent.toFixed(2)}%)
                          </span>
                        )}
                      </TableCell>
                      <TableCell className={`text-right num text-[12px] ${pnlCls(s?.pnlToday)}`}>
                        {s && s.pnlToday !== 0 ? `${s.pnlToday >= 0 ? '+' : ''}${s.pnlToday.toFixed(2)}` : '—'}
                      </TableCell>
                      <TableCell className="text-right num text-[12px] text-muted-foreground">
                        {s && s.totalTrades > 0 ? `${s.winRatePercent.toFixed(0)}%` : '—'}
                      </TableCell>
                      <TableCell className="text-right num text-[12px] text-muted-foreground">
                        {s ? `${s.totalTrades}${s.openPositions > 0 ? `/+${s.openPositions}` : ''}` : '—'}
                      </TableCell>
                      <TableCell className="text-right num text-[12px] text-warning">
                        {s && s.maxDrawdownPercent > 0 ? `${s.maxDrawdownPercent.toFixed(1)}%` : '—'}
                      </TableCell>
                      <TableCell className="pr-3 text-right">
                        <div className="flex justify-end gap-1">
                          {b.state === 'Running'
                            ? <Button size="sm" variant="outline" className="h-7 w-7 p-0" onClick={() => stop(b.id)}><Square className="h-3.5 w-3.5" /></Button>
                            : <Button size="sm" variant="outline" className="h-7 w-7 p-0" onClick={() => start(b.id)}><Play className="h-3.5 w-3.5" /></Button>}
                          <Button size="sm" variant="ghost" className="h-7 w-7 p-0 text-destructive hover:bg-destructive/10" onClick={() => remove(b.id)}><Trash2 className="h-3.5 w-3.5" /></Button>
                        </div>
                      </TableCell>
                    </TableRow>
                  )
                })}
                {bots.length === 0 && (
                  <TableRow><TableCell colSpan={10} className="py-6 text-center text-xs text-muted-foreground">No bots yet. Create one →</TableCell></TableRow>
                )}
              </TableBody>
            </Table>
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm">New Paper Bot</CardTitle>
            <p className="text-[11px] text-muted-foreground">Position sizing = Risk% × Equity / SL distance</p>
          </CardHeader>
          <CardContent className="px-4 pb-4">
            <form onSubmit={create} className="space-y-2.5">
              <Field label="Name">
                <Input value={name} onChange={e => setName(e.target.value)} required />
              </Field>

              <Field label="Strategy">
                <Select value={strategyId} onChange={e => setStrategyId(e.target.value)} required>
                  <option value="">— pick —</option>
                  {strategies.map(s => <option key={s.id} value={s.id}>{s.name} ({s.kind})</option>)}
                </Select>
              </Field>

              <Field label="Symbol">
                <Input className="font-mono uppercase" value={symbol} onChange={e => setSymbol(e.target.value)} required />
                <Hint>Only BTC/ETH/SOL/BNB/XRP USDT stream in phase 3.</Hint>
              </Field>

              <Field label="Bot kind">
                <Select value={kind} onChange={e => setKind(e.target.value as BotKind)}>
                  <option value="Signal">Signal — strategy-driven (cần Strategy)</option>
                  <option value="Dca">DCA — base + safety orders averaging down</option>
                  <option value="Grid">Grid — buy/sell ladder giữa lower/upper</option>
                  <option value="Scalp">Scalp — small TP/SL trên low-range candle</option>
                </Select>
              </Field>

              {kind !== 'Signal' && (
                <Field label={`${kind} config (JSON)`}>
                  <textarea
                    className="h-32 w-full rounded-sm border border-border bg-surface px-2.5 py-1.5 font-mono text-[11px] outline-none focus:border-primary"
                    value={kindConfigJson}
                    onChange={e => setKindConfigJson(e.target.value)}
                  />
                  <Hint>{kindHint(kind)}</Hint>
                </Field>
              )}

              <Field label="Run mode">
                <Select value={runMode} onChange={e => setRunMode(e.target.value as BotRunMode)}>
                  <option value="PaperTrading">Paper trading — sinh signal + đặt lệnh giả lập</option>
                  <option value="ScanOnly">Scan only — sinh signal, không đặt lệnh</option>
                  <option value="Off">Off — bot tạm tắt</option>
                  <option value="LiveTrading" disabled={liveKeys.length === 0}>
                    Live (Spot/Futures TESTNET){liveKeys.length === 0 ? ' — cần API key đã validate ↓' : ''}
                  </option>
                </Select>
                <Hint>Live trading chỉ chạy trên Binance Futures TESTNET (không phải tiền thật).</Hint>
              </Field>

              {runMode === 'LiveTrading' && (() => {
                const selectedKey = liveKeys.find(k => k.id === apiKeyId)
                const isFutures = selectedKey?.exchangeCode === 'binance-futures-testnet'
                return (
                  <>
                    <Field label="API key (Spot/Futures TESTNET, mode=Live, validated)">
                      <Select value={apiKeyId} onChange={e => setApiKeyId(e.target.value)} required>
                        <option value="">— pick a validated key —</option>
                        {liveKeys.map(k => (
                          <option key={k.id} value={k.id}>
                            [{k.exchangeCode === 'binance-spot-testnet' ? 'SPOT' : 'FUT'}] {k.label} · {k.keyPreview} · validated {k.lastValidatedAt ? new Date(k.lastValidatedAt).toLocaleDateString() : 'never'}
                          </option>
                        ))}
                      </Select>
                      <Hint>Key phải: mode=Live, exchange Spot/Futures testnet, validate trong 7 ngày, KHÔNG có withdraw permission. Thêm/validate ở /settings.</Hint>
                    </Field>
                    {isFutures && (
                      <Field label="Leverage (1–125)">
                        <Input type="number" min={1} max={125} step={1} value={leverage} onChange={e => setLeverage(e.target.value)} />
                        <Hint>Bot tự gọi <code className="font-mono">/fapi/v1/leverage</code> trước lệnh entry. Spot bỏ qua field này.</Hint>
                      </Field>
                    )}
                  </>
                )
              })()}

              <Divider label="Capital & sizing" />

              <div className="grid grid-cols-3 gap-2">
                <Field label="Equity USDT">
                  <Input type="number" step="1" value={baseEquity} onChange={e => setBaseEquity(e.target.value)} required />
                </Field>
                <Field label="Risk %/trade">
                  <Input type="number" step="0.1" value={riskPct} onChange={e => setRiskPct(e.target.value)} />
                </Field>
                <Field label="Max qty cap">
                  <Input type="number" step="0.0001" value={maxQty} onChange={e => setMaxQty(e.target.value)} required />
                </Field>
              </div>

              <Divider label="Stop loss / Take profit" />

              <div className="grid grid-cols-3 gap-2">
                <Field label="SL % (fixed)"><Input type="number" step="0.1" value={slPct} onChange={e => setSlPct(e.target.value)} /></Field>
                <Field label="TP % (legacy)"><Input type="number" step="0.1" value={tpPct} onChange={e => setTpPct(e.target.value)} /></Field>
                <Field label="Trailing %"><Input type="number" step="0.1" placeholder="opt" value={trailingPct} onChange={e => setTrailingPct(e.target.value)} /></Field>
              </div>

              <button
                type="button"
                onClick={() => setShowAdvanced(v => !v)}
                className="flex w-full items-center justify-between rounded-sm border border-border bg-surface px-2.5 py-1.5 text-[11px] uppercase tracking-wider text-muted-foreground hover:text-foreground"
              >
                <span>Advanced SL/TP (ATR · Multi-TP · Break-even)</span>
                <span className="font-mono">{showAdvanced ? '−' : '+'}</span>
              </button>

              {showAdvanced && (
                <div className="space-y-3 rounded-sm border border-border/60 bg-surface/40 p-2.5">
                  <Field label="Stop loss kind">
                    <Select value={slKind} onChange={e => setSlKind(e.target.value as 'FixedPercent' | 'Atr')}>
                      <option value="FixedPercent">Fixed percent</option>
                      <option value="Atr">ATR-based</option>
                    </Select>
                  </Field>
                  {slKind === 'Atr' && (
                    <div className="grid grid-cols-2 gap-2">
                      <Field label="ATR period"><Input type="number" step="1" value={atrPeriod} onChange={e => setAtrPeriod(e.target.value)} /></Field>
                      <Field label="ATR multiplier"><Input type="number" step="0.1" value={atrMult} onChange={e => setAtrMult(e.target.value)} /></Field>
                    </div>
                  )}

                  <div className="space-y-1.5">
                    <div className="flex items-center justify-between">
                      <span className="text-[10px] uppercase tracking-wider text-muted-foreground">Take-profit ladder</span>
                      <button
                        type="button"
                        onClick={() => setTpLevels(prev => [...prev, { profitPercent: '', closePercent: '' }])}
                        className="text-[11px] text-primary hover:underline"
                      >+ Add level</button>
                    </div>
                    {tpLevels.map((lv, i) => {
                      const sumClose = tpLevels.reduce((acc, l) => acc + (Number(l.closePercent) || 0), 0)
                      return (
                        <div key={i} className="flex items-center gap-1.5">
                          <span className="w-7 text-center font-mono text-[10px] text-muted-foreground">TP{i + 1}</span>
                          <Input
                            type="number"
                            step="0.1"
                            placeholder="profit %"
                            value={lv.profitPercent}
                            onChange={e => setTpLevels(prev => prev.map((p, idx) => idx === i ? { ...p, profitPercent: e.target.value } : p))}
                            className="flex-1"
                          />
                          <Input
                            type="number"
                            step="1"
                            placeholder="close %"
                            value={lv.closePercent}
                            onChange={e => setTpLevels(prev => prev.map((p, idx) => idx === i ? { ...p, closePercent: e.target.value } : p))}
                            className="flex-1"
                          />
                          <button
                            type="button"
                            onClick={() => setTpLevels(prev => prev.filter((_, idx) => idx !== i))}
                            className="text-destructive hover:text-destructive/70"
                            aria-label="remove"
                          >×</button>
                          {i === tpLevels.length - 1 && (
                            <span className={`ml-1 font-mono text-[10px] ${sumClose === 100 ? 'text-up' : sumClose > 100 ? 'text-destructive' : 'text-warning'}`}>
                              Σ{sumClose}%
                            </span>
                          )}
                        </div>
                      )
                    })}
                    <Hint>Multi-TP overrides legacy single TP. Σ close% nên = 100. PositionMonitor sẽ partial-close từng level.</Hint>
                  </div>

                  <label className="flex items-center gap-2 rounded-sm border border-border bg-surface px-2.5 py-1.5 text-xs">
                    <input type="checkbox" checked={beEnabled} onChange={e => setBeEnabled(e.target.checked)} className="accent-primary" />
                    <span>Break-even shift</span>
                  </label>
                  {beEnabled && (
                    <div className="grid grid-cols-2 gap-2">
                      <Field label="Trigger profit %"><Input type="number" step="0.1" value={beTrigger} onChange={e => setBeTrigger(e.target.value)} /></Field>
                      <Field label="SL offset above entry %"><Input type="number" step="0.05" value={beOffset} onChange={e => setBeOffset(e.target.value)} /></Field>
                    </div>
                  )}
                </div>
              )}

              <Divider label="Risk Engine" />

              <div className="grid grid-cols-2 gap-2">
                <Field label="Daily loss stop %"><Input type="number" step="0.1" value={dailyLoss} onChange={e => setDailyLoss(e.target.value)} /></Field>
                <Field label="Max open positions"><Input type="number" step="1" value={maxOpen} onChange={e => setMaxOpen(e.target.value)} /></Field>
                <Field label="Max consec. losses"><Input type="number" step="1" value={maxConsec} onChange={e => setMaxConsec(e.target.value)} /></Field>
                <Field label="Cooldown (min)"><Input type="number" step="1" value={cooldown} onChange={e => setCooldown(e.target.value)} /></Field>
              </div>

              <label className="flex items-center gap-2 rounded-sm border border-border bg-surface px-2.5 py-2 text-xs">
                <input type="checkbox" checked={killEnabled} onChange={e => setKillEnabled(e.target.checked)} className="accent-primary" />
                <span>Kill switch auto-trip on limit breach</span>
              </label>

              {err && <p className="text-xs text-destructive">{err}</p>}
              <Button type="submit" className="w-full bg-primary text-primary-foreground hover:bg-primary/90" disabled={createMut.isPending || !strategyId}>
                {createMut.isPending ? 'Saving…' : 'Create bot'}
              </Button>
              {strategies.length === 0 && <p className="text-[11px] text-warning">Create a strategy first in /strategies.</p>}
            </form>
          </CardContent>
        </Card>
      </div>
    </main>
  )
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="block space-y-1">
      <span className="text-[10px] uppercase tracking-wider text-muted-foreground">{label}</span>
      <div>{children}</div>
    </label>
  )
}

function Input(props: React.InputHTMLAttributes<HTMLInputElement>) {
  const { className = '', ...rest } = props
  return <input {...rest} className={`h-8 w-full rounded-sm border border-border bg-surface px-2.5 text-sm outline-none focus:border-primary ${className}`} />
}

function Select(props: React.SelectHTMLAttributes<HTMLSelectElement>) {
  const { className = '', children, ...rest } = props
  return <select {...rest} className={`h-8 w-full rounded-sm border border-border bg-surface px-2 text-sm outline-none focus:border-primary ${className}`}>{children}</select>
}

function Hint({ children }: { children: React.ReactNode }) {
  return <p className="mt-1 text-[10px] text-muted-foreground">{children}</p>
}

function pnlCls(v?: number): string {
  if (v == null || v === 0) return 'text-muted-foreground'
  return v > 0 ? 'text-up' : 'text-down'
}

function runModeBadgeCls(rm: BotRunMode): string {
  switch (rm) {
    case 'PaperTrading': return 'bg-primary/20 text-primary'
    case 'ScanOnly': return 'bg-info/15 text-info'
    case 'LiveTrading': return 'bg-destructive/20 text-destructive'
    case 'Off':
    default: return 'bg-surface-2 text-muted-foreground'
  }
}

function runModeShort(rm: BotRunMode): string {
  switch (rm) {
    case 'PaperTrading': return 'PAPER'
    case 'ScanOnly': return 'SCAN'
    case 'LiveTrading': return 'LIVE'
    default: return 'OFF'
  }
}

function defaultKindConfig(kind: BotKind): string {
  switch (kind) {
    case 'Dca':
      return JSON.stringify({ baseQuoteAmount: 100, safetyQuoteAmount: 100, maxSafetyOrders: 5, priceStepPercent: 1.5, volumeScale: 1.5, takeProfitPercent: 1.0 }, null, 2)
    case 'Grid':
      return JSON.stringify({ upperPrice: 0, lowerPrice: 0, gridLevels: 10, quotePerGrid: 50, takeProfitPercent: 0.5 }, null, 2)
    case 'Scalp':
      return JSON.stringify({ quoteAmount: 50, spreadBpsMin: 1, spreadBpsMax: 30, takeProfitPercent: 0.2, stopLossPercent: 0.3, cooldownSeconds: 30 }, null, 2)
    default:
      return ''
  }
}

function kindHint(kind: BotKind): string {
  switch (kind) {
    case 'Dca': return 'Mỗi lần giá rớt PriceStepPercent so với entry, bot mua thêm SafetyOrder theo VolumeScale. Đóng toàn bộ khi đạt TP%.'
    case 'Grid': return 'Đặt UpperPrice & LowerPrice (USDT). Bot chia GridLevels rung, BUY khi giá rớt qua rung, SELL khi vượt lên.'
    case 'Scalp': return 'Vào lệnh nhỏ khi range candle nằm trong [SpreadBpsMin, SpreadBpsMax]. SL/TP dùng cài đặt mặc định của bot.'
    default: return ''
  }
}

function Divider({ label }: { label: string }) {
  return (
    <div className="flex items-center gap-2 pt-1">
      <span className="text-[9px] uppercase tracking-[0.2em] text-muted-foreground">{label}</span>
      <div className="h-px flex-1 bg-border/50" />
    </div>
  )
}
