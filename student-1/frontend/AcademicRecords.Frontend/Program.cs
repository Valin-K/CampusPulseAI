using AcademicRecords.Frontend;
using CampusPulse.Shared.Components.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var backendUrl = builder.Configuration["BackendUrl"] ?? "http://localhost:5020";
builder.Services.AddCampusAccountServices(builder.Configuration["AcademicRecordsBackendUrl"] ?? backendUrl);
builder.Services.AddScoped(serviceProvider =>
{
    var handler = new CampusUserIdHeaderHandler(serviceProvider.GetRequiredService<IJSRuntime>())
    {
        InnerHandler = new HttpClientHandler()
    };
    return new HttpClient(handler) { BaseAddress = new Uri(backendUrl) };
});

await builder.Build().RunAsync();
