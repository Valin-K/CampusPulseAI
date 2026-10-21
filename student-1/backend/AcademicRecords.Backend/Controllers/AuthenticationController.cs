using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace AcademicRecords.Backend.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthenticationController(
    IHttpClientFactory clients,
    IConfiguration configuration) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("token")]
    public async Task<ActionResult<TokenResponse>> CreateToken(
        TokenRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await clients.CreateClient("academicRecordsCredentials")
            .PostAsJsonAsync("/api/users/authenticate", new
            {
                request.AccountId,
                request.Role,
                request.Password
            }, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return Unauthorized();
        }

        response.EnsureSuccessStatusCode();
        var user = await response.Content.ReadFromJsonAsync<AuthenticatedUser>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("The Academic Records database returned no authenticated user.");
        var role = user.Role.ToUpperInvariant() switch
        {
            "STUDENT" => "Student",
            "STAFF" => "Staff",
            _ => throw new InvalidOperationException("The Academic Records database returned an unsupported user role.")
        };
        user = user with { Role = role };

        var issuer = configuration["Authentication:Issuer"]
            ?? throw new InvalidOperationException("The token issuer is not configured.");
        var audience = configuration["Authentication:Audience"]
            ?? throw new InvalidOperationException("The token audience is not configured.");
        var signingKey = configuration["Authentication:SigningKey"]
            ?? throw new InvalidOperationException("The token signing key is not configured.");
        if (string.IsNullOrWhiteSpace(signingKey) || Encoding.UTF8.GetByteCount(signingKey) < 32)
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
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new Claim("user_id", user.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Name, user.FirstName + " " + user.LastName),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim("scope", "CampusPulse.Access")
        ]);
        if (!string.IsNullOrWhiteSpace(user.StudentId))
        {
            subject.AddClaim(new Claim("student_id", user.StudentId));
        }
        if (!string.IsNullOrWhiteSpace(user.StaffId))
        {
            subject.AddClaim(new Claim("staff_id", user.StaffId));
        }
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

        return Ok(new TokenResponse(token, "Bearer", expires, user));
    }

    public sealed record TokenRequest(string AccountId, string Role, string Password);

    public sealed record TokenResponse(string AccessToken, string TokenType, DateTime ExpiresAt, AuthenticatedUser User);

    public sealed record AuthenticatedUser(
        int UserId,
        string FirstName,
        string LastName,
        string Email,
        string Role,
        string? StudentId,
        string? StaffId);
}
