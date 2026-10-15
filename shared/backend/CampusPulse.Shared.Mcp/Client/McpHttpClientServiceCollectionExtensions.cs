using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace CampusPulse.Shared.Mcp.Client;

public sealed class McpAuthorizationHeaderHandler(IHttpContextAccessor contextAccessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var authorization = contextAccessor.HttpContext?.Request.Headers.Authorization;
        if (authorization is { Count: > 0 } && !request.Headers.Contains("Authorization"))
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization.ToString());
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
        services.AddHttpContextAccessor();
        services.AddTransient<McpAuthorizationHeaderHandler>();
        return services.AddHttpClient<IMcpClient, McpClient>(configureClient)
            .AddHttpMessageHandler<McpAuthorizationHeaderHandler>();
    }
}
