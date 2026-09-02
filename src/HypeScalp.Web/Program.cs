using HypeScalp.Core.Interfaces;
using HypeScalp.Exchange;
using HypeScalp.Web.Components;
using HypeScalp.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Data Protection — encrypts API secrets at rest (keys in App_Data/keys)
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(
        Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys")));

builder.Services.AddSingleton<IExchangeClientFactory, ExchangeClientFactory>();
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<ConnectionManager>();
builder.Services.AddSingleton<MarketDataHub>();

var app = builder.Build();

Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "App_Data", "keys"));

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

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
