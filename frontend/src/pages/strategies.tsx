import { useEffect, useState } from 'react'
import { Pencil, Trash2 } from 'lucide-react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AuthGuard } from '@/components/auth-guard'
import { NavBar } from '@/components/nav-bar'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { api, ApiError, type StrategyDto } from '@/lib/api'
import { qk } from '@/lib/queries'

type ParamDoc = {
  key: string
  type: 'number' | 'integer' | 'string' | 'enum'
  default: string | number
  desc: string
  options?: string[]   // for enum
}
type StrategyDoc = {
  summary: string
  params: ParamDoc[]
  note?: string
}

// Single source of truth: param metadata. We render the JSON template AND the help panel
// from this so the two never drift out of sync.
const STRATEGY_DOCS: Record<string, StrategyDoc> = {
  sma_cross: {
    summary: 'Hai đường SMA nhanh/chậm — BUY khi SMA fast cắt SMA slow lên.',
    params: [
      { key: 'fast', type: 'integer', default: 9, desc: 'Chu kỳ SMA nhanh (số nến).' },
      { key: 'slow', type: 'integer', default: 21, desc: 'Chu kỳ SMA chậm (số nến). Phải > fast.' },
    ],
  },
  rsi: {
    summary: 'RSI mean-reversion — BUY khi vượt oversold từ dưới lên, SELL khi cắt overbought xuống.',
    params: [
      { key: 'period', type: 'integer', default: 14, desc: 'Chu kỳ RSI.' },
      { key: 'oversold', type: 'number', default: 30, desc: 'Mức quá bán — RSI vượt lên qua đây thì BUY.' },
      { key: 'overbought', type: 'number', default: 70, desc: 'Mức quá mua — RSI cắt xuống qua đây thì SELL.' },
    ],
  },
  breakout: {
    summary: 'Breakout — BUY khi close phá đỉnh lookback (có biên đệm).',
    params: [
      { key: 'lookback', type: 'integer', default: 20, desc: 'Số nến tham chiếu tính high/low.' },
      { key: 'buffer', type: 'number', default: 0.001, desc: 'Biên đệm. 0.001 = 0.1% — close phải vượt high × (1+buffer).' },
    ],
  },
  volume_spike: {
    summary: 'Volume đột biến so với trung bình N nến trước — kết hợp lọc thanh khoản 24h.',
    params: [
      { key: 'multiplier', type: 'number', default: 5, desc: 'Volume hiện tại phải gấp N lần trung bình lookback nến trước.' },
      { key: 'lookback', type: 'integer', default: 20, desc: 'Số nến đóng gần nhất dùng làm baseline (cho phép 5–50).' },
      { key: 'minVolume24h', type: 'number', default: 500000, desc: 'Vol/24h tối thiểu (USDT) — bỏ qua coin rác.' },
      { key: 'direction', type: 'enum', default: 'buy', options: ['buy', 'sell', 'both'], desc: 'Chiều spike chấp nhận.' },
    ],
  },
  vwap_emotion_cross: {
    summary: 'VWAP = "lý trí", MA20/28 = "cảm xúc". Entry khi lý trí đi ngang + cảm xúc đảo chiều + close cắt MA.',
    params: [
      { key: 'maPeriod', type: 'enum', default: 20, options: ['20', '28'], desc: 'MA cảm xúc — chọn 20 hoặc 28.' },
      { key: 'vwapAnchor', type: 'enum', default: 'daily', options: ['daily', 'weekly', 'monthly'], desc: 'VWAP lý trí neo theo phiên. daily/weekly → interval 1h; monthly → interval 2h.' },
      { key: 'vwapFlatThresholdPct', type: 'number', default: 0.05, desc: '|Δ VWAP / price| dưới mức này (%/bar) coi là "lý trí đi ngang". Số càng nhỏ → ít signal nhưng chắc hơn.' },
      { key: 'direction', type: 'enum', default: 'both', options: ['buy', 'sell', 'both'], desc: 'Chiều entry chấp nhận.' },
    ],
    note: 'Metadata kèm tín hiệu: `vwapAboveMa` (true = lý trí > cảm xúc → setup bền) · `maDistanceFromVwapPct` lớn = đang pump quá đà.',
  },
  vwap_ma_stretch: {
    summary: 'COUNTER-TREND mean-reversion: vào lệnh ngược chiều khi GIÁ cách MA ≥ X% VÀ MA cách VWAP ≥ Y%. Stretched↑ → SELL (overheated), stretched↓ → BUY (oversold). Khác vwap_emotion_cross (đi theo breakout) — chiến lược này đi ngược stretch.',
    params: [
      { key: 'maPeriod', type: 'enum', default: 20, options: ['20', '28'], desc: 'MA cảm xúc — chọn 20 hoặc 28.' },
      { key: 'vwapAnchor', type: 'enum', default: 'daily', options: ['daily', 'weekly', 'monthly'], desc: 'VWAP neo theo phiên. daily/weekly → interval 1h; monthly → interval 2h.' },
      { key: 'priceMaDistancePct', type: 'number', default: 5, desc: 'Tối thiểu |Price − MA| / MA × 100 (%). Default 5%. Nhỏ → nhiều signal yếu, lớn → ít signal nhưng setup mạnh.' },
      { key: 'maVwapDistancePct', type: 'number', default: 5, desc: 'Tối thiểu |MA − VWAP| / VWAP × 100 (%). Default 5%. "Cảm xúc đã xa lý trí bao nhiêu".' },
      { key: 'direction', type: 'enum', default: 'both', options: ['buy', 'sell', 'both'], desc: 'buy = chỉ bắt đáy stretched↓, sell = chỉ short đỉnh stretched↑, both = cả 2. Spot/backtest chỉ hỗ trợ long — sell entry bị bỏ qua khi không có position (Futures long/short sẽ mở sau).' },
      { key: 'maxLossPct', type: 'number', default: 3, desc: 'SL cứng: lỗ tối đa % từ entry khi lệnh LỖ NGAY từ đầu (chưa từng dương). Clamp 0.2–50.' },
      { key: 'breakEvenEnabled', type: 'enum', default: 'true', options: ['true', 'false'], desc: 'Bật/tắt break-even stop. Tắt (false) → maxLossPct áp dụng xuyên suốt kể cả khi lệnh từng có lãi, lệnh có thêm không gian chạy tới TP.' },
      { key: 'breakEvenArmPct', type: 'number', default: 0.5, desc: 'Chỉ dùng khi breakEvenEnabled=true. Lệnh từng lãi >= % này → SL dời về entry, giá quay về entry → thoát hòa vốn. Để nhỏ quá (0.05) dễ bị noise đẩy ra hòa vốn liên tục.' },
      { key: 'bbEnabled', type: 'enum', default: 'true', options: ['true', 'false'], desc: 'Bật filter Bollinger Band: BUY cần giá sát/dưới band Lower, SHORT cần giá sát/trên band Upper.' },
      { key: 'bbPeriod', type: 'number', default: 20, desc: 'Chu kỳ BB (SMA close). Chuẩn TradingView = 20.' },
      { key: 'bbStdDev', type: 'number', default: 2, desc: 'Hệ số độ lệch chuẩn. Chuẩn = 2. Tăng → band rộng hơn → điều kiện khó hơn.' },
      { key: 'bbDistancePct', type: 'number', default: 1, desc: 'Vùng giá x%: BUY khi giá <= BBLower × (1 + x%); SHORT khi giá >= BBUpper × (1 − x%). Giá vượt hẳn ra ngoài band luôn thỏa.' },
    ],
    note: 'ENTRY = 3 điều kiện AND: (1) giá cách MA >= priceMaDistancePct, (2) MA cách VWAP >= maVwapDistancePct, (3) giá sát BB Lower (buy) / Upper (short) trong vùng bbDistancePct. EXIT 2 tầng: TP khi giá hồi về MA; lệnh từng dương >= breakEvenArmPct → SL = entry (hòa vốn); lệnh lỗ ngay → SL cứng maxLossPct. Stop khớp intra-candle, TP chờ nến đóng. Short chỉ chạy ở backtest/bot Futures.',
  },
  sentiment_momentum: {
    summary: 'Rolling sentiment score từ tin tức scraped + manual — vượt threshold thì BUY.',
    params: [
      { key: 'threshold', type: 'number', default: 0.5, desc: 'BUY khi điểm sentiment rolling vượt mức này.' },
      { key: 'minSampleCount', type: 'integer', default: 3, desc: 'Cần ít nhất N tin gần đây mới kích hoạt.' },
    ],
  },
  composite: {
    summary: 'Gộp nhiều strategy con — bot chỉ vào lệnh khi tất cả (hoặc N/M) cùng phát tín hiệu.',
    params: [
      { key: 'logic', type: 'enum', default: 'all', options: ['all', 'any', 'quorum'], desc: '"all" = mọi child phải đồng thuận · "any" = chỉ cần 1 · "quorum" = cần minMatch child trong số N child cùng tín hiệu.' },
      { key: 'minMatch', type: 'integer', default: 0, desc: 'Chỉ dùng khi logic="quorum". Mặc định 0 → tự tính = đa số (N/2+1). Set thủ công nếu muốn ngưỡng khác.' },
      { key: 'directionMustMatch', type: 'enum', default: 'true', options: ['true', 'false'], desc: 'true = mọi tín hiệu phải cùng chiều (buy/sell), bất đồng = bỏ qua. false = bỏ phiếu theo đa số, hoà = bỏ qua.' },
      { key: 'children', type: 'string', default: '[ ... ]', desc: 'Mảng strategy con. Mỗi item: { "kind": "<kind>", "params": { ... } }. KHÔNG được dùng "composite" làm child (no nesting).' },
    ],
    note: 'Ví dụ thực dụng: VWAP flat (lý trí) + MA cross (cảm xúc) + Volume spike (xác nhận lực mua) — chỉ vào khi cả 3 đồng thuận. Score đầu ra = trung bình score của các child contribute. WarmupBars = max của các child.',
  },
}

function defaultsJsonFor(kind: string): string {
  // Composite needs a real nested example because the doc's `default` for `children` can't
  // express the full structure in a single line.
  if (kind === 'composite') {
    return `{
  "logic": "all",
  "minMatch": 0,
  "directionMustMatch": true,
  "children": [
    {
      "kind": "vwap_emotion_cross",
      "params": {
        "maPeriod": 20,
        "vwapAnchor": "daily",
        "vwapFlatThresholdPct": 0.05,
        "direction": "both"
      }
    },
    {
      "kind": "sma_cross",
      "params": { "fast": 9, "slow": 21 }
    },
    {
      "kind": "volume_spike",
      "params": { "multiplier": 3, "lookback": 20, "minVolume24h": 500000, "direction": "buy" }
    }
  ]
}`
  }
  const doc = STRATEGY_DOCS[kind]
  if (!doc) return '{}'
  const body = doc.params
    .map(p => `  ${JSON.stringify(p.key)}: ${JSON.stringify(p.default)}`)
    .join(',\n')
  return `{\n${body}\n}`
}

export default function StrategiesPage() {
  return <AuthGuard><Inner /></AuthGuard>
}

function Inner() {
  const qc = useQueryClient()
  const [name, setName] = useState('')
  const [kind, setKind] = useState('sma_cross')
  const [params, setParams] = useState(() => defaultsJsonFor('sma_cross'))
  const [err, setErr] = useState<string | null>(null)
  // Edit mode: null = create, string = edit id. Khi vào edit mode form load values cũ +
  // submit button đổi sang "Save changes". Kind dropdown disabled (đổi kind = strategy khác,
  // user nên Clone).
  const [editingId, setEditingId] = useState<string | null>(null)

  const { data: list = [] } = useQuery({
    queryKey: qk.strategies,
    queryFn: () => api<StrategyDto[]>('/api/strategies'),
  })
  const { data: kinds = [] } = useQuery({
    queryKey: ['strategy-kinds'],
    queryFn: () => api<string[]>('/api/strategies/kinds'),
    staleTime: Infinity,
  })

  // Reset params chỉ khi đổi kind ở create mode. Ở edit mode giữ nguyên params đã load.
  useEffect(() => {
    if (editingId === null) setParams(defaultsJsonFor(kind))
  }, [kind, editingId])

  const createMut = useMutation({
    mutationFn: (body: { name: string; kind: string; parametersJson: string }) =>
      api('/api/strategies', { method: 'POST', body: JSON.stringify(body) }),
    onSuccess: () => {
      resetForm()
      qc.invalidateQueries({ queryKey: qk.strategies })
    },
    onError: (e) => setErr(e instanceof ApiError ? e.message : (e as Error).message),
  })

  const updateMut = useMutation({
    mutationFn: (body: { id: string; name: string; parametersJson: string }) =>
      api(`/api/strategies/${body.id}`, { method: 'PATCH', body: JSON.stringify({ name: body.name, parametersJson: body.parametersJson }) }),
    onSuccess: () => {
      resetForm()
      qc.invalidateQueries({ queryKey: qk.strategies })
    },
    onError: (e) => setErr(e instanceof ApiError ? e.message : (e as Error).message),
  })

  const removeMut = useMutation({
    mutationFn: (id: string) => api(`/api/strategies/${id}`, { method: 'DELETE' }),
    onSuccess: () => qc.invalidateQueries({ queryKey: qk.strategies }),
  })

  function resetForm() {
    setEditingId(null)
    setName('')
    setKind('sma_cross')
    setParams(defaultsJsonFor('sma_cross'))
    setErr(null)
  }

  function startEdit(s: StrategyDto) {
    setEditingId(s.id)
    setName(s.name)
    setKind(s.kind)
    setParams(s.parametersJson)
    setErr(null)
    // Scroll lên đầu form để mobile/short viewport thấy ngay.
    window.scrollTo({ top: 0, behavior: 'smooth' })
  }

  function submit(e: React.FormEvent) {
    e.preventDefault()
    setErr(null)
    try {
      // Backend parser accepts // comments + trailing commas; strip them here so this
      // pre-validate matches BE behaviour and templates with inline help still pass.
      JSON.parse(stripJsonComments(params))
    } catch (e) {
      setErr('Invalid JSON: ' + (e as Error).message)
      return
    }

    if (editingId) {
      // Cảnh báo khi có bot Running — params mới sẽ áp dụng từ nến tiếp theo.
      const target = list.find(s => s.id === editingId)
      if (target && target.runningBotCount > 0) {
        if (!confirm(`${target.runningBotCount} bot đang chạy strategy này. Params mới sẽ áp dụng từ nến tiếp theo. Tiếp tục?`)) return
      }
      updateMut.mutate({ id: editingId, name, parametersJson: params })
    } else {
      createMut.mutate({ name, kind, parametersJson: params })
    }
  }

  function remove(id: string) {
    if (!confirm('Delete strategy?')) return
    removeMut.mutate(id)
    // Nếu đang edit chính strategy bị xóa → reset form.
    if (editingId === id) resetForm()
  }

  const isEditing = editingId !== null
  const isSubmitting = createMut.isPending || updateMut.isPending

  return (
    <main className="min-h-screen">
      <NavBar />
      {/* minmax(0,1fr) thay vì 1fr: cho phép cột trái shrink xuống dưới content width khi
          params JSON dài. 1fr mặc định có min-width:auto → con dài thì cột phình ra → cột phải
          bị đẩy ra ngoài viewport. */}
      <div className="container grid gap-5 py-5 lg:grid-cols-[minmax(0,1fr)_380px]">
        <Card className="min-w-0">
          <CardHeader><CardTitle>Strategies</CardTitle></CardHeader>
          <CardContent>
            {/* Card-per-strategy thay cho table: name + kind chip ở header dòng, params wrap
                xuống dưới (font-mono, break-all). Trash nằm fixed bên phải nên luôn click được
                không cần scroll ngang. Tránh hoàn toàn vấn đề header bị clip + button trôi
                ngoài viewport khi params JSON dài. */}
            <div className="space-y-2">
              {list.map(s => {
                const isActive = editingId === s.id
                return (
                  <div
                    key={s.id}
                    className={`flex items-start justify-between gap-3 rounded-md border px-3 py-2.5 ${
                      isActive ? 'border-primary bg-primary/5' : 'border-border bg-surface/40'
                    }`}
                  >
                    <div className="min-w-0 flex-1">
                      <div className="flex flex-wrap items-center gap-2">
                        <span className="text-sm font-medium">{s.name}</span>
                        <Badge variant="outline" className="font-mono text-[10px]">{s.kind}</Badge>
                        {s.runningBotCount > 0 && (
                          <Badge className="bg-up/15 text-up text-[10px]" title="Số bot đang Running dùng strategy này">
                            {s.runningBotCount} bot live
                          </Badge>
                        )}
                      </div>
                      <div className="mt-1 break-all font-mono text-[11px] leading-relaxed text-muted-foreground">
                        {s.parametersJson}
                      </div>
                    </div>
                    <div className="flex shrink-0 items-center gap-0.5">
                      <Button size="sm" variant="ghost" onClick={() => startEdit(s)} title="Edit">
                        <Pencil className="h-4 w-4" />
                      </Button>
                      <Button size="sm" variant="ghost" onClick={() => remove(s.id)} title="Delete">
                        <Trash2 className="h-4 w-4 text-destructive" />
                      </Button>
                    </div>
                  </div>
                )
              })}
              {list.length === 0 && (
                <p className="py-6 text-center text-sm text-muted-foreground">No strategies yet.</p>
              )}
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardHeader><CardTitle>{isEditing ? 'Edit strategy' : 'Create strategy'}</CardTitle></CardHeader>
          <CardContent>
            <form onSubmit={submit} className="space-y-3">
              <Field label="Name"><input className="w-full rounded-md border px-3 py-2 text-sm" value={name} onChange={e => setName(e.target.value)} required /></Field>
              <Field label={isEditing ? 'Kind (không đổi được khi edit — clone để đổi kind)' : 'Kind'}>
                <select
                  className="w-full rounded-sm border border-border bg-surface px-3 py-2 text-sm text-foreground disabled:opacity-60"
                  value={kind}
                  onChange={e => setKind(e.target.value)}
                  disabled={isEditing}
                >
                  {kinds.map(k => <option key={k} value={k}>{k}</option>)}
                </select>
              </Field>
              <Field label="Parameters (JSON)">
                <textarea
                  className="h-32 w-full rounded-md border border-border bg-surface px-3 py-2 font-mono text-xs outline-none focus:border-primary"
                  value={params}
                  onChange={e => setParams(e.target.value)}
                  spellCheck={false}
                />
              </Field>

              <ParamGuide doc={STRATEGY_DOCS[kind]} onResetDefaults={() => setParams(defaultsJsonFor(kind))} />

              {err && <p className="text-sm text-destructive">{err}</p>}
              <div className="flex gap-2">
                <Button type="submit" className="flex-1" disabled={isSubmitting}>
                  {isSubmitting ? 'Saving...' : isEditing ? 'Save changes' : 'Create'}
                </Button>
                {isEditing && (
                  <Button type="button" variant="outline" onClick={resetForm} disabled={isSubmitting}>
                    Cancel
                  </Button>
                )}
              </div>
            </form>
          </CardContent>
        </Card>
      </div>
    </main>
  )
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return <label className="block"><span className="text-sm text-muted-foreground">{label}</span><div className="mt-1">{children}</div></label>
}

function ParamGuide({ doc, onResetDefaults }: { doc: StrategyDoc | undefined; onResetDefaults: () => void }) {
  if (!doc) return null
  return (
    <div className="rounded-md border border-border/60 bg-surface-2/30 text-xs">
      <div className="flex items-start justify-between gap-2 border-b border-border/40 px-3 py-2">
        <div className="space-y-0.5">
          <p className="text-[10px] uppercase tracking-wider text-muted-foreground">Parameter guide</p>
          <p className="text-[11px] leading-snug text-foreground/90">{doc.summary}</p>
        </div>
        <button
          type="button"
          onClick={onResetDefaults}
          className="shrink-0 rounded-sm border border-border/60 px-2 py-1 text-[10px] uppercase tracking-wider text-muted-foreground hover:border-primary/40 hover:text-foreground"
          title="Khôi phục JSON về giá trị mặc định"
        >
          Reset
        </button>
      </div>
      <ul className="divide-y divide-border/30">
        {doc.params.map(p => (
          <li key={p.key} className="grid grid-cols-[140px_1fr] gap-3 px-3 py-2">
            <div className="space-y-1">
              <code className="font-mono text-[11px] text-primary">{p.key}</code>
              <div className="flex flex-wrap items-center gap-1">
                <span className="rounded-sm bg-surface-2 px-1.5 py-0.5 font-mono text-[9px] uppercase tracking-wider text-muted-foreground">
                  {p.type}
                </span>
                <span className="rounded-sm border border-border/60 px-1.5 py-0.5 font-mono text-[9px] text-muted-foreground">
                  default {JSON.stringify(p.default)}
                </span>
              </div>
            </div>
            <div className="space-y-1 text-[11px] leading-snug text-foreground/85">
              <p>{p.desc}</p>
              {p.options && (
                <p className="text-[10px] text-muted-foreground">
                  Cho phép:{' '}
                  {p.options.map((o, i) => (
                    <span key={o}>
                      <code className="rounded-sm bg-surface-2 px-1 font-mono">{o}</code>
                      {i < p.options!.length - 1 ? ' · ' : ''}
                    </span>
                  ))}
                </p>
              )}
            </div>
          </li>
        ))}
      </ul>
      {doc.note && (
        <p className="border-t border-border/40 px-3 py-2 text-[10px] leading-snug text-muted-foreground">
          {doc.note}
        </p>
      )}
    </div>
  )
}

// Mirrors the backend's JsonDocumentOptions { CommentHandling = Skip, AllowTrailingCommas = true }.
// We strip `// line` and `/* block */` comments outside strings, then drop trailing commas.
function stripJsonComments(src: string): string {
  let out = ''
  let i = 0
  let inStr = false
  let strCh = ''
  while (i < src.length) {
    const c = src[i], n = src[i + 1]
    if (inStr) {
      out += c
      if (c === '\\' && i + 1 < src.length) { out += src[i + 1]; i += 2; continue }
      if (c === strCh) inStr = false
      i++; continue
    }
    if (c === '"' || c === "'") { inStr = true; strCh = c; out += c; i++; continue }
    if (c === '/' && n === '/') { while (i < src.length && src[i] !== '\n') i++; continue }
    if (c === '/' && n === '*') { i += 2; while (i < src.length && !(src[i] === '*' && src[i + 1] === '/')) i++; i += 2; continue }
    out += c; i++
  }
  return out.replace(/,(\s*[}\]])/g, '$1')
}
