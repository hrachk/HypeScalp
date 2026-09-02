# HypeScalp Web — .NET 8 Blazor Scalping Terminal

## Run

```bash
dotnet restore
dotnet run --project src/HypeScalp.Web
```

## Features (HS-1)

| Feature | Details |
|---------|---------|
| **Live DOM** | Binance / Bybit / OKX public WebSocket depth + trades |
| **Chart overlay** | Real multi-exchange lines (Binance + Bybit + OKX) |
| **Trading from DOM** | Market / Limit via connected API client (`TradingService`) |
| **Data Protection** | API secrets encrypted at rest |
| **Layout** | Drag windows → 💾 saves to `localStorage` |

## Connections

1. Open **Connections**
2. API Key + Secret (+ Passphrase for OKX)
3. **Connect**
4. BUY/SELL / click ladder level → order through that client

Public market data works **without** keys. Orders need a connected exchange.

## Layout

Drag panels by header. Click **💾** on chart window to save positions.
