using HypeScalp.Core.Models;

namespace HypeScalp.Core.Interfaces;

public interface IExchangeClientFactory
{
    IExchangeClient Create(ExchangeConnection connection);
}
