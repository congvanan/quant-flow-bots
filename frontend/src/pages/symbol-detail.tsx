import { useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { useEffect, useState } from 'react'
import { ArrowLeft } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { AuthGuard } from '@/components/auth-guard'
import { TradingViewChart } from '@/components/tradingview-chart'
import { useSignalR, type TickerEvent } from '@/lib/signalr-context'

export default function SymbolDetailPage() {
  return <AuthGuard><SymbolDetailInner /></AuthGuard>
}

function SymbolDetailInner() {
  const params = useParams<{ code: string }>()
  const code = (params.code ?? '').toUpperCase()
  const [search] = useSearchParams()
  // `?market=futures` cho Alpha tokens — TradingView cần suffix `.P` để load đúng perpetual.
  const market: 'spot' | 'futures' = search.get('market') === 'futures' ? 'futures' : 'spot'
  const navigate = useNavigate()
  const { subscribeTicker } = useSignalR()
  const [ticker, setTicker] = useState<{ price: number; pct: number } | null>(null)

  useEffect(() => {
    return subscribeTicker((evt: TickerEvent) => {
      if (evt.symbol !== code) return
      setTicker({ price: evt.price, pct: evt.priceChangePercent })
    })
  }, [subscribeTicker, code])

  return (
    <main className="min-h-screen">
      <header className="border-b border-border bg-surface">
        <div className="container flex min-h-16 items-center justify-between gap-4">
          <div className="flex items-center gap-3">
            {/* navigate(-1) trả về đúng trang trước (Alpha hoặc Spot) thay vì luôn về "/". */}
            <Button variant="ghost" size="sm" className="gap-2" onClick={() => navigate(-1)}>
              <ArrowLeft className="h-4 w-4" /> Back
            </Button>
            <div>
              <h1 className="text-lg font-semibold leading-tight">{code}</h1>
              <p className="text-sm text-muted-foreground">
                TradingView Advanced Chart · BINANCE:{code}{market === 'futures' ? '.P' : ''}
              </p>
            </div>
          </div>
          {ticker && (
            <div className="text-right">
              <div className="text-xl font-semibold">{ticker.price}</div>
              <div className={ticker.pct >= 0 ? 'text-sm text-up' : 'text-sm text-down'}>
                {ticker.pct >= 0 ? '+' : ''}{ticker.pct.toFixed(2)}%
              </div>
            </div>
          )}
        </div>
      </header>

      <div className="container py-5">
        <div className="overflow-hidden rounded-md border border-border bg-card">
          <TradingViewChart symbol={code} market={market} interval="60" height={760} />
        </div>
      </div>
    </main>
  )
}
