using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.JSInterop;

namespace CampusPulse.Shared.Components.Services;

public sealed class TokenHandler(IJSRuntime javaScript) : DelegatingHandler
{
    public const string SessionStorageKey = "campuspulse.auth.session";

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            var sessionJson = await javaScript.InvokeAsync<string?>(
                "sessionStorage.getItem",
                cancellationToken,
                SessionStorageKey);
            if (!string.IsNullOrWhiteSpace(sessionJson))
            {
                using var document = JsonDocument.Parse(sessionJson);
                if (document.RootElement.TryGetProperty("accessToken", out var tokenElement)
                    && tokenElement.GetString() is { Length: > 0 } token)
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }
            }
        }
        catch (Exception exception) when (exception is JSException or JsonException)
        {
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
