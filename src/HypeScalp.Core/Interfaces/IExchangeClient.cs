using HypeScalp.Core.Models;

namespace HypeScalp.Core.Interfaces;

public interface IExchangeClient : IAsyncDisposable
{
    ExchangeType Exchange { get; }
    Guid ConnectionId { get; }
    bool IsConnected { get; }

    event Action<OrderBookSnapshot>? OnOrderBookUpdate;
    event Action<TradeTick>? OnTrade;
    event Action<string>? OnError;

    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();
    Task SubscribeOrderBookAsync(string symbol, int depth = 20);
    Task<IReadOnlyList<string>> GetSymbolsAsync();
    Task<Order> PlaceOrderAsync(string symbol, OrderSide side, OrderType type, decimal quantity, decimal? price = null);
    Task CancelAllOrdersAsync(string symbol);
    Task<IReadOnlyList<Position>> GetPositionsAsync();
}

public interface IExchangeClientFactory
{
    IExchangeClient Create(ExchangeConnection connection);
}
