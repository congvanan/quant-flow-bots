# Quant Flow Bots

Fintech dashboard + trading-bot control room (paper-mode first). Lấy dữ liệu từ Binance (mở rộng đa sàn qua abstraction), phân tích, ra báo cáo, tự động trade qua bot.

## Kiến trúc phase 1

```
quant-flow-bots/
├── backend/
│   ├── QuantFlowBots.sln
│   └── src/
│       ├── QuantFlowBots.Domain          entities + enums (12 bảng)
│       ├── QuantFlowBots.Application     interfaces (IExchangeClient, IMarketStreamClient, IStrategy, ...)
│       ├── QuantFlowBots.Infrastructure  EF Core (Postgres+Timescale), Binance REST+WS, encryption, in-memory bus
│       ├── QuantFlowBots.Worker          4 IHostedService (MarketStream, CandleIngestion, SignalScanner, BotExecution)
│       └── QuantFlowBots.Api             Minimal API + Identity/JWT + SignalR Hub
├── frontend/                              Next.js 16 + React 19 + Tailwind 4
├── docker-compose.yml                     timescaledb, redis, pgadmin
└── start-dev.ps1
```

Luồng dữ liệu realtime:

```
Binance WS ──► MarketStreamWorker ──┬──► Channel<Ticker>    ──► SignalR ─► FE
                                    │
                                    └──► Channel<KlineClosed> ─┬─► CandleIngestion ─► Postgres (hypertable)
                                                               └─► SignalScanner   ─► Signal ─► BotExecution ─► Order
```

## Nguyên tắc cứng

- **Frontend không được kết nối WS Binance trực tiếp.** Mọi stream đi qua Worker → SignalR.
- **Live trading bị disable cứng** (`BotStatus.LiveTradingEnabled=false`). Sửa sang `Live` phải qua code change có chủ ý.
- **API key của user mã hóa AES-256** bằng `Security:EncryptionKey`, lưu ở backend, không bao giờ trả về plain text.
- **Chỉ persist candle 1m + signals + orders + positions.** Ticker realtime sống trong memory.

## Chạy dev

```powershell
# 1. copy .env.example -> .env, đặt POSTGRES_PASSWORD (1 lần)
copy .env.example .env
# rồi mở .env, đổi POSTGRES_PASSWORD thành chuỗi mạnh.
# Tạo appsettings.Development.json cho cả Api và Worker khớp với POSTGRES_PASSWORD:
copy backend\src\QuantFlowBots.Api\appsettings.Development.example.json     backend\src\QuantFlowBots.Api\appsettings.Development.json
copy backend\src\QuantFlowBots.Worker\appsettings.Development.example.json  backend\src\QuantFlowBots.Worker\appsettings.Development.json
# rồi sửa Password trong 2 file vừa copy cho khớp .env.

# 2. dependencies frontend (1 lần)
cd .\frontend; npm install; cd ..

# 3. db secrets (1 lần): set encryption key + jwt signing key
cd .\backend\src\QuantFlowBots.Api
dotnet user-secrets set "Security:EncryptionKey" "$(([guid]::NewGuid()).ToString() + [guid]::NewGuid().ToString())"
dotnet user-secrets set "Jwt:SigningKey"          "$(([guid]::NewGuid()).ToString() + [guid]::NewGuid().ToString())"
cd ..\QuantFlowBots.Worker
dotnet user-secrets set "Security:EncryptionKey" "$(([guid]::NewGuid()).ToString() + [guid]::NewGuid().ToString())"
cd ..\..\..

# 4. tạo migration đầu tiên (1 lần)
dotnet ef migrations add Initial `
  --project .\backend\src\QuantFlowBots.Infrastructure `
  --startup-project .\backend\src\QuantFlowBots.Api `
  -o Persistence\Migrations

# 5. khởi động cả stack
.\start-dev.ps1
```

API sẽ tự `Database.MigrateAsync()` + tạo Timescale hypertable + seed `Binance` exchange khi start.

## Endpoint phase 1

| Method | Path | Mô tả |
|---|---|---|
| GET  | `/health` | health check |
| POST | `/api/auth/register` | tạo user |
| POST | `/api/auth/login` | trả JWT |
| GET  | `/api/market/overview` | top gainers/volume/sharp movers (Binance REST) |
| GET  | `/api/market/symbols` | symbols đã seed trong DB |
| GET  | `/api/market/candles?symbol=BTCUSDT&interval=OneMinute&limit=200` | klines |
| GET  | `/api/bots/status` | trạng thái paper mode (anonymous) |
| GET  | `/api/bots` | danh sách bot của user (yêu cầu JWT) |
| WS   | `/hubs/market` | SignalR hub — events: `ticker`, `kline`, `signal`, `bot` |

## Phase tiếp theo

- **Phase 2**: bind FE với `/api/market/*` thật (bỏ mock), Market Explorer với SignalR ticker realtime, candles chart.
- **Phase 3**: implement `IStrategy` (SMA cross / RSI / breakout), wire `SignalScannerWorker`, paper order engine với fill model, vòng đời bot start/stop/pause.
- **Phase 4**: `IBacktestRunner` đầy đủ, equity curve, Sharpe/MDD/winrate, FE Backtest + Reports.
