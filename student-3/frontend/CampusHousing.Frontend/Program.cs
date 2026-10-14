using CampusHousing.Frontend;
using CampusHousing.Frontend.Services;
using CampusPulse.Shared.Components.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
var backendUrl = builder.Configuration["BackendUrl"] ?? "http://localhost:5083";
var academicRecordsBackendUrl = builder.Configuration["AcademicRecordsBackendUrl"] ?? "http://localhost:5020";
builder.Services.AddScoped<HousingUserContext>(sp =>
{
    var handler = CampusDemoAuthenticationServiceCollectionExtensions
        .CreateCampusDemoTokenHandler(sp);
    var apiBaseAddress = backendUrl.EndsWith('/') ? backendUrl : $"{backendUrl}/";
    var http = new HttpClient(handler) { BaseAddress = new Uri(apiBaseAddress) };
    return new HousingUserContext(
        sp.GetRequiredService<IJSRuntime>(),
        sp.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>(),
        http);
});
builder.Services.AddScoped<HousingRoleHandler>();

builder.Services.AddCampusDemoAuthentication(academicRecordsBackendUrl);
builder.Services.AddCampusAccountServices(academicRecordsBackendUrl);
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
