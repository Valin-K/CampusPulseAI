using EventScheduling.Frontend;
using EventScheduling.Frontend.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var configuredBackendUrl = builder.Configuration["BackendUrl"];
var backendUrl = string.IsNullOrWhiteSpace(configuredBackendUrl)
    ? builder.HostEnvironment.BaseAddress
    : configuredBackendUrl;
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(backendUrl) });
builder.Services.AddScoped<EventSchedulingApiClient>();
builder.Services.AddScoped<CurrentUser>();

await builder.Build().RunAsync();
