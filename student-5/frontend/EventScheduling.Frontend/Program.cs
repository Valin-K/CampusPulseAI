using EventScheduling.Frontend;
using EventScheduling.Frontend.Services;
using CampusPulse.Shared.Components.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var configuredBackendUrl = builder.Configuration["BackendUrl"];
var backendUrl = string.IsNullOrWhiteSpace(configuredBackendUrl)
    ? builder.HostEnvironment.BaseAddress
    : configuredBackendUrl;
var academicRecordsBackendUrl = builder.Configuration["AcademicRecordsBackendUrl"] ?? "http://localhost:5020";
builder.Services.AddCampusDemoAuthentication(academicRecordsBackendUrl);
builder.Services.AddCampusAccountServices(academicRecordsBackendUrl);
builder.Services.AddScoped(serviceProvider =>
{
    var handler = new CampusUserIdHeaderHandler(serviceProvider.GetRequiredService<IJSRuntime>())
    {
        InnerHandler = CampusDemoAuthenticationServiceCollectionExtensions
            .CreateCampusDemoTokenHandler(serviceProvider)
    };
    return new HttpClient(handler) { BaseAddress = new Uri(backendUrl) };
});
builder.Services.AddScoped<EventSchedulingApiClient>();
builder.Services.AddScoped<CurrentUser>();

await builder.Build().RunAsync();
