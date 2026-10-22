using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components.Authorization;

namespace CampusPulse.Shared.Components.Services;

public static class AccountServiceCollectionExtensions
{
    public static IServiceCollection AddCampusAccountServices(
        this IServiceCollection services,
        string academicRecordsBackendUrl)
    {
        services.AddScoped<AccountService>(serviceProvider =>
        {
            var handler = AuthenticationServiceCollectionExtensions.CreateTokenHandler(serviceProvider);
            var http = new HttpClient(handler)
            {
                BaseAddress = new Uri(EnsureTrailingSlash(academicRecordsBackendUrl))
            };
            return new AccountService(
                http,
                serviceProvider.GetRequiredService<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider>());
        });

        return services;
    }

    private static string EnsureTrailingSlash(string address) =>
        address.EndsWith("/", StringComparison.Ordinal) ? address : $"{address}/";
}
