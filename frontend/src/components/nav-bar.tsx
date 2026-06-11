import { Link, useLocation } from 'react-router-dom'
import { ChevronDown, LogOut, Wifi, WifiOff, Zap } from 'lucide-react'
import { HubConnectionState } from '@microsoft/signalr'
import { useQuery } from '@tanstack/react-query'
import { Button } from '@/components/ui/button'
import { useAuth } from '@/lib/auth-context'
import { useSignalR } from '@/lib/signalr-context'
import { api } from '@/lib/api'

type TradingStatus = {
  mode: string
  liveTradingEnabled: boolean
  liveTradingScope?: string
  message: string
}

// Item con `children` → render dropdown thay vì link đơn. Markets là cha của Spot/Alpha
// (cùng phạm vi "thông tin thị trường" — chỉ khác universe).
type NavItem = { href: string; label: string; children?: { href: string; label: string; desc?: string }[] }
const links: NavItem[] = [
  {
    href: '/',
    label: 'Markets',
    children: [
      { href: '/', label: 'Spot', desc: 'Top USDT pairs · realtime ticker, walls, VWAP' },
      { href: '/alpha', label: 'Alpha', desc: 'Binance Alpha tokens đã list Futures' },
    ],
  },
  { href: '/strategies', label: 'Strategies' },
  { href: '/bots', label: 'Bots' },
  { href: '/backtest', label: 'Backtest' },
  { href: '/settings', label: 'Settings' },
]

export function NavBar() {
  const { pathname } = useLocation()
  const { displayName, email, logout } = useAuth()
  const { state } = useSignalR()
  const connected = state === HubConnectionState.Connected
  const { data: status } = useQuery({
    queryKey: ['bots', 'status'],
    queryFn: () => api<TradingStatus>('/api/bots/status'),
    staleTime: 5 * 60_000,
    refetchOnWindowFocus: false,
  })
  const subtitle = status
    ? status.liveTradingEnabled
      ? `paper + live · ${status.liveTradingScope ?? 'testnet'}`
      : 'paper · live disabled'
    : 'paper'

  return (
    <header className="sticky top-0 z-40 border-b border-border bg-surface/80 backdrop-blur supports-[backdrop-filter]:bg-surface/60">
      <div className="container flex h-14 items-center justify-between gap-4">
        <div className="flex items-center gap-7">
          <Link to="/" className="flex items-center gap-2">
            <div className="flex h-7 w-7 items-center justify-center rounded-md bg-primary text-primary-foreground">
              <Zap className="h-4 w-4" strokeWidth={2.5} />
            </div>
            <div className="flex flex-col leading-none">
              <span className="text-[13px] font-bold tracking-tight">QUANT FLOW</span>
              <span
                className={`text-[9px] uppercase tracking-[0.18em] ${status?.liveTradingEnabled ? 'text-primary' : 'text-muted-foreground'}`}
                title={status?.message}
              >
                {subtitle}
              </span>
            </div>
          </Link>
          <nav className="flex items-center gap-0.5">
            {links.map(l => {
              // Cha có children → bật khi pathname khớp BẤT KỲ child route nào (Spot `/` + Alpha `/alpha`).
              const childActive = l.children?.some(c => c.href === '/' ? pathname === '/' : pathname.startsWith(c.href))
              const active = childActive || pathname === l.href || (l.href !== '/' && !l.children && pathname.startsWith(l.href))
              if (l.children) {
                return (
                  <div key={l.href} className="group relative">
                    <button
                      type="button"
                      className={`relative flex items-center gap-1 rounded-sm px-3 py-1.5 text-[13px] font-medium transition-colors ${
                        active ? 'text-primary' : 'text-muted-foreground hover:text-foreground'
                      }`}
                    >
                      {l.label}
                      <ChevronDown className="h-3 w-3 opacity-70 transition-transform group-hover:rotate-180" />
                      {active && <span className="absolute -bottom-[15px] left-2 right-2 h-[2px] bg-primary" />}
                    </button>
                    {/* Hover dropdown — pt-2 tạo "cầu nối" để chuột không mất focus khi di từ button xuống menu */}
                    <div className="invisible absolute left-0 top-full z-50 pt-2 opacity-0 transition-opacity group-hover:visible group-hover:opacity-100">
                      <div className="min-w-[240px] rounded-md border border-border bg-surface shadow-lg">
                        {l.children.map(c => {
                          const isActive = c.href === '/' ? pathname === '/' : pathname.startsWith(c.href)
                          return (
                            <Link
                              key={c.href}
                              to={c.href}
                              className={`block border-b border-border/30 px-3 py-2 text-[12px] last:border-b-0 hover:bg-background ${
                                isActive ? 'text-primary' : 'text-foreground'
                              }`}
                            >
                              <div className="font-medium">{c.label}</div>
                              {c.desc && <div className="text-[10.5px] text-muted-foreground">{c.desc}</div>}
                            </Link>
                          )
                        })}
                      </div>
                    </div>
                  </div>
                )
              }
              return (
                <Link
                  key={l.href}
                  to={l.href}
                  className={`relative rounded-sm px-3 py-1.5 text-[13px] font-medium transition-colors ${
                    active
                      ? 'text-primary'
                      : 'text-muted-foreground hover:text-foreground'
                  }`}
                >
                  {l.label}
                  {active && <span className="absolute -bottom-[15px] left-2 right-2 h-[2px] bg-primary" />}
                </Link>
              )
            })}
          </nav>
        </div>
        <div className="flex items-center gap-3">
          <span className={`chip ${connected ? '!text-up !border-success/40' : '!text-warning'}`}>
            {connected ? <Wifi className="h-3 w-3" /> : <WifiOff className="h-3 w-3" />}
            <span className="font-mono text-[10px]">{connected ? 'LIVE' : state}</span>
          </span>
          <span className="hidden text-[12px] text-muted-foreground sm:inline">{displayName ?? email}</span>
          <Button size="sm" variant="ghost" className="h-7 gap-1.5 text-muted-foreground hover:text-foreground" onClick={logout}>
            <LogOut className="h-3.5 w-3.5" /> <span className="text-[12px]">Logout</span>
          </Button>
        </div>
      </div>
    </header>
  )
}
