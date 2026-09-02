using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using HypeScalp.Core.Models;
using HypeScalp.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace HypeScalp.Web.Services;

/// <summary>
/// MetaScalp-style account feed: Binance User Data Stream (listenKey), not REST polling.
/// - POST /fapi/v1/listenKey once on connect
/// - WS pushes ORDER_TRADE_UPDATE + ACCOUNT_UPDATE
/// - PUT keepalive every 30 minutes
/// - REST only for initial snapshot and rare force refresh
/// </summary>
public class UserDataStreamService : IAsyncDisposable
{
    private readonly ConnectionManager _connections;
    private readonly AccountDataCache _cache;
    private readonly IHubContext<MarketStreamHub> _hub;
    private readonly ILogger<UserDataStreamService> _log;
    private readonly ConcurrentDictionary<Guid, StreamSession> _sessions = new();

    // Live state from WS (source of truth after snapshot)
    private readonly ConcurrentDictionary<string, Order> _orders = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Position> _positions = new(StringComparer.OrdinalIgnoreCase);

    public UserDataStreamService(
        ConnectionManager connections,
        AccountDataCache cache,
        IHubContext<MarketStreamHub> hub,
        ILogger<UserDataStreamService> log)
    {
        _connections = connections;
        _cache = cache;
        _hub = hub;
        _log = log;
        _connections.OnChanged += OnConnectionsChanged;
    }

    public IReadOnlyList<Order> SnapshotOrders() => _orders.Values.ToList();
    public IReadOnlyList<Position> SnapshotPositions() => _positions.Values.Where(p => p.Size != 0).ToList();

    private void OnConnectionsChanged()
    {
        _ = SyncSessionsAsync();
    }

    public async Task SyncSessionsAsync()
    {
        var connected = _connections.Connections
            .Where(c => c.Status == ConnectionStatus.Connected && c.Exchange == ExchangeType.Binance)
            .ToList();

        foreach (var c in connected)
        {
            if (_sessions.ContainsKey(c.Id)) continue;
            try
            {
                await StartSessionAsync(c);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to start user stream for {Name}", c.Name);
            }
        }

        foreach (var id in _sessions.Keys.ToList())
        {
            if (connected.All(c => c.Id != id))
                await StopSessionAsync(id);
        }
    }

    private async Task StartSessionAsync(ExchangeConnection conn)
    {
        var http = CreateHttp(conn);
        var listenKey = await CreateListenKeyAsync(http, conn);
        if (string.IsNullOrEmpty(listenKey))
            throw new InvalidOperationException("Empty listenKey");

        var session = new StreamSession(conn.Id, conn, listenKey, http);
        if (!_sessions.TryAdd(conn.Id, session))
        {
            http.Dispose();
            return;
        }

        // One REST snapshot seed (MetaScalp also hydrates once, then WS)
        await SeedFromRestAsync(conn);

        session.LoopTask = Task.Run(() => RunLoopAsync(session));
        session.KeepaliveTask = Task.Run(() => KeepaliveLoopAsync(session));
        _log.LogInformation("UserDataStream started for {Name}", conn.Name);
        await _hub.Clients.All.SendAsync("accountStatus", new { mode = "user_stream", exchange = "Binance", ready = true });
    }

    private async Task StopSessionAsync(Guid id)
    {
        if (!_sessions.TryRemove(id, out var session)) return;
        session.Cts.Cancel();
        try
        {
            if (session.Ws != null)
                await session.Ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "stop", CancellationToken.None);
        }
        catch { /* ignore */ }
        session.Http.Dispose();
        _log.LogInformation("UserDataStream stopped {Id}", id);
    }

    private async Task SeedFromRestAsync(ExchangeConnection conn)
    {
        try
        {
            // Force one REST pull into cache, then copy into stream state
            var positions = await _cache.GetPositionsAsync(force: true, exchange: ExchangeType.Binance);
            var orders = await _cache.GetOpenOrdersAsync(force: true, exchange: ExchangeType.Binance);
            foreach (var p in positions)
                _positions[PosKey(p)] = p;
            foreach (var o in orders)
                _orders[OrderKey(o)] = o;
            await PublishStateAsync();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Initial REST seed failed");
        }
    }

    private async Task RunLoopAsync(StreamSession session)
    {
        var backoff = 1000;
        while (!session.Cts.IsCancellationRequested)
        {
            try
            {
                // Prefer private route; fallback to classic /ws/listenKey
                var urls = new[]
                {
                    $"wss://fstream.binance.com/private/ws/{session.ListenKey}",
                    $"wss://fstream.binance.com/ws/{session.ListenKey}"
                };

                Exception? last = null;
                foreach (var url in urls)
                {
                    try
                    {
                        using var ws = new ClientWebSocket();
                        session.Ws = ws;
                        using var cts = CancellationTokenSource.CreateLinkedTokenSource(session.Cts.Token);
                        cts.CancelAfter(TimeSpan.FromSeconds(12));
                        await ws.ConnectAsync(new Uri(url), cts.Token);
                        _log.LogInformation("User stream WS connected {Url}", url);
                        backoff = 1000;
                        await ReadLoopAsync(session, ws);
                        last = null;
                        break;
                    }
                    catch (Exception ex)
                    {
                        last = ex;
                        _log.LogDebug(ex, "User stream connect fail {Url}", url);
                    }
                }
                if (last != null) throw last;
            }
            catch (OperationCanceledException) when (session.Cts.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _log.LogWarning("User stream error, retry {Ms}ms: {Msg}", backoff, ex.Message);
                // Refresh listenKey on hard failures
                try
                {
                    session.ListenKey = await CreateListenKeyAsync(session.Http, session.Conn) ?? session.ListenKey;
                }
                catch { /* ignore */ }
                await Task.Delay(backoff, session.Cts.Token);
                backoff = Math.Min(backoff * 2, 30000);
            }
        }
    }

    private async Task ReadLoopAsync(StreamSession session, ClientWebSocket ws)
    {
        var buffer = new byte[128 * 1024];
        while (ws.State == WebSocketState.Open && !session.Cts.IsCancellationRequested)
        {
            using var ms = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(buffer, session.Cts.Token);
                if (result.MessageType == WebSocketMessageType.Close) return;
                ms.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            HandleMessage(session, Encoding.UTF8.GetString(ms.ToArray()));
        }
    }

    private void HandleMessage(StreamSession session, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var eventType = root.TryGetProperty("e", out var e) ? e.GetString() : null;
            if (eventType == null) return;

            if (eventType is "ORDER_TRADE_UPDATE" or "executionReport")
            {
                ApplyOrderUpdate(root);
                _ = PublishOrdersAsync();
            }
            else if (eventType is "ACCOUNT_UPDATE" or "outboundAccountPosition")
            {
                ApplyAccountUpdate(root);
                _ = PublishPositionsAsync();
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "User stream parse fail");
        }
    }

    private void ApplyOrderUpdate(JsonElement root)
    {
        // Futures: { e, o: { s, c, S, o, q, p, X, i, ... } }
        var o = root.TryGetProperty("o", out var ord) ? ord : root;
        var symbol = o.TryGetProperty("s", out var s) ? s.GetString()! : "";
        var orderId = o.TryGetProperty("i", out var i)
            ? i.GetRawText()
            : (o.TryGetProperty("orderId", out var oid) ? oid.GetRawText() : "");
        var status = o.TryGetProperty("X", out var st) ? st.GetString() :
                     (o.TryGetProperty("x", out var x) ? x.GetString() : "");
        var sideStr = o.TryGetProperty("S", out var side) ? side.GetString() : "BUY";
        var price = Dec(o, "p");
        var qty = Dec(o, "q");

        var key = $"{ExchangeType.Binance}:{symbol}:{orderId}";
        if (status is "NEW" or "PARTIALLY_FILLED")
        {
            _orders[key] = new Order
            {
                OrderId = orderId,
                Symbol = symbol,
                Exchange = ExchangeType.Binance,
                Side = sideStr == "SELL" ? OrderSide.Sell : OrderSide.Buy,
                Type = OrderType.Limit,
                Price = price,
                Quantity = qty,
                Status = OrderStatus.New
            };
        }
        else if (status is "CANCELED" or "FILLED" or "EXPIRED" or "REJECTED")
        {
            _orders.TryRemove(key, out _);
        }

        _ = _hub.Clients.All.SendAsync("orderUpdate", new
        {
            exchange = "Binance",
            symbol,
            orderId,
            status,
            side = sideStr,
            price,
            qty
        });
    }

    private void ApplyAccountUpdate(JsonElement root)
    {
        // Futures ACCOUNT_UPDATE: a.P positions array
        if (!root.TryGetProperty("a", out var a)) return;
        if (a.TryGetProperty("P", out var positions))
        {
            foreach (var p in positions.EnumerateArray())
            {
                var symbol = p.GetProperty("s").GetString()!;
                var size = Dec(p, "pa");
                var entry = Dec(p, "ep");
                var upnl = Dec(p, "up");
                var key = $"Binance:{symbol}";
                if (size == 0)
                    _positions.TryRemove(key, out _);
                else
                {
                    _positions[key] = new Position
                    {
                        Symbol = symbol,
                        Exchange = ExchangeType.Binance,
                        Size = size,
                        EntryPrice = entry,
                        MarkPrice = 0,
                        UnrealizedPnl = upnl
                    };
                }
            }
        }

        _ = _hub.Clients.All.SendAsync("positionUpdate", new
        {
            exchange = "Binance",
            positions = SnapshotPositions()
        });
    }

    private async Task PublishStateAsync()
    {
        await PublishOrdersAsync();
        await PublishPositionsAsync();
    }

    private Task PublishOrdersAsync() =>
        _hub.Clients.All.SendAsync("ordersSnapshot", SnapshotOrders());

    private Task PublishPositionsAsync() =>
        _hub.Clients.All.SendAsync("positionsSnapshot", SnapshotPositions());

    private async Task KeepaliveLoopAsync(StreamSession session)
    {
        while (!session.Cts.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(30), session.Cts.Token);
                var path = session.Conn.Market == MarketType.Spot
                    ? "/api/v3/userDataStream"
                    : "/fapi/v1/listenKey";
                // Futures keepalive is PUT /fapi/v1/listenKey
                var req = new HttpRequestMessage(HttpMethod.Put, path);
                var resp = await session.Http.SendAsync(req, session.Cts.Token);
                if (!resp.IsSuccessStatusCode)
                {
                    _log.LogWarning("listenKey keepalive failed {Status}", resp.StatusCode);
                    session.ListenKey = await CreateListenKeyAsync(session.Http, session.Conn) ?? session.ListenKey;
                }
                else
                    _log.LogDebug("listenKey keepalive ok");
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "keepalive error");
            }
        }
    }

    private static async Task<string?> CreateListenKeyAsync(HttpClient http, ExchangeConnection conn)
    {
        var path = conn.Market == MarketType.Spot ? "/api/v3/userDataStream" : "/fapi/v1/listenKey";
        var resp = await http.PostAsync(path, null);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"listenKey failed: {resp.StatusCode} {body}");
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("listenKey").GetString();
    }

    private static HttpClient CreateHttp(ExchangeConnection conn)
    {
        var futures = conn.Market != MarketType.Spot;
        var baseUrl = conn.IsTestnet
            ? (futures ? "https://testnet.binancefuture.com" : "https://testnet.binance.vision")
            : (futures ? "https://fapi.binance.com" : "https://api.binance.com");
        var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        http.DefaultRequestHeaders.Add("X-MBX-APIKEY", conn.ApiKey);
        return http;
    }

    private static string OrderKey(Order o) => $"{o.Exchange}:{o.Symbol}:{o.OrderId}";
    private static string PosKey(Position p) => $"{p.Exchange}:{p.Symbol}";

    private static decimal Dec(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p)) return 0;
        if (p.ValueKind == JsonValueKind.String)
            return decimal.Parse(p.GetString()!, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture);
        if (p.ValueKind == JsonValueKind.Number) return p.GetDecimal();
        return 0;
    }

    public async ValueTask DisposeAsync()
    {
        _connections.OnChanged -= OnConnectionsChanged;
        foreach (var id in _sessions.Keys.ToList())
            await StopSessionAsync(id);
    }

    private sealed class StreamSession
    {
        public Guid Id { get; }
        public ExchangeConnection Conn { get; }
        public string ListenKey { get; set; }
        public HttpClient Http { get; }
        public CancellationTokenSource Cts { get; } = new();
        public ClientWebSocket? Ws { get; set; }
        public Task? LoopTask { get; set; }
        public Task? KeepaliveTask { get; set; }

        public StreamSession(Guid id, ExchangeConnection conn, string listenKey, HttpClient http)
        {
            Id = id;
            Conn = conn;
            ListenKey = listenKey;
            Http = http;
        }
    }
}
