using HypeScalp.Core.Interfaces;
using HypeScalp.Core.Models;

namespace HypeScalp.Web.Services;

public class ConnectionManager
{
    private readonly IExchangeClientFactory _factory;
    private readonly SettingsService _settings;
    private readonly Dictionary<Guid, IExchangeClient> _clients = new();

    public event Action? OnChanged;

    public IReadOnlyList<ExchangeConnection> Connections => _settings.Settings.Connections;

    public ConnectionManager(IExchangeClientFactory factory, SettingsService settings)
    {
        _factory = factory;
        _settings = settings;
    }

    public async Task<bool> ConnectAsync(Guid id)
    {
        var conn = _settings.Settings.Connections.FirstOrDefault(c => c.Id == id);
        if (conn == null) return false;
        try
        {
            if (_clients.TryGetValue(id, out var old))
            {
                await old.DisposeAsync();
                _clients.Remove(id);
            }
            var client = _factory.Create(conn);
            await client.ConnectAsync();
            _clients[id] = client;
            conn.Status = ConnectionStatus.Connected;
            OnChanged?.Invoke();
            return true;
        }
        catch
        {
            conn.Status = ConnectionStatus.Error;
            OnChanged?.Invoke();
            return false;
        }
    }

    public async Task DisconnectAsync(Guid id)
    {
        if (_clients.TryGetValue(id, out var c))
        {
            await c.DisconnectAsync();
            await c.DisposeAsync();
            _clients.Remove(id);
        }
        var conn = _settings.Settings.Connections.FirstOrDefault(x => x.Id == id);
        if (conn != null) conn.Status = ConnectionStatus.Disconnected;
        OnChanged?.Invoke();
    }

    public IExchangeClient? GetClient(Guid id) =>
        _clients.TryGetValue(id, out var c) ? c : null;

    public int ConnectedCount => _clients.Values.Count(c => c.IsConnected);
}
