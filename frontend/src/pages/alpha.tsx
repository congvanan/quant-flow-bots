import { useEffect, useMemo, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { ArrowDown, ArrowUp, ArrowUpDown, RefreshCw, Search, TrendingDown, TrendingUp } from 'lucide-react'
import { Card, CardContent } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { AuthGuard } from '@/components/auth-guard'
import { NavBar } from '@/components/nav-bar'
import { api } from '@/lib/api'

type AlphaToken = {
  symbol: string
  futuresSymbol: string
  name: string
  iconUrl?: string | null
  chain?: string | null
  price: number
  percentChange24h: number
  marketCap: number
  volume24h: number
  fdv?: number | null
  liquidity?: number | null
  holders?: number | null
  sparkline: number[]
  at: string
}

type AlphaResponse = { updatedAt: string; count: number; items: AlphaToken[] }
type AlphaPriceTick = {
  symbol: string // = FuturesSymbol e.g. LABUSDT
  price: number
  percentChange24h: number
  high24h: number
  low24h: number
  quoteVolume24h: number
  fundingRate: number // tỷ lệ funding chu kỳ (Binance mặc định 8h). +0.01% nghĩa long trả short.
  nextFundingTime?: string | null
  at: string
}
type AlphaPricesResponse = { updatedAt: string; count: number; items: AlphaPriceTick[] }

export default function AlphaPage() {
  return (
    <AuthGuard>
      <Inner />
    </AuthGuard>
  )
}

type SortKey = 'rank' | 'price' | 'pct' | 'marketCap' | 'volume24h' | 'liquidity' | 'holders' | 'funding'
type DirFilter = 'all' | 'gainers' | 'losers'

function Inner() {
  const [filter, setFilter] = useState('')
  const [dirFilter, setDirFilter] = useState<DirFilter>('all')
  const [chainFilter, setChainFilter] = useState<string>('all')
  const [sortKey, setSortKey] = useState<SortKey>('rank')
  const [sortDir, setSortDir] = useState<'asc' | 'desc'>('desc')
  const { data, isLoading, isFetching, refetch } = useQuery({
    queryKey: ['market', 'alpha'],
    queryFn: () => api<AlphaResponse>('/api/market/alpha'),
    // Heavy data (sparkline, marketCap, iconUrl): backend cache 10 phút, FE poll 60s ăn cache.
    staleTime: 60_000,
    refetchInterval: 60_000,
    refetchOnWindowFocus: false,
  })

  // Realtime price + 24h%: lightweight endpoint, poll 2s. Worker đẩy WS @ticker vào cache,
  // endpoint chỉ return snapshot ~10KB → cheap to poll fast.
  const { data: pricesData } = useQuery({
    queryKey: ['market', 'alpha', 'prices'],
    queryFn: () => api<AlphaPricesResponse>('/api/market/alpha/prices'),
    refetchInterval: 2_000,
    refetchOnWindowFocus: false,
  })

  // Map FuturesSymbol → live tick để lookup O(1) trong row render.
  const liveMap = useMemo(() => {
    const m: Record<string, AlphaPriceTick> = {}
    for (const t of pricesData?.items ?? []) m[t.symbol] = t
    return m
  }, [pricesData])

  const items = data?.items ?? []

  // Danh sách chain unique cho dropdown — extract trực tiếp từ data hiện có.
  const chains = useMemo(() => {
    const set = new Set<string>()
    for (const t of items) if (t.chain) set.add(t.chain)
    return Array.from(set).sort()
  }, [items])

  // Pipeline: textSearch → chainFilter → directionFilter (theo live pct nếu có) → sort.
  // Direction lấy live pct vì ngưỡng +/- thay đổi realtime, dùng snapshot pct sẽ lệch.
  const filtered = useMemo(() => {
    const q = filter.trim().toLowerCase()
    let out = items
    if (q) out = out.filter(t => t.symbol.toLowerCase().includes(q) || t.name.toLowerCase().includes(q))
    if (chainFilter !== 'all') out = out.filter(t => t.chain === chainFilter)
    if (dirFilter !== 'all') {
      out = out.filter(t => {
        const livePct = liveMap[t.futuresSymbol]?.percentChange24h
        const pct = livePct ?? t.percentChange24h
        return dirFilter === 'gainers' ? pct > 0 : pct < 0
      })
    }
    if (sortKey !== 'rank') {
      const factor = sortDir === 'desc' ? -1 : 1
      out = [...out].sort((a, b) => {
        const av = readSortValue(a, sortKey, liveMap)
        const bv = readSortValue(b, sortKey, liveMap)
        return (av - bv) * factor
      })
    }
    return out
  }, [items, filter, chainFilter, dirFilter, sortKey, sortDir, liveMap])

  function toggleSort(key: SortKey) {
    // 2-state đơn giản: cùng cột → đổi chiều. Khác cột → switch column + reset desc.
    // KHÔNG còn cycle 3-state (desc → asc → reset) vì gây nhầm "click 3 lần thì sort biến mất".
    // Muốn về default rank: click "All" tab/refresh hoặc tạo nút riêng "Reset sort" nếu cần.
    if (sortKey === key) {
      setSortDir(sortDir === 'desc' ? 'asc' : 'desc')
    } else {
      setSortKey(key)
      setSortDir('desc')
    }
  }

  return (
    <div className="min-h-screen bg-background">
      <NavBar />
      <main className="container py-5">
        <div className="mb-4 flex items-end justify-between gap-4">
          <div>
            <h1 className="text-lg font-semibold tracking-tight">Alpha × Futures</h1>
            <p className="text-[12px] text-muted-foreground">
              Binance Alpha tokens cũng đã list Futures USDT-M ({data?.count ?? 0} tokens) ·
              {' '}cache 10 phút · last sync {fmtTime(data?.updatedAt)}
            </p>
          </div>
          <div className="flex items-center gap-2">
            {/* Direction filter: All / Gainers / Losers — toggle button group. */}
            <div className="flex h-8 items-center rounded-sm border border-border bg-surface">
              <button
                type="button"
                onClick={() => setDirFilter('all')}
                className={`h-full px-2.5 text-[11px] font-medium ${dirFilter === 'all' ? 'bg-muted-foreground/10 text-foreground' : 'text-muted-foreground hover:text-foreground'}`}
              >All</button>
              <button
                type="button"
                onClick={() => setDirFilter('gainers')}
                className={`flex h-full items-center gap-1 border-l border-border px-2.5 text-[11px] font-medium ${dirFilter === 'gainers' ? 'text-up' : 'text-muted-foreground hover:text-foreground'}`}
                title="Chỉ tokens tăng giá (24h % > 0)"
              ><TrendingUp className="h-3 w-3" /> Gainers</button>
              <button
                type="button"
                onClick={() => setDirFilter('losers')}
                className={`flex h-full items-center gap-1 border-l border-border px-2.5 text-[11px] font-medium ${dirFilter === 'losers' ? 'text-down' : 'text-muted-foreground hover:text-foreground'}`}
                title="Chỉ tokens giảm giá (24h % < 0)"
              ><TrendingDown className="h-3 w-3" /> Losers</button>
            </div>
            <select
              value={chainFilter}
              onChange={e => setChainFilter(e.target.value)}
              className="h-8 rounded-sm border border-border bg-surface px-2 text-[12px]"
              title="Lọc theo mạng blockchain"
            >
              <option value="all">All chains</option>
              {chains.map(c => <option key={c} value={c}>{c}</option>)}
            </select>
            <div className="relative">
              <Search className="absolute left-2 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-muted-foreground" />
              <input
                type="text"
                placeholder="filter symbol / name"
                value={filter}
                onChange={e => setFilter(e.target.value)}
                className="h-8 w-48 rounded-sm border border-border bg-surface pl-7 pr-2 font-mono text-[12px]"
              />
            </div>
            <Button size="sm" variant="outline" className="h-8 gap-1.5" onClick={() => refetch()} disabled={isFetching}>
              <RefreshCw className={`h-3.5 w-3.5 ${isFetching ? 'animate-spin' : ''}`} /> Refresh
            </Button>
          </div>
        </div>

        <Card>
          <CardContent className="p-0">
            <div className="flex items-center justify-between border-b border-border/40 px-3 py-1.5 text-[10px] text-muted-foreground">
              <span>{pricesData?.count ?? 0} live tickers via Futures WS · update mỗi 2s</span>
              <span className="font-mono">tick {fmtTime(pricesData?.updatedAt)}</span>
            </div>
            {isLoading ? (
              <div className="p-6 text-[13px] text-muted-foreground">Loading…</div>
            ) : filtered.length === 0 ? (
              <div className="p-6 text-[13px] text-muted-foreground">
                {filter ? `Không có token nào khớp "${filter}".` : 'Không có Alpha token nào đang list Futures.'}
              </div>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full text-[12px]">
                  <thead className="border-b border-border/60 text-[10px] uppercase tracking-wider text-muted-foreground">
                    <tr>
                      <th className="px-3 py-2 text-left">#</th>
                      <th className="px-3 py-2 text-left">Token</th>
                      <SortHeader label="Price" col="price" sortKey={sortKey} sortDir={sortDir} onClick={toggleSort} />
                      <SortHeader label="24h %" col="pct" sortKey={sortKey} sortDir={sortDir} onClick={toggleSort} />
                      <SortHeader label="Market Cap" col="marketCap" sortKey={sortKey} sortDir={sortDir} onClick={toggleSort} />
                      <SortHeader label="Volume 24h" col="volume24h" sortKey={sortKey} sortDir={sortDir} onClick={toggleSort} />
                      <SortHeader label="Liquidity" col="liquidity" sortKey={sortKey} sortDir={sortDir} onClick={toggleSort} />
                      <SortHeader label="Holders" col="holders" sortKey={sortKey} sortDir={sortDir} onClick={toggleSort} />
                      <SortHeader label="Funding" col="funding" sortKey={sortKey} sortDir={sortDir} onClick={toggleSort} />
                      <th className="px-3 py-2 text-right">Last 24h</th>
                    </tr>
                  </thead>
                  <tbody>
                    {filtered.map((t, idx) => (
                      // Key dùng futuresSymbol — unique theo Binance Futures, an toàn khi BE
                      // có nhiều Alpha tokens cùng base symbol (vd 2 token cùng tên "AIA").
                      <AlphaRow key={t.futuresSymbol} rank={idx + 1} t={t} live={liveMap[t.futuresSymbol]} />
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </CardContent>
        </Card>
      </main>
    </div>
  )
}

function AlphaRow({ rank, t, live }: { rank: number; t: AlphaToken; live?: AlphaPriceTick }) {
  // Ưu tiên live data nếu có (cache đã có tick), fallback về snapshot cũ.
  // QUAN TRỌNG: volume cũng dùng live giống sort source — nếu hiển thị snapshot mà sort
  // dùng live thì giá trị trên row không khớp thứ tự sort → tưởng sort bị broken.
  const price = live?.price ?? t.price
  const pct = live?.percentChange24h ?? t.percentChange24h
  const volume24h = live?.quoteVolume24h ?? t.volume24h
  const positive = pct >= 0

  // Flash highlight khi price đổi: nháy xanh nếu tăng, đỏ nếu giảm. Dùng ref để so prev
  // và class state để CSS animation kick in ~400ms rồi tự reset.
  const prevPrice = useRef(price)
  const [flash, setFlash] = useState<'up' | 'down' | null>(null)
  useEffect(() => {
    if (live && prevPrice.current !== price && prevPrice.current > 0) {
      setFlash(price > prevPrice.current ? 'up' : 'down')
      const t = setTimeout(() => setFlash(null), 400)
      return () => clearTimeout(t)
    }
    prevPrice.current = price
  }, [price, live])

  const flashCls = flash === 'up' ? 'bg-up/15' : flash === 'down' ? 'bg-down/15' : ''

  return (
    <tr className="border-b border-border/30 hover:bg-surface/40">
      <td className="px-3 py-2.5 text-muted-foreground">{rank}</td>
      <td className="px-3 py-2.5">
        <Link to={`/symbol/${t.futuresSymbol}?market=futures`} className="flex items-center gap-2 hover:underline">
          {t.iconUrl ? (
            <img src={t.iconUrl} alt="" className="h-5 w-5 rounded-full" loading="lazy" />
          ) : (
            <div className="h-5 w-5 rounded-full bg-muted-foreground/20" />
          )}
          <div className="flex flex-col leading-tight">
            <span className="font-medium">{t.symbol}</span>
            <span className="text-[10px] text-muted-foreground">{t.name}{t.chain ? ` · ${t.chain}` : ''}</span>
          </div>
        </Link>
      </td>
      <td className={`px-3 py-2.5 text-right font-mono transition-colors duration-300 ${flashCls}`}>
        ${fmtPrice(price)}
      </td>
      <td className={`px-3 py-2.5 text-right font-mono ${positive ? 'text-up' : 'text-down'}`}>
        {positive ? '+' : ''}{pct.toFixed(2)}%
      </td>
      <td className="px-3 py-2.5 text-right font-mono text-muted-foreground">{fmtUsd(t.marketCap)}</td>
      <td className="px-3 py-2.5 text-right font-mono text-muted-foreground">{fmtUsd(volume24h)}</td>
      <td className="px-3 py-2.5 text-right font-mono text-muted-foreground">{t.liquidity ? fmtUsd(t.liquidity) : '—'}</td>
      <td className="px-3 py-2.5 text-right font-mono text-muted-foreground">{t.holders ? fmtCount(t.holders) : '—'}</td>
      <FundingCell live={live} />
      <td className="px-3 py-2.5 text-right">
        <Sparkline data={t.sparkline} positive={positive} />
      </td>
    </tr>
  )
}

function SortHeader({
  label, col, sortKey, sortDir, onClick,
}: {
  label: string
  col: SortKey
  sortKey: SortKey
  sortDir: 'asc' | 'desc'
  onClick: (k: SortKey) => void
}) {
  const active = sortKey === col
  const Icon = !active ? ArrowUpDown : sortDir === 'desc' ? ArrowDown : ArrowUp
  return (
    <th className="px-3 py-2 text-right">
      <button
        type="button"
        onClick={() => onClick(col)}
        className={`inline-flex items-center gap-1 rounded px-1.5 py-0.5 transition-colors hover:bg-muted-foreground/10 hover:text-foreground ${
          active ? 'bg-primary/15 text-primary' : ''
        }`}
        title={active ? `Đang sort ${sortDir === 'desc' ? 'giảm dần' : 'tăng dần'} — click để đổi chiều` : `Sort theo ${label}`}
      >
        {label}
        <Icon className={active ? 'h-3.5 w-3.5' : 'h-3 w-3 opacity-60'} />
      </button>
    </th>
  )
}

// Lookup giá trị sort cho 1 column. Live data ưu tiên cho price/pct/volume24h vì các field
// đó nhảy realtime; marketCap/liquidity dùng snapshot vì không có trong stream tick.
function readSortValue(
  t: AlphaToken,
  key: SortKey,
  liveMap: Record<string, AlphaPriceTick>,
): number {
  const live = liveMap[t.futuresSymbol]
  switch (key) {
    case 'price': return live?.price ?? t.price ?? 0
    case 'pct': return live?.percentChange24h ?? t.percentChange24h ?? 0
    case 'marketCap': return t.marketCap ?? 0
    case 'volume24h': return live?.quoteVolume24h ?? t.volume24h ?? 0
    case 'liquidity': return t.liquidity ?? 0
    case 'holders': return t.holders ?? 0
    case 'funding': return live?.fundingRate ?? 0
    default: return 0
  }
}

// Funding cell: rate hiển thị %, kèm countdown đến nextFundingTime nếu có.
// Convention: rate > 0 (long trả short) → màu đỏ với long, ta show xanh/đỏ theo dấu
// thuần — viewer phải tự hiểu side. Format 4 chữ số sau %.
function FundingCell({ live }: { live?: AlphaPriceTick }) {
  // Tick countdown mỗi giây để UI luôn cập nhật, không depend vào poll 2s của data.
  const [, force] = useState(0)
  useEffect(() => {
    const id = setInterval(() => force(x => x + 1), 1_000)
    return () => clearInterval(id)
  }, [])

  if (!live || !isFinite(live.fundingRate)) {
    return <td className="px-3 py-2.5 text-right font-mono text-muted-foreground">—</td>
  }
  const rate = live.fundingRate
  const positive = rate >= 0
  const pct = (rate * 100).toFixed(4)
  const next = live.nextFundingTime ? new Date(live.nextFundingTime).getTime() : null
  const countdown = next ? fmtCountdown(next - Date.now()) : null
  return (
    <td className="px-3 py-2.5 text-right font-mono">
      <div className={`leading-tight ${positive ? 'text-up' : 'text-down'}`}>
        {positive ? '+' : ''}{pct}%
      </div>
      {countdown && (
        <div className="text-[10px] text-muted-foreground" title="Đến funding kỳ kế">
          {countdown}
        </div>
      )}
    </td>
  )
}

function fmtCountdown(ms: number): string {
  if (!isFinite(ms) || ms <= 0) return '—'
  const total = Math.floor(ms / 1000)
  const h = Math.floor(total / 3600)
  const m = Math.floor((total % 3600) / 60)
  const s = total % 60
  if (h > 0) return `${h}h ${m}m`
  if (m > 0) return `${m}m ${s.toString().padStart(2, '0')}s`
  return `${s}s`
}

function Sparkline({ data, positive }: { data: number[]; positive: boolean }) {
  if (!data || data.length < 2) return <span className="text-[10px] text-muted-foreground">—</span>
  const w = 100, h = 28
  const min = Math.min(...data), max = Math.max(...data)
  const range = max - min || 1
  const stepX = w / (data.length - 1)
  const path = data
    .map((v, i) => `${i === 0 ? 'M' : 'L'} ${(i * stepX).toFixed(1)} ${(h - ((v - min) / range) * h).toFixed(1)}`)
    .join(' ')
  const fillPath = `${path} L ${w.toFixed(1)} ${h} L 0 ${h} Z`
  const color = positive ? '#22a34a' : '#dc3838'
  return (
    <svg width={w} height={h} className="inline-block">
      <path d={fillPath} fill={color} opacity={0.15} />
      <path d={path} stroke={color} strokeWidth={1.2} fill="none" />
    </svg>
  )
}

function fmtPrice(p: number): string {
  if (!isFinite(p) || p <= 0) return '—'
  if (p >= 1000) return p.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })
  if (p >= 1) return p.toFixed(4)
  if (p >= 0.01) return p.toFixed(5)
  return p.toExponential(3)
}

// Số đếm (holders): không có dấu $, format K/M giống fmtUsd nhưng integer-friendly.
function fmtCount(n: number): string {
  if (!isFinite(n) || n <= 0) return '—'
  if (n >= 1_000_000) return `${(n / 1_000_000).toFixed(2)}M`
  if (n >= 1_000) return `${(n / 1_000).toFixed(1)}K`
  return n.toFixed(0)
}

function fmtUsd(n: number): string {
  if (!isFinite(n) || n <= 0) return '—'
  if (n >= 1_000_000_000) return `$${(n / 1_000_000_000).toFixed(2)}B`
  if (n >= 1_000_000) return `$${(n / 1_000_000).toFixed(2)}M`
  if (n >= 1_000) return `$${(n / 1_000).toFixed(2)}K`
  return `$${n.toFixed(0)}`
}

function fmtTime(iso?: string): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit', second: '2-digit' })
}
