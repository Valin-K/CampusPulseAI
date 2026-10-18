using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components.Authorization;

namespace CampusPulse.Shared.Components.Services;

public static class CampusAccountServiceCollectionExtensions
{
    public static IServiceCollection AddCampusAccountServices(
        this IServiceCollection services,
        string academicRecordsBackendUrl)
    {
        services.AddScoped<CampusAccountService>(serviceProvider =>
        {
            var handler = CampusDemoAuthenticationServiceCollectionExtensions.CreateCampusDemoTokenHandler(serviceProvider);
            var http = new HttpClient(handler)
            {
                BaseAddress = new Uri(EnsureTrailingSlash(academicRecordsBackendUrl))
            };
            return new CampusAccountService(
                http,
                serviceProvider.GetRequiredService<AuthenticationStateProvider>());
        });

        return services;
    }

    private static string EnsureTrailingSlash(string address) =>
        address.EndsWith("/", StringComparison.Ordinal) ? address : $"{address}/";
}
