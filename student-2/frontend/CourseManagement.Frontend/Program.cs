using CourseManagement.Frontend;
using CourseManagement.Frontend.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Empty/missing BaseAddress (e.g. the cloud Production build) falls back to this app's own
// origin, where nginx proxies /api/ to the real backend.
var configuredApiBaseAddress = builder.Configuration["CourseManagementApi:BaseAddress"];
var apiBaseAddress = string.IsNullOrWhiteSpace(configuredApiBaseAddress)
    ? builder.HostEnvironment.BaseAddress
    : configuredApiBaseAddress;
builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri(apiBaseAddress, UriKind.Absolute)
});
builder.Services.AddScoped<CourseApiClient>();
builder.Services.AddScoped<CourseUserContext>();

await builder.Build().RunAsync();
