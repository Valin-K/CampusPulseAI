using CampusHousing.Frontend;
using CampusHousing.Frontend.Services;
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
builder.Services.AddScoped<HousingUserContext>();
builder.Services.AddScoped<HousingRoleHandler>();
builder.Services.AddScoped(sp =>
{
    var housingHandler = sp.GetRequiredService<HousingRoleHandler>();
    housingHandler.InnerHandler = CampusDemoAuthenticationServiceCollectionExtensions
        .CreateCampusDemoTokenHandler(sp);
    var userIdHandler = new CampusUserIdHeaderHandler(sp.GetRequiredService<IJSRuntime>())
    {
        InnerHandler = housingHandler
    };
    return new HttpClient(userIdHandler) { BaseAddress = new Uri(backendUrl) };
});
await builder.Build().RunAsync();
