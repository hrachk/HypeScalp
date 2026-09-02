# HypeScalp vs MetaScalp — feature map

Research base: MetaScalp docs, API/SDK, reviews (DOM, clusters, hotkeys, links, 30+ exchanges).

## Core (MetaScalp parity)

| Feature | MetaScalp | HypeScalp |
|---------|-----------|-----------|
| Vertical DOM (ask / spread / bid) | ✅ | ✅ |
| Trade tape in DOM | ✅ | ✅ |
| Clusters / footprint columns | ✅ TF M1–D1 | ✅ 1s + M1–D1 settings |
| Working volume slots | ✅ 5 (USD/coin) | ✅ 5 slots, configurable |
| One-click limit from ladder | ✅ | ✅ |
| Market BUY/SELL buttons | ✅ | ✅ |
| Density / large amount (2 thresholds) | ✅ | ✅ USD thresholds + highlight |
| Link groups (DOM ↔ chart) | ✅ | ✅ groups 1–3, combo window |
| Multi-exchange side-by-side | ✅ | ✅ Binance / Bybit / Gate / OKX |
| Workspaces / tabs | ✅ | ✅ Scalp / Multi / Alts / Listing |
| Hotkeys (center, cancel, flatten, best bid/ask) | ✅ | ✅ C/Esc/X/B/S/Space/F1–F3 |
| Positions + session PnL | ✅ | ✅ panel + topbar |
| Global tape | ✅ | ✅ |
| Screener / watchlist | ✅ | ✅ |
| Themes | ✅ | ✅ toggle |
| API Key connections | ✅ | ✅ `/settings` + Data Protection |
| Public WS market data | ✅ | ✅ SignalR bridge |
| Mark / Index / Funding UI | ✅ | ✅ Funding window |
| Combo open (DOM+Chart) | ✅ | ✅ + Window → Combo |
| Sound on large size | ✅ | ✅ optional beep |
| DOM settings modal | ✅ 5 tabs | ✅ density / clusters / vols |

## Beyond MetaScalp (Hype goals)

| Feature | Status |
|---------|--------|
| Full web stack (.NET 8 + browser) | ✅ no desktop lock-in |
| Open source architecture | ✅ |
| Hype neon design system | ✅ |
| Multi-endpoint OKX fallback | ✅ |
| Gate.io native symbol `_` | ✅ |
| Layout save (localStorage) | ✅ (prototype dock) |
| SignalR fan-out for UI | ✅ |

## Roadmap (next implementation)

1. Real Binance mark/index/funding WS → Funding window
2. Active DOM focus + real cancel/flatten via TradingService API
3. Cluster history matrix (price × TF) like MetaScalp columns
4. Screener filters (volume, change %, funding)
5. Bitget / KuCoin / MEXC public feeds
6. Order flags on ladder (own limits)
7. Proxy per connection
