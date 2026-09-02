using HypeScalp.Core.Models;
using HypeScalp.Exchange.Common;

namespace HypeScalp.Exchange.Gate;

/// <summary>Gate.io client stub — API Key + Secret (HMAC-SHA512).</summary>
public class GateClient : BaseExchangeClient
{
    public GateClient(ExchangeConnection connection) : base(connection) { }

    public override Task ConnectAsync(CancellationToken ct = default)
    {
        // TODO: signed REST https://api.gateio.ws/api/v4
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
        Task.FromResult<IReadOnlyList<string>>(new[] { "BTC_USDT", "ETH_USDT" });
    public override Task<Order> PlaceOrderAsync(string symbol, OrderSide side, OrderType type, decimal quantity, decimal? price = null)
        => throw new NotImplementedException("Gate.io PlaceOrder — implement signed API v4");
    public override Task CancelAllOrdersAsync(string symbol) => Task.CompletedTask;
    public override Task CancelOrderAsync(string symbol, string orderId) => Task.CompletedTask;
    public override Task<IReadOnlyList<Order>> GetOpenOrdersAsync(string? symbol = null) =>
        Task.FromResult<IReadOnlyList<Order>>(Array.Empty<Order>());
    public override Task<IReadOnlyList<Position>> GetPositionsAsync() =>
        Task.FromResult<IReadOnlyList<Position>>(Array.Empty<Position>());
}
