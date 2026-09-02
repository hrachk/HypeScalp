using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HypeScalp.Core.Models;
using HypeScalp.Exchange.Common;

namespace HypeScalp.Exchange.Binance;

public class BinanceClient : BaseExchangeClient
{
    private readonly HttpClient _http;
    private readonly bool _futures;

    public BinanceClient(ExchangeConnection connection) : base(connection)
    {
        _futures = connection.Market != MarketType.Spot;
        var baseUrl = connection.IsTestnet
            ? (_futures ? "https://testnet.binancefuture.com" : "https://testnet.binance.vision")
            : (_futures ? "https://fapi.binance.com" : "https://api.binance.com");
        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        _http.DefaultRequestHeaders.Add("X-MBX-APIKEY", connection.ApiKey);
    }

    public override async Task ConnectAsync(CancellationToken ct = default)
    {
        try
        {
            Connection.Status = ConnectionStatus.Connecting;
            var path = _futures ? "/fapi/v2/account" : "/api/v3/account";
            var qs = $"timestamp={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            var resp = await _http.GetAsync($"{path}?{qs}&signature={Sign(qs)}", ct);
            if (!resp.IsSuccessStatusCode)
                throw new Exception($"Auth failed: {resp.StatusCode} {await resp.Content.ReadAsStringAsync(ct)}");
            _isConnected = true;
            Connection.Status = ConnectionStatus.Connected;
            Connection.LastConnectedAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            Connection.Status = ConnectionStatus.Error;
            RaiseError(ex.Message);
            throw;
        }
    }

    public override Task DisconnectAsync()
    {
        _isConnected = false;
        Connection.Status = ConnectionStatus.Disconnected;
        return Task.CompletedTask;
    }

    public override async Task SubscribeOrderBookAsync(string symbol, int depth = 20)
    {
        var path = _futures ? "/fapi/v1/depth" : "/api/v3/depth";
        var resp = await _http.GetAsync($"{path}?symbol={symbol.ToUpperInvariant()}&limit={depth}");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        var snap = new OrderBookSnapshot { Symbol = symbol, Exchange = ExchangeType.Binance };
        foreach (var a in root.GetProperty("asks").EnumerateArray())
            snap.Asks.Add(new OrderBookLevel { Price = decimal.Parse(a[0].GetString()!), Quantity = decimal.Parse(a[1].GetString()!) });
        foreach (var b in root.GetProperty("bids").EnumerateArray())
            snap.Bids.Add(new OrderBookLevel { Price = decimal.Parse(b[0].GetString()!), Quantity = decimal.Parse(b[1].GetString()!) });
        RaiseOrderBook(snap);
    }

    public override async Task<IReadOnlyList<string>> GetSymbolsAsync()
    {
        var path = _futures ? "/fapi/v1/exchangeInfo" : "/api/v3/exchangeInfo";
        var resp = await _http.GetAsync(path);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("symbols").EnumerateArray()
            .Where(s => s.GetProperty("status").GetString() == "TRADING")
            .Select(s => s.GetProperty("symbol").GetString()!)
            .ToList();
    }

    public override async Task<Order> PlaceOrderAsync(string symbol, OrderSide side, OrderType type, decimal quantity, decimal? price = null)
    {
        var path = _futures ? "/fapi/v1/order" : "/api/v3/order";
        var p = new Dictionary<string, string>
        {
            ["symbol"] = symbol.ToUpperInvariant(),
            ["side"] = side == OrderSide.Buy ? "BUY" : "SELL",
            ["type"] = type == OrderType.Market ? "MARKET" : "LIMIT",
            ["quantity"] = quantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
        };
        if (type == OrderType.Limit && price.HasValue)
        {
            p["price"] = price.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            p["timeInForce"] = "GTC";
        }
        var qs = string.Join("&", p.Select(kv => $"{kv.Key}={kv.Value}"));
        var content = new StringContent($"{qs}&signature={Sign(qs)}", Encoding.UTF8, "application/x-www-form-urlencoded");
        var resp = await _http.PostAsync(path, content);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode) throw new Exception(body);
        using var doc = JsonDocument.Parse(body);
        return new Order
        {
            OrderId = doc.RootElement.GetProperty("orderId").GetRawText(),
            Symbol = symbol,
            Exchange = ExchangeType.Binance,
            Side = side,
            Type = type,
            Price = price ?? 0,
            Quantity = quantity,
            Status = OrderStatus.New
        };
    }


    public override async Task CancelOrderAsync(string symbol, string orderId)
    {
        var path = _futures ? "/fapi/v1/order" : "/api/v3/order";
        var qs = $"symbol={symbol.ToUpperInvariant()}&orderId={orderId}&timestamp={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var resp = await _http.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"{path}?{qs}&signature={Sign(qs)}"));
        if (!resp.IsSuccessStatusCode)
            throw new Exception(await resp.Content.ReadAsStringAsync());
    }

    public override async Task<IReadOnlyList<Order>> GetOpenOrdersAsync(string? symbol = null)
    {
        var path = _futures ? "/fapi/v1/openOrders" : "/api/v3/openOrders";
        var qs = $"timestamp={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        if (!string.IsNullOrEmpty(symbol))
            qs = $"symbol={symbol.ToUpperInvariant()}&{qs}";
        var resp = await _http.GetAsync($"{path}?{qs}&signature={Sign(qs)}");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.EnumerateArray().Select(o => new Order
        {
            OrderId = o.GetProperty("orderId").GetRawText(),
            Symbol = o.GetProperty("symbol").GetString()!,
            Exchange = ExchangeType.Binance,
            Side = o.GetProperty("side").GetString() == "BUY" ? OrderSide.Buy : OrderSide.Sell,
            Type = o.GetProperty("type").GetString() == "MARKET" ? OrderType.Market : OrderType.Limit,
            Price = decimal.Parse(o.GetProperty("price").GetString()!, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture),
            Quantity = decimal.Parse(o.GetProperty("origQty").GetString()!, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture),
            Status = OrderStatus.New
        }).ToList();
    }

    public override async Task CancelAllOrdersAsync(string symbol)
    {
        var path = _futures ? "/fapi/v1/allOpenOrders" : "/api/v3/openOrders";
        var qs = $"symbol={symbol.ToUpperInvariant()}&timestamp={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        await _http.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"{path}?{qs}&signature={Sign(qs)}"));
    }

    public override async Task<IReadOnlyList<Position>> GetPositionsAsync()
    {
        if (!_futures) return Array.Empty<Position>();
        var qs = $"timestamp={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var resp = await _http.GetAsync($"/fapi/v2/positionRisk?{qs}&signature={Sign(qs)}");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.EnumerateArray()
            .Select(p => new Position
            {
                Symbol = p.GetProperty("symbol").GetString()!,
                Exchange = ExchangeType.Binance,
                Size = decimal.Parse(p.GetProperty("positionAmt").GetString()!),
                EntryPrice = decimal.Parse(p.GetProperty("entryPrice").GetString()!),
                MarkPrice = decimal.Parse(p.GetProperty("markPrice").GetString()!),
                UnrealizedPnl = decimal.Parse(p.GetProperty("unRealizedProfit").GetString()!)
            })
            .Where(p => p.Size != 0)
            .ToList();
    }

    private string Sign(string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Connection.ApiSecret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }
}
