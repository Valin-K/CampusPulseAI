using AcademicRecords.Frontend;
using CampusPulse.Shared.Components.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var backendUrl = builder.Configuration["BackendUrl"] ?? "http://localhost:5020";
var academicRecordsBackendUrl = builder.Configuration["AcademicRecordsBackendUrl"] ?? backendUrl;
builder.Services.AddCampusDemoAuthentication(academicRecordsBackendUrl);
builder.Services.AddCampusAccountServices(
    academicRecordsBackendUrl);
builder.Services.AddScoped(serviceProvider =>
{
    var userIdHandler = new CampusUserIdHeaderHandler(serviceProvider.GetRequiredService<Microsoft.JSInterop.IJSRuntime>())
    {
        InnerHandler = CampusDemoAuthenticationServiceCollectionExtensions
            .CreateCampusDemoTokenHandler(serviceProvider)
    };
    return new HttpClient(userIdHandler) { BaseAddress = new Uri(backendUrl) };
});

await builder.Build().RunAsync();
