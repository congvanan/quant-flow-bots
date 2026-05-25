import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { Layers, RefreshCw, SlidersHorizontal } from 'lucide-react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { api, type OrderBookWallsResponse } from '@/lib/api'
import { qk, type OrderBookWallParams } from '@/lib/queries'
import { useSignalR } from '@/lib/signalr-context'

/**
 * Order-book wall scanner widget. Lists symbols whose single price level holds an
 * unusually large limit order (>= MinNotional). Worker re-scans top-N symbols every
 * ~60s; SignalR push freshens the query immediately as new walls are detected.
 *
 * Default threshold = $200M USDT but you can lower it inline to find smaller walls
 * (e.g. $10M is typical for BTC near the touch).
 */
export function OrderBookWalls() {
  const qc = useQueryClient()
  const { connection } = useSignalR()
  const [filter, setFilter] = useState<OrderBookWallParams>({
    minNotional: '200000000',
    maxDistancePct: '2',
    side: '',
    limit: '15',
  })
  const [open, setOpen] = useState(false)

  const queryString = useMemo(() => {
    const p = new URLSearchParams()
    if (filter.minNotional) p.set('minNotional', filter.minNotional)
    if (filter.maxDistancePct) p.set('maxDistancePct', filter.maxDistancePct)
    if (filter.side) p.set('side', filter.side)
    if (filter.limit) p.set('limit', filter.limit)
    return p.toString()
  }, [filter])

  const { data, isFetching, refetch } = useQuery({
    queryKey: qk.orderBookWalls(filter),
    queryFn: () => api<OrderBookWallsResponse>(`/api/market/order-book-walls?${queryString}`),
    refetchInterval: 30_000,
  })

  // Realtime: any new wall push freshens the active filter's query.
  useEffect(() => {
    if (!connection) return
    const onWall = () => qc.invalidateQueries({ queryKey: ['market', 'order-book-walls'] })
    connection.on('orderBookWall', onWall)
    return () => { connection.off('orderBookWall', onWall) }
  }, [connection, qc])

  const results = data?.results ?? []

  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between gap-2 pb-2">
        <div>
          <CardTitle className="flex items-center gap-2 text-sm">
            <Layers className="h-4 w-4 text-primary" /> Order-book Walls
          </CardTitle>
          <p className="text-[11px] text-muted-foreground">
            {data ? `${data.count}/${data.totalCached} cached · ≥ $${fmtBigUsdt(Number(filter.minNotional))} · |Δ| ≤ ${filter.maxDistancePct}%` : 'loading…'}
          </p>
        </div>
        <div className="flex items-center gap-1">
          <Button size="sm" variant="outline" className="h-6 px-2" onClick={() => setOpen(v => !v)} title="Filters">
            <SlidersHorizontal className="h-3.5 w-3.5" />
          </Button>
          <Button size="sm" variant="outline" className="h-6 px-2" onClick={() => void refetch()} title="Refresh">
            <RefreshCw className={`h-3.5 w-3.5 ${isFetching ? 'animate-spin' : ''}`} />
          </Button>
        </div>
      </CardHeader>

      {open && (
        <CardContent className="space-y-2 border-b border-border/40 px-3 pb-3 pt-0 text-xs">
          <FilterRow label="Min notional (USDT)">
            <input
              type="number"
              min={0}
              step={1000000}
              className="h-7 w-32 rounded-sm border border-border bg-surface px-2 font-mono text-xs outline-none focus:border-primary"
              value={filter.minNotional}
              onChange={e => setFilter(f => ({ ...f, minNotional: e.target.value }))}
            />
            <span className="font-mono text-[10px] text-muted-foreground">${fmtBigUsdt(Number(filter.minNotional) || 0)}</span>
          </FilterRow>
          <FilterRow label="Max distance from mid (%)">
            <input
              type="number"
              min={0}
              max={20}
              step={0.1}
              className="h-7 w-20 rounded-sm border border-border bg-surface px-2 font-mono text-xs outline-none focus:border-primary"
              value={filter.maxDistancePct}
              onChange={e => setFilter(f => ({ ...f, maxDistancePct: e.target.value }))}
            />
          </FilterRow>
          <FilterRow label="Side">
            <select
              className="h-7 rounded-sm border border-border bg-surface px-2 text-xs outline-none focus:border-primary"
              value={filter.side}
              onChange={e => setFilter(f => ({ ...f, side: e.target.value as OrderBookWallParams['side'] }))}
            >
              <option value="">Both</option>
              <option value="Bid">Bid (support)</option>
              <option value="Ask">Ask (resistance)</option>
            </select>
          </FilterRow>
          <FilterRow label="Limit">
            <input
              type="number"
              min={1}
              max={100}
              step={1}
              className="h-7 w-16 rounded-sm border border-border bg-surface px-2 font-mono text-xs outline-none focus:border-primary"
              value={filter.limit}
              onChange={e => setFilter(f => ({ ...f, limit: e.target.value }))}
            />
          </FilterRow>
          {data && (
            <p className="pt-1 text-[10px] text-muted-foreground">
              Worker scans top {data.defaults.maxSymbols} symbols every {data.defaults.scanIntervalSeconds}s.
              Default detection ≥ ${fmtBigUsdt(data.defaults.minNotional)}.
            </p>
          )}
        </CardContent>
      )}

      <CardContent className="space-y-1 px-3 pb-3 pt-2">
        {results.length === 0 && (
          <p className="text-[11px] text-muted-foreground">
            No walls match. Try lowering Min notional (e.g. <button className="text-primary hover:underline" onClick={() => setFilter(f => ({ ...f, minNotional: '10000000' }))}>$10M</button>) or widening |Δ|.
          </p>
        )}
        {results.map(w => (
          <Link
            key={`${w.symbol}-${w.side}-${w.price}`}
            to={`/symbol/${w.symbol}`}
            className="flex items-center justify-between gap-2 rounded-sm border border-border/40 bg-surface px-2 py-1.5 text-[11px] hover:border-primary/40"
            title={`mid=${w.midPrice.toFixed(4)} · ${w.multiplier.toFixed(1)}× avg level`}
          >
            <div className="flex min-w-0 items-center gap-2">
              <span className={`h-1.5 w-1.5 shrink-0 rounded-full ${w.side === 'Bid' ? 'bg-up' : 'bg-down'}`} />
              <span className="font-mono font-medium">{w.symbol}</span>
              <span className={`font-mono text-[10px] uppercase ${w.side === 'Bid' ? 'text-up' : 'text-down'}`}>{w.side === 'Bid' ? 'BUY' : 'SELL'}</span>
              <span className="num text-muted-foreground">@{fmtPrice(w.price)}</span>
            </div>
            <div className="flex items-center gap-2">
              <span className="num font-medium">${fmtBigUsdt(w.quoteNotional)}</span>
              <span className="w-12 text-right num text-[10px] text-muted-foreground">{w.distanceFromMidPercent.toFixed(2)}%</span>
            </div>
          </Link>
        ))}
      </CardContent>
    </Card>
  )
}

function FilterRow({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="flex items-center justify-between gap-2">
      <span className="text-[10px] uppercase tracking-wider text-muted-foreground">{label}</span>
      <div className="flex items-center gap-2">{children}</div>
    </label>
  )
}

function fmtBigUsdt(n: number): string {
  if (!n || !isFinite(n)) return '0'
  if (n >= 1e9) return `${(n / 1e9).toFixed(2)}B`
  if (n >= 1e6) return `${(n / 1e6).toFixed(2)}M`
  if (n >= 1e3) return `${(n / 1e3).toFixed(2)}K`
  return n.toFixed(0)
}

function fmtPrice(n: number): string {
  if (n >= 1000) return n.toLocaleString('en-US', { maximumFractionDigits: 2 })
  if (n >= 1) return n.toFixed(4)
  return n.toPrecision(4)
}
