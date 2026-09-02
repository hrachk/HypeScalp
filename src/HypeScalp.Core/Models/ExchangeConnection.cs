namespace HypeScalp.Core.Models;

public enum ExchangeType { Binance, Bybit, Okx, Bitget, Mexc, Gate, KuCoin, Htx }
public enum MarketType { Spot, UsdtFutures, CoinFutures }
public enum ConnectionStatus { Disconnected, Connecting, Connected, Error }

public class ExchangeConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public ExchangeType Exchange { get; set; }
    public MarketType Market { get; set; } = MarketType.UsdtFutures;
    public string ApiKey { get; set; } = "";
    public string ApiSecret { get; set; } = "";
    public string? Passphrase { get; set; }
    public bool IsTestnet { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string? Proxy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastConnectedAt { get; set; }
    public ConnectionStatus Status { get; set; } = ConnectionStatus.Disconnected;
}

public class AppSettings
{
    public List<ExchangeConnection> Connections { get; set; } = new();
    public string Theme { get; set; } = "hype";
}
