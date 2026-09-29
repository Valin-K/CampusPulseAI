using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace FinancialAidBilling.Tests;

internal sealed record RecordedRequest(HttpMethod Method, string Path, string? Body);

internal sealed class RecordingHttpHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public List<RecordedRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(new(request.Method, request.RequestUri!.AbsolutePath,
            request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
        return await respond(request, cancellationToken);
    }
}

internal sealed class TestDatabaseFactory(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name)
    {
        Assert.That(name, Is.EqualTo("billingDatabase"));
        return client;
    }
}

internal static class TestHttp
{
    public static readonly JsonSerializerOptions JsonOptions = JsonSerializerOptions.Web;

    public static HttpClient Client(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("https://integration.test/") };

    public static HttpResponseMessage Json<T>(T value) =>
        new(System.Net.HttpStatusCode.OK) { Content = JsonContent.Create(value) };

    public static HttpResponseMessage Raw(string value) =>
        new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
}
