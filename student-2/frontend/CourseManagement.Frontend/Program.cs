using CourseManagement.Frontend;
using CourseManagement.Frontend.Services;
using CampusPulse.Shared.Components.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var endpoints = await CampusEndpoints.LoadAsync(builder.HostEnvironment.BaseAddress);
builder.Services.AddSingleton(endpoints);
var configuredAcademicRecordsBackendUrl = builder.Configuration["AcademicRecordsBackendUrl"];
var academicRecordsBackendUrl = endpoints.UseSameOriginApi || string.IsNullOrWhiteSpace(configuredAcademicRecordsBackendUrl)
    ? builder.HostEnvironment.BaseAddress.TrimEnd('/') + "/academic-api/"
    : configuredAcademicRecordsBackendUrl;
builder.Services.AddAuthentication(academicRecordsBackendUrl);
var configuredApiBaseAddress = builder.Configuration["CourseManagementApi:BaseAddress"];
var apiBaseAddress = endpoints.UseSameOriginApi || string.IsNullOrWhiteSpace(configuredApiBaseAddress)
    ? builder.HostEnvironment.BaseAddress
    : configuredApiBaseAddress;
builder.Services.AddCampusAccountServices(academicRecordsBackendUrl);
builder.Services.AddScoped(serviceProvider =>
{
    var handler = new UserIdHeaderHandler(serviceProvider.GetRequiredService<IJSRuntime>())
    {
        InnerHandler = AuthenticationServiceCollectionExtensions
            .CreateTokenHandler(serviceProvider)
    };
    return new HttpClient(handler) { BaseAddress = new Uri(apiBaseAddress, UriKind.Absolute) };
});
builder.Services.AddScoped<CourseApiClient>();
builder.Services.AddScoped<CourseUserContext>();

var host = builder.Build();
await host.Services.GetRequiredService<IJSRuntime>().InvokeVoidAsync("campusPulseWaitForNavigation");
await host.RunAsync();
