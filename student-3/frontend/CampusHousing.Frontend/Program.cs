using CampusHousing.Frontend;
using CampusHousing.Frontend.Services;
using CampusPulse.Shared.Components.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
var configuredBackendUrl = builder.Configuration["BackendUrl"];
var backendUrl = string.IsNullOrWhiteSpace(configuredBackendUrl)
    ? builder.HostEnvironment.BaseAddress
    : configuredBackendUrl;
var academicRecordsBackendUrl = builder.Configuration["AcademicRecordsBackendUrl"] ?? "http://localhost:5020";
builder.Services.AddAuthentication(academicRecordsBackendUrl);
builder.Services.AddCampusAccountServices(academicRecordsBackendUrl);
builder.Services.AddScoped<HousingUserContext>();
builder.Services.AddScoped(sp =>
{
    var tokenHandler = AuthenticationServiceCollectionExtensions.CreateTokenHandler(sp);
    return new HttpClient(tokenHandler) { BaseAddress = new Uri(backendUrl) };
});
await builder.Build().RunAsync();
