using HypeScalp.Core.Models;

namespace HypeScalp.Web.Services;

/// <summary>
/// Throttles REST calls to the exchange for positions/open orders.
/// Public market WebSockets are unlimited for practical purposes;
/// signed REST (positionRisk weight 5, openOrders weight 1–40) must be paced.
/// </summary>
public class AccountDataCache
{
    private readonly TradingService _trading;
    private readonly ILogger<AccountDataCache> _log;
    private readonly object _lock = new();

    private IReadOnlyList<Position> _positions = Array.Empty<Position>();
    private IReadOnlyList<Order> _orders = Array.Empty<Order>();
    private DateTime _posAt = DateTime.MinValue;
    private DateTime _ordAt = DateTime.MinValue;
    private string? _ordSymbol;

    /// <summary>Min interval between exchange REST hits (positions).</summary>
    public TimeSpan PositionsTtl { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Min interval between exchange REST hits (open orders).</summary>
    public TimeSpan OrdersTtl { get; set; } = TimeSpan.FromSeconds(5);

    public AccountDataCache(TradingService trading, ILogger<AccountDataCache> log)
    {
        _trading = trading;
        _log = log;
    }

    public async Task<IReadOnlyList<Position>> GetPositionsAsync(bool force = false, ExchangeType? exchange = null)
    {
        lock (_lock)
        {
            if (!force && DateTime.UtcNow - _posAt < PositionsTtl)
                return _positions;
        }

        try
        {
            var list = await _trading.GetPositionsAsync(exchange);
            lock (_lock)
            {
                _positions = list;
                _posAt = DateTime.UtcNow;
            }
            return list;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "positions fetch failed — serving cache");
            lock (_lock) return _positions;
        }
    }

    public async Task<IReadOnlyList<Order>> GetOpenOrdersAsync(string? symbol = null, bool force = false, ExchangeType? exchange = null)
    {
        lock (_lock)
        {
            var sameSym = string.Equals(_ordSymbol, symbol, StringComparison.OrdinalIgnoreCase);
            if (!force && sameSym && DateTime.UtcNow - _ordAt < OrdersTtl)
                return _orders;
        }

        try
        {
            var list = await _trading.GetOpenOrdersAsync(symbol, exchange);
            lock (_lock)
            {
                _orders = list;
                _ordAt = DateTime.UtcNow;
                _ordSymbol = symbol;
            }
            return list;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "orders fetch failed — serving cache");
            lock (_lock) return _orders;
        }
    }

    public void Invalidate()
    {
        lock (_lock)
        {
            _posAt = DateTime.MinValue;
            _ordAt = DateTime.MinValue;
        }
    }
}
