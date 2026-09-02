using HypeScalp.Core.Interfaces;
using HypeScalp.Core.Models;

namespace HypeScalp.Web.Services;

/// <summary>
/// Fast-path trading: market/limit/cancel/flatten through connected exchange clients.
/// </summary>
public class TradingService
{
    private readonly ConnectionManager _connections;
    private readonly ILogger<TradingService> _log;

    public TradingService(ConnectionManager connections, ILogger<TradingService> log)
    {
        _connections = connections;
        _log = log;
    }

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
            return TradeResult.Ok($"MARKET {side} {quantity} {symbol}", order, client.Exchange);
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
            _log.LogInformation("LIMIT {Side} {Qty}@{Price} {Symbol} via {Ex} -> {Id}", side, quantity, price, symbol, client.Exchange, order.OrderId);
            return TradeResult.Ok($"LIMIT {side} {quantity} @ {price}", order, client.Exchange);
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
            await client.CancelAllOrdersAsync(NormalizeSymbol(client.Exchange, symbol));
            _log.LogInformation("CANCEL ALL {Symbol} via {Ex}", symbol, client.Exchange);
            return TradeResult.Ok($"CANCEL ALL {symbol}", null, client.Exchange);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Cancel all failed");
            return TradeResult.Fail("EXCHANGE", ex.Message);
        }
    }

    /// <summary>Close position with reduce-only market (best-effort: market opposite side for full size).</summary>
    public async Task<TradeResult> FlattenAsync(string symbol, ExchangeType? exchange = null)
    {
        var client = ResolveClient(exchange);
        if (client == null)
            return TradeResult.Fail("NO_CONNECTION", "No connected exchange.");

        try
        {
            var positions = await client.GetPositionsAsync();
            var sym = NormalizeSymbol(client.Exchange, symbol);
            var pos = positions.FirstOrDefault(p =>
                p.Symbol.Equals(sym, StringComparison.OrdinalIgnoreCase) ||
                p.Symbol.Replace("_", "").Replace("-", "")
                    .Equals(sym.Replace("_", "").Replace("-", ""), StringComparison.OrdinalIgnoreCase));

            if (pos == null || pos.Size == 0)
                return TradeResult.Ok($"No open position on {symbol}", null, client.Exchange);

            var side = pos.Size > 0 ? OrderSide.Sell : OrderSide.Buy;
            var qty = Math.Abs(pos.Size);
            var order = await client.PlaceOrderAsync(pos.Symbol, side, OrderType.Market, qty);
            _log.LogInformation("FLATTEN {Symbol} size={Size} via {Ex}", symbol, pos.Size, client.Exchange);
            return TradeResult.Ok($"FLATTEN {side} {qty} {symbol}", order, client.Exchange);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Flatten failed");
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
    public bool Ok { get; init; }
    public string Code { get; init; } = "";
    public string Message { get; init; } = "";
    public string? OrderId { get; init; }
    public string? Exchange { get; init; }

    public static TradeResult Ok(string message, Order? order, ExchangeType? ex = null) => new()
    {
        Ok = true,
        Code = "OK",
        Message = message,
        OrderId = order?.OrderId,
        Exchange = ex?.ToString()
    };

    public static TradeResult Fail(string code, string message) => new()
    {
        Ok = false,
        Code = code,
        Message = message
    };
}
