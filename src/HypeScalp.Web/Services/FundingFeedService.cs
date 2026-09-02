using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using HypeScalp.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace HypeScalp.Web.Services;

/// <summary>
/// Binance public mark price / funding stream → SignalR "funding".
/// </summary>
public class FundingFeedService : BackgroundService
{
    private readonly IHubContext<MarketStreamHub> _hub;
    private readonly ILogger<FundingFeedService> _log;

    public FundingFeedService(IHubContext<MarketStreamHub> hub, ILogger<FundingFeedService> log)
    {
        _hub = hub;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var backoff = 1000;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var ws = new ClientWebSocket();
                // Combined: BTC + ETH mark price (includes funding rate)
                var url = "wss://fstream.binance.com/stream?streams=btcusdt@markPrice/ethusdt@markPrice";
                await ws.ConnectAsync(new Uri(url), stoppingToken);
                _log.LogInformation("Funding WS connected");
                backoff = 1000;

                var buffer = new byte[64 * 1024];
                while (ws.State == WebSocketState.Open && !stoppingToken.IsCancellationRequested)
                {
                    using var ms = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await ws.ReceiveAsync(buffer, stoppingToken);
                        if (result.MessageType == WebSocketMessageType.Close) break;
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close) break;
                    Handle(Encoding.UTF8.GetString(ms.ToArray()));
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _log.LogWarning("Funding WS error, retry {Ms}ms: {Msg}", backoff, ex.Message);
                await Task.Delay(backoff, stoppingToken);
                backoff = Math.Min(backoff * 2, 15000);
            }
        }
    }

    private void Handle(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var data = root.TryGetProperty("data", out var d) ? d : root;
            if (!data.TryGetProperty("s", out var symEl)) return;

            var payload = new
            {
                symbol = symEl.GetString(),
                mark = Dec(data, "p"),
                index = Dec(data, "i"),
                funding = Dec(data, "r"),
                nextFundingTime = data.TryGetProperty("T", out var t) ? t.GetInt64() : 0L
            };
            _ = _hub.Clients.All.SendAsync("funding", payload);
        }
        catch { /* ignore */ }
    }

    private static decimal Dec(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p)) return 0;
        if (p.ValueKind == JsonValueKind.String)
            return decimal.Parse(p.GetString()!, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture);
        if (p.ValueKind == JsonValueKind.Number) return p.GetDecimal();
        return 0;
    }
}
