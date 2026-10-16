using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using CampusPulse.Shared.Components.Models;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace CampusPulse.Shared.Components.Services;

public sealed record CampusDemoSession(
    string AccessToken,
    DateTime ExpiresAt,
    CampusAccountUser User);

public sealed class CampusDemoAuthenticationStateProvider : AuthenticationStateProvider
{
    private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity());
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient http;
    private readonly IJSRuntime javaScript;
    private CampusDemoSession? currentSession;
    private bool initialized;

    public CampusDemoAuthenticationStateProvider(HttpClient http, IJSRuntime javaScript)
    {
        this.http = http;
        this.javaScript = javaScript;
    }

    public CampusDemoSession? CurrentSession => currentSession;

    public async Task<IReadOnlyList<CampusAccountUser>> GetUsersAsync(string role)
    {
        using var response = await http.GetAsync($"api/auth/demo/users?role={Uri.EscapeDataString(role)}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<CampusAccountUser>>(JsonOptions) ?? [];
    }

    public async Task SignInAsync(int userId, string role)
    {
        using var response = await http.PostAsJsonAsync("api/auth/demo/token", new { userId, role });
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException("The selected demo account could not be signed in.", null, response.StatusCode);
        }

        var token = await response.Content.ReadFromJsonAsync<DemoTokenResponse>(JsonOptions)
            ?? throw new JsonException("The Academic Records API returned no demo token.");
        currentSession = new CampusDemoSession(token.AccessToken, token.ExpiresAt, token.User);
        await StoreSessionAsync(currentSession);
        initialized = true;
        NotifyAuthenticationStateChanged(Task.FromResult(CreateAuthenticationState(currentSession)));
    }

    public async Task SignOutAsync()
    {
        currentSession = null;
        initialized = true;
        await javaScript.InvokeVoidAsync("sessionStorage.removeItem", CampusDemoTokenHandler.SessionStorageKey);
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
                CampusDemoTokenHandler.SessionStorageKey);
            if (!string.IsNullOrWhiteSpace(value))
            {
                currentSession = JsonSerializer.Deserialize<CampusDemoSession>(value, JsonOptions);
                if (currentSession is not null && currentSession.ExpiresAt <= DateTime.UtcNow)
                {
                    currentSession = null;
                    await javaScript.InvokeVoidAsync("sessionStorage.removeItem", CampusDemoTokenHandler.SessionStorageKey);
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
                        currentSession = JsonSerializer.Deserialize<CampusDemoSession>(transferredSession, JsonOptions);
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

    private static AuthenticationState CreateAuthenticationState(CampusDemoSession session)
    {
        var user = session.User;
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Name, user.FullName),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role)
        };
        return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(claims, "CampusPulseDemo")));
    }

    private async Task StoreSessionAsync(CampusDemoSession session)
    {
        await javaScript.InvokeVoidAsync(
            "sessionStorage.setItem",
            CampusDemoTokenHandler.SessionStorageKey,
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
