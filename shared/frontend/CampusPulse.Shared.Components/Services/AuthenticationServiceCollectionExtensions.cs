using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace CampusPulse.Shared.Components.Services;

public static class AuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddAuthentication(
        this IServiceCollection services,
        string academicRecordsBackendUrl)
    {
        services.AddAuthorizationCore();
        services.AddScoped(serviceProvider =>
        {
            var http = new HttpClient(CreateTokenHandler(serviceProvider))
            {
                BaseAddress = new Uri(EnsureTrailingSlash(academicRecordsBackendUrl))
            };
            return new AuthenticationStateProvider(
                http,
                serviceProvider.GetRequiredService<IJSRuntime>());
        });
        services.AddScoped<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<AuthenticationStateProvider>());
        return services;
    }

    public static HttpMessageHandler CreateTokenHandler(IServiceProvider services) =>
        new TokenHandler(services.GetRequiredService<IJSRuntime>())
        {
            InnerHandler = new HttpClientHandler()
        };

    private static string EnsureTrailingSlash(string address) =>
        address.EndsWith("/", StringComparison.Ordinal) ? address : $"{address}/";
}
