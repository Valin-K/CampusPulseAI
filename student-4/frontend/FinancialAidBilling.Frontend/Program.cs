using FinancialAidBilling.Frontend;
using FinancialAidBilling.Frontend.Services;
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
var configuredAcademicRecordsBackendUrl = builder.Configuration["AcademicRecordsBackendUrl"];
var academicRecordsBackendUrl = string.IsNullOrWhiteSpace(configuredAcademicRecordsBackendUrl)
    ? builder.HostEnvironment.BaseAddress.TrimEnd('/') + "/academic-api/"
    : configuredAcademicRecordsBackendUrl;
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
builder.Services.AddScoped<FinancialAidUserContext>();

await builder.Build().RunAsync();
