using CourseManagement.Frontend;
using CourseManagement.Frontend.Services;
using CampusPulse.Shared.Components.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var academicRecordsBackendUrl = builder.Configuration["AcademicRecordsBackendUrl"] ?? "http://localhost:5020";
builder.Services.AddCampusDemoAuthentication(academicRecordsBackendUrl);
var apiBaseAddress = builder.Configuration["CourseManagementApi:BaseAddress"]
    ?? throw new InvalidOperationException("Course Management API base address is not configured.");
builder.Services.AddCampusAccountServices(academicRecordsBackendUrl);
builder.Services.AddScoped(serviceProvider =>
{
    var handler = new CampusUserIdHeaderHandler(serviceProvider.GetRequiredService<IJSRuntime>())
    {
        InnerHandler = CampusDemoAuthenticationServiceCollectionExtensions
            .CreateCampusDemoTokenHandler(serviceProvider)
    };
    return new HttpClient(handler) { BaseAddress = new Uri(apiBaseAddress, UriKind.Absolute) };
});
builder.Services.AddScoped<CourseApiClient>();
builder.Services.AddScoped<CourseUserContext>();

await builder.Build().RunAsync();
