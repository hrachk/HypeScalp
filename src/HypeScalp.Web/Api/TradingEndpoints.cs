using HypeScalp.Core.Models;
using HypeScalp.Web.Services;

namespace HypeScalp.Web.Api;

public static class TradingEndpoints
{
    public static void MapTradingApi(this WebApplication app)
    {
        var g = app.MapGroup("/api/trade").WithTags("Trading");

        g.MapGet("/status", (TradingService trading) =>
        {
            var conns = trading.ListConnections();
            var client = trading.ResolveClient();
            return Results.Ok(new
            {
                ready = client != null,
                exchange = client?.Exchange.ToString(),
                connections = conns
            });
        });

        g.MapPost("/market", async (TradeRequest req, TradingService trading, AccountDataCache cache) =>
        {
            if (!TrySide(req.Side, out var side))
                return Results.BadRequest(new { ok = false, message = "side must be buy|sell" });
            var ex = ParseEx(req.Exchange);
            var r = await trading.PlaceMarketAsync(req.Symbol, side, req.Quantity, ex);
            if (r.Ok) cache.Invalidate();
            return r.Ok ? Results.Ok(r) : Results.BadRequest(r);
        });

        g.MapPost("/limit", async (TradeRequest req, TradingService trading, AccountDataCache cache) =>
        {
            if (!TrySide(req.Side, out var side))
                return Results.BadRequest(new { ok = false, message = "side must be buy|sell" });
            if (req.Price is null or <= 0)
                return Results.BadRequest(new { ok = false, message = "price required for limit" });
            var ex = ParseEx(req.Exchange);
            var r = await trading.PlaceLimitAsync(req.Symbol, side, req.Quantity, req.Price.Value, ex);
            if (r.Ok) cache.Invalidate();
            return r.Ok ? Results.Ok(r) : Results.BadRequest(r);
        });

        g.MapPost("/cancel-all", async (TradeRequest req, TradingService trading, AccountDataCache cache) =>
        {
            var r = await trading.CancelAllAsync(req.Symbol, ParseEx(req.Exchange));
            if (r.Ok) cache.Invalidate();
            return r.Ok ? Results.Ok(r) : Results.BadRequest(r);
        });

        g.MapPost("/cancel", async (CancelRequest req, TradingService trading, AccountDataCache cache) =>
        {
            var r = await trading.CancelOrderAsync(req.Symbol, req.OrderId, ParseEx(req.Exchange));
            if (r.Ok) cache.Invalidate();
            return r.Ok ? Results.Ok(r) : Results.BadRequest(r);
        });

        g.MapPost("/flatten", async (TradeRequest req, TradingService trading, AccountDataCache cache) =>
        {
            var r = await trading.FlattenAsync(req.Symbol, ParseEx(req.Exchange));
            if (r.Ok) cache.Invalidate();
            return r.Ok ? Results.Ok(r) : Results.BadRequest(r);
        });

        g.MapGet("/positions", async (string? exchange, bool? force, AccountDataCache cache, UserDataStreamService uds) =>
        {
            // MetaScalp model: memory from user stream first
            if (force != true)
                return Results.Ok(uds.SnapshotPositions());
            var list = await cache.GetPositionsAsync(true, ParseEx(exchange));
            return Results.Ok(list);
        });

        g.MapGet("/orders", async (string? symbol, string? exchange, bool? force, AccountDataCache cache, UserDataStreamService uds) =>
        {
            if (force != true)
            {
                var live = uds.SnapshotOrders();
                if (!string.IsNullOrWhiteSpace(symbol))
                {
                    var norm = symbol.Replace("-", "").Replace("_", "").ToUpperInvariant();
                    live = live.Where(o => o.Symbol.Replace("-", "").Replace("_", "")
                        .Equals(norm, StringComparison.OrdinalIgnoreCase)).ToList();
                }
                return Results.Ok(live);
            }
            var list = await cache.GetOpenOrdersAsync(symbol, true, ParseEx(exchange));
            return Results.Ok(list);
        });
    }

    private static ExchangeType? ParseEx(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return TradingService.TryParseExchange(name, out var ex) ? ex : null;
    }

    private static bool TrySide(string? s, out OrderSide side)
    {
        side = OrderSide.Buy;
        if (string.IsNullOrWhiteSpace(s)) return false;
        if (s.Equals("buy", StringComparison.OrdinalIgnoreCase)) { side = OrderSide.Buy; return true; }
        if (s.Equals("sell", StringComparison.OrdinalIgnoreCase)) { side = OrderSide.Sell; return true; }
        return false;
    }

    public sealed class TradeRequest
    {
        public string Symbol { get; set; } = "BTCUSDT";
        public string Side { get; set; } = "buy";
        public decimal Quantity { get; set; } = 0.01m;
        public decimal? Price { get; set; }
        public string? Exchange { get; set; }
    }

    public sealed class CancelRequest
    {
        public string Symbol { get; set; } = "BTCUSDT";
        public string OrderId { get; set; } = "";
        public string? Exchange { get; set; }
    }
}
