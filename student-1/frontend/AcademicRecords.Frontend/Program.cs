using AcademicRecords.Frontend;
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
var academicRecordsBackendUrl = endpoints.UseSameOriginApi ? backendUrl
    : builder.Configuration["AcademicRecordsBackendUrl"] ?? backendUrl;
builder.Services.AddAuthentication(academicRecordsBackendUrl);
builder.Services.AddCampusAccountServices(academicRecordsBackendUrl);
builder.Services.AddScoped(serviceProvider =>
{
    var userIdHandler = new UserIdHeaderHandler(serviceProvider.GetRequiredService<Microsoft.JSInterop.IJSRuntime>())
    {
        InnerHandler = AuthenticationServiceCollectionExtensions
            .CreateTokenHandler(serviceProvider)
    };
    return new HttpClient(userIdHandler) { BaseAddress = new Uri(backendUrl) };
});

var host = builder.Build();
await host.Services.GetRequiredService<Microsoft.JSInterop.IJSRuntime>().InvokeVoidAsync("campusPulseWaitForNavigation");
await host.RunAsync();
