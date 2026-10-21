using System.Globalization;
using Microsoft.JSInterop;

namespace CampusPulse.Shared.Components.Services;

public sealed class UserIdHeaderHandler(IJSRuntime javaScript) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            var storedId = await javaScript.InvokeAsync<string?>(
                "campusPulseAccountSession.getUserId",
                cancellationToken);
            if (int.TryParse(storedId, NumberStyles.None, CultureInfo.InvariantCulture, out var userId)
                && userId > 0)
            {
                request.Headers.Remove("X-CampusPulse-User-Id");
                request.Headers.TryAddWithoutValidation(
                    "X-CampusPulse-User-Id",
                    userId.ToString(CultureInfo.InvariantCulture));
            }
        }
        catch (JSException)
        {
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
