import { CandlestickSeries, createChart, type CandlestickData, type IChartApi, type ISeriesApi, type Time } from 'lightweight-charts'
import { useEffect, useRef } from 'react'
import type { CandleData } from '@/lib/api'

export type CandleChartHandle = {
  setInitial: (candles: CandleData[]) => void
  update: (c: CandleData) => void
}

export function CandleChart({ height = 420, onReady }: { height?: number; onReady?: (h: CandleChartHandle) => void }) {
  const ref = useRef<HTMLDivElement | null>(null)
  const chartRef = useRef<IChartApi | null>(null)
  const seriesRef = useRef<ISeriesApi<'Candlestick'> | null>(null)

  useEffect(() => {
    if (!ref.current) return
    const chart = createChart(ref.current, {
      height,
      layout: { background: { color: '#ffffff' }, textColor: '#1f2937' },
      grid: { vertLines: { color: '#f1f5f9' }, horzLines: { color: '#f1f5f9' } },
      rightPriceScale: { borderColor: '#e2e8f0' },
      timeScale: { borderColor: '#e2e8f0', timeVisible: true, secondsVisible: false },
    })
    const series = chart.addSeries(CandlestickSeries, {
      upColor: '#16a34a', downColor: '#dc2626',
      borderUpColor: '#16a34a', borderDownColor: '#dc2626',
      wickUpColor: '#16a34a', wickDownColor: '#dc2626',
    })
    chartRef.current = chart
    seriesRef.current = series

    const ro = new ResizeObserver(entries => {
      const el = entries[0]
      if (el && chart) chart.applyOptions({ width: el.contentRect.width })
    })
    ro.observe(ref.current)

    onReady?.({
      setInitial: (candles) => {
        const data: CandlestickData[] = candles.map(c => ({
          time: Math.floor(new Date(c.openTime).getTime() / 1000) as Time,
          open: Number(c.open), high: Number(c.high), low: Number(c.low), close: Number(c.close),
        }))
        series.setData(data)
        chart.timeScale().fitContent()
      },
      update: (c) => {
        series.update({
          time: Math.floor(new Date(c.openTime).getTime() / 1000) as Time,
          open: Number(c.open), high: Number(c.high), low: Number(c.low), close: Number(c.close),
        })
      },
    })

    return () => { ro.disconnect(); chart.remove() }
  }, [height, onReady])

  return <div ref={ref} className="w-full" />
}
