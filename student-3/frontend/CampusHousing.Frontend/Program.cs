using CampusHousing.Frontend;
using CampusHousing.Frontend.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
builder.Services.AddScoped<HousingUserContext>();
builder.Services.AddScoped<HousingRoleHandler>();

var configuredBackendUrl = builder.Configuration["BackendUrl"];
var backendUrl = string.IsNullOrWhiteSpace(configuredBackendUrl)
    ? builder.HostEnvironment.BaseAddress
    : configuredBackendUrl;
builder.Services.AddScoped(sp =>
{
    var handler = sp.GetRequiredService<HousingRoleHandler>();
    handler.InnerHandler = new HttpClientHandler();
    return new HttpClient(handler) { BaseAddress = new Uri(backendUrl) };
});

await builder.Build().RunAsync();
