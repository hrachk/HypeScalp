using HypeScalp.Core.Interfaces;
using HypeScalp.Exchange;
using HypeScalp.Web.Components;
using HypeScalp.Web.Hubs;
using HypeScalp.Web.Services;
using HypeScalp.Web.Api;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSignalR();

var keysPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys");
Directory.CreateDirectory(keysPath);
builder.Services.AddDataProtection()
    .SetApplicationName("HypeScalp")
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath));

builder.Services.AddSingleton<IExchangeClientFactory, ExchangeClientFactory>();
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<ConnectionManager>();
builder.Services.AddSingleton<MarketDataHub>();
builder.Services.AddSingleton<TradingService>();
builder.Services.AddHostedService<MarketBroadcastService>();
builder.Services.AddHostedService<FundingFeedService>();

var app = builder.Build();

Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "App_Data"));

var settings = app.Services.GetRequiredService<SettingsService>();
await settings.LoadAsync();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapGet("/", () => Results.Redirect("/terminal.html"));

app.MapHub<MarketStreamHub>("/hubs/market");

app.MapTradingApi();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
