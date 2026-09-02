namespace HypeScalp.Core.Models;

public class OrderBookLevel
{
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public bool IsWall { get; set; }
}

public class OrderBookSnapshot
{
    public string Symbol { get; set; } = "";
    public ExchangeType Exchange { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public List<OrderBookLevel> Bids { get; set; } = new();
    public List<OrderBookLevel> Asks { get; set; } = new();
    public decimal BestBid => Bids.Count > 0 ? Bids[0].Price : 0;
    public decimal BestAsk => Asks.Count > 0 ? Asks[0].Price : 0;
    public decimal Spread => BestAsk > 0 && BestBid > 0 ? BestAsk - BestBid : 0;
}

public class TradeTick
{
    public string Symbol { get; set; } = "";
    public ExchangeType Exchange { get; set; }
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public bool IsBuy { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public bool IsLarge { get; set; }
}

public class Position
{
    public string Symbol { get; set; } = "";
    public ExchangeType Exchange { get; set; }
    public decimal Size { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal MarkPrice { get; set; }
    public decimal UnrealizedPnl { get; set; }
    public bool IsLong => Size > 0;
}

public enum OrderSide { Buy, Sell }
public enum OrderType { Limit, Market }
public enum OrderStatus { New, Filled, Canceled, Rejected }

public class Order
{
    public string OrderId { get; set; } = "";
    public string Symbol { get; set; } = "";
    public ExchangeType Exchange { get; set; }
    public OrderSide Side { get; set; }
    public OrderType Type { get; set; }
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public OrderStatus Status { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
