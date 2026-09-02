using HypeScalp.Core.Interfaces;
using HypeScalp.Exchange;
using HypeScalp.Web.Components;
using HypeScalp.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSingleton<IExchangeClientFactory, ExchangeClientFactory>();
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<ConnectionManager>();

var app = builder.Build();

// Load settings on startup
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
