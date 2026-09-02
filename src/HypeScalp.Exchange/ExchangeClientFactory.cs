using HypeScalp.Core.Interfaces;
using HypeScalp.Core.Models;
using HypeScalp.Exchange.Binance;
using HypeScalp.Exchange.Bybit;
using HypeScalp.Exchange.Gate;
using HypeScalp.Exchange.Okx;

namespace HypeScalp.Exchange;

public class ExchangeClientFactory : IExchangeClientFactory
{
    public IExchangeClient Create(ExchangeConnection connection) => connection.Exchange switch
    {
        ExchangeType.Binance => new BinanceClient(connection),
        ExchangeType.Bybit   => new BybitClient(connection),
        ExchangeType.Okx     => new OkxClient(connection),
        ExchangeType.Gate    => new GateClient(connection),
        _ => throw new NotSupportedException($"{connection.Exchange} not implemented yet")
    };
}
