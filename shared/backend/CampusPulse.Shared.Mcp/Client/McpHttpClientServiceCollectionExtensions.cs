using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using CampusPulse.Shared.Security;

namespace CampusPulse.Shared.Mcp.Client;

public sealed class McpAuthorizationHeaderHandler(
    IHttpContextAccessor contextAccessor,
    AuthorizationRequestContext authorizationContext) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var authorization = contextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization))
        {
            authorization = authorizationContext.CurrentAuthorization;
        }

        if (!string.IsNullOrWhiteSpace(authorization) && !request.Headers.Contains("Authorization"))
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }

        return base.SendAsync(request, cancellationToken);
    }
}

public static class McpHttpClientServiceCollectionExtensions
{
    public static IHttpClientBuilder AddMcpClient(
        this IServiceCollection services,
        Action<HttpClient> configureClient)
    {
        services.AddCampusPulseAuthorizationForwarding();
        services.AddTransient<McpAuthorizationHeaderHandler>();
        return services.AddHttpClient<IMcpClient, McpClient>(configureClient)
            .AddHttpMessageHandler<McpAuthorizationHeaderHandler>();
    }
}
