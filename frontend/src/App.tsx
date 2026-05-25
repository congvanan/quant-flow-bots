import { Routes, Route, Navigate } from 'react-router-dom'
import DashboardPage from '@/pages/dashboard'
import LoginPage from '@/pages/login'
import BotsPage from '@/pages/bots'
import BotDetailPage from '@/pages/bot-detail'
import StrategiesPage from '@/pages/strategies'
import BacktestPage from '@/pages/backtest'
import SettingsPage from '@/pages/settings'
import SymbolDetailPage from '@/pages/symbol-detail'

export default function App() {
  return (
    <Routes>
      <Route path="/" element={<DashboardPage />} />
      <Route path="/login" element={<LoginPage />} />
      <Route path="/bots" element={<BotsPage />} />
      <Route path="/bots/:id" element={<BotDetailPage />} />
      <Route path="/strategies" element={<StrategiesPage />} />
      <Route path="/backtest" element={<BacktestPage />} />
      <Route path="/settings" element={<SettingsPage />} />
      <Route path="/symbol/:code" element={<SymbolDetailPage />} />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
