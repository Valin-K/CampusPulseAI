using FinancialAidBilling.Frontend;
using FinancialAidBilling.Frontend.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var backendUrl = builder.Configuration["BackendUrl"] ?? "http://localhost:8089";
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(backendUrl) });
builder.Services.AddScoped<FinancialAidUserContext>();

await builder.Build().RunAsync();
