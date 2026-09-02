# HypeScalp PRO — Full Terminal (.NET 8)

Полноценный UI как в **HypeScalp_Prototype.html** + backend на ASP.NET Core.

## Run

```bash
dotnet restore
dotnet run --project src/HypeScalp.Web
```

Откроется `https://localhost:7150/` → **`/terminal.html`** (полный терминал).

## UI (prototype)

- Floating / drag / resize окна
- **+ Window**: Chart, DOM, Multi-monitor, Screener, Positions, Tape, Hotkeys
- Workspace tabs, Themes, Flatten
- Hotkeys: Space buy, X flatten, N chart, D DOM
- Layout стартовый: watchlist + multi chart + Binance/Bybit/Gate DOM + positions + tape

## Live data

- SignalR hub: `/hubs/market`
- `MarketDataHub` → Binance / Bybit / Gate / OKX public WS
- DOM обновляется по `orderBook` / `trade` (после connect toast «Live feed connected»)

## Connections (API keys)

- Blazor page: **`/settings`**
- Data Protection для секретов
- TradingService для ордеров (при подключённом клиенте)

## Structure

```
wwwroot/terminal.html   ← full prototype terminal
Hubs/MarketStreamHub    ← SignalR
Services/MarketDataHub  ← exchange WebSockets
Components/…            ← Blazor settings + legacy panels
```
