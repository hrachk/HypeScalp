using HypeScalp.Core.Models;
using HypeScalp.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace HypeScalp.Web.Services;

/// <summary>
/// Bridges MarketDataHub → SignalR so the prototype terminal UI can receive live books/trades.
/// </summary>
public class MarketBroadcastService : IHostedService
{
    private readonly MarketDataHub _market;
    private readonly IHubContext<MarketStreamHub> _hub;
    private readonly ILogger<MarketBroadcastService> _log;

    public MarketBroadcastService(MarketDataHub market, IHubContext<MarketStreamHub> hub, ILogger<MarketBroadcastService> log)
    {
        _market = market;
        _hub = hub;
        _log = log;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _market.OnOrderBook += OnBook;
        _market.OnTrade += OnTrade;
        _ = Task.Run(async () =>
        {
            try
            {
                await _market.SubscribeAsync(ExchangeType.Binance, "BTCUSDT", true);
                await _market.SubscribeAsync(ExchangeType.Binance, "ETHUSDT", true);
                await _market.SubscribeAsync(ExchangeType.Bybit, "BTCUSDT", true);
                await _market.SubscribeAsync(ExchangeType.Bybit, "ETHUSDT", true);
                await _market.SubscribeAsync(ExchangeType.Gate, "BTC_USDT", true);
                await _market.SubscribeAsync(ExchangeType.Okx, "BTC-USDT", true);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Pre-subscribe failed");
            }
        }, cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _market.OnOrderBook -= OnBook;
        _market.OnTrade -= OnTrade;
        return Task.CompletedTask;
    }

    void OnBook(string key, OrderBookSnapshot snap)
    {
        var payload = new
        {
            exchange = snap.Exchange.ToString(),
            symbol = snap.Symbol,
            mid = snap.BestBid > 0 && snap.BestAsk > 0 ? (snap.BestBid + snap.BestAsk) / 2 : 0,
            spread = snap.Spread,
            bids = snap.Bids.Take(20).Select(l => new { p = l.Price, q = l.Quantity, wall = l.IsWall }),
            asks = snap.Asks.Take(20).Select(l => new { p = l.Price, q = l.Quantity, wall = l.IsWall }),
            ts = snap.Timestamp
        };
        _ = _hub.Clients.All.SendAsync("orderBook", payload);
    }

    void OnTrade(string key, TradeTick t)
    {
        var payload = new
        {
            exchange = t.Exchange.ToString(),
            symbol = t.Symbol,
            price = t.Price,
            qty = t.Quantity,
            buy = t.IsBuy,
            large = t.IsLarge,
            ts = t.Timestamp
        };
        _ = _hub.Clients.All.SendAsync("trade", payload);
    }
}
