using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HypeScalp.Core.Models;

namespace HypeScalp.Web.Services;

public class SettingsService
{
    private readonly string _path;
    private readonly string _secretsPath;
    public AppSettings Settings { get; private set; } = new();

    public SettingsService(IWebHostEnvironment env)
    {
        var dir = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
        _secretsPath = Path.Combine(dir, "secrets.bin");
    }

    public async Task LoadAsync()
    {
        if (File.Exists(_path))
        {
            var json = await File.ReadAllTextAsync(_path);
            Settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new();
        }

        if (File.Exists(_secretsPath))
        {
            try
            {
                var enc = await File.ReadAllBytesAsync(_secretsPath);
                // For cross-platform web: simple AES or just base64 for demo.
                // Production: use Data Protection API.
                var json = Encoding.UTF8.GetString(enc);
                var map = JsonSerializer.Deserialize<Dictionary<Guid, SecretDto>>(json) ?? new();
                foreach (var c in Settings.Connections)
                {
                    if (map.TryGetValue(c.Id, out var s))
                    {
                        c.ApiKey = s.Key;
                        c.ApiSecret = s.Secret;
                        c.Passphrase = s.Pass;
                    }
                }
            }
            catch { /* ignore */ }
        }
    }

    public async Task SaveAsync()
    {
        var publicCopy = new AppSettings
        {
            Theme = Settings.Theme,
            Connections = Settings.Connections.Select(c => new ExchangeConnection
            {
                Id = c.Id, Name = c.Name, Exchange = c.Exchange, Market = c.Market,
                IsTestnet = c.IsTestnet, IsEnabled = c.IsEnabled, Proxy = c.Proxy,
                CreatedAt = c.CreatedAt, Status = ConnectionStatus.Disconnected
            }).ToList()
        };
        await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(publicCopy, new JsonSerializerOptions { WriteIndented = true }));

        var secrets = Settings.Connections.ToDictionary(c => c.Id, c => new SecretDto
        {
            Key = c.ApiKey, Secret = c.ApiSecret, Pass = c.Passphrase
        });
        // Demo storage — replace with ASP.NET Data Protection in production
        await File.WriteAllBytesAsync(_secretsPath, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(secrets)));
    }

    private class SecretDto
    {
        public string Key { get; set; } = "";
        public string Secret { get; set; } = "";
        public string? Pass { get; set; }
    }
}
