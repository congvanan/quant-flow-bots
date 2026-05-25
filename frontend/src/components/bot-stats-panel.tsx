import { useQuery } from '@tanstack/react-query'
import { TrendingUp, TrendingDown, Activity, Percent, Target } from 'lucide-react'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { EquityChart } from '@/components/equity-chart'
import { api, type BotStatsDto } from '@/lib/api'
import { qk } from '@/lib/queries'

/**
 * P&L scoreboard for one bot. Refetches when the SignalR bridge invalidates
 * `bot-stats:<id>` (fired on every order / auto_close / risk event), so the
 * numbers stay live while paper trades fill.
 */
export function BotStatsPanel({ botId }: { botId: string }) {
  const { data, error } = useQuery({
    queryKey: qk.botStats(botId),
    queryFn: () => api<BotStatsDto>(`/api/bots/${botId}/stats`),
    refetchInterval: 30_000,
  })

  if (error) return <Card><CardContent className="py-3 text-xs text-destructive">Stats error: {(error as Error).message}</CardContent></Card>
  if (!data) return <Card><CardContent className="py-3 text-xs text-muted-foreground">Loading stats…</CardContent></Card>

  const totalReturn = data.totalReturnPercent
  const totalPnl = data.totalRealizedPnl + data.unrealizedPnl

  return (
    <Card>
      <CardHeader className="pb-2">
        <div className="flex items-center justify-between">
          <CardTitle className="flex items-center gap-2 text-sm">
            <Activity className="h-4 w-4 text-primary" /> Performance · Paper P&L
          </CardTitle>
          <div className="flex items-center gap-1.5">
            <span className="text-[10px] text-muted-foreground">Equity</span>
            <span className={`num text-sm font-semibold ${totalReturn >= 0 ? 'text-up' : 'text-down'}`}>
              {data.currentEquity.toFixed(2)}
            </span>
            <Badge variant="outline" className={`h-5 border-0 px-2 text-[10px] font-mono ${totalReturn >= 0 ? 'bg-success/15 text-up' : 'bg-destructive/15 text-down'}`}>
              {totalReturn >= 0 ? '+' : ''}{totalReturn.toFixed(2)}%
            </Badge>
          </div>
        </div>
        <p className="mt-0.5 text-[11px] text-muted-foreground">
          Base {data.baseEquity.toFixed(2)} USDT · {data.totalTrades} closed trades · {data.openPositions} open
          {data.firstTradeAt && ` · since ${new Date(data.firstTradeAt).toLocaleDateString()}`}
        </p>
      </CardHeader>
      <CardContent className="space-y-3 px-3 pb-3">
        {/* Hero metrics */}
        <div className="grid grid-cols-2 gap-2 md:grid-cols-4">
          <Hero label="Realized P&L" value={fmtUsd(data.totalRealizedPnl)} tone={data.totalRealizedPnl >= 0 ? 'up' : 'down'} icon={data.totalRealizedPnl >= 0 ? <TrendingUp className="h-3 w-3" /> : <TrendingDown className="h-3 w-3" />} />
          <Hero label="Unrealized" value={fmtUsd(data.unrealizedPnl)} tone={data.unrealizedPnl >= 0 ? 'up' : 'down'} sub={`${data.openPositions} open`} />
          <Hero label="Win rate" value={`${data.winRatePercent.toFixed(1)}%`} sub={`${data.winningTrades}W · ${data.losingTrades}L`} icon={<Percent className="h-3 w-3" />} />
          <Hero label="Max drawdown" value={`${data.maxDrawdownPercent.toFixed(2)}%`} tone="warn" />
        </div>

        {/* Period buckets */}
        <div className="grid grid-cols-3 gap-2 rounded-sm border border-border/40 bg-surface/40 p-2">
          <Period label="Today" value={data.pnlToday} sub={`${data.tradesToday} trades`} />
          <Period label="7d" value={data.pnl7d} />
          <Period label="30d" value={data.pnl30d} />
        </div>

        {/* Detail grid */}
        <div className="grid grid-cols-2 gap-2 md:grid-cols-3">
          <Detail label="Profit factor" value={data.profitFactor >= 999 ? '∞' : data.profitFactor.toFixed(2)} tone={data.profitFactor >= 1.5 ? 'up' : data.profitFactor < 1 ? 'down' : undefined} />
          <Detail label="Expectancy / trade" value={fmtUsd(data.expectancy)} tone={data.expectancy >= 0 ? 'up' : 'down'} />
          <Detail label="Avg win" value={fmtUsd(data.averageWin)} tone="up" />
          <Detail label="Avg loss" value={fmtUsd(data.averageLoss)} tone="down" />
          <Detail label="Largest win" value={fmtUsd(data.largestWin)} tone="up" />
          <Detail label="Largest loss" value={fmtUsd(data.largestLoss)} tone="down" />
        </div>

        {/* Equity curve */}
        {data.equityCurve.length > 1 ? (
          <div className="overflow-hidden rounded-sm border border-border/40">
            <EquityChart data={data.equityCurve} />
          </div>
        ) : (
          <p className="text-[11px] text-muted-foreground">Equity curve will appear after first closed trade.</p>
        )}

        <Verdict stats={data} />
      </CardContent>
    </Card>
  )
}

function Hero({ label, value, tone, sub, icon }: { label: string; value: string; tone?: 'up' | 'down' | 'warn'; sub?: string; icon?: React.ReactNode }) {
  const t = tone === 'up' ? 'text-up' : tone === 'down' ? 'text-down' : tone === 'warn' ? 'text-warning' : 'text-foreground'
  return (
    <div className="rounded-sm border border-border/40 bg-surface p-2">
      <div className="flex items-center gap-1 text-[10px] uppercase tracking-wider text-muted-foreground">
        {icon}<span>{label}</span>
      </div>
      <div className={`num mt-0.5 text-sm font-semibold ${t}`}>{value}</div>
      {sub && <div className="text-[10px] text-muted-foreground">{sub}</div>}
    </div>
  )
}

function Period({ label, value, sub }: { label: string; value: number; sub?: string }) {
  const tone = value > 0 ? 'text-up' : value < 0 ? 'text-down' : 'text-muted-foreground'
  return (
    <div className="text-center">
      <div className="text-[10px] uppercase tracking-wider text-muted-foreground">{label}</div>
      <div className={`num text-sm font-semibold ${tone}`}>{value >= 0 ? '+' : ''}{value.toFixed(2)}</div>
      {sub && <div className="text-[10px] text-muted-foreground">{sub}</div>}
    </div>
  )
}

function Detail({ label, value, tone }: { label: string; value: string; tone?: 'up' | 'down' }) {
  const t = tone === 'up' ? 'text-up' : tone === 'down' ? 'text-down' : 'text-foreground'
  return (
    <div className="rounded-sm bg-surface px-2 py-1 text-[11px]">
      <div className="text-[9px] uppercase tracking-wider text-muted-foreground">{label}</div>
      <div className={`num font-medium ${t}`}>{value}</div>
    </div>
  )
}

function Verdict({ stats }: { stats: BotStatsDto }) {
  if (stats.totalTrades < 5) return null
  const hints: { kind: 'good' | 'bad' | 'warn'; text: string }[] = []
  if (stats.profitFactor >= 1.5) hints.push({ kind: 'good', text: `PF ${stats.profitFactor.toFixed(2)} — chiến lược có edge.` })
  else if (stats.profitFactor < 1 && stats.totalTrades >= 10) hints.push({ kind: 'bad', text: `PF ${stats.profitFactor.toFixed(2)} < 1 — gross loss > gross win. Cân nhắc đổi params hoặc dừng bot.` })
  if (stats.winRatePercent >= 55) hints.push({ kind: 'good', text: `Winrate ${stats.winRatePercent.toFixed(0)}% cao.` })
  else if (stats.winRatePercent < 35 && stats.profitFactor < 1.2) hints.push({ kind: 'warn', text: `Winrate ${stats.winRatePercent.toFixed(0)}% thấp + PF chưa bù lại — review SL/TP ratio.` })
  if (stats.maxDrawdownPercent > 15) hints.push({ kind: 'warn', text: `MDD ${stats.maxDrawdownPercent.toFixed(1)}% — cân nhắc giảm RiskPerTradePercent hoặc bật kill switch chặt hơn.` })
  if (Math.abs(stats.averageLoss) > stats.averageWin * 2 && stats.totalTrades >= 10)
    hints.push({ kind: 'bad', text: `Avg loss gấp ${(Math.abs(stats.averageLoss) / Math.max(stats.averageWin, 0.01)).toFixed(1)}× avg win — SL đang quá rộng so với TP.` })
  if (hints.length === 0) return null
  return (
    <div className="space-y-1 rounded-sm border border-border/40 bg-surface/40 p-2">
      <div className="flex items-center gap-1 text-[10px] uppercase tracking-wider text-muted-foreground"><Target className="h-3 w-3" /> Verdict</div>
      {hints.map((h, i) => (
        <p key={i} className={`text-[11px] ${h.kind === 'good' ? 'text-up' : h.kind === 'bad' ? 'text-destructive' : 'text-warning'}`}>
          {h.kind === 'good' ? '✓ ' : h.kind === 'bad' ? '✕ ' : '⚠ '}{h.text}
        </p>
      ))}
    </div>
  )
}

function fmtUsd(n: number): string {
  if (Math.abs(n) >= 1e6) return `${(n / 1e6).toFixed(2)}M`
  if (Math.abs(n) >= 1e3) return `${(n / 1e3).toFixed(2)}K`
  return `${n >= 0 ? '+' : ''}${n.toFixed(2)}`
}
