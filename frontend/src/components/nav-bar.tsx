import { Link, useLocation } from 'react-router-dom'
import { LogOut, Wifi, WifiOff, Zap } from 'lucide-react'
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

const links = [
  { href: '/', label: 'Markets' },
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
              const active = pathname === l.href || (l.href !== '/' && pathname.startsWith(l.href))
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
