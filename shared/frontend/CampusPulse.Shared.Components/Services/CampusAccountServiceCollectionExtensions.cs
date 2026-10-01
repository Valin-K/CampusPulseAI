using Microsoft.JSInterop;
using Microsoft.Extensions.DependencyInjection;

namespace CampusPulse.Shared.Components.Services;

public static class CampusAccountServiceCollectionExtensions
{
    public static IServiceCollection AddCampusAccountServices(
        this IServiceCollection services,
        string academicRecordsBackendUrl)
    {
        services.AddScoped(serviceProvider =>
        {
            var javaScript = serviceProvider.GetRequiredService<IJSRuntime>();
            var handler = new CampusUserIdHeaderHandler(javaScript)
            {
                InnerHandler = new HttpClientHandler()
            };
            var http = new HttpClient(handler)
            {
                BaseAddress = new Uri(EnsureTrailingSlash(academicRecordsBackendUrl))
            };
            return new CampusAccountService(http, javaScript);
        });

        return services;
    }

    private static string EnsureTrailingSlash(string address) =>
        address.EndsWith("/", StringComparison.Ordinal) ? address : $"{address}/";
}
