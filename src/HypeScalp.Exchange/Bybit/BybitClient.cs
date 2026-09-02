using HypeScalp.Core.Models;
using HypeScalp.Exchange.Common;

namespace HypeScalp.Exchange.Bybit;

public class BybitClient : BaseExchangeClient
{
    public BybitClient(ExchangeConnection connection) : base(connection) { }

    public override Task ConnectAsync(CancellationToken ct = default)
    {
        _isConnected = true;
        Connection.Status = ConnectionStatus.Connected;
        Connection.LastConnectedAt = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    public override Task DisconnectAsync()
    {
        _isConnected = false;
        Connection.Status = ConnectionStatus.Disconnected;
        return Task.CompletedTask;
    }

    public override Task SubscribeOrderBookAsync(string symbol, int depth = 20) => Task.CompletedTask;
    public override Task<IReadOnlyList<string>> GetSymbolsAsync() =>
        Task.FromResult<IReadOnlyList<string>>(new[] { "BTCUSDT", "ETHUSDT", "SOLUSDT" });
    public override Task<Order> PlaceOrderAsync(string symbol, OrderSide side, OrderType type, decimal quantity, decimal? price = null)
        => throw new NotImplementedException("Bybit V5 — implement signed request");
    public override Task CancelAllOrdersAsync(string symbol) => Task.CompletedTask;
    public override Task<IReadOnlyList<Position>> GetPositionsAsync() =>
        Task.FromResult<IReadOnlyList<Position>>(Array.Empty<Position>());
}
