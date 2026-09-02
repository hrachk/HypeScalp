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

        g.MapPost("/market", async (TradeRequest req, TradingService trading) =>
        {
            if (!TrySide(req.Side, out var side))
                return Results.BadRequest(new { ok = false, message = "side must be buy|sell" });
            TradingService.TryParseExchange(req.Exchange, out var ex);
            var r = await trading.PlaceMarketAsync(req.Symbol, side, req.Quantity,
                string.IsNullOrWhiteSpace(req.Exchange) ? null : ex);
            return r.Ok ? Results.Ok(r) : Results.BadRequest(r);
        });

        g.MapPost("/limit", async (TradeRequest req, TradingService trading) =>
        {
            if (!TrySide(req.Side, out var side))
                return Results.BadRequest(new { ok = false, message = "side must be buy|sell" });
            if (req.Price is null or <= 0)
                return Results.BadRequest(new { ok = false, message = "price required for limit" });
            TradingService.TryParseExchange(req.Exchange, out var ex);
            var r = await trading.PlaceLimitAsync(req.Symbol, side, req.Quantity, req.Price.Value,
                string.IsNullOrWhiteSpace(req.Exchange) ? null : ex);
            return r.Ok ? Results.Ok(r) : Results.BadRequest(r);
        });

        g.MapPost("/cancel-all", async (TradeRequest req, TradingService trading) =>
        {
            TradingService.TryParseExchange(req.Exchange, out var ex);
            var r = await trading.CancelAllAsync(req.Symbol,
                string.IsNullOrWhiteSpace(req.Exchange) ? null : ex);
            return r.Ok ? Results.Ok(r) : Results.BadRequest(r);
        });

        g.MapPost("/flatten", async (TradeRequest req, TradingService trading) =>
        {
            TradingService.TryParseExchange(req.Exchange, out var ex);
            var r = await trading.FlattenAsync(req.Symbol,
                string.IsNullOrWhiteSpace(req.Exchange) ? null : ex);
            return r.Ok ? Results.Ok(r) : Results.BadRequest(r);
        });

        g.MapGet("/positions", async (string? exchange, TradingService trading) =>
        {
            ExchangeType? ex = null;
            if (!string.IsNullOrWhiteSpace(exchange) && TradingService.TryParseExchange(exchange, out var parsed))
                ex = parsed;
            var list = await trading.GetPositionsAsync(ex);
            return Results.Ok(list);
        });
    }

    private static bool TrySide(string? s, out OrderSide side)
    {
        side = OrderSide.Buy;
        if (string.IsNullOrWhiteSpace(s)) return false;
        if (s.Equals("buy", StringComparison.OrdinalIgnoreCase) || s.Equals("long", StringComparison.OrdinalIgnoreCase))
        { side = OrderSide.Buy; return true; }
        if (s.Equals("sell", StringComparison.OrdinalIgnoreCase) || s.Equals("short", StringComparison.OrdinalIgnoreCase))
        { side = OrderSide.Sell; return true; }
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
}
