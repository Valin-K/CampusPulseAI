using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using CampusPulse.Shared.Security;

namespace CampusPulse.Shared.Ai;

public static class AiServiceCollectionExtensions
{
    public static IServiceCollection AddAiGateway(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddCampusPulseAuthorizationForwarding();
        services.AddHttpClient<IAiGatewayClient, AiGatewayClient>(client =>
        {
            client.BaseAddress = new Uri(
                (configuration["Services:AiGateway"] ?? "http://localhost:5269").TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(100);
        }).ForwardCampusPulseAuthorization();
        return services;
    }
}
