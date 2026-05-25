import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ReactQueryDevtools } from '@tanstack/react-query-devtools'
import App from './App'
import { AuthProvider } from './lib/auth-context'
import { SignalRProvider } from './lib/signalr-context'
import { SignalRQueryBridge } from './lib/signalr-query-bridge'
import './globals.css'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 5_000,            // fresh for 5s — most trading data is short-lived
      gcTime: 5 * 60_000,          // keep in cache 5 min
      refetchOnWindowFocus: true,  // re-sync when user comes back to tab
      retry: (failureCount, err) => {
        // Don't retry auth errors — they're handled by api.ts (redirects to /login)
        if (err instanceof Error && err.message.startsWith('401')) return false
        return failureCount < 2
      },
    },
  },
})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BrowserRouter>
      <QueryClientProvider client={queryClient}>
        <AuthProvider>
          <SignalRProvider>
            <SignalRQueryBridge>
              <App />
            </SignalRQueryBridge>
          </SignalRProvider>
        </AuthProvider>
        {import.meta.env.DEV && <ReactQueryDevtools buttonPosition="bottom-right" />}
      </QueryClientProvider>
    </BrowserRouter>
  </StrictMode>,
)
