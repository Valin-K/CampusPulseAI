using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using CampusPulse.Shared.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;

namespace CampusPulse.Shared.Ai.Tests;

public sealed class AiGatewayAuthorizationTests
{
    private const string SigningKey = "gateway-test-signing-key-at-least-32-bytes";

    [Test]
    public async Task HealthIsAnonymousAndAiEndpointsRequireCampusPulseAuthorization()
    {
        var previousSigningKey = Environment.GetEnvironmentVariable("Authentication__SigningKey");
        Environment.SetEnvironmentVariable("Authentication__SigningKey", SigningKey);
        try
        {
            using var factory = new GatewayFactory();
            using var client = factory.CreateClient();

            using var health = await client.GetAsync("/health");
            using var status = await client.GetAsync("/api/ai/status");
            using var generation = await client.PostAsJsonAsync("/api/ai/generate", new { prompt = "prompt", context = "context" });
            using var embeddings = await client.PostAsJsonAsync("/api/ai/embeddings", new { texts = new[] { "text" }, isQuery = false });

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken());
            using var authorizedStatus = await client.GetAsync("/api/ai/status");

            Assert.Multiple(() =>
            {
                Assert.That(health.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(status.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
                Assert.That(generation.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
                Assert.That(embeddings.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
                Assert.That(authorizedStatus.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("Authentication__SigningKey", previousSigningKey);
        }
    }

    private static string CreateToken()
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = "CampusPulse",
            Audience = "CampusPulse.Api",
            Subject = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "test-user"),
                new Claim(ClaimTypes.Role, "Student"),
                new Claim("scope", CampusPulseSecurityExtensions.AccessScope)
            ]),
            NotBefore = DateTime.UtcNow.AddSeconds(-30),
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                SecurityAlgorithms.HmacSha256)
        };

        return new JwtSecurityTokenHandler().CreateEncodedJwt(descriptor);
    }

    private sealed class GatewayFactory : WebApplicationFactory<Program> { }
}
