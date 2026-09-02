# HypeScalp Web — .NET 8 Blazor Scalping Terminal

Web terminal (ASP.NET Core 8 + Blazor Server) with Hype design, **live Binance WebSocket DOM**, chart overlay, and **Data Protection** for API secrets.

## Run

```bash
dotnet restore
dotnet run --project src/HypeScalp.Web
```

Open `https://localhost:7150`.

## What's new (HS-1)

| Feature | Status |
|---------|--------|
| **Data Protection** for API secrets | ✅ `App_Data/secrets.protected` + key ring |
| **Binance public WebSocket** depth20@100ms + aggTrade | ✅ `MarketDataHub` |
| **Live DOM** (ladder, tape, 1s clusters) | ✅ `DomPanel` |
| **Chart overlay** multi-line | ✅ `ChartOverlay` + canvas |
| Connections UI (API Key / Secret) | ✅ `/settings` |
| Binance signed REST (account/orders) | ✅ when API connected |

Public market data works **without** API keys. Trading / positions need keys in **Connections**.

## Structure

```
src/
  HypeScalp.Web/     Blazor UI, MarketDataHub, SettingsService
  HypeScalp.Core/    Models, IExchangeClient
  HypeScalp.Exchange/ Binance / Bybit / OKX clients
```

## Security

- Secrets encrypted via ASP.NET Core Data Protection
- Keys stored under `App_Data/keys` (do not commit)
- Add `App_Data/` to `.gitignore` (already recommended)

## Next

- Real multi-exchange WS (Bybit/OKX public streams)
- Place order from DOM via connected client
- Persist workspace layout
