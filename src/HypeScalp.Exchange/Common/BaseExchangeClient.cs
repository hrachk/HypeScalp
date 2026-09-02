using HypeScalp.Core.Interfaces;
using HypeScalp.Core.Models;

namespace HypeScalp.Exchange.Common;

public abstract class BaseExchangeClient : IExchangeClient
{
    protected readonly ExchangeConnection Connection;
    protected bool _isConnected;

    public ExchangeType Exchange => Connection.Exchange;
    public Guid ConnectionId => Connection.Id;
    public bool IsConnected => _isConnected;

    public event Action<OrderBookSnapshot>? OnOrderBookUpdate;
    public event Action<TradeTick>? OnTrade;
    public event Action<string>? OnError;

    protected BaseExchangeClient(ExchangeConnection connection) => Connection = connection;

    public abstract Task ConnectAsync(CancellationToken ct = default);
    public abstract Task DisconnectAsync();
    public abstract Task SubscribeOrderBookAsync(string symbol, int depth = 20);
    public abstract Task<IReadOnlyList<string>> GetSymbolsAsync();
    public abstract Task<Order> PlaceOrderAsync(string symbol, OrderSide side, OrderType type, decimal quantity, decimal? price = null);
    public abstract Task CancelAllOrdersAsync(string symbol);
    public abstract Task CancelOrderAsync(string symbol, string orderId);
    public abstract Task<IReadOnlyList<Order>> GetOpenOrdersAsync(string? symbol = null);
    public abstract Task<IReadOnlyList<Position>> GetPositionsAsync();

    protected void RaiseOrderBook(OrderBookSnapshot s) => OnOrderBookUpdate?.Invoke(s);
    protected void RaiseTrade(TradeTick t) => OnTrade?.Invoke(t);
    protected void RaiseError(string m) => OnError?.Invoke(m);

    public virtual async ValueTask DisposeAsync() => await DisconnectAsync();
}
