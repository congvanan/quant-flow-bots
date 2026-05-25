import { AreaSeries, createChart, type IChartApi, type ISeriesApi, type LineData, type Time } from 'lightweight-charts'
import { useEffect, useRef } from 'react'
import type { EquityPoint } from '@/lib/api'

export function EquityChart({ data, height = 320 }: { data: EquityPoint[]; height?: number }) {
  const ref = useRef<HTMLDivElement | null>(null)
  const chartRef = useRef<IChartApi | null>(null)
  const seriesRef = useRef<ISeriesApi<'Area'> | null>(null)

  useEffect(() => {
    if (!ref.current) return
    const chart = createChart(ref.current, {
      height,
      layout: { background: { color: '#ffffff' }, textColor: '#1f2937' },
      grid: { vertLines: { color: '#f1f5f9' }, horzLines: { color: '#f1f5f9' } },
      rightPriceScale: { borderColor: '#e2e8f0' },
      timeScale: { borderColor: '#e2e8f0', timeVisible: true, secondsVisible: false },
    })
    const series = chart.addSeries(AreaSeries, {
      lineColor: '#2563eb', topColor: 'rgba(37,99,235,0.3)', bottomColor: 'rgba(37,99,235,0.02)',
    })
    chartRef.current = chart
    seriesRef.current = series
    const ro = new ResizeObserver(entries => {
      const el = entries[0]; if (el && chart) chart.applyOptions({ width: el.contentRect.width })
    })
    ro.observe(ref.current)
    return () => { ro.disconnect(); chart.remove() }
  }, [height])

  useEffect(() => {
    const series = seriesRef.current
    const chart = chartRef.current
    if (!series || !chart) return
    const points: LineData[] = data.map(p => ({
      time: Math.floor(new Date(p.at).getTime() / 1000) as Time,
      value: Number(p.equity),
    }))
    series.setData(points)
    chart.timeScale().fitContent()
  }, [data])

  return <div ref={ref} className="w-full" />
}
