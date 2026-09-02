# HypeScalp Web — .NET 8 Blazor Scalping Terminal

Web-приложение на **ASP.NET Core 8 + Blazor Server** с дизайном HypeScalp и подключением бирж по **API Key + Secret** (как в MetaScalp).

## Запуск

```bash
cd HypeScalp
dotnet restore
dotnet run --project src/HypeScalp.Web
```

Открой в браузере: `https://localhost:5xxx` (порт покажет консоль).

## Возможности

- **Hype UI** — тёмный неон, floating/drag окна
- **Connections** (`/settings`) — добавление бирж по API Key / Secret / Passphrase
- Сохранение настроек в `App_Data/`
- **Binance** — реальный signed REST (account, depth, place order, positions)
- Bybit / OKX — заготовки клиентов
- Terminal page — screener, multi-chart placeholder, DOM-панели с кластерами и лентой
- Interactive Server — real-time UI без отдельного SPA-фреймворка

## Структура

```
HypeScalp/
├── HypeScalp.sln
└── src/
    ├── HypeScalp.Web/          # Blazor Web App
    │   ├── Components/         # Pages, Layout, Shared
    │   ├── Services/           # SettingsService, ConnectionManager
    │   └── wwwroot/css|js      # Hype design + dock
    ├── HypeScalp.Core/         # Models + IExchangeClient
    └── HypeScalp.Exchange/     # Binance / Bybit / OKX
```

## Подключение биржи

1. Меню **Connections**
2. Exchange + Market
3. **API Key** + **API Secret** (+ Passphrase для OKX)
4. Add → Connect

## Далее

- WebSocket depth/trades
- Привязка DOM к live OrderBook
- Chart (canvas / library) с overlay
- Data Protection для секретов в production
- Docker / reverse proxy
