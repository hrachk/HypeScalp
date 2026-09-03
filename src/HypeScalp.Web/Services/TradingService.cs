using System.Collections.Concurrent;
using HypeScalp.Core.Interfaces;
using HypeScalp.Core.Models;

namespace HypeScalp.Web.Services;

public class TradingService
{
    private readonly ConnectionManager _connections;
    private readonly ILogger<TradingService> _log;
    // Local cache of open orders for ladder flags (updated after place/cancel + poll)
    private readonly ConcurrentDictionary<string, Order> _openOrders = new(StringComparer.OrdinalIgnoreCase);

    public TradingService(ConnectionManager connections, ILogger<TradingService> log)
    {
        _connections = connections;
        _log = log;
    }

    public event Action? OnOrdersChanged;
    public event Action? OnPositionsChanged;

    public IReadOnlyList<object> ListConnections() =>
        _connections.Connections.Select(c => new
        {
            c.Id,
            c.Name,
            exchange = c.Exchange.ToString(),
            market = c.Market.ToString(),
            status = c.Status.ToString(),
            connected = c.Status == ConnectionStatus.Connected
        }).ToList<object>();

    public IExchangeClient? ResolveClient(ExchangeType? preferred = null)
    {
        var list = _connections.Connections
            .Where(c => c.Status == ConnectionStatus.Connected)
            .Select(c => _connections.GetClient(c.Id))
            .Where(c => c is { IsConnected: true })
            .Cast<IExchangeClient>()
            .ToList();

        if (preferred != null)
        {
            var match = list.FirstOrDefault(c => c.Exchange == preferred);
            if (match != null) return match;
        }
        return list.FirstOrDefault();
    }

    public static bool TryParseExchange(string? name, out ExchangeType ex)
    {
        ex = ExchangeType.Binance;
        if (string.IsNullOrWhiteSpace(name)) return false;
        return Enum.TryParse(name.Trim(), true, out ex);
    }

    public async Task<TradeResult> PlaceMarketAsync(string symbol, OrderSide side, decimal quantity, ExchangeType? exchange = null)
    {
        var client = ResolveClient(exchange);
        if (client == null)
            return TradeResult.Fail("NO_CONNECTION", "No connected exchange. Open /settings and Connect API keys.");
        if (quantity <= 0)
            return TradeResult.Fail("BAD_QTY", "Quantity must be > 0");

        try
        {
            var order = await client.PlaceOrderAsync(NormalizeSymbol(client.Exchange, symbol), side, OrderType.Market, quantity);
            _log.LogInformation("MARKET {Side} {Qty} {Symbol} via {Ex} -> {Id}", side, quantity, symbol, client.Exchange, order.OrderId);
            OnPositionsChanged?.Invoke();
            return TradeResult.Success($"MARKET {side} {quantity} {symbol}", order, client.Exchange);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Market order failed");
            return TradeResult.Fail("EXCHANGE", ex.Message);
        }
    }

    public async Task<TradeResult> PlaceLimitAsync(string symbol, OrderSide side, decimal quantity, decimal price, ExchangeType? exchange = null)
    {
        var client = ResolveClient(exchange);
        if (client == null)
            return TradeResult.Fail("NO_CONNECTION", "No connected exchange. Open /settings and Connect API keys.");
        if (quantity <= 0 || price <= 0)
            return TradeResult.Fail("BAD_ARGS", "Quantity and price must be > 0");

        try
        {
            var order = await client.PlaceOrderAsync(NormalizeSymbol(client.Exchange, symbol), side, OrderType.Limit, quantity, price);
            CacheOrder(order);
            _log.LogInformation("LIMIT {Side} {Qty}@{Price} {Symbol} via {Ex} -> {Id}", side, quantity, price, symbol, client.Exchange, order.OrderId);
            OnOrdersChanged?.Invoke();
            return TradeResult.Success($"LIMIT {side} {quantity} @ {price}", order, client.Exchange);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Limit order failed");
            return TradeResult.Fail("EXCHANGE", ex.Message);
        }
    }

    public async Task<TradeResult> CancelAllAsync(string symbol, ExchangeType? exchange = null)
    {
        var client = ResolveClient(exchange);
        if (client == null)
            return TradeResult.Fail("NO_CONNECTION", "No connected exchange.");

        try
        {
            var sym = NormalizeSymbol(client.Exchange, symbol);
            await client.CancelAllOrdersAsync(sym);
            foreach (var key in _openOrders.Keys.Where(k => k.Contains(sym, StringComparison.OrdinalIgnoreCase)).ToList())
                _openOrders.TryRemove(key, out _);
            OnOrdersChanged?.Invoke();
            _log.LogInformation("CANCEL ALL {Symbol} via {Ex}", symbol, client.Exchange);
            return TradeResult.Success($"CANCEL ALL {symbol}", null, client.Exchange);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Cancel all failed");
            return TradeResult.Fail("EXCHANGE", ex.Message);
        }
    }

    public async Task<TradeResult> CancelOrderAsync(string symbol, string orderId, ExchangeType? exchange = null)
    {
        var client = ResolveClient(exchange);
        if (client == null)
            return TradeResult.Fail("NO_CONNECTION", "No connected exchange.");
        try
        {
            var sym = NormalizeSymbol(client.Exchange, symbol);
            await client.CancelOrderAsync(sym, orderId);
            _openOrders.TryRemove(OrderKey(client.Exchange, sym, orderId), out _);
            OnOrdersChanged?.Invoke();
            return TradeResult.Success($"CANCEL {orderId}", null, client.Exchange);
        }
        catch (Exception ex)
        {
            return TradeResult.Fail("EXCHANGE", ex.Message);
        }
    }


    /// <summary>STOP_MARKET or TAKE_PROFIT_MARKET (Binance futures).</summary>
    public async Task<TradeResult> PlaceStopAsync(string symbol, OrderSide side, decimal quantity, decimal stopPrice, bool takeProfit = false, ExchangeType? exchange = null)
    {
        var client = ResolveClient(exchange);
        if (client == null)
            return TradeResult.Fail("NO_CONNECTION", "No connected exchange.");
        if (quantity <= 0 || stopPrice <= 0)
            return TradeResult.Fail("BAD_ARGS", "Quantity and stopPrice must be > 0");
        try
        {
            // Use limit as fallback for non-Binance; BinanceClient handles via PlaceOrderAsync with special type later
            Order order;
            if (client is HypeScalp.Exchange.Binance.BinanceClient bin)
                order = await bin.PlaceStopOrderAsync(NormalizeSymbol(client.Exchange, symbol), side, quantity, stopPrice, takeProfit);
            else
                order = await client.PlaceOrderAsync(NormalizeSymbol(client.Exchange, symbol), side, OrderType.Limit, quantity, stopPrice);
            _log.LogInformation("STOP/TP {Side} {Qty} @ {Price} TP={Tp}", side, quantity, stopPrice, takeProfit);
            OnOrdersChanged?.Invoke();
            return TradeResult.Success($"{(takeProfit ? "TP" : "SL")} {side} {quantity} @ {stopPrice}", order, client.Exchange);
        }
        catch (Exception ex)
        {
            return TradeResult.Fail("EXCHANGE", ex.Message);
        }
    }

    public async Task<TradeResult> FlattenAsync(string symbol, ExchangeType? exchange = null)
    {
        var client = ResolveClient(exchange);
        if (client == null)
            return TradeResult.Fail("NO_CONNECTION", "No connected exchange.");

        try
        {
            var positions = await client.GetPositionsAsync();
            var sym = NormalizeSymbol(client.Exchange, symbol);
            var pos = positions.FirstOrDefault(p => NormSym(p.Symbol) == NormSym(sym));
            if (pos == null || pos.Size == 0)
                return TradeResult.Success($"No open position on {symbol}", null, client.Exchange);

            var side = pos.Size > 0 ? OrderSide.Sell : OrderSide.Buy;
            var qty = Math.Abs(pos.Size);
            var order = await client.PlaceOrderAsync(pos.Symbol, side, OrderType.Market, qty);
            OnPositionsChanged?.Invoke();
            return TradeResult.Success($"FLATTEN {side} {qty} {symbol}", order, client.Exchange);
        }
        catch (Exception ex)
        {
            return TradeResult.Fail("EXCHANGE", ex.Message);
        }
    }

    public async Task<IReadOnlyList<Position>> GetPositionsAsync(ExchangeType? exchange = null)
    {
        var client = ResolveClient(exchange);
        if (client == null) return Array.Empty<Position>();
        try { return await client.GetPositionsAsync(); }
        catch { return Array.Empty<Position>(); }
    }

    public async Task<IReadOnlyList<Order>> GetOpenOrdersAsync(string? symbol = null, ExchangeType? exchange = null)
    {
        var client = ResolveClient(exchange);
        if (client == null)
            return _openOrders.Values.Where(o => symbol == null || NormSym(o.Symbol) == NormSym(symbol)).ToList();

        try
        {
            var sym = symbol == null ? null : NormalizeSymbol(client.Exchange, symbol);
            var remote = await client.GetOpenOrdersAsync(sym);
            // Refresh cache for this symbol
            if (sym != null)
            {
                foreach (var k in _openOrders.Keys.Where(k => k.Contains(sym, StringComparison.OrdinalIgnoreCase)).ToList())
                    _openOrders.TryRemove(k, out _);
            }
            foreach (var o in remote) CacheOrder(o);
            return remote;
        }
        catch
        {
            return _openOrders.Values.Where(o => symbol == null || NormSym(o.Symbol) == NormSym(symbol)).ToList();
        }
    }

    private void CacheOrder(Order o) =>
        _openOrders[OrderKey(o.Exchange, o.Symbol, o.OrderId)] = o;

    private static string OrderKey(ExchangeType ex, string symbol, string id) => $"{ex}:{symbol}:{id}";
    private static string NormSym(string s) => s.Replace("-", "").Replace("_", "").ToUpperInvariant();

    private static string NormalizeSymbol(ExchangeType ex, string symbol)
    {
        symbol = symbol.Trim().ToUpperInvariant();
        return ex switch
        {
            ExchangeType.Okx => symbol.Contains('-') ? symbol : symbol.Replace("USDT", "-USDT"),
            ExchangeType.Gate => symbol.Contains('_') ? symbol : symbol.Replace("USDT", "_USDT"),
            _ => symbol.Replace("-", "").Replace("_", "")
        };
    }
}

public sealed class TradeResult
{
    public bool IsOk { get; init; }
    public string Code { get; init; } = "";
    public string Message { get; init; } = "";
    public string? OrderId { get; init; }
    public string? Exchange { get; init; }

    /// <summary>JSON-friendly alias used by terminal UI (data.ok).</summary>
    public bool Ok => IsOk;

    public static TradeResult Success(string message, Order? order, ExchangeType? ex = null) => new()
    {
        IsOk = true,
        Code = "OK",
        Message = message,
        OrderId = order?.OrderId,
        Exchange = ex?.ToString()
    };

    public static TradeResult Fail(string code, string message) => new()
    {
        IsOk = false,
        Code = code,
        Message = message
    };
}
