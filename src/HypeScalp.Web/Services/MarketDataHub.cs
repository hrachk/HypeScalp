using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using HypeScalp.Core.Models;

namespace HypeScalp.Web.Services;

/// <summary>
/// Public market data WebSocket hub for Binance, Bybit, OKX (no API key).
/// Key: "BINANCE:BTCUSDT", "BYBIT:ETHUSDT", "OKX:BTC-USDT"
/// </summary>
public class MarketDataHub : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, Subscription> _subs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<MarketDataHub> _log;

    public MarketDataHub(ILogger<MarketDataHub> log) => _log = log;

    /// <summary>symbolKey e.g. BTCUSDT — for backward compat raises with plain symbol too.</summary>
    public event Action<string, OrderBookSnapshot>? OnOrderBook;
    public event Action<string, TradeTick>? OnTrade;

    public Task SubscribeAsync(string symbol, bool futures = true, CancellationToken ct = default)
        => SubscribeAsync(ExchangeType.Binance, symbol, futures, ct);

    public async Task SubscribeAsync(ExchangeType exchange, string symbol, bool futures = true, CancellationToken ct = default)
    {
        symbol = NormalizeSymbol(exchange, symbol);
        var key = MakeKey(exchange, symbol);
        if (_subs.ContainsKey(key)) return;

        var urls = BuildUrls(exchange, symbol, futures);
        if (urls.Length == 0)
        {
            _log.LogWarning("No public WS URL for {Exchange} {Symbol}", exchange, symbol);
            return;
        }

        var sub = new Subscription(key, exchange, symbol, futures);
        if (!_subs.TryAdd(key, sub)) return;

        _ = Task.Run(() => RunLoopAsync(sub, urls), ct);
        await Task.CompletedTask;
    }

    public async Task UnsubscribeAsync(ExchangeType exchange, string symbol)
    {
        var key = MakeKey(exchange, NormalizeSymbol(exchange, symbol));
        if (!_subs.TryRemove(key, out var sub)) return;
        sub.Cts.Cancel();
        if (sub.Ws != null)
        {
            try { await sub.Ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "unsub", CancellationToken.None); }
            catch { /* ignore */ }
            sub.Ws.Dispose();
        }
    }

    public Task UnsubscribeAsync(string symbol)
        => UnsubscribeAsync(ExchangeType.Binance, symbol);

    private static string MakeKey(ExchangeType ex, string symbol) => $"{ex}:{symbol}";

    private static string NormalizeSymbol(ExchangeType ex, string symbol)
    {
        symbol = symbol.Trim().ToUpperInvariant().Replace("-", "_");
        return ex switch
        {
            ExchangeType.Okx => symbol.Contains('_')
                ? symbol.Replace("_", "-")
                : symbol.Replace("USDT", "-USDT"),
            ExchangeType.Gate => symbol.Contains('_')
                ? symbol
                : symbol.Replace("USDT", "_USDT"),
            _ => symbol.Replace("_", "").Replace("-", "")
        };
    }

    private static string[] BuildUrls(ExchangeType ex, string symbol, bool futures)
    {
        var s = symbol.ToLowerInvariant();
        return ex switch
        {
            ExchangeType.Binance => new[]
            {
                futures
                    ? $"wss://fstream.binance.com/stream?streams={s}@depth20@100ms/{s}@aggTrade"
                    : $"wss://stream.binance.com:9443/stream?streams={s}@depth20@100ms/{s}@aggTrade"
            },
            ExchangeType.Bybit => new[]
            {
                futures ? "wss://stream.bybit.com/v5/public/linear" : "wss://stream.bybit.com/v5/public/spot"
            },
            // OKX: several public endpoints (region / CDN differences)
            ExchangeType.Okx => new[]
            {
                "wss://ws.okx.com:8443/ws/v5/public",
                "wss://wsaws.okx.com:8443/ws/v5/public",
                "wss://wspap.okx.com:8443/ws/v5/public"
            },
            ExchangeType.Gate => new[]
            {
                futures ? "wss://fx-ws.gateio.ws/v4/ws/usdt" : "wss://api.gateio.ws/ws/v4/",
                futures ? "wss://fx-ws.gateio.ws/v4/ws/usdt" : "wss://api.gateio.ws/ws/v4/"
            },
            _ => Array.Empty<string>()
        };
    }

    private async Task RunLoopAsync(Subscription sub, string[] urls)
    {
        var backoff = 1000;
        var urlIndex = 0;
        while (!sub.Cts.IsCancellationRequested)
        {
            var url = urls[urlIndex % urls.Length];
            try
            {
                using var ws = new ClientWebSocket();
                sub.Ws = ws;
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(sub.Cts.Token);
                connectCts.CancelAfter(TimeSpan.FromSeconds(8));
                await ws.ConnectAsync(new Uri(url), connectCts.Token);
                await SendSubscribeAsync(ws, sub);
                _log.LogInformation("WS connected {Key} via {Url}", sub.Key, url);
                backoff = 1000;

                var buffer = new byte[128 * 1024];
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
                    HandleMessage(sub, Encoding.UTF8.GetString(ms.ToArray()));
                }
            }
            catch (OperationCanceledException) when (sub.Cts.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                urlIndex++;
                var refused = ex is System.Net.WebSockets.WebSocketException
                    || ex.InnerException is System.Net.Http.HttpRequestException
                    || ex.InnerException is System.Net.Sockets.SocketException;
                if (refused)
                    _log.LogWarning("WS unreachable {Key} ({Url}), try next in {Ms}ms", sub.Key, url, backoff);
                else
                    _log.LogWarning(ex, "WS error {Key}, retry {Ms}ms", sub.Key, backoff);
                try { await Task.Delay(backoff, sub.Cts.Token); }
                catch (OperationCanceledException) { break; }
                backoff = Math.Min(backoff * 2, 15000);
            }
        }
    }

    private static async Task SendSubscribeAsync(ClientWebSocket ws, Subscription sub)
    {
        string? msg = sub.Exchange switch
        {
            ExchangeType.Bybit => JsonSerializer.Serialize(new
            {
                op = "subscribe",
                args = new[]
                {
                    $"orderbook.50.{sub.Symbol}",
                    $"publicTrade.{sub.Symbol}"
                }
            }),
            ExchangeType.Okx => JsonSerializer.Serialize(new
            {
                op = "subscribe",
                args = new object[]
                {
                    new { channel = "books5", instId = sub.Symbol },
                    new { channel = "trades", instId = sub.Symbol }
                }
            }),
            ExchangeType.Gate => null, // sent as two messages below
            _ => null // Binance uses URL streams
        };
        if (sub.Exchange == ExchangeType.Gate)
        {
            var contract = sub.Symbol; // BTC_USDT
            var channelBook = sub.Futures ? "futures.order_book" : "spot.order_book";
            var channelTrade = sub.Futures ? "futures.trades" : "spot.trades";
            var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            // order book: payload [contract, level, interval]
            var bookMsg = JsonSerializer.Serialize(new
            {
                time = t,
                channel = channelBook,
                @event = "subscribe",
                payload = sub.Futures
                    ? new object[] { contract, "20", "0" }
                    : new object[] { contract, "20", "100ms" }
            });
            var tradeMsg = JsonSerializer.Serialize(new
            {
                time = t,
                channel = channelTrade,
                @event = "subscribe",
                payload = new[] { contract }
            });
            await ws.SendAsync(Encoding.UTF8.GetBytes(bookMsg), WebSocketMessageType.Text, true, sub.Cts.Token);
            await ws.SendAsync(Encoding.UTF8.GetBytes(tradeMsg), WebSocketMessageType.Text, true, sub.Cts.Token);
            return;
        }
        if (msg == null) return;
        var bytes = Encoding.UTF8.GetBytes(msg);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, sub.Cts.Token);
    }

    private void HandleMessage(Subscription sub, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            switch (sub.Exchange)
            {
                case ExchangeType.Binance:
                    HandleBinance(sub, root);
                    break;
                case ExchangeType.Bybit:
                    HandleBybit(sub, root);
                    break;
                case ExchangeType.Okx:
                    HandleOkx(sub, root);
                    break;
                case ExchangeType.Gate:
                    HandleGate(sub, root);
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Parse fail {Key}", sub.Key);
        }
    }

    private void HandleBinance(Subscription sub, JsonElement root)
    {
        if (!root.TryGetProperty("stream", out var streamProp) || !root.TryGetProperty("data", out var data))
            return;
        var stream = streamProp.GetString() ?? "";
        if (stream.Contains("depth", StringComparison.Ordinal))
            RaiseBook(sub, ParseLevels(data, "bids"), ParseLevels(data, "asks"));
        else if (stream.Contains("aggTrade", StringComparison.Ordinal))
        {
            var price = Dec(data.GetProperty("p"));
            var qty = Dec(data.GetProperty("q"));
            var isBuy = !data.GetProperty("m").GetBoolean();
            RaiseTrade(sub, price, qty, isBuy);
        }
    }

    private void HandleBybit(Subscription sub, JsonElement root)
    {
        if (!root.TryGetProperty("topic", out var topicEl)) return;
        var topic = topicEl.GetString() ?? "";
        if (!root.TryGetProperty("data", out var data)) return;

        if (topic.StartsWith("orderbook", StringComparison.Ordinal))
        {
            // data can be object or array
            var book = data.ValueKind == JsonValueKind.Array ? data[0] : data;
            var bids = ParseBybitSides(book, "b");
            var asks = ParseBybitSides(book, "a");
            if (bids.Count > 0 || asks.Count > 0)
                RaiseBook(sub, bids, asks);
        }
        else if (topic.StartsWith("publicTrade", StringComparison.Ordinal))
        {
            foreach (var t in data.EnumerateArray())
            {
                var price = Dec(t.GetProperty("p"));
                var qty = Dec(t.GetProperty("v"));
                var isBuy = t.GetProperty("S").GetString() == "Buy";
                RaiseTrade(sub, price, qty, isBuy);
            }
        }
    }

    private void HandleOkx(Subscription sub, JsonElement root)
    {
        if (!root.TryGetProperty("arg", out var arg)) return;
        var channel = arg.TryGetProperty("channel", out var ch) ? ch.GetString() : "";
        if (!root.TryGetProperty("data", out var data) || data.GetArrayLength() == 0) return;
        var item = data[0];

        if (channel is "books5" or "books")
        {
            var bids = ParseOkxSides(item, "bids");
            var asks = ParseOkxSides(item, "asks");
            RaiseBook(sub, bids, asks);
        }
        else if (channel == "trades")
        {
            foreach (var t in data.EnumerateArray())
            {
                var price = Dec(t.GetProperty("px"));
                var qty = Dec(t.GetProperty("sz"));
                var isBuy = t.GetProperty("side").GetString() == "buy";
                RaiseTrade(sub, price, qty, isBuy);
            }
        }
    }

    private void RaiseBook(Subscription sub, List<OrderBookLevel> bids, List<OrderBookLevel> asks)
    {
        MarkWalls(bids);
        MarkWalls(asks);
        var snap = new OrderBookSnapshot
        {
            Symbol = sub.Symbol,
            Exchange = sub.Exchange,
            Timestamp = DateTime.UtcNow,
            Bids = bids,
            Asks = asks
        };
        OnOrderBook?.Invoke(sub.Symbol, snap);
        OnOrderBook?.Invoke(sub.Key, snap);
    }

    private void RaiseTrade(Subscription sub, decimal price, decimal qty, bool isBuy)
    {
        var tick = new TradeTick
        {
            Symbol = sub.Symbol,
            Exchange = sub.Exchange,
            Price = price,
            Quantity = qty,
            IsBuy = isBuy,
            IsLarge = qty * price >= 25000m,
            Timestamp = DateTime.UtcNow
        };
        OnTrade?.Invoke(sub.Symbol, tick);
        OnTrade?.Invoke(sub.Key, tick);
    }


    private void HandleGate(Subscription sub, JsonElement root)
    {
        // { channel, event, result }
        if (!root.TryGetProperty("channel", out var chEl)) return;
        var channel = chEl.GetString() ?? "";
        if (!root.TryGetProperty("result", out var result)) return;
        // ignore subscribe acks
        if (root.TryGetProperty("event", out var ev) && ev.GetString() == "subscribe") return;

        if (channel.Contains("order_book", StringComparison.Ordinal))
        {
            // result: { t, contract/currency_pair, bids:[[p,s]], asks:[[p,s]] } or array update
            var book = result.ValueKind == JsonValueKind.Array && result.GetArrayLength() > 0
                ? result[0]
                : result;
            var bids = ParseGateSides(book, "bids");
            var asks = ParseGateSides(book, "asks");
            if (bids.Count > 0 || asks.Count > 0)
                RaiseBook(sub, bids, asks);
        }
        else if (channel.Contains("trades", StringComparison.Ordinal))
        {
            // result is array of trades
            var trades = result.ValueKind == JsonValueKind.Array ? result : default;
            if (trades.ValueKind != JsonValueKind.Array) return;
            foreach (var t in trades.EnumerateArray())
            {
                var price = t.TryGetProperty("price", out var px) ? Dec(px) : 0;
                var qty = t.TryGetProperty("size", out var sz) ? Math.Abs(Dec(sz))
                    : t.TryGetProperty("amount", out var am) ? Math.Abs(Dec(am)) : 0;
                if (price == 0) continue;
                // Gate futures: size > 0 buy, < 0 sell; spot: side take / make
                bool isBuy;
                if (t.TryGetProperty("size", out var sizeEl))
                    isBuy = Dec(sizeEl) > 0;
                else if (t.TryGetProperty("side", out var sideEl))
                    isBuy = string.Equals(sideEl.GetString(), "buy", StringComparison.OrdinalIgnoreCase);
                else
                    isBuy = true;
                RaiseTrade(sub, price, qty, isBuy);
            }
        }
    }

    private static List<OrderBookLevel> ParseGateSides(JsonElement book, string name)
    {
        var list = new List<OrderBookLevel>();
        if (!book.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
        foreach (var x in arr.EnumerateArray())
        {
            if (x.ValueKind == JsonValueKind.Array && x.GetArrayLength() >= 2)
                list.Add(new OrderBookLevel { Price = Dec(x[0]), Quantity = Math.Abs(Dec(x[1])) });
        }
        return list;
    }

    private static List<OrderBookLevel> ParseLevels(JsonElement data, string name)
    {
        var list = new List<OrderBookLevel>();
        if (!data.TryGetProperty(name, out var arr)) return list;
        foreach (var x in arr.EnumerateArray())
        {
            list.Add(new OrderBookLevel { Price = Dec(x[0]), Quantity = Dec(x[1]) });
        }
        return list;
    }

    private static List<OrderBookLevel> ParseBybitSides(JsonElement book, string name)
    {
        var list = new List<OrderBookLevel>();
        if (!book.TryGetProperty(name, out var arr)) return list;
        foreach (var x in arr.EnumerateArray())
        {
            // [price, size]
            list.Add(new OrderBookLevel { Price = Dec(x[0]), Quantity = Dec(x[1]) });
        }
        return list;
    }

    private static List<OrderBookLevel> ParseOkxSides(JsonElement item, string name)
    {
        var list = new List<OrderBookLevel>();
        if (!item.TryGetProperty(name, out var arr)) return list;
        foreach (var x in arr.EnumerateArray())
        {
            list.Add(new OrderBookLevel { Price = Dec(x[0]), Quantity = Dec(x[1]) });
        }
        return list;
    }

    private static decimal Dec(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.String)
            return decimal.Parse(el.GetString()!, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture);
        if (el.ValueKind == JsonValueKind.Number)
            return el.GetDecimal();
        return 0;
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
        {
            if (_subs.TryRemove(key, out var sub))
            {
                sub.Cts.Cancel();
                sub.Ws?.Dispose();
            }
        }
        await Task.CompletedTask;
    }

    private sealed class Subscription
    {
        public string Key { get; }
        public ExchangeType Exchange { get; }
        public string Symbol { get; }
        public bool Futures { get; }
        public CancellationTokenSource Cts { get; } = new();
        public ClientWebSocket? Ws { get; set; }

        public Subscription(string key, ExchangeType exchange, string symbol, bool futures)
        {
            Key = key;
            Exchange = exchange;
            Symbol = symbol;
            Futures = futures;
        }
    }
}
