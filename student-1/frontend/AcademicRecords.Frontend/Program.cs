using AcademicRecords.Frontend;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var backendUrl = builder.Configuration["BackendUrl"] ?? "http://localhost:5020";
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(backendUrl) });

await builder.Build().RunAsync();
