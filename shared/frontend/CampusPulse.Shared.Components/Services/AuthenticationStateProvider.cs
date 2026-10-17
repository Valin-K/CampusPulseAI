using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using CampusPulse.Shared.Components.Models;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace CampusPulse.Shared.Components.Services;

public sealed record Session(
    string AccessToken,
    DateTime ExpiresAt,
    CampusAccountUser User);

public sealed class AuthenticationStateProvider : Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider
{
    private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity());
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient http;
    private readonly IJSRuntime javaScript;
    private Session? currentSession;
    private bool initialized;

    public AuthenticationStateProvider(HttpClient http, IJSRuntime javaScript)
    {
        this.http = http;
        this.javaScript = javaScript;
    }

    public Session? CurrentSession => currentSession;

    public async Task<IReadOnlyList<CampusAccountUser>> GetUsersAsync(string role)
    {
        using var response = await http.GetAsync($"api/auth/demo/users?role={Uri.EscapeDataString(role)}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<CampusAccountUser>>(JsonOptions) ?? [];
    }

    public async Task SignInAsync(int userId, string role, string password)
    {
        using var response = await http.PostAsJsonAsync("api/auth/demo/token", new { userId, role, password });
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException("The account or password is incorrect.", null, response.StatusCode);
        }

        var token = await response.Content.ReadFromJsonAsync<DemoTokenResponse>(JsonOptions)
            ?? throw new JsonException("The Academic Records API returned no demo token.");
        currentSession = new Session(token.AccessToken, token.ExpiresAt, token.User);
        await StoreSessionAsync(currentSession);
        initialized = true;
        NotifyAuthenticationStateChanged(Task.FromResult(CreateAuthenticationState(currentSession)));
    }

    public async Task SignOutAsync()
    {
        currentSession = null;
        initialized = true;
        await javaScript.InvokeVoidAsync("sessionStorage.removeItem", TokenHandler.SessionStorageKey);
        await javaScript.InvokeVoidAsync("campusPulseAccountSession.removeUserId");
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(Anonymous)));
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (!initialized)
        {
            initialized = true;
            var value = await javaScript.InvokeAsync<string?>(
                "sessionStorage.getItem",
                TokenHandler.SessionStorageKey);
            if (!string.IsNullOrWhiteSpace(value))
            {
                currentSession = JsonSerializer.Deserialize<Session>(value, JsonOptions);
                if (currentSession is not null && currentSession.ExpiresAt <= DateTime.UtcNow)
                {
                    currentSession = null;
                    await javaScript.InvokeVoidAsync("sessionStorage.removeItem", TokenHandler.SessionStorageKey);
                    await javaScript.InvokeVoidAsync("campusPulseAccountSession.removeUserId");
                }
                else if (currentSession is not null)
                {
                    await javaScript.InvokeVoidAsync(
                        "campusPulseAccountSession.setUserId",
                        currentSession.User.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }

            if (currentSession is null)
            {
                var transferredSession = await javaScript.InvokeAsync<string?>(
                    "campusPulseNavigation.getSessionFragment");
                if (!string.IsNullOrWhiteSpace(transferredSession))
                {
                    try
                    {
                        currentSession = JsonSerializer.Deserialize<Session>(transferredSession, JsonOptions);
                        if (currentSession is not null && currentSession.ExpiresAt > DateTime.UtcNow)
                        {
                            await StoreSessionAsync(currentSession);
                        }
                        else
                        {
                            currentSession = null;
                        }
                    }
                    catch (JsonException)
                    {
                        currentSession = null;
                    }

                    await javaScript.InvokeVoidAsync("campusPulseNavigation.cleanSessionFragment");
                }
            }
        }

        return currentSession is null
            ? new AuthenticationState(Anonymous)
            : CreateAuthenticationState(currentSession);
    }

    private static AuthenticationState CreateAuthenticationState(Session session)
    {
        var user = session.User;
        var role = ReadSessionRole(session.AccessToken);
        if (role is null)
        {
            return new AuthenticationState(Anonymous);
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Name, user.FullName),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, role)
        };
        return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(claims, "CampusPulseDemo")));
    }

    private static string? ReadSessionRole(string accessToken)
    {
        var segments = accessToken.Split('.');
        if (segments.Length != 3)
        {
            return null;
        }

        try
        {
            var payload = segments[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=');
            using var document = JsonDocument.Parse(Convert.FromBase64String(payload));
            if (!document.RootElement.TryGetProperty("role", out var roleClaim)
                && !document.RootElement.TryGetProperty(ClaimTypes.Role, out roleClaim))
            {
                return null;
            }

            if (roleClaim.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return roleClaim.GetString()?.ToUpperInvariant() switch
            {
                "STUDENT" => "Student",
                "STAFF" => "Staff",
                _ => null
            };
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task StoreSessionAsync(Session session)
    {
        await javaScript.InvokeVoidAsync(
            "sessionStorage.setItem",
            TokenHandler.SessionStorageKey,
            JsonSerializer.Serialize(session, JsonOptions));
        await javaScript.InvokeVoidAsync(
            "campusPulseAccountSession.setUserId",
            session.User.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private sealed record DemoTokenResponse(
        string AccessToken,
        string TokenType,
        DateTime ExpiresAt,
        CampusAccountUser User);
}
