import { useEffect, useRef } from 'react'

type Props = {
  symbol: string
  interval?: '1' | '5' | '15' | '30' | '60' | '120' | '240' | 'D' | 'W'
  exchange?: string
  height?: number
  theme?: 'light' | 'dark'
}

export function TradingViewChart({
  symbol,
  interval = '60',
  exchange = 'BINANCE',
  height = 760,
  theme = 'light',
}: Props) {
  const ref = useRef<HTMLDivElement | null>(null)

  useEffect(() => {
    if (!ref.current) return
    const container = ref.current
    container.innerHTML = ''

    const inner = document.createElement('div')
    inner.className = 'tradingview-widget-container__widget'
    inner.style.height = '100%'
    inner.style.width = '100%'
    container.appendChild(inner)

    const script = document.createElement('script')
    script.src = 'https://s3.tradingview.com/external-embedding/embed-widget-advanced-chart.js'
    script.type = 'text/javascript'
    script.async = true
    script.innerHTML = JSON.stringify({
      width: '100%',
      height,
      symbol: `${exchange}:${symbol.toUpperCase()}`,
      interval,
      timezone: 'Asia/Ho_Chi_Minh',
      theme,
      style: '1',
      locale: 'vi_VN',
      enable_publishing: false,
      withdateranges: true,
      hide_side_toolbar: false,
      allow_symbol_change: true,
      details: true,
      hotlist: false,
      calendar: false,
      studies: [
        'STD;EMA',
        'STD;Bollinger_Bands',
        'STD;MACD',
      ],
      support_host: 'https://www.tradingview.com',
    })
    container.appendChild(script)

    return () => { container.innerHTML = '' }
  }, [symbol, interval, exchange, theme, height])

  return (
    <div
      ref={ref}
      className="tradingview-widget-container w-full"
      style={{ height: `${height}px`, minHeight: '600px' }}
    />
  )
}
