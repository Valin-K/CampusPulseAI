using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace CampusPulse.Shared.Components.Services;

public static class CampusDemoAuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddCampusDemoAuthentication(
        this IServiceCollection services,
        string academicRecordsBackendUrl)
    {
        services.AddAuthorizationCore();
        services.AddScoped(serviceProvider =>
        {
            var http = new HttpClient(CreateCampusDemoTokenHandler(serviceProvider))
            {
                BaseAddress = new Uri(EnsureTrailingSlash(academicRecordsBackendUrl))
            };
            return new CampusDemoAuthenticationStateProvider(
                http,
                serviceProvider.GetRequiredService<IJSRuntime>());
        });
        services.AddScoped<AuthenticationStateProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<CampusDemoAuthenticationStateProvider>());
        return services;
    }

    public static HttpMessageHandler CreateCampusDemoTokenHandler(IServiceProvider services) =>
        new CampusDemoTokenHandler(services.GetRequiredService<IJSRuntime>())
        {
            InnerHandler = new HttpClientHandler()
        };

    private static string EnsureTrailingSlash(string address) =>
        address.EndsWith("/", StringComparison.Ordinal) ? address : $"{address}/";
}
