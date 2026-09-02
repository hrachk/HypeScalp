using HypeScalp.Core.Interfaces;
using HypeScalp.Core.Models;

namespace HypeScalp.Web.Services;

/// <summary>
/// Places orders through the first connected client matching exchange (or any connected).
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

    public IExchangeClient? ResolveClient(ExchangeType? preferred = null)
    {
        var list = _connections.Connections
            .Where(c => c.Status == ConnectionStatus.Connected)
            .Select(c => _connections.GetClient(c.Id))
            .Where(c => c != null && c.IsConnected)
            .Cast<IExchangeClient>()
            .ToList();

        if (preferred != null)
        {
            var match = list.FirstOrDefault(c => c.Exchange == preferred);
            if (match != null) return match;
        }
        return list.FirstOrDefault();
    }

    public async Task<(bool Ok, string Message, Order? Order)> PlaceMarketAsync(
        string symbol, OrderSide side, decimal quantity, ExchangeType? exchange = null)
    {
        var client = ResolveClient(exchange);
        if (client == null)
            return (false, "No connected exchange. Add API keys in Connections.", null);

        try
        {
            var order = await client.PlaceOrderAsync(symbol, side, OrderType.Market, quantity);
            _log.LogInformation("Order placed {Side} {Qty} {Symbol} via {Ex}", side, quantity, symbol, client.Exchange);
            return (true, $"OK {side} {quantity} {symbol} @ {client.Exchange}", order);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Place order failed");
            return (false, ex.Message, null);
        }
    }

    public async Task<(bool Ok, string Message, Order? Order)> PlaceLimitAsync(
        string symbol, OrderSide side, decimal quantity, decimal price, ExchangeType? exchange = null)
    {
        var client = ResolveClient(exchange);
        if (client == null)
            return (false, "No connected exchange. Add API keys in Connections.", null);

        try
        {
            var order = await client.PlaceOrderAsync(symbol, side, OrderType.Limit, quantity, price);
            return (true, $"OK {side} limit {quantity} @ {price} {symbol}", order);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, null);
        }
    }
}
