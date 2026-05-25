import { useEffect, useMemo, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { Activity, ArrowDownRight, ArrowUpRight, CircleDollarSign, Gauge, RefreshCw, ShieldCheck, WalletCards } from 'lucide-react'
import { useQuery } from '@tanstack/react-query'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { AuthGuard } from '@/components/auth-guard'
import { NavBar } from '@/components/nav-bar'
import { NewListings } from '@/components/new-listings'
import { SymbolScanner } from '@/components/symbol-scanner'
import { VolumeSpikes } from '@/components/volume-spikes'
import { SentimentWidget } from '@/components/sentiment-widget'
import { OrderBookWalls } from '@/components/order-book-walls'
import { useSignalR, type TickerEvent } from '@/lib/signalr-context'
import { api, type MarketOverview, type MarketTicker } from '@/lib/api'
import { qk } from '@/lib/queries'

export default function DashboardPage() {
  return (
    <AuthGuard>
      <DashboardInner />
    </AuthGuard>
  )
}

function DashboardInner() {
  const { subscribeTicker } = useSignalR()
  const [livePrices, setLivePrices] = useState<Record<string, { price: number; pct: number; flash: 'up' | 'down' | null }>>({})
  const prevPrices = useRef<Record<string, number>>({})

  const { data: overview, isFetching: loading, error, refetch } = useQuery({
    queryKey: qk.marketOverview,
    queryFn: () => api<MarketOverview>('/api/market/overview'),
    staleTime: 30_000,
    refetchInterval: 60_000,
  })

  useEffect(() => {
    return subscribeTicker((evt: TickerEvent) => {
      setLivePrices(prev => {
        const last = prevPrices.current[evt.symbol]
        const flash = last == null ? null : evt.price > last ? 'up' : evt.price < last ? 'down' : null
        prevPrices.current[evt.symbol] = evt.price
        return { ...prev, [evt.symbol]: { price: evt.price, pct: evt.priceChangePercent, flash } }
      })
    })
  }, [subscribeTicker])

  const watchlist = useMemo(() => {
    if (!overview) return []
    const seen = new Set<string>()
    const merge: MarketTicker[] = []
    for (const t of [...overview.topGainers, ...overview.topVolume]) {
      if (!seen.has(t.symbol)) { seen.add(t.symbol); merge.push(t) }
      if (merge.length >= 12) break
    }
    return merge
  }, [overview])

  return (
    <main className="min-h-screen bg-background">
      <NavBar />

      <div className="container space-y-4 py-4">
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-lg font-semibold tracking-tight">Markets</h1>
            <p className="text-xs text-muted-foreground">Realtime via SignalR · {overview?.updatedAt ? new Date(overview.updatedAt).toLocaleTimeString() : 'loading…'}</p>
          </div>
          <Button size="sm" variant="outline" className="h-7 gap-1.5 border-border bg-surface text-xs" onClick={() => void refetch()} disabled={loading}>
            <RefreshCw className={`h-3.5 w-3.5 ${loading ? 'animate-spin' : ''}`} /> Refresh
          </Button>
        </div>

        <div className="grid gap-3 md:grid-cols-2 lg:grid-cols-4">
          <MetricCard
            title="Top Gainer (24h)"
            value={overview?.topGainers[0]?.symbol ?? '—'}
            detail={fmtPct(overview?.topGainers[0]?.priceChangePercent)}
            tone="up"
            icon={<Activity className="h-4 w-4" />}
          />
          <MetricCard
            title="Top Volume"
            value={overview?.topVolume[0]?.symbol ?? '—'}
            detail={fmtBigNum(overview?.topVolume[0]?.quoteVolume) + ' USDT'}
            icon={<CircleDollarSign className="h-4 w-4" />}
          />
          <MetricCard
            title="Trading Mode"
            value="PAPER + LIVE"
            detail="Live = Binance Futures TESTNET"
            tone="up"
            icon={<ShieldCheck className="h-4 w-4" />}
          />
          <MetricCard
            title="Risk Budget"
            value="1.0%"
            detail="Per trade (default)"
            icon={<Gauge className="h-4 w-4" />}
          />
        </div>

        {error && (
          <Card className="border-destructive/40">
            <CardContent className="py-3 text-xs text-destructive">Error: {(error as Error).message}</CardContent>
          </Card>
        )}

        <div className="grid gap-4 lg:grid-cols-[1fr_380px]">
          <Card>
            <CardHeader className="flex flex-row items-center justify-between gap-3 pb-2">
              <div>
                <CardTitle className="text-sm">Momentum Watchlist</CardTitle>
                <p className="text-[11px] text-muted-foreground">Top gainers ∪ top volume · live ticks</p>
              </div>
              <Badge variant="outline" className="h-5 border-border bg-surface-2 text-[10px] font-mono">{overview ? `${watchlist.length}` : '…'}</Badge>
            </CardHeader>
            <CardContent className="px-3 pt-0 pb-2">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead className="pl-3">Symbol</TableHead>
                    <TableHead className="text-right">Price</TableHead>
                    <TableHead className="text-right">24h%</TableHead>
                    <TableHead className="text-right pr-3">Volume</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {watchlist.map(row => {
                    const live = livePrices[row.symbol]
                    const price = live?.price ?? row.lastPrice
                    const pct = live?.pct ?? row.priceChangePercent
                    return (
                      <TableRow key={row.symbol} className={live?.flash === 'up' ? 'flash-up' : live?.flash === 'down' ? 'flash-down' : ''}>
                        <TableCell className="pl-3 font-medium">
                          <Link to={`/symbol/${row.symbol}`} className="hover:text-primary">{row.symbol}</Link>
                        </TableCell>
                        <TableCell className={`text-right num ${live?.flash === 'up' ? 'text-up' : live?.flash === 'down' ? 'text-down' : ''}`}>
                          {fmtPrice(price)}
                        </TableCell>
                        <TableCell className="text-right">
                          <span className={`inline-flex items-center gap-0.5 num text-[12px] ${pct >= 0 ? 'text-up' : 'text-down'}`}>
                            {pct >= 0 ? <ArrowUpRight className="h-3 w-3" /> : <ArrowDownRight className="h-3 w-3" />}
                            {pct >= 0 ? '+' : ''}{Number(pct).toFixed(2)}%
                          </span>
                        </TableCell>
                        <TableCell className="pr-3 text-right num text-muted-foreground">{fmtBigNum(row.quoteVolume)}</TableCell>
                      </TableRow>
                    )
                  })}
                </TableBody>
              </Table>
            </CardContent>
          </Card>

          <aside className="space-y-4">
            <OrderBookWalls />
            <SentimentWidget />
            <SymbolScanner />
            <VolumeSpikes />
            <NewListings />

            <Card>
              <CardHeader className="pb-2"><CardTitle className="flex items-center gap-2 text-sm"><WalletCards className="h-4 w-4 text-primary" /> Bot Guardrails</CardTitle></CardHeader>
              <CardContent className="space-y-1.5 px-3 pb-3 text-xs">
                <StatusLine label="Mode" value="Paper + Live (Futures TESTNET)" />
                <StatusLine label="Max open trades" value="3" />
                <StatusLine label="Daily loss stop" value="4%" />
                <StatusLine label="Exchange key" value="Server-side AES" />
              </CardContent>
            </Card>

            <Card>
              <CardHeader className="pb-2"><CardTitle className="text-sm">Sharp Movers <span className="text-muted-foreground font-normal">(≥ 5%)</span></CardTitle></CardHeader>
              <CardContent className="space-y-1 px-3 pb-3 text-xs">
                {overview?.sharpMovers?.slice(0, 8).map(t => (
                  <Link key={t.symbol} to={`/symbol/${t.symbol}`} className="flex items-center justify-between rounded-sm border border-border/40 bg-surface px-2.5 py-1.5 hover:border-primary/40 hover:bg-surface-2">
                    <span className="font-medium">{t.symbol}</span>
                    <span className={`num ${t.priceChangePercent >= 0 ? 'text-up' : 'text-down'}`}>
                      {t.priceChangePercent >= 0 ? '+' : ''}{Number(t.priceChangePercent).toFixed(2)}%
                    </span>
                  </Link>
                ))}
                {!overview?.sharpMovers?.length && <p className="text-muted-foreground">No sharp movers right now.</p>}
              </CardContent>
            </Card>
          </aside>
        </div>
      </div>
    </main>
  )
}

function MetricCard({ title, value, detail, icon, tone }: { title: string; value: string; detail: string; icon: React.ReactNode; tone?: 'up' | 'down' | 'warn' }) {
  const toneCls = tone === 'up' ? 'text-up' : tone === 'down' ? 'text-down' : tone === 'warn' ? 'text-warning' : 'text-foreground'
  return (
    <Card className="!gap-2 !py-3">
      <CardContent className="flex items-start justify-between px-4">
        <div className="space-y-1">
          <p className="text-[10px] uppercase tracking-wider text-muted-foreground">{title}</p>
          <p className={`num text-lg font-semibold ${toneCls}`}>{value}</p>
          <p className="text-[11px] text-muted-foreground">{detail}</p>
        </div>
        <div className="rounded-sm bg-surface-2 p-1.5 text-primary">{icon}</div>
      </CardContent>
    </Card>
  )
}

function StatusLine({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-center justify-between rounded-sm bg-surface px-2.5 py-1.5">
      <span className="text-muted-foreground">{label}</span>
      <span className="font-medium">{value}</span>
    </div>
  )
}

function fmtPrice(n: number): string {
  if (n >= 1000) return n.toLocaleString('en-US', { maximumFractionDigits: 2 })
  if (n >= 1) return n.toFixed(2)
  return n.toPrecision(4)
}

function fmtPct(n: number | undefined): string {
  if (n == null) return '—'
  return `${n >= 0 ? '+' : ''}${n.toFixed(2)}%`
}

function fmtBigNum(n: number | undefined): string {
  if (n == null) return '—'
  if (n >= 1e9) return `${(n / 1e9).toFixed(2)}B`
  if (n >= 1e6) return `${(n / 1e6).toFixed(2)}M`
  if (n >= 1e3) return `${(n / 1e3).toFixed(2)}K`
  return n.toFixed(0)
}
