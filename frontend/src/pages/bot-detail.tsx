import { useParams } from 'react-router-dom'
import { Fragment, useEffect, useState } from 'react'
import { Play, Square, AlertOctagon, ShieldCheck, ShieldOff } from 'lucide-react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AuthGuard } from '@/components/auth-guard'
import { NavBar } from '@/components/nav-bar'
import { BotStatsPanel } from '@/components/bot-stats-panel'
import { BotAccountsPanel } from '@/components/bot-accounts-panel'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { api, ApiError, type BotDto, type BotRunMode, type FuturesPositionSnapshot, type OrderDto, type PositionDto, type RiskEventDto, type SignalDto, type TpLevelState } from '@/lib/api'
import { qk } from '@/lib/queries'
import { useSignalR } from '@/lib/signalr-context'

type LiveEvent = { id: string; kind: string; message: string; at: string }

export default function BotDetailPage() {
  return <AuthGuard><Inner /></AuthGuard>
}

function Inner() {
  const params = useParams<{ id: string }>()
  const id = params.id ?? ''
  const qc = useQueryClient()
  const { connection } = useSignalR()
  const [live, setLive] = useState<LiveEvent[]>([])

  // SignalR bridge handles invalidation; here we only query the data.
  const { data: bot, error: botErr } = useQuery({
    queryKey: qk.bot(id),
    queryFn: () => api<BotDto[]>('/api/bots').then(list => list.find(x => x.id === id) ?? null),
    enabled: !!id,
  })
  const { data: orders = [] } = useQuery({
    queryKey: qk.botOrders(id),
    queryFn: () => api<OrderDto[]>(`/api/bots/${id}/orders`),
    enabled: !!id,
  })
  const { data: positions = [] } = useQuery({
    queryKey: qk.botPositions(id),
    queryFn: () => api<PositionDto[]>(`/api/bots/${id}/positions`),
    enabled: !!id,
  })
  const { data: signals = [] } = useQuery({
    queryKey: qk.botSignals(id),
    queryFn: () => api<SignalDto[]>(`/api/bots/${id}/signals`),
    enabled: !!id,
  })
  const { data: riskEvents = [] } = useQuery({
    queryKey: qk.botRiskEvents(id),
    queryFn: () => api<RiskEventDto[]>(`/api/bots/${id}/risk-events`),
    enabled: !!id,
  })

  // Join SignalR bot group + maintain a local "live events" log.
  // Cache invalidation is centralized in SignalRQueryBridge.
  useEffect(() => {
    if (!connection || !id) return
    void connection.invoke('JoinBotGroup', id).catch(() => undefined)
    const handler = (e: { botId: string; kind: string; message: string; at: string }) => {
      if (e.botId !== id) return
      setLive(prev => [{ id: `${e.at}-${e.kind}-${Math.random()}`, kind: e.kind, message: e.message, at: e.at }, ...prev].slice(0, 80))
    }
    connection.on('bot', handler)
    return () => {
      connection.off('bot', handler)
      void connection.invoke('LeaveBotGroup', id).catch(() => undefined)
    }
  }, [connection, id])

  const invalidateBot = () => {
    qc.invalidateQueries({ queryKey: qk.bot(id) })
    qc.invalidateQueries({ queryKey: qk.bots })
  }

  const startMut = useMutation({
    mutationFn: () => api(`/api/bots/${id}/start`, { method: 'POST' }),
    onSuccess: invalidateBot,
  })
  const stopMut = useMutation({
    mutationFn: () => api(`/api/bots/${id}/stop`, { method: 'POST' }),
    onSuccess: invalidateBot,
  })
  const tripKillMut = useMutation({
    mutationFn: (reason: string) => api(`/api/bots/${id}/kill-switch`, { method: 'POST', body: JSON.stringify({ reason }) }),
    onSuccess: invalidateBot,
  })
  const resetKillMut = useMutation({
    mutationFn: () => api(`/api/bots/${id}/kill-switch/reset`, { method: 'POST' }),
    onSuccess: invalidateBot,
  })
  const runModeMut = useMutation({
    mutationFn: (next: BotRunMode) => api(`/api/bots/${id}/risk`, { method: 'PATCH', body: JSON.stringify({ runMode: next }) }),
    onSuccess: invalidateBot,
  })
  const { data: livePos, refetch: refetchLive } = useQuery({
    queryKey: ['bot', id, 'live-position'],
    queryFn: () => api<FuturesPositionSnapshot>(`/api/bots/${id}/live-position`),
    enabled: !!bot && bot.runMode === 'LiveTrading',
    refetchInterval: 15_000,
  })
  const smokeMut = useMutation({
    mutationFn: () => api<unknown>(`/api/bots/${id}/live-smoke-test`, { method: 'POST' }),
    onSuccess: (r) => { alert('Smoke test result:\n' + JSON.stringify(r, null, 2)); void refetchLive() },
    onError: (e) => alert('Smoke test failed: ' + (e instanceof ApiError ? e.message : (e as Error).message)),
  })

  const start = () => startMut.mutate()
  const stop = () => stopMut.mutate()
  const tripKill = () => {
    const reason = prompt('Reason for tripping kill switch?', 'manual')
    if (reason != null) tripKillMut.mutate(reason)
  }
  const resetKill = () => { if (confirm('Reset kill switch?')) resetKillMut.mutate() }
  const changeRunMode = (next: BotRunMode) => runModeMut.mutate(next)

  if (!bot) return <main className="min-h-screen"><NavBar /><div className="container py-6 text-sm text-muted-foreground">{(botErr as Error | undefined)?.message ?? 'Loading…'}</div></main>

  const openPositions = positions.filter(p => p.status === 'Open')
  const totalPnl = positions.reduce((acc, p) => acc + Number(p.realizedPnl ?? 0), 0)
  const lossesToday = positions.filter(p => p.status === 'Closed' && p.closedAt && new Date(p.closedAt).toDateString() === new Date().toDateString() && p.realizedPnl < 0).length
  const tripped = !!bot.killSwitchTrippedAt

  return (
    <main className="min-h-screen">
      <NavBar />
      <div className="container space-y-4 py-4">
        {/* Header card */}
        <Card>
          <CardHeader className="flex flex-row items-center justify-between gap-4 pb-2">
            <div className="space-y-1">
              <div className="flex items-center gap-2">
                <CardTitle className="text-base">{bot.name}</CardTitle>
                <Badge variant="outline" className={`h-5 border-0 px-2 text-[10px] font-mono ${bot.state === 'Running' ? 'bg-success/15 text-up' : 'bg-surface-2 text-muted-foreground'}`}>
                  {bot.state.toUpperCase()}
                </Badge>
                <select
                  value={bot.runMode}
                  onChange={e => changeRunMode(e.target.value as BotRunMode)}
                  className={`h-5 rounded-sm border-0 px-1.5 text-[10px] font-mono outline-none ${runModeBadgeCls(bot.runMode)}`}
                  title="Run mode"
                >
                  <option value="PaperTrading">PAPER</option>
                  <option value="ScanOnly">SCAN ONLY</option>
                  <option value="Off">OFF</option>
                  <option value="LiveTrading" disabled={!bot.apiKeyId}>LIVE (TESTNET)</option>
                </select>
                {tripped && (
                  <Badge variant="outline" className="h-5 border-0 bg-destructive/15 px-2 text-[10px] font-mono text-destructive">
                    KILL SWITCH
                  </Badge>
                )}
              </div>
              <p className="text-[12px] text-muted-foreground">
                <span className="font-mono">{bot.symbolCode}</span> · {bot.strategyKind} · {bot.mode}
              </p>
            </div>
            <div className="flex items-center gap-1.5">
              {tripped ? (
                <Button onClick={resetKill} size="sm" variant="outline" className="h-7 gap-1.5 border-warning/50 text-warning hover:bg-warning/10">
                  <ShieldCheck className="h-3.5 w-3.5" /> Reset
                </Button>
              ) : (
                <Button onClick={tripKill} size="sm" variant="outline" className="h-7 gap-1.5 border-destructive/40 text-destructive hover:bg-destructive/10">
                  <ShieldOff className="h-3.5 w-3.5" /> Kill
                </Button>
              )}
              {bot.state === 'Running' ? (
                <Button onClick={stop} size="sm" variant="outline" className="h-7 gap-1.5"><Square className="h-3.5 w-3.5" /> Stop</Button>
              ) : (
                <Button onClick={start} size="sm" className="h-7 gap-1.5 bg-primary text-primary-foreground hover:bg-primary/90"><Play className="h-3.5 w-3.5" /> Start</Button>
              )}
            </div>
          </CardHeader>

          <CardContent className="grid grid-cols-2 gap-3 md:grid-cols-6">
            <Metric label="Open positions" value={`${openPositions.length} / ${bot.maxOpenPositions}`} />
            <Metric label="Realized PnL" value={totalPnl.toFixed(2)} tone={totalPnl >= 0 ? 'up' : 'down'} />
            <Metric label="Equity" value={bot.baseEquityUsdt.toString()} />
            <Metric label="Risk / trade" value={bot.riskPerTradePercent != null ? `${bot.riskPerTradePercent}%` : '—'} />
            <Metric label="Daily loss stop" value={`${bot.dailyLossStopPercent}%`} tone="warn" />
            <Metric label="Losses today" value={lossesToday.toString()} tone={lossesToday > 0 ? 'down' : undefined} />
          </CardContent>
        </Card>

        <BotStatsPanel botId={bot.id} />

        <BotAccountsPanel botId={bot.id} executionMarket={bot.executionMarket} />

        {bot.runMode === 'LiveTrading' && (() => {
          const isSpot = livePos && !('error' in livePos) && 'kind' in livePos && livePos.kind === 'spot'
          const isFutures = livePos && !('error' in livePos) && !('kind' in livePos)
          return (
          <Card className="border-primary/40">
            <CardHeader className="flex flex-row items-center justify-between gap-2 pb-2">
              <CardTitle className="text-sm">
                Live ({isSpot ? 'Spot' : 'Futures'} TESTNET){!isSpot && ` · leverage ${bot.leverage}x`}
              </CardTitle>
              <Button size="sm" variant="outline" onClick={() => smokeMut.mutate()} disabled={smokeMut.isPending}>
                {smokeMut.isPending ? 'Testing…' : 'Smoke test order'}
              </Button>
            </CardHeader>
            <CardContent className="space-y-2 px-4 pb-3 text-xs">
              {!livePos && <p className="text-muted-foreground">Loading live snapshot…</p>}
              {livePos && 'error' in livePos && <p className="text-destructive">snapshot error: {livePos.error}</p>}
              {isFutures && (
                <div className="grid grid-cols-3 gap-2 md:grid-cols-6">
                  <LiveMetric label="Position" value={livePos.positionAmt.toFixed(6)} tone={livePos.positionAmt === 0 ? undefined : livePos.positionAmt > 0 ? 'up' : 'down'} />
                  <LiveMetric label="Entry" value={livePos.entryPrice ? livePos.entryPrice.toFixed(2) : '—'} />
                  <LiveMetric label="Mark" value={livePos.markPrice ? livePos.markPrice.toFixed(2) : '—'} />
                  <LiveMetric label="uPnL" value={livePos.unrealizedProfit.toFixed(2)} tone={livePos.unrealizedProfit >= 0 ? 'up' : 'down'} />
                  <LiveMetric label="Liq" value={livePos.liquidationPrice ? livePos.liquidationPrice.toFixed(2) : '—'} tone="warn" />
                  <LiveMetric label="Lev" value={`${livePos.leverage}x`} />
                </div>
              )}
              {isSpot && (
                <div className="grid grid-cols-3 gap-2 md:grid-cols-6">
                  <LiveMetric label={`${livePos.baseAsset} free`} value={livePos.baseFree.toFixed(6)} tone={livePos.baseFree > 0 ? 'up' : undefined} />
                  <LiveMetric label={`${livePos.baseAsset} locked`} value={livePos.baseLocked.toFixed(6)} />
                  <LiveMetric label={`${livePos.quoteAsset} free`} value={livePos.quoteFree.toFixed(2)} />
                  <LiveMetric label={`${livePos.quoteAsset} locked`} value={livePos.quoteLocked.toFixed(2)} />
                  <LiveMetric label="canTrade" value={livePos.canTrade ? 'yes' : 'NO'} tone={livePos.canTrade ? 'up' : 'down'} />
                  <LiveMetric label="canWithdraw" value={livePos.canWithdraw ? 'YES' : 'no'} tone={livePos.canWithdraw ? 'down' : 'up'} />
                </div>
              )}
              <p className="text-[10px] text-muted-foreground">
                {isSpot
                  ? 'Spot: SL/TP do PositionMonitor in-process canh, đặt MARKET sell qua dispatcher. Reconciler so balance vs DB mỗi 30s.'
                  : 'Reconciler chạy 30s/lần đối chiếu DB ↔ /fapi/v2/positionRisk. Server-side STOP_MARKET + TAKE_PROFIT_MARKET (reduce-only) bảo vệ vị thế kể cả khi process die.'}
              </p>
            </CardContent>
          </Card>
          )
        })()}

        {tripped && (
          <Card className="border-destructive/40">
            <CardContent className="flex items-start gap-3 px-4 py-3 text-sm">
              <AlertOctagon className="mt-0.5 h-4 w-4 shrink-0 text-destructive" />
              <div className="space-y-1">
                <p className="font-medium text-destructive">Kill switch active — new orders blocked</p>
                <p className="text-xs text-muted-foreground">
                  Reason: <span className="font-mono">{bot.killSwitchReason}</span> · tripped at <span className="font-mono">{new Date(bot.killSwitchTrippedAt!).toLocaleString()}</span>
                </p>
                <p className="text-xs text-muted-foreground">Open positions tiếp tục được PositionMonitor canh SL/TP. Click <strong>Reset</strong> để cho phép vào lệnh mới.</p>
              </div>
            </CardContent>
          </Card>
        )}

        <div className="grid gap-4 lg:grid-cols-2">
          {/* Open positions */}
          <Card>
            <CardHeader className="pb-2"><CardTitle className="text-sm">Open positions</CardTitle></CardHeader>
            <CardContent className="px-3 pt-0 pb-2">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead className="pl-3">Side</TableHead>
                    <TableHead className="text-right">Qty</TableHead>
                    <TableHead className="text-right">Entry</TableHead>
                    <TableHead className="text-right">SL</TableHead>
                    <TableHead className="text-right">TP</TableHead>
                    <TableHead className="text-right">Trail</TableHead>
                    <TableHead className="pr-3 text-right">Opened</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {openPositions.map(p => {
                    const tpLevels = parseTpLevels(p.takeProfitLevelsJson)
                    const remainPct = p.originalQuantity > 0 ? Math.round((p.quantity / p.originalQuantity) * 100) : 100
                    return (
                      <Fragment key={p.id}>
                        <TableRow>
                          <TableCell className="pl-3">
                            <span className={p.side === 'Long' ? 'text-up' : 'text-down'}>{p.side}</span>
                            {p.breakEvenTriggered && (
                              <span className="ml-1.5 chip !text-up !border-success/40 !text-[9px]">BE</span>
                            )}
                          </TableCell>
                          <TableCell className="text-right num">
                            {p.quantity}
                            {p.originalQuantity !== p.quantity && (
                              <span className="ml-1 text-[10px] text-muted-foreground">/{p.originalQuantity}</span>
                            )}
                          </TableCell>
                          <TableCell className="text-right num">{p.entryPrice}</TableCell>
                          <TableCell className="text-right num text-down">{p.stopLossPrice?.toFixed(4) ?? '—'}</TableCell>
                          <TableCell className="text-right num text-up">
                            {tpLevels.length > 0
                              ? <span title={tpLevels.map((l, i) => `TP${i + 1}: ${l.closePrice.toFixed(4)} (${l.closePercent}%)${l.hitAt ? ' ✓' : ''}`).join('\n')}>
                                  {tpLevels.filter(l => !l.hitAt).length}/{tpLevels.length}
                                </span>
                              : (p.takeProfitPrice?.toFixed(4) ?? '—')}
                          </TableCell>
                          <TableCell className="text-right num">{p.trailingStopPercent ? `${p.trailingStopPercent}%` : '—'}</TableCell>
                          <TableCell className="pr-3 text-right text-[11px] text-muted-foreground">{new Date(p.openedAt).toLocaleTimeString()}</TableCell>
                        </TableRow>
                        {tpLevels.length > 0 && (
                          <TableRow className="!border-b-0 hover:!bg-transparent">
                            <TableCell colSpan={7} className="px-3 py-1.5">
                              <TpLadder levels={tpLevels} remainPct={remainPct} />
                            </TableCell>
                          </TableRow>
                        )}
                      </Fragment>
                    )
                  })}
                  {openPositions.length === 0 && (
                    <TableRow><TableCell colSpan={7} className="py-4 text-center text-xs text-muted-foreground">No open positions.</TableCell></TableRow>
                  )}
                </TableBody>
              </Table>
            </CardContent>
          </Card>

          {/* Live events */}
          <Card>
            <CardHeader className="pb-2"><CardTitle className="text-sm">Live events</CardTitle></CardHeader>
            <CardContent className="space-y-1.5 px-3 pb-3 text-xs">
              {live.length === 0 && <p className="px-2 py-1 text-muted-foreground">Waiting for bot events…</p>}
              {live.map(e => (
                <div key={e.id} className={`rounded-sm border border-border/40 px-2.5 py-1.5 ${kindBg(e.kind)}`}>
                  <div className="flex items-center justify-between text-[10px] uppercase tracking-wider text-muted-foreground">
                    <span>{e.kind}</span>
                    <span className="font-mono">{new Date(e.at).toLocaleTimeString()}</span>
                  </div>
                  <div className="font-mono text-[12px]">{e.message}</div>
                </div>
              ))}
            </CardContent>
          </Card>

          {/* Risk events */}
          <Card className="lg:col-span-2">
            <CardHeader className="pb-2">
              <CardTitle className="text-sm flex items-center gap-2">
                <ShieldCheck className="h-4 w-4 text-primary" /> Risk events
                <Badge variant="outline" className="h-5 border-border bg-surface-2 text-[10px] font-mono">{riskEvents.length}</Badge>
              </CardTitle>
            </CardHeader>
            <CardContent className="px-3 pt-0 pb-2">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead className="pl-3">Type</TableHead>
                    <TableHead>Severity</TableHead>
                    <TableHead>Action</TableHead>
                    <TableHead>Message</TableHead>
                    <TableHead className="pr-3 text-right">At</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {riskEvents.map(r => (
                    <TableRow key={r.id}>
                      <TableCell className="pl-3 font-mono text-[12px]">{r.eventType}</TableCell>
                      <TableCell>
                        <span className={`chip ${
                          r.severity === 'critical' ? '!text-destructive !border-destructive/50' :
                          r.severity === 'warn' ? '!text-warning !border-warning/50' :
                          '!text-muted-foreground'
                        }`}>{r.severity}</span>
                      </TableCell>
                      <TableCell className="text-[12px] text-muted-foreground">{r.actionTaken ?? '—'}</TableCell>
                      <TableCell className="text-[12px]">{r.message}</TableCell>
                      <TableCell className="pr-3 text-right text-[11px] text-muted-foreground font-mono">{new Date(r.createdAt).toLocaleString()}</TableCell>
                    </TableRow>
                  ))}
                  {riskEvents.length === 0 && (
                    <TableRow><TableCell colSpan={5} className="py-4 text-center text-xs text-muted-foreground">No risk events yet.</TableCell></TableRow>
                  )}
                </TableBody>
              </Table>
            </CardContent>
          </Card>

          {/* Recent orders */}
          <Card>
            <CardHeader className="pb-2"><CardTitle className="text-sm">Recent orders</CardTitle></CardHeader>
            <CardContent className="px-3 pt-0 pb-2">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead className="pl-3">Side</TableHead>
                    <TableHead>Status</TableHead>
                    <TableHead className="text-right">Price</TableHead>
                    <TableHead className="text-right">Qty</TableHead>
                    <TableHead className="pr-3 text-right">At</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {orders.slice(0, 20).map(o => (
                    <TableRow key={o.id}>
                      <TableCell className="pl-3"><span className={o.side === 'Buy' ? 'text-up' : 'text-down'}>{o.side}</span></TableCell>
                      <TableCell className="text-[12px] text-muted-foreground">{o.status}</TableCell>
                      <TableCell className="text-right num">{o.price}</TableCell>
                      <TableCell className="text-right num">{o.quantity}</TableCell>
                      <TableCell className="pr-3 text-right text-[11px] text-muted-foreground">{new Date(o.createdAt).toLocaleTimeString()}</TableCell>
                    </TableRow>
                  ))}
                  {orders.length === 0 && (
                    <TableRow><TableCell colSpan={5} className="py-4 text-center text-xs text-muted-foreground">No orders.</TableCell></TableRow>
                  )}
                </TableBody>
              </Table>
            </CardContent>
          </Card>

          {/* Recent signals */}
          <Card>
            <CardHeader className="pb-2"><CardTitle className="text-sm">Recent signals</CardTitle></CardHeader>
            <CardContent className="px-3 pt-0 pb-2">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead className="pl-3">Type</TableHead>
                    <TableHead>Side</TableHead>
                    <TableHead className="text-right">Price</TableHead>
                    <TableHead className="text-right">Score</TableHead>
                    <TableHead className="pr-3 text-right">At</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {signals.slice(0, 20).map(s => (
                    <TableRow key={s.id}>
                      <TableCell className="pl-3 text-[12px]">{s.type}</TableCell>
                      <TableCell><span className={s.side === 'Buy' ? 'text-up' : s.side === 'Sell' ? 'text-down' : 'text-muted-foreground'}>{s.side ?? '—'}</span></TableCell>
                      <TableCell className="text-right num">{s.price}</TableCell>
                      <TableCell className="text-right num">{s.score}</TableCell>
                      <TableCell className="pr-3 text-right text-[11px] text-muted-foreground">{new Date(s.generatedAt).toLocaleTimeString()}</TableCell>
                    </TableRow>
                  ))}
                  {signals.length === 0 && (
                    <TableRow><TableCell colSpan={5} className="py-4 text-center text-xs text-muted-foreground">No signals.</TableCell></TableRow>
                  )}
                </TableBody>
              </Table>
            </CardContent>
          </Card>
        </div>
      </div>
    </main>
  )
}

function Metric({ label, value, tone }: { label: string; value: string; tone?: 'up' | 'down' | 'warn' }) {
  const toneCls = tone === 'up' ? 'text-up' : tone === 'down' ? 'text-down' : tone === 'warn' ? 'text-warning' : 'text-foreground'
  return (
    <div className="rounded-sm border border-border/40 bg-surface px-3 py-2">
      <p className="text-[10px] uppercase tracking-wider text-muted-foreground">{label}</p>
      <p className={`mt-0.5 num text-base font-semibold ${toneCls}`}>{value}</p>
    </div>
  )
}

function parseTpLevels(json?: string | null): TpLevelState[] {
  if (!json) return []
  try {
    const arr = JSON.parse(json)
    if (!Array.isArray(arr)) return []
    return arr as TpLevelState[]
  } catch {
    return []
  }
}

function TpLadder({ levels, remainPct }: { levels: TpLevelState[]; remainPct: number }) {
  return (
    <div className="flex items-center gap-1.5">
      <div className="flex h-1.5 flex-1 overflow-hidden rounded-full bg-surface-2">
        {levels.map((l, i) => {
          const width = l.closePercent
          const bg = l.hitAt ? 'bg-success/70' : 'bg-primary/30'
          return (
            <div
              key={i}
              style={{ width: `${width}%` }}
              className={`${bg} border-r border-background/30 last:border-r-0`}
              title={`TP${i + 1}: +${l.profitPercent}% → close ${l.closePercent}% @ ${l.closePrice.toFixed(4)}${l.hitAt ? ' (hit ' + new Date(l.hitAt).toLocaleTimeString() + ')' : ''}`}
            />
          )
        })}
      </div>
      <span className="num text-[10px] text-muted-foreground">remain {remainPct}%</span>
    </div>
  )
}

function LiveMetric({ label, value, tone }: { label: string; value: string; tone?: 'up' | 'down' | 'warn' }) {
  const t = tone === 'up' ? 'text-up' : tone === 'down' ? 'text-down' : tone === 'warn' ? 'text-warning' : 'text-foreground'
  return (
    <div className="rounded-sm border border-border/40 bg-surface p-1.5">
      <div className="text-[9px] uppercase tracking-wider text-muted-foreground">{label}</div>
      <div className={`num text-[12px] font-medium ${t}`}>{value}</div>
    </div>
  )
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

function kindBg(kind: string): string {
  if (kind === 'risk') return 'bg-destructive/10'
  if (kind === 'order') return 'bg-primary/5'
  if (kind === 'auto_close') return 'bg-warning/10'
  if (kind === 'signal') return 'bg-info/5'
  return 'bg-surface'
}
