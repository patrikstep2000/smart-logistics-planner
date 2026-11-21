using System.Globalization;
using SmartLogistics.Web.Components;

// Hrvatski format brojeva u prikazu (200,1 km).
var hr = CultureInfo.GetCultureInfo("hr-HR");
CultureInfo.DefaultThreadCurrentCulture = hr;
CultureInfo.DefaultThreadCurrentUICulture = hr;

var builder = WebApplication.CreateBuilder(args);

// Blazor Server: komponente se izvršavaju na serveru, preglednik dobiva promjene preko SignalR veze.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
