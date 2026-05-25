import { Link } from 'react-router-dom'
import { Sparkles } from 'lucide-react'
import { useQuery } from '@tanstack/react-query'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { api, type NewListingDto } from '@/lib/api'
import { qk } from '@/lib/queries'

export function NewListings() {
  const { data: list = [], isLoading, error } = useQuery({
    queryKey: qk.newListings(5),
    queryFn: () => api<NewListingDto[]>('/api/market/new-listings?limit=5'),
    refetchInterval: 30_000,
    staleTime: 25_000,
  })

  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between">
        <CardTitle className="flex items-center gap-2">
          <Sparkles className="h-5 w-5 text-amber-500" /> New Listings
        </CardTitle>
        <Badge variant="secondary">USDT pairs</Badge>
      </CardHeader>
      <CardContent>
        {isLoading && <p className="text-sm text-muted-foreground">Loading...</p>}
        {error && <p className="text-sm text-destructive">{(error as Error).message}</p>}
        {!isLoading && !error && list.length === 0 && (
          <p className="text-sm text-muted-foreground">No recent listings yet. Backfill is running, check back in a few minutes.</p>
        )}
        <ul className="divide-y">
          {list.map(item => (
            <li key={item.code}>
              <Link to={`/symbol/${item.code}`}
                className="flex items-center justify-between gap-3 py-2.5 hover:bg-surface-2 rounded-sm px-2 -mx-2">
                <div className="flex items-center gap-3 min-w-0">
                  <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-primary/10 text-xs font-bold text-primary">
                    {item.baseAsset.slice(0, 3)}
                  </div>
                  <div className="min-w-0">
                    <div className="font-medium truncate">{item.baseAsset}</div>
                    <div className="text-xs text-muted-foreground">{item.code} · {fmtAge(item.listedAt)}</div>
                  </div>
                </div>
                <div className="text-right shrink-0">
                  <div className="font-medium">${fmtPrice(item.price)}</div>
                  <div className={`text-xs ${item.priceChangePercent >= 0 ? 'text-up' : 'text-down'}`}>
                    {item.priceChangePercent >= 0 ? '+' : ''}{item.priceChangePercent.toFixed(2)}%
                  </div>
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

function fmtAge(iso: string): string {
  const days = Math.floor((Date.now() - new Date(iso).getTime()) / 86_400_000)
  if (days < 1) return 'today'
  if (days === 1) return '1 day ago'
  if (days < 30) return `${days} days ago`
  if (days < 365) return `${Math.floor(days / 30)}mo ago`
  return `${Math.floor(days / 365)}y ago`
}
