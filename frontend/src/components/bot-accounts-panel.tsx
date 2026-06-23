import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Trash2, Plus, ShieldOff, Power } from 'lucide-react'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { api, ApiError, type ApiKeyDto, type BotAccountDto, type MarketKind } from '@/lib/api'
import { qk } from '@/lib/queries'

// Mã exchange tương thích với ExecutionMarket của bot (cùng quy ước BE).
function keyMatchesMarket(exchangeCode: string, market: MarketKind): boolean {
  if (exchangeCode === 'binance-futures-testnet') return market === 'Futures'
  if (exchangeCode === 'binance-spot-testnet') return market === 'Spot'
  return true // exchange seed/dev → không gắt
}

export function BotAccountsPanel({ botId, executionMarket }: { botId: string; executionMarket: MarketKind }) {
  const qc = useQueryClient()
  const [err, setErr] = useState<string | null>(null)
  const [showAdd, setShowAdd] = useState(false)

  const { data: accounts = [] } = useQuery({
    queryKey: qk.botAccounts(botId),
    queryFn: () => api<BotAccountDto[]>(`/api/bots/${botId}/accounts`),
    enabled: !!botId,
  })
  const { data: allKeys = [] } = useQuery({
    queryKey: qk.apiKeys,
    queryFn: () => api<ApiKeyDto[]>('/api/settings/api-keys'),
  })

  const invalidate = () => {
    qc.invalidateQueries({ queryKey: qk.botAccounts(botId) })
    qc.invalidateQueries({ queryKey: qk.botPositions(botId) })
  }
  const onErr = (e: unknown) => setErr(e instanceof ApiError ? e.message : String(e))

  const addMut = useMutation({
    mutationFn: (body: unknown) => api<BotAccountDto>(`/api/bots/${botId}/accounts`, { method: 'POST', body: JSON.stringify(body) }),
    onSuccess: () => { setErr(null); setShowAdd(false); invalidate() },
    onError: onErr,
  })
  const patchMut = useMutation({
    mutationFn: ({ id, body }: { id: string; body: unknown }) =>
      api<BotAccountDto>(`/api/bots/${botId}/accounts/${id}`, { method: 'PATCH', body: JSON.stringify(body) }),
    onSuccess: () => { setErr(null); invalidate() },
    onError: onErr,
  })
  const delMut = useMutation({
    mutationFn: (id: string) => api(`/api/bots/${botId}/accounts/${id}`, { method: 'DELETE' }),
    onSuccess: () => { setErr(null); invalidate() },
    onError: onErr,
  })
  const resetKillMut = useMutation({
    mutationFn: (id: string) => api(`/api/bots/${botId}/accounts/${id}/kill-switch/reset`, { method: 'POST' }),
    onSuccess: () => { setErr(null); invalidate() },
    onError: onErr,
  })

  // Chỉ cho chọn key đúng market + chưa được gắn.
  const usedKeyIds = new Set(accounts.map(a => a.apiKeyId))
  const availableKeys = allKeys.filter(k => keyMatchesMarket(k.exchangeCode, executionMarket) && !usedKeyIds.has(k.id))
  const totalWeight = accounts.filter(a => a.isActive).reduce((s, a) => s + a.weight, 0)

  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between">
        <div>
          <CardTitle>Multi-account ({accounts.length})</CardTitle>
          <p className="mt-1 text-xs text-muted-foreground">
            Mỗi account vào lệnh độc lập theo vốn riêng × weight. Weight bằng nhau = chia đều theo vốn;
            weight lệch = phân bổ theo tỷ lệ. Trống = bot chạy single-account qua API key gắn trực tiếp.
          </p>
        </div>
        <Button size="sm" variant="outline" onClick={() => { setShowAdd(s => !s); setErr(null) }}>
          <Plus className="mr-1 h-4 w-4" /> Thêm account
        </Button>
      </CardHeader>
      <CardContent className="space-y-3">
        {err && <div className="rounded border border-red-500/40 bg-red-500/10 px-3 py-2 text-sm text-red-400">{err}</div>}

        {showAdd && (
          <AddForm
            availableKeys={availableKeys}
            executionMarket={executionMarket}
            onSubmit={body => addMut.mutate(body)}
            busy={addMut.isPending}
          />
        )}

        {accounts.length === 0 ? (
          <p className="text-sm text-muted-foreground">Chưa có account nào — bot dùng API key đơn gắn trong cấu hình.</p>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Account</TableHead>
                <TableHead className="text-right">Weight (% vốn)</TableHead>
                <TableHead className="text-right">Vốn (USDT)</TableHead>
                <TableHead className="text-right">Lệnh mở</TableHead>
                <TableHead className="text-right">PnL / Hôm nay</TableHead>
                <TableHead className="text-right">Win%</TableHead>
                <TableHead>Trạng thái</TableHead>
                <TableHead></TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {accounts.map(a => {
                const allocPct = totalWeight > 0 && a.isActive ? (a.weight / totalWeight) * 100 : 0
                return (
                  <TableRow key={a.id}>
                    <TableCell>
                      <div className="font-medium">{a.label}</div>
                      <div className="text-xs text-muted-foreground">{a.keyLabel} · {a.exchangeCode}</div>
                    </TableCell>
                    <TableCell className="text-right">
                      <input
                        type="number" step="0.1" min="0.1" defaultValue={a.weight}
                        className="w-16 rounded border border-border bg-background px-1 py-0.5 text-right text-sm"
                        onBlur={e => {
                          const w = parseFloat(e.target.value)
                          if (w > 0 && w !== a.weight) patchMut.mutate({ id: a.id, body: { weight: w } })
                        }}
                      />
                      <div className="text-xs text-muted-foreground">{allocPct.toFixed(0)}%</div>
                    </TableCell>
                    <TableCell className="text-right">
                      <input
                        type="number" step="50" min="0" defaultValue={a.baseEquityUsdt}
                        className="w-24 rounded border border-border bg-background px-1 py-0.5 text-right text-sm"
                        onBlur={e => {
                          const v = parseFloat(e.target.value)
                          if (v >= 0 && v !== a.baseEquityUsdt) patchMut.mutate({ id: a.id, body: { baseEquityUsdt: v } })
                        }}
                      />
                    </TableCell>
                    <TableCell className="text-right">{a.openPositions}</TableCell>
                    <TableCell className="text-right">
                      <span className={a.realizedPnl >= 0 ? 'text-emerald-400' : 'text-red-400'}>{a.realizedPnl.toFixed(2)}</span>
                      <div className={`text-xs ${a.pnlToday >= 0 ? 'text-emerald-400' : 'text-red-400'}`}>{a.pnlToday.toFixed(2)}</div>
                    </TableCell>
                    <TableCell className="text-right">{a.winRatePercent.toFixed(0)}% <span className="text-xs text-muted-foreground">({a.totalTrades})</span></TableCell>
                    <TableCell>
                      {a.killSwitchTrippedAt ? (
                        <Badge variant="destructive" title={a.killSwitchReason ?? ''}>KILL</Badge>
                      ) : a.isActive ? (
                        <Badge variant="outline" className="text-emerald-400">active</Badge>
                      ) : (
                        <Badge variant="secondary">tắt</Badge>
                      )}
                    </TableCell>
                    <TableCell>
                      <div className="flex items-center justify-end gap-1">
                        {a.killSwitchTrippedAt && (
                          <Button size="icon" variant="ghost" title="Reset kill-switch" onClick={() => resetKillMut.mutate(a.id)}>
                            <ShieldOff className="h-4 w-4 text-amber-400" />
                          </Button>
                        )}
                        <Button size="icon" variant="ghost" title={a.isActive ? 'Tắt account' : 'Bật account'}
                          onClick={() => patchMut.mutate({ id: a.id, body: { isActive: !a.isActive } })}>
                          <Power className={`h-4 w-4 ${a.isActive ? 'text-emerald-400' : 'text-muted-foreground'}`} />
                        </Button>
                        <Button size="icon" variant="ghost" title="Xóa" onClick={() => delMut.mutate(a.id)}>
                          <Trash2 className="h-4 w-4 text-red-400" />
                        </Button>
                      </div>
                    </TableCell>
                  </TableRow>
                )
              })}
            </TableBody>
          </Table>
        )}
      </CardContent>
    </Card>
  )
}

function AddForm({
  availableKeys, executionMarket, onSubmit, busy,
}: {
  availableKeys: ApiKeyDto[]
  executionMarket: MarketKind
  onSubmit: (body: unknown) => void
  busy: boolean
}) {
  const [apiKeyId, setApiKeyId] = useState('')
  const [label, setLabel] = useState('')
  const [weight, setWeight] = useState('1')
  const [baseEquity, setBaseEquity] = useState('1000')

  return (
    <div className="space-y-2 rounded border border-border bg-muted/30 p-3">
      {availableKeys.length === 0 ? (
        <p className="text-sm text-amber-400">
          Không có API key {executionMarket} nào khả dụng (chưa thêm hoặc đã gắn hết). Thêm key ở trang Settings.
        </p>
      ) : (
        <>
          <div className="grid grid-cols-2 gap-2 md:grid-cols-4">
            <label className="col-span-2 text-sm">
              API key
              <select value={apiKeyId} onChange={e => setApiKeyId(e.target.value)}
                className="mt-1 w-full rounded border border-border bg-background px-2 py-1 text-sm">
                <option value="">— chọn key —</option>
                {availableKeys.map(k => <option key={k.id} value={k.id}>{k.label} ({k.mode} · {k.exchangeCode})</option>)}
              </select>
            </label>
            <label className="text-sm">
              Nhãn
              <input value={label} onChange={e => setLabel(e.target.value)} placeholder="(theo key)"
                className="mt-1 w-full rounded border border-border bg-background px-2 py-1 text-sm" />
            </label>
            <label className="text-sm">
              Weight
              <input type="number" step="0.1" min="0.1" value={weight} onChange={e => setWeight(e.target.value)}
                className="mt-1 w-full rounded border border-border bg-background px-2 py-1 text-sm" />
            </label>
            <label className="text-sm">
              Vốn (USDT)
              <input type="number" step="50" min="0" value={baseEquity} onChange={e => setBaseEquity(e.target.value)}
                className="mt-1 w-full rounded border border-border bg-background px-2 py-1 text-sm" />
            </label>
          </div>
          <Button size="sm" disabled={!apiKeyId || busy}
            onClick={() => onSubmit({
              apiKeyId,
              label: label.trim() || undefined,
              weight: parseFloat(weight) || 1,
              baseEquityUsdt: parseFloat(baseEquity) || undefined,
            })}>
            {busy ? 'Đang thêm…' : 'Thêm vào bot'}
          </Button>
        </>
      )}
    </div>
  )
}
