import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Brain, Send } from 'lucide-react'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { api, ApiError, type SentimentEventDto, type SentimentSnapshotDto } from '@/lib/api'
import { qk } from '@/lib/queries'

/**
 * Compact sentiment panel: top bullish / bearish symbols (rolling EWMA) +
 * recent headlines + a manual ingest form (keyword scorer runs server-side).
 * Real-time: SignalRQueryBridge invalidates `['sentiment', …]` on push.
 */
export function SentimentWidget() {
  const qc = useQueryClient()
  const { data: bull = [] } = useQuery({
    queryKey: qk.sentimentTop(5, 'bull'),
    queryFn: () => api<SentimentSnapshotDto[]>('/api/sentiment/top?n=5&direction=bull'),
    refetchInterval: 30_000,
  })
  const { data: bear = [] } = useQuery({
    queryKey: qk.sentimentTop(5, 'bear'),
    queryFn: () => api<SentimentSnapshotDto[]>('/api/sentiment/top?n=5&direction=bear'),
    refetchInterval: 30_000,
  })
  const { data: recent = [] } = useQuery({
    queryKey: qk.sentimentRecent,
    queryFn: () => api<SentimentEventDto[]>('/api/sentiment/recent?limit=15'),
    refetchInterval: 30_000,
  })

  const [symbol, setSymbol] = useState('BTCUSDT')
  const [headline, setHeadline] = useState('')
  const [err, setErr] = useState<string | null>(null)

  const ingest = useMutation({
    mutationFn: (body: unknown) => api('/api/sentiment/manual', { method: 'POST', body: JSON.stringify(body) }),
    onSuccess: () => { setHeadline(''); setErr(null); qc.invalidateQueries({ queryKey: ['sentiment'] }) },
    onError: (e) => setErr(e instanceof ApiError ? e.message : (e as Error).message),
  })

  function submit(e: React.FormEvent) {
    e.preventDefault()
    if (!headline.trim() || !symbol.trim()) return
    ingest.mutate({ symbolCode: symbol.toUpperCase(), headline })
  }

  return (
    <Card>
      <CardHeader className="pb-2">
        <CardTitle className="flex items-center gap-2 text-sm">
          <Brain className="h-4 w-4 text-primary" /> Sentiment
        </CardTitle>
        <p className="text-[11px] text-muted-foreground">
          Rolling EWMA score per symbol from manual + scraped headlines. Strategy <code className="font-mono">sentiment_momentum</code> reads the same aggregator.
        </p>
      </CardHeader>
      <CardContent className="space-y-3 px-3 pb-3">
        <div className="grid grid-cols-2 gap-3 text-xs">
          <Column title="Bullish" items={bull} accent="text-up" />
          <Column title="Bearish" items={bear} accent="text-down" />
        </div>

        <form onSubmit={submit} className="flex gap-1.5">
          <input
            className="h-7 w-24 rounded-sm border border-border bg-surface px-2 font-mono text-xs uppercase outline-none focus:border-primary"
            value={symbol}
            onChange={e => setSymbol(e.target.value)}
            placeholder="SYMBOL"
            maxLength={20}
          />
          <input
            className="h-7 flex-1 rounded-sm border border-border bg-surface px-2 text-xs outline-none focus:border-primary"
            value={headline}
            onChange={e => setHeadline(e.target.value)}
            placeholder="Headline (e.g. 'BTC ETF approved')"
          />
          <Button type="submit" size="sm" className="h-7 px-2" disabled={ingest.isPending}>
            <Send className="h-3.5 w-3.5" />
          </Button>
        </form>
        {err && <p className="text-[11px] text-destructive">{err}</p>}

        <div className="space-y-1 border-t border-border/40 pt-2">
          {recent.slice(0, 8).map(s => (
            <div key={s.id} className="flex items-center gap-2 text-[11px]">
              <span className="w-16 font-mono uppercase text-muted-foreground">{s.symbolCode}</span>
              <span className={scoreClass(s.score)}>{s.score.toFixed(2)}</span>
              <span className="truncate text-foreground" title={s.headline}>{s.headline}</span>
              <span className="ml-auto whitespace-nowrap text-[10px] text-muted-foreground">{relTime(s.at)}</span>
            </div>
          ))}
          {recent.length === 0 && <p className="text-[11px] text-muted-foreground">No sentiment events yet — try the form above.</p>}
        </div>
      </CardContent>
    </Card>
  )
}

function Column({ title, items, accent }: { title: string; items: SentimentSnapshotDto[]; accent: string }) {
  return (
    <div>
      <div className="mb-1 text-[10px] uppercase tracking-wider text-muted-foreground">{title}</div>
      <div className="space-y-0.5">
        {items.length === 0 && <div className="text-[11px] text-muted-foreground">—</div>}
        {items.map(s => (
          <div key={s.symbolCode} className="flex items-center justify-between gap-2 rounded-sm bg-surface/40 px-1.5 py-0.5">
            <span className="font-mono text-[11px]">{s.symbolCode}</span>
            <Badge variant="outline" className={`h-4 border-0 px-1.5 text-[10px] font-mono ${accent}`}>{s.rollingScore.toFixed(2)}</Badge>
          </div>
        ))}
      </div>
    </div>
  )
}

function scoreClass(s: number): string {
  if (s > 0.1) return 'w-9 text-right font-mono text-up'
  if (s < -0.1) return 'w-9 text-right font-mono text-down'
  return 'w-9 text-right font-mono text-muted-foreground'
}

function relTime(iso: string): string {
  const dt = new Date(iso)
  const diff = (Date.now() - dt.getTime()) / 1000
  if (diff < 60) return `${Math.round(diff)}s`
  if (diff < 3600) return `${Math.round(diff / 60)}m`
  if (diff < 86400) return `${Math.round(diff / 3600)}h`
  return `${Math.round(diff / 86400)}d`
}
