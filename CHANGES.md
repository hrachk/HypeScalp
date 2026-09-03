# HypeScalp — Изменения (Рефакторинг v2)

## Что было исправлено

### 🐛 Критические баги

1. **dock.js был сломан** — файл содержал смесь JavaScript + CSS + C# кода в одном файле.
   Полностью переписан как чистый JS-модуль.

2. **Поле `isWall` не читалось** — backend отправлял `wall`, frontend ждал `isWall`.
   Исправлено: `l.isWall ?? l.IsWall ?? l.wall ?? false`.

3. **CSS дублировался** — стили были и в `hype.css` и inline в `terminal.html`.
   Terminal.html теперь просто подключает `<link rel="stylesheet" href="/css/hype.css"/>`.

### 🎨 Дизайн (Design System v2)

- Единый `hype.css` — полная дизайн-система, все компоненты
- Сетка для кластеров полностью стилизована (была пустой)
- Стены (wall levels) с подсветкой ask/bid
- Индикатор LIVE в заголовке каждого DOM-окна
- Окно можно свернуть (minimize) — кнопка `─`
- Resize handle в углу каждого окна
- Workspace с точечной сеткой (subtle grid bg)
- Улучшенные цвета status-лампочек с анимацией
- Filter для screener/watchlist

### 🏗️ Архитектура UI

- **Workspace Tabs** реализованы: Scalp / Multi DOM / Alts / Listing
  - Каждый workspace открывает свой набор окон
  - Переключение очищает рабочее пространство
- **+ Window** кнопка — dropdown для добавления любого окна
- **Layout persistence** — позиции окон сохраняются в localStorage (ключ `hs2.layout`)
- **Minimize** — каждое окно можно свернуть без закрытия
- Drag/resize через `dock.js` стал надёжнее (boundary checks)

### 📡 Backend (MarketBroadcastService)

- Добавлены подписки: SOLUSDT, BNBUSDT, XRPUSDT, DOGEUSDT, AVAXUSDT, LTCUSDT на Binance
- Все символы из Watchlist теперь получают live-данные

### ✅ Что НЕ изменялось

- Вся C# логика (TradingService, ExchangeClients, SignalR Hub)
- API endpoints
- Settings страница (Blazor)
- Program.cs, appsettings.json
- Интерфейсы и модели

## Roadmap (следующие шаги)

1. Реализовать Bybit/OKX/Gate signed orders (сейчас `NotImplementedException`)
2. Кластерная история (price × timeframe матрица)
3. Screener фильтры (volume, change%, funding rate)
4. Funding rate окно (WS уже есть в MarketDataHub)
5. Свои ордера на стакане (flags уже есть, нужен UserDataStream)
6. Proxy per connection
