import { useEffect } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useSignalR } from './signalr-context'

/**
 * Bridge between SignalR push events and TanStack Query cache.
 *
 * When the backend pushes a bot event ("order", "auto_close", "risk", etc.),
 * we don't refetch ourselves — we mark related queries as stale and let
 * React Query refetch them on demand (next mount / next access).
 *
 * Mount this once at the app root (under QueryClientProvider + SignalRProvider).
 */
export function SignalRQueryBridge({ children }: { children: React.ReactNode }) {
  const qc = useQueryClient()
  const { connection } = useSignalR()

  useEffect(() => {
    if (!connection) return
    const onBotEvent = (e: { botId: string; kind: string; message: string; at: string }) => {
      // Anything that changes position/order state should invalidate per-bot queries
      if (e.kind === 'order' || e.kind === 'auto_close' || e.kind === 'risk') {
        qc.invalidateQueries({ queryKey: ['bot', e.botId] })
        qc.invalidateQueries({ queryKey: ['bot-orders', e.botId] })
        qc.invalidateQueries({ queryKey: ['bot-positions', e.botId] })
        qc.invalidateQueries({ queryKey: ['bot-risk-events', e.botId] })
        qc.invalidateQueries({ queryKey: ['bot-stats', e.botId] })
        qc.invalidateQueries({ queryKey: ['bots-stats-summary'] })
        // Bot list shows PnL/state summary
        qc.invalidateQueries({ queryKey: ['bots'] })
      }
      if (e.kind === 'started' || e.kind === 'stopped') {
        qc.invalidateQueries({ queryKey: ['bots'] })
        qc.invalidateQueries({ queryKey: ['bot', e.botId] })
      }
      if (e.kind === 'signal') {
        qc.invalidateQueries({ queryKey: ['bot-signals', e.botId] })
      }
    }
    connection.on('bot', onBotEvent)
    const onSentiment = () => {
      // New sentiment event → freshen recent list + top movers.
      qc.invalidateQueries({ queryKey: ['sentiment'] })
    }
    connection.on('sentiment', onSentiment)
    return () => {
      connection.off('bot', onBotEvent)
      connection.off('sentiment', onSentiment)
    }
  }, [connection, qc])

  return <>{children}</>
}
