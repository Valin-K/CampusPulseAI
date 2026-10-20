using CourseManagement.Frontend;
using CourseManagement.Frontend.Services;
using CampusPulse.Shared.Components.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var configuredAcademicRecordsBackendUrl = builder.Configuration["AcademicRecordsBackendUrl"];
var academicRecordsBackendUrl = string.IsNullOrWhiteSpace(configuredAcademicRecordsBackendUrl)
    ? builder.HostEnvironment.BaseAddress.TrimEnd('/') + "/academic-api/"
    : configuredAcademicRecordsBackendUrl;
builder.Services.AddAuthentication(academicRecordsBackendUrl);
var configuredApiBaseAddress = builder.Configuration["CourseManagementApi:BaseAddress"];
var apiBaseAddress = string.IsNullOrWhiteSpace(configuredApiBaseAddress)
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

await builder.Build().RunAsync();
