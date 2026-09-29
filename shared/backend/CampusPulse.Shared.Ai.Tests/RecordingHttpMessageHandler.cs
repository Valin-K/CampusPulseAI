using System.Net;
using System.Text;
using System.Text.Json;

namespace CampusPulse.Shared.Ai.Tests;

internal sealed class RecordingHttpMessageHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public List<RecordedRequest> Requests { get; } = [];

    public RecordingHttpMessageHandler(string response, HttpStatusCode status = HttpStatusCode.OK)
        : this((_, _) => Task.FromResult(JsonResponse(response, status)))
    {
    }

    public static HttpResponseMessage JsonResponse(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        JsonElement body = default;
        if (request.Content is not null)
        {
            using var document = JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
            body = document.RootElement.Clone();
        }

        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, body,
            request.Headers.ToDictionary(header => header.Key, header => string.Join(",", header.Value),
                StringComparer.OrdinalIgnoreCase)));
        return await respond(request, cancellationToken);
    }
}

internal sealed record RecordedRequest(
    HttpMethod Method, Uri Uri, JsonElement Body, IReadOnlyDictionary<string, string> Headers);
