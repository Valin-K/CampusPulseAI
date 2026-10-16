using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace AcademicRecords.Backend.Controllers;

[ApiController]
[Route("api/auth/demo")]
public sealed class DemoAuthenticationController(
    IHttpClientFactory clients,
    IConfiguration configuration,
    IWebHostEnvironment environment) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("users")]
    public async Task<ActionResult<IReadOnlyList<DemoUser>>> GetUsers(string? role, CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        var users = await GetDirectoryUsersAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(role))
        {
            users = users.Where(user => string.Equals(user.Role, role, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return Ok(users);
    }

    [AllowAnonymous]
    [HttpPost("token")]
    public async Task<ActionResult<DemoTokenResponse>> CreateToken(
        DemoTokenRequest request,
        CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        var users = await GetDirectoryUsersAsync(cancellationToken);
        var user = users.SingleOrDefault(candidate => candidate.UserId == request.UserId
            && string.Equals(candidate.Role, request.Role, StringComparison.OrdinalIgnoreCase));
        if (user is null)
        {
            return Unauthorized();
        }

        var issuer = configuration["Authentication:Issuer"]
            ?? throw new InvalidOperationException("The token issuer is not configured.");
        var audience = configuration["Authentication:Audience"]
            ?? throw new InvalidOperationException("The token audience is not configured.");
        var signingKey = configuration["Authentication:SigningKey"]
            ?? "campuspulse-demo-only-signing-key-change-before-any-use";
        if (Encoding.UTF8.GetByteCount(signingKey) < 32)
        {
            throw new InvalidOperationException("The token signing key must contain at least 32 bytes.");
        }

        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(60);
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            SecurityAlgorithms.HmacSha256);
        var subject = new ClaimsIdentity(
        [
            new Claim(JwtRegisteredClaimNames.Sub, user.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim("user_id", user.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Name, user.FirstName + " " + user.LastName),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim("scope", "CampusPulse.Access")
        ]);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = subject,
            NotBefore = now,
            Expires = expires,
            SigningCredentials = credentials
        };
        var token = new JwtSecurityTokenHandler().CreateEncodedJwt(descriptor);

        return Ok(new DemoTokenResponse(token, "Bearer", expires, user));
    }

    private async Task<List<DemoUser>> GetDirectoryUsersAsync(CancellationToken cancellationToken)
    {
        using var response = await clients.CreateClient("academicRecordsDatabase")
            .GetAsync("/api/users", cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<DemoUser>>(cancellationToken: cancellationToken) ?? [];
    }

    public sealed record DemoTokenRequest(int UserId, string Role);

    public sealed record DemoTokenResponse(string AccessToken, string TokenType, DateTime ExpiresAt, DemoUser User);

    public sealed record DemoUser(
        int UserId,
        string FirstName,
        string LastName,
        string Email,
        string Role,
        string? StudentId,
        string? StaffId);
}
