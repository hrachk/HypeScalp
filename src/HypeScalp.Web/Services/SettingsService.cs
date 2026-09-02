using System.Text;
using System.Text.Json;
using HypeScalp.Core.Models;
using Microsoft.AspNetCore.DataProtection;

namespace HypeScalp.Web.Services;

/// <summary>
/// Persists settings; API secrets are encrypted with ASP.NET Core Data Protection.
/// </summary>
public class SettingsService
{
    private readonly string _path;
    private readonly string _secretsPath;
    private readonly IDataProtector _protector;

    public AppSettings Settings { get; private set; } = new();

    public SettingsService(IWebHostEnvironment env, IDataProtectionProvider dataProtection)
    {
        var dir = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
        _secretsPath = Path.Combine(dir, "secrets.protected");
        _protector = dataProtection.CreateProtector("HypeScalp.Secrets.v1");
    }

    public async Task LoadAsync()
    {
        if (File.Exists(_path))
        {
            var json = await File.ReadAllTextAsync(_path);
            Settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new();
        }

        if (!File.Exists(_secretsPath)) return;

        try
        {
            var protectedBytes = await File.ReadAllBytesAsync(_secretsPath);
            var plain = _protector.Unprotect(protectedBytes);
            var map = JsonSerializer.Deserialize<Dictionary<Guid, SecretDto>>(Encoding.UTF8.GetString(plain)) ?? new();
            foreach (var c in Settings.Connections)
            {
                if (!map.TryGetValue(c.Id, out var s)) continue;
                c.ApiKey = s.Key;
                c.ApiSecret = s.Secret;
                c.Passphrase = s.Pass;
            }
        }
        catch
        {
            // Corrupted or different key ring — secrets unavailable until re-entered
        }
    }

    public async Task SaveAsync()
    {
        var publicCopy = new AppSettings
        {
            Theme = Settings.Theme,
            Connections = Settings.Connections.Select(c => new ExchangeConnection
            {
                Id = c.Id,
                Name = c.Name,
                Exchange = c.Exchange,
                Market = c.Market,
                IsTestnet = c.IsTestnet,
                IsEnabled = c.IsEnabled,
                Proxy = c.Proxy,
                CreatedAt = c.CreatedAt,
                Status = ConnectionStatus.Disconnected
            }).ToList()
        };
        await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(publicCopy, new JsonSerializerOptions { WriteIndented = true }));

        var secrets = Settings.Connections.ToDictionary(
            c => c.Id,
            c => new SecretDto { Key = c.ApiKey, Secret = c.ApiSecret, Pass = c.Passphrase });
        var json = JsonSerializer.Serialize(secrets);
        var protectedBytes = _protector.Protect(Encoding.UTF8.GetBytes(json));
        await File.WriteAllBytesAsync(_secretsPath, protectedBytes);
    }

    private class SecretDto
    {
        public string Key { get; set; } = "";
        public string Secret { get; set; } = "";
        public string? Pass { get; set; }
    }
}
