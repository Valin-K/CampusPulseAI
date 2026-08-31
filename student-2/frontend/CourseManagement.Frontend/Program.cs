using CourseManagement.Frontend;
using CourseManagement.Frontend.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBaseAddress = builder.Configuration["CourseManagementApi:BaseAddress"]
    ?? throw new InvalidOperationException("Course Management API base address is not configured.");
builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri(apiBaseAddress, UriKind.Absolute)
});
builder.Services.AddScoped<CourseApiClient>();
builder.Services.AddScoped<CourseUserContext>();

await builder.Build().RunAsync();
