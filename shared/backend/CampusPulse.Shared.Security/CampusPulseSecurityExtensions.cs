using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.IdentityModel.Tokens;

namespace CampusPulse.Shared.Security;

public static class CampusPulseSecurityExtensions
{
    public const string StaffOnlyPolicy = "CampusPulse.StaffOnly";
    public const string StudentOnlyPolicy = "CampusPulse.StudentOnly";
    public const string AccessScope = "CampusPulse.Access";

    public static IServiceCollection AddCampusPulseSecurity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var issuer = configuration["Authentication:Issuer"] ?? "CampusPulse";
        var audience = configuration["Authentication:Audience"] ?? "CampusPulse.Api";
        var signingKey = configuration["Authentication:SigningKey"];
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            throw new InvalidOperationException("The token signing key is not configured.");
        }
        var keyBytes = Encoding.UTF8.GetBytes(signingKey);
        if (keyBytes.Length < 32)
        {
            throw new InvalidOperationException("The token signing key must contain at least 32 bytes.");
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    RoleClaimType = ClaimTypes.Role,
                    NameClaimType = ClaimTypes.Name
                };
            });

        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireClaim("scope", AccessScope)
                .RequireRole("Student", "Staff")
                .Build();
            options.AddPolicy(StaffOnlyPolicy, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim("scope", AccessScope)
                .RequireRole("Staff"));
            options.AddPolicy(StudentOnlyPolicy, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim("scope", AccessScope)
                .RequireRole("Student"));
        });

        services.AddCampusPulseAuthorizationForwarding();
        return services;
    }

    public static IServiceCollection AddCampusPulseAuthorizationForwarding(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.TryAddSingleton<AuthorizationRequestContext>();
        services.AddTransient<ForwardCurrentAuthorizationHeaderHandler>();
        return services;
    }

    public static IHttpClientBuilder ForwardCampusPulseAuthorization(this IHttpClientBuilder builder) =>
        builder.AddHttpMessageHandler<ForwardCurrentAuthorizationHeaderHandler>();
}

public static class CampusPulseClaimsPrincipalExtensions
{
    public static bool TryGetUserId(this ClaimsPrincipal user, out int userId) =>
        int.TryParse(user.FindFirst("user_id")?.Value, out userId);

    public static bool CanAccessStudent(this ClaimsPrincipal user, string studentId) =>
        user.IsInRole("Staff")
        || user.IsInRole("Student")
            && !string.IsNullOrWhiteSpace(studentId)
            && string.Equals(user.FindFirst("student_id")?.Value, studentId, StringComparison.OrdinalIgnoreCase);

    public static bool CanAccessUser(this ClaimsPrincipal user, int userId) =>
        user.IsInRole("Staff")
        || user.IsInRole("Student")
            && userId > 0
            && user.TryGetUserId(out var authenticatedUserId)
            && userId == authenticatedUserId;
}

public sealed class AuthorizationRequestContext
{
    private readonly AsyncLocal<string?> currentAuthorization = new();

    public string? CurrentAuthorization => currentAuthorization.Value;

    public IDisposable Push(string? authorization)
    {
        var previous = currentAuthorization.Value;
        currentAuthorization.Value = authorization;
        return new RestoreAuthorization(() => currentAuthorization.Value = previous);
    }

    private sealed class RestoreAuthorization(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}

public sealed class ForwardCurrentAuthorizationHeaderHandler(
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
