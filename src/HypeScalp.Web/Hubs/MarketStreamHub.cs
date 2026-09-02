using Microsoft.AspNetCore.SignalR;

namespace HypeScalp.Web.Hubs;

public class MarketStreamHub : Hub
{
    public async Task Subscribe(string exchange, string symbol)
    {
        var group = $"{exchange}:{symbol}".ToUpperInvariant();
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
    }

    public async Task Unsubscribe(string exchange, string symbol)
    {
        var group = $"{exchange}:{symbol}".ToUpperInvariant();
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
    }
}
