import { Link } from 'react-router-dom'
import { useEffect } from 'react'
import { TrendingDown, TrendingUp, Zap } from 'lucide-react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { SparkLine } from '@/components/spark-line'
import { api, type VolumeSpikeDto } from '@/lib/api'
import { qk } from '@/lib/queries'
import { useSignalR, type VolumeSpikeEvent } from '@/lib/signalr-context'

export function VolumeSpikes() {
  const qc = useQueryClient()
  const { subscribeVolumeSpike } = useSignalR()
  const key = qk.volumeSpikes(10)
  const { data: list = [], isLoading: loading } = useQuery({
    queryKey: key,
    queryFn: () => api<VolumeSpikeDto[]>('/api/market/volume-spikes?limit=10'),
    staleTime: 30_000,
  })

  // Live SignalR spike → prepend into cache (no refetch needed).
  useEffect(() => {
    return subscribeVolumeSpike((evt: VolumeSpikeEvent) => {
      qc.setQueryData<VolumeSpikeDto[]>(key, prev =>
        [evt as VolumeSpikeDto, ...(prev ?? []).filter(p => p.symbol !== evt.symbol)].slice(0, 10)
      )
    })
  }, [subscribeVolumeSpike, qc, key])

  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between">
        <CardTitle className="flex items-center gap-2">
          <Zap className="h-5 w-5 text-amber-500" /> Whale Volume Spikes
        </CardTitle>
        <Badge variant="secondary">≥$200K · 3× avg</Badge>
      </CardHeader>
      <CardContent>
        {loading && <p className="text-sm text-muted-foreground">Loading...</p>}
        {!loading && list.length === 0 && (
          <p className="text-sm text-muted-foreground">Đang theo dõi ~400 USDT pairs. Spike sẽ xuất hiện khi candle 1m đóng với volume bất thường.</p>
        )}
        <ul className="space-y-2">
          {list.map((s, idx) => (
            <li key={`${s.symbol}-${s.at}-${idx}`}>
              <Link to={`/symbol/${s.symbol}`} className="block rounded-sm border border-border/40 bg-surface px-3 py-2 hover:border-primary/40 hover:bg-surface-2">
                <div className="flex items-center justify-between gap-3">
                  <div className="flex items-center gap-2 min-w-0">
                    {s.direction === 'Buy'
                      ? <TrendingUp className="h-4 w-4 shrink-0 text-up" />
                      : <TrendingDown className="h-4 w-4 shrink-0 text-down" />}
                    <div className="min-w-0">
                      <div className="font-medium text-sm truncate">{s.symbol}</div>
                      <div className="text-xs text-muted-foreground">
                        <Badge variant={s.direction === 'Buy' ? 'default' : 'outline'} className="mr-1">{s.direction}</Badge>
                        {s.multiplier.toFixed(1)}× · taker {(s.takerBuyRatio * 100).toFixed(0)}%
                      </div>
                    </div>
                  </div>
                  <SparkLine
                    values={s.sparkline}
                    color={s.direction === 'Buy' ? '#16a34a' : '#dc2626'}
                  />
                  <div className="text-right shrink-0">
                    <div className="font-mono text-sm">${fmtPrice(s.price)}</div>
                    <div className={`text-xs ${s.priceChange5mPercent >= 0 ? 'text-up' : 'text-down'}`}>
                      {s.priceChange5mPercent >= 0 ? '+' : ''}{s.priceChange5mPercent.toFixed(2)}% 5m
                    </div>
                  </div>
                </div>
                <div className="mt-1 text-xs text-muted-foreground">
                  Volume: {fmtBigNum(s.quoteVolume)} USDT (avg {fmtBigNum(s.averageQuoteVolume)}) · {fmtAge(s.at)}
                </div>
              </Link>
            </li>
          ))}
        </ul>
      </CardContent>
    </Card>
  )
}

function fmtPrice(n: number): string {
  if (n >= 1000) return n.toLocaleString('en-US', { maximumFractionDigits: 2 })
  if (n >= 1) return n.toFixed(4)
  if (n >= 0.01) return n.toFixed(5)
  return n.toPrecision(4)
}

function fmtBigNum(n: number): string {
  if (n >= 1e9) return `${(n / 1e9).toFixed(2)}B`
  if (n >= 1e6) return `${(n / 1e6).toFixed(2)}M`
  if (n >= 1e3) return `${(n / 1e3).toFixed(2)}K`
  return n.toFixed(0)
}

function fmtAge(iso: string): string {
  const sec = Math.floor((Date.now() - new Date(iso).getTime()) / 1000)
  if (sec < 60) return `${sec}s ago`
  if (sec < 3600) return `${Math.floor(sec / 60)}m ago`
  return `${Math.floor(sec / 3600)}h ago`
}
