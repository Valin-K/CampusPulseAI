using CampusHousing.Frontend;
using CampusHousing.Frontend.Services;
using CampusPulse.Shared.Components.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
builder.Services.AddScoped<HousingUserContext>();
builder.Services.AddScoped<HousingRoleHandler>();

var backendUrl = builder.Configuration["BackendUrl"] ?? "http://localhost:5083";
builder.Services.AddCampusAccountServices(builder.Configuration["AcademicRecordsBackendUrl"] ?? "http://localhost:5020");
builder.Services.AddScoped(sp =>
{
    var housingHandler = sp.GetRequiredService<HousingRoleHandler>();
    housingHandler.InnerHandler = new HttpClientHandler();
    var userIdHandler = new CampusUserIdHeaderHandler(sp.GetRequiredService<IJSRuntime>())
    {
        InnerHandler = housingHandler
    };
    return new HttpClient(userIdHandler) { BaseAddress = new Uri(backendUrl) };
});

await builder.Build().RunAsync();
