using CampusHousing.Frontend;
using CampusHousing.Frontend.Services;
using CampusPulse.Shared.Components.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
var endpoints = await CampusEndpoints.LoadAsync(builder.HostEnvironment.BaseAddress);
builder.Services.AddSingleton(endpoints);
var configuredBackendUrl = builder.Configuration["BackendUrl"];
var backendUrl = endpoints.UseSameOriginApi || string.IsNullOrWhiteSpace(configuredBackendUrl)
    ? builder.HostEnvironment.BaseAddress
    : configuredBackendUrl;
var configuredAcademicRecordsBackendUrl = builder.Configuration["AcademicRecordsBackendUrl"];
var academicRecordsBackendUrl = endpoints.UseSameOriginApi || string.IsNullOrWhiteSpace(configuredAcademicRecordsBackendUrl)
    ? builder.HostEnvironment.BaseAddress.TrimEnd('/') + "/academic-api/"
    : configuredAcademicRecordsBackendUrl;
builder.Services.AddAuthentication(academicRecordsBackendUrl);
builder.Services.AddCampusAccountServices(academicRecordsBackendUrl);
builder.Services.AddScoped<HousingUserContext>();
builder.Services.AddScoped(sp =>
{
    var tokenHandler = AuthenticationServiceCollectionExtensions.CreateTokenHandler(sp);
    return new HttpClient(tokenHandler) { BaseAddress = new Uri(backendUrl) };
});
var host = builder.Build();
await host.Services.GetRequiredService<IJSRuntime>().InvokeVoidAsync("campusPulseWaitForNavigation");
await host.RunAsync();
