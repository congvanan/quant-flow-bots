import { Link } from 'react-router-dom'
import { useState } from 'react'
import { Filter, RefreshCw, Radar } from 'lucide-react'
import { useQuery } from '@tanstack/react-query'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { api, type ScannerResponse } from '@/lib/api'
import { qk } from '@/lib/queries'

const WINDOW_OPTIONS = ['15m', '30m', '1h', '2h', '4h', '6h', '12h', '1d', '3d', '7d']

export function SymbolScanner() {
  const [minVolume, setMinVolume] = useState('50000000')
  const [minPct, setMinPct] = useState('5')
  const [maxPct, setMaxPct] = useState('25')
  const [windowSize, setWindowSize] = useState('15m')
  const [direction, setDirection] = useState<'any' | 'up' | 'down'>('any')
  const [maxSymbols, setMaxSymbols] = useState('15')
  const [exclude, setExclude] = useState('USDCUSDT,FDUSDUSDT,TUSDUSDT')
  const [open, setOpen] = useState(false)

  const params = { minVolume, minPct, maxPct, windowSize, direction, maxSymbols, exclude }
  const { data, isFetching: loading, error, refetch } = useQuery({
    queryKey: qk.scanner(params),
    queryFn: () => {
      const q = new URLSearchParams(params)
      return api<ScannerResponse>(`/api/market/scanner?${q.toString()}`)
    },
    staleTime: 15_000,
  })
  const scan = () => { void refetch() }

  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between gap-2 pb-2">
        <div>
          <CardTitle className="flex items-center gap-2 text-sm">
            <Radar className="h-4 w-4 text-primary" /> Symbol Scanner
          </CardTitle>
          <p className="text-[11px] text-muted-foreground">
            {data ? `${data.count} symbols match` : 'Scanning...'}
            {data && ` | ${data.filter.windowSize} | vol >= ${fmtBigNum(data.filter.minVolume)} | ${dirLabel(direction)} ${data.filter.minPct}-${data.filter.maxPct}%`}
          </p>
        </div>
        <div className="flex items-center gap-1">
          <Button size="sm" variant="outline" className="h-7 w-7 p-0" onClick={() => setOpen(v => !v)} title="Filter">
            <Filter className="h-3.5 w-3.5" />
          </Button>
          <Button size="sm" variant="outline" className="h-7 w-7 p-0" onClick={() => void scan()} disabled={loading} title="Refresh">
            <RefreshCw className={`h-3.5 w-3.5 ${loading ? 'animate-spin' : ''}`} />
          </Button>
        </div>
      </CardHeader>

      {open && (
        <CardContent className="px-3 pb-2 pt-0">
          <div className="space-y-2 rounded-sm border border-border/60 bg-surface/40 p-2.5">
            <div className="grid grid-cols-2 gap-2">
              <FilterField label="Window">
                <FilterSelect value={windowSize} onChange={setWindowSize} />
              </FilterField>
              <FilterField label="Direction">
                <DirectionSelect value={direction} onChange={setDirection} />
              </FilterField>
              <FilterField label="Min volume (USDT)">
                <FilterInput value={minVolume} onChange={setMinVolume} />
              </FilterField>
              <FilterField label="Max symbols">
                <FilterInput value={maxSymbols} onChange={setMaxSymbols} />
              </FilterField>
              <FilterField label={direction === 'down' ? 'Drop min %' : direction === 'up' ? 'Pump min %' : '|Delta| min %'}>
                <FilterInput value={minPct} onChange={setMinPct} />
              </FilterField>
              <FilterField label={direction === 'down' ? 'Drop max %' : direction === 'up' ? 'Pump max %' : '|Delta| max %'}>
                <FilterInput value={maxPct} onChange={setMaxPct} />
              </FilterField>
            </div>
            <FilterField label="Exclude (csv)">
              <FilterInput value={exclude} onChange={setExclude} />
            </FilterField>
            <Button size="sm" className="h-7 w-full bg-primary text-primary-foreground hover:bg-primary/90" onClick={() => void scan()}>
              Apply filter
            </Button>
          </div>
        </CardContent>
      )}

      <CardContent className="px-3 pb-3 pt-0">
        {error && <p className="text-xs text-destructive">{(error as Error).message}</p>}
        {!error && data && data.results.length === 0 && (
          <EmptyState
            data={data}
            onLoosenPct={() => {
              if (data.nearMissPct && data.nearMissPct.maxAbsPctSeen > 0) {
                const next = Math.max(0.5, Math.floor(data.nearMissPct.maxAbsPctSeen * 10) / 10 - 0.1)
                setMinPct(next.toString())
              }
            }}
            onLoosenVolume={() => setMinVolume('10000000')}
          />
        )}
        {!error && data && data.results.length > 0 && (
          <div className="space-y-1 text-xs">
            {data.results.map(r => (
              <Link
                key={r.symbol}
                to={`/symbol/${r.symbol}`}
                className="flex items-center justify-between gap-2 rounded-sm border border-border/40 bg-surface px-2.5 py-1.5 hover:border-primary/40 hover:bg-surface-2"
              >
                <span className="font-medium font-mono">{r.symbol}</span>
                <div className="flex items-center gap-3 num">
                  <span className={r.priceChangePercent >= 0 ? 'text-up' : 'text-down'}>
                    {r.priceChangePercent >= 0 ? '+' : ''}{r.priceChangePercent.toFixed(2)}%
                  </span>
                  <span className="text-muted-foreground">{fmtBigNum(r.quoteVolume)}</span>
                </div>
              </Link>
            ))}
          </div>
        )}
      </CardContent>
    </Card>
  )
}

function EmptyState({ data, onLoosenPct, onLoosenVolume }: { data: ScannerResponse; onLoosenPct: () => void; onLoosenVolume: () => void }) {
  const s = data.stages
  const blockedByPct = s && s.afterVolume > 0 && s.afterPctRange === 0
  const blockedByVolume = s && s.afterVolume === 0
  const nearMaxPct = data.nearMissPct?.maxAbsPctSeen ?? 0
  return (
    <div className="space-y-2 rounded-sm border border-border/40 bg-surface px-2.5 py-2 text-xs">
      <p className="text-muted-foreground">No symbols match - market is quiet right now.</p>
      {s && (
        <div className="space-y-0.5 font-mono text-[10px] text-muted-foreground">
          <div>USDT pairs: <span className="text-foreground">{s.totalUsdtPairs}</span></div>
          <div>pass volume: <span className={s.afterVolume > 0 ? 'text-foreground' : 'text-destructive'}>{s.afterVolume}</span></div>
          <div>pass |Delta| range: <span className={s.afterPctRange > 0 ? 'text-foreground' : 'text-destructive'}>{s.afterPctRange}</span></div>
          <div>after blacklist: <span className="text-foreground">{s.afterBlacklist}</span></div>
        </div>
      )}
      {blockedByPct && nearMaxPct > 0 && (
        <div className="space-y-1">
          <p className="text-[11px]">Closest mover with enough volume: <span className="num text-warning">|{nearMaxPct.toFixed(2)}|%</span></p>
          <Button size="sm" variant="outline" className="h-7 w-full text-[11px]" onClick={onLoosenPct}>
            Lower |Delta| min to {Math.max(0.5, Math.floor(nearMaxPct * 10) / 10 - 0.1).toFixed(1)}%
          </Button>
        </div>
      )}
      {blockedByVolume && (
        <Button size="sm" variant="outline" className="h-7 w-full text-[11px]" onClick={onLoosenVolume}>
          Lower min volume to 10M USDT
        </Button>
      )}
    </div>
  )
}

function FilterField({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="block space-y-0.5">
      <span className="text-[10px] uppercase tracking-wider text-muted-foreground">{label}</span>
      {children}
    </label>
  )
}

function FilterInput({ value, onChange }: { value: string; onChange: (v: string) => void }) {
  return (
    <input
      value={value}
      onChange={e => onChange(e.target.value)}
      className="h-7 w-full rounded-sm border border-border bg-surface px-2 text-xs outline-none focus:border-primary"
    />
  )
}

function FilterSelect({ value, onChange }: { value: string; onChange: (v: string) => void }) {
  return (
    <select
      value={value}
      onChange={e => onChange(e.target.value)}
      className="h-7 w-full rounded-sm border border-border bg-surface px-2 text-xs outline-none focus:border-primary"
    >
      {WINDOW_OPTIONS.map(w => <option key={w} value={w}>{w}</option>)}
    </select>
  )
}

function DirectionSelect({ value, onChange }: { value: 'any' | 'up' | 'down'; onChange: (v: 'any' | 'up' | 'down') => void }) {
  return (
    <select
      value={value}
      onChange={e => onChange(e.target.value as 'any' | 'up' | 'down')}
      className="h-7 w-full rounded-sm border border-border bg-surface px-2 text-xs outline-none focus:border-primary"
    >
      <option value="any">Both</option>
      <option value="up">Up only</option>
      <option value="down">Down only</option>
    </select>
  )
}

function dirLabel(d: 'any' | 'up' | 'down'): string {
  return d === 'up' ? 'Pump' : d === 'down' ? 'Drop' : '|Delta|'
}

function fmtBigNum(n: number | undefined): string {
  if (n == null) return '-'
  if (n >= 1e9) return `${(n / 1e9).toFixed(2)}B`
  if (n >= 1e6) return `${(n / 1e6).toFixed(2)}M`
  if (n >= 1e3) return `${(n / 1e3).toFixed(2)}K`
  return n.toFixed(0)
}
