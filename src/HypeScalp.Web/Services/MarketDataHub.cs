using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using HypeScalp.Core.Models;

namespace HypeScalp.Web.Services;

/// <summary>
/// Public Binance market data via combined WebSocket streams (no API key required).
/// Depth + aggregate trades for live DOM and tape.
/// </summary>
public class MarketDataHub : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, Subscription> _subs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<MarketDataHub> _log;

    public MarketDataHub(ILogger<MarketDataHub> log) => _log = log;

    public event Action<string, OrderBookSnapshot>? OnOrderBook;
    public event Action<string, TradeTick>? OnTrade;

    public async Task SubscribeAsync(string symbol, bool futures = true, CancellationToken ct = default)
    {
        symbol = symbol.ToUpperInvariant();
        if (_subs.ContainsKey(symbol)) return;

        var streamSymbol = symbol.ToLowerInvariant();
        var host = futures ? "fstream.binance.com" : "stream.binance.com";
        // combined stream: depth20@100ms + aggTrade
        var url = $"wss://{host}/stream?streams={streamSymbol}@depth20@100ms/{streamSymbol}@aggTrade";

        var sub = new Subscription(symbol, futures);
        if (!_subs.TryAdd(symbol, sub)) return;

        _ = Task.Run(() => RunLoopAsync(sub, url), ct);
        await Task.CompletedTask;
    }

    public async Task UnsubscribeAsync(string symbol)
    {
        symbol = symbol.ToUpperInvariant();
        if (_subs.TryRemove(symbol, out var sub))
        {
            sub.Cts.Cancel();
            if (sub.Ws != null)
            {
                try { await sub.Ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "unsub", CancellationToken.None); }
                catch { /* ignore */ }
                sub.Ws.Dispose();
            }
        }
    }

    private async Task RunLoopAsync(Subscription sub, string url)
    {
        var backoff = 1000;
        while (!sub.Cts.IsCancellationRequested)
        {
            try
            {
                using var ws = new ClientWebSocket();
                sub.Ws = ws;
                await ws.ConnectAsync(new Uri(url), sub.Cts.Token);
                _log.LogInformation("WS connected {Symbol}", sub.Symbol);
                backoff = 1000;

                var buffer = new byte[1024 * 64];
                while (ws.State == WebSocketState.Open && !sub.Cts.IsCancellationRequested)
                {
                    using var ms = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await ws.ReceiveAsync(buffer, sub.Cts.Token);
                        if (result.MessageType == WebSocketMessageType.Close) break;
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close) break;

                    var json = Encoding.UTF8.GetString(ms.ToArray());
                    HandleMessage(sub.Symbol, json);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "WS error {Symbol}, reconnect in {Ms}ms", sub.Symbol, backoff);
                await Task.Delay(backoff, CancellationToken.None);
                backoff = Math.Min(backoff * 2, 15000);
            }
        }
    }

    private void HandleMessage(string symbol, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("stream", out var streamProp) || !root.TryGetProperty("data", out var data))
                return;

            var stream = streamProp.GetString() ?? "";
            if (stream.Contains("depth", StringComparison.Ordinal))
            {
                var snap = new OrderBookSnapshot
                {
                    Symbol = symbol,
                    Exchange = ExchangeType.Binance,
                    Timestamp = DateTime.UtcNow
                };
                if (data.TryGetProperty("asks", out var asks))
                {
                    foreach (var a in asks.EnumerateArray())
                    {
                        snap.Asks.Add(new OrderBookLevel
                        {
                            Price = decimal.Parse(a[0].GetString()!, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture),
                            Quantity = decimal.Parse(a[1].GetString()!, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture)
                        });
                    }
                }
                if (data.TryGetProperty("bids", out var bids))
                {
                    foreach (var b in bids.EnumerateArray())
                    {
                        snap.Bids.Add(new OrderBookLevel
                        {
                            Price = decimal.Parse(b[0].GetString()!, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture),
                            Quantity = decimal.Parse(b[1].GetString()!, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture)
                        });
                    }
                }
                // Mark walls (top volume)
                MarkWalls(snap.Asks);
                MarkWalls(snap.Bids);
                OnOrderBook?.Invoke(symbol, snap);
            }
            else if (stream.Contains("aggTrade", StringComparison.Ordinal))
            {
                var price = decimal.Parse(data.GetProperty("p").GetString()!, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture);
                var qty = decimal.Parse(data.GetProperty("q").GetString()!, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture);
                var isBuy = !data.GetProperty("m").GetBoolean(); // m=true => seller was maker => sell aggressor
                var tick = new TradeTick
                {
                    Symbol = symbol,
                    Exchange = ExchangeType.Binance,
                    Price = price,
                    Quantity = qty,
                    IsBuy = isBuy,
                    IsLarge = qty * price >= 25000m,
                    Timestamp = DateTime.UtcNow
                };
                OnTrade?.Invoke(symbol, tick);
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Parse fail");
        }
    }

    private static void MarkWalls(List<OrderBookLevel> levels)
    {
        if (levels.Count == 0) return;
        var max = levels.Max(l => l.Quantity);
        foreach (var l in levels)
            l.IsWall = max > 0 && l.Quantity >= max * 0.55m;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var key in _subs.Keys.ToList())
            await UnsubscribeAsync(key);
    }

    private sealed class Subscription
    {
        public string Symbol { get; }
        public bool Futures { get; }
        public CancellationTokenSource Cts { get; } = new();
        public ClientWebSocket? Ws { get; set; }
        public Subscription(string symbol, bool futures) { Symbol = symbol; Futures = futures; }
    }
}
