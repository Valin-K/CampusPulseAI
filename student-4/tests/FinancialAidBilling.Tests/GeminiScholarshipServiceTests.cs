using System.Net;
using System.Net.Http.Json;
using CampusPulse.Shared.Ai;
using FinancialAidBilling.Backend.Models;
using FinancialAidBilling.Backend.Services;

namespace FinancialAidBilling.Tests;

public sealed class GeminiScholarshipServiceTests
{
    [Test]
    public async Task RecommendPreservesEligibilityAndDescendingAwardOrderInContext()
    {
        var gateway = new RecordingGateway();
        var service = new GeminiScholarshipService(new DatabaseFactory(), gateway);

        var response = await service.RecommendAsync(new StudentProfile(4, "Student Name", "Computer Science", 2), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(response.StudentId, Is.EqualTo(4));
            Assert.That(response.MatchCount, Is.EqualTo(2));
            Assert.That(response.Recommendation, Is.EqualTo("Scholarship recommendation"));
            Assert.That(gateway.Prompt, Does.Contain("Recommend scholarships only"));
            Assert.That(gateway.Prompt, Does.Not.Contain("Student Name"));
            Assert.That(gateway.Context, Does.Contain("Student Name"));
            Assert.That(gateway.Context, Does.Contain("STEM Award"));
            Assert.That(gateway.Context, Does.Contain("General Award"));
            Assert.That(gateway.Context, Does.Not.Contain("Excluded Award"));
            Assert.That(gateway.Context!.IndexOf("STEM Award", StringComparison.Ordinal),
                Is.LessThan(gateway.Context.IndexOf("General Award", StringComparison.Ordinal)));
        });
    }

    [Test]
    public void RecommendPropagatesGatewayFailure()
    {
        var service = new GeminiScholarshipService(new DatabaseFactory(), new RecordingGateway(fail: true));
        Assert.That(async () => await service.RecommendAsync(new StudentProfile(4, "Student", "Science", 2), CancellationToken.None),
            Throws.TypeOf<HttpRequestException>());
    }

    [Test]
    public void RecommendPropagatesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = new GeminiScholarshipService(new DatabaseFactory(), new RecordingGateway());
        Assert.That(async () => await service.RecommendAsync(new StudentProfile(4, "Student", "Science", 2), cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    private sealed class RecordingGateway(bool fail = false) : IAiGatewayClient
    {
        public Task<AiEmbeddingResponse> EmbedAsync(IReadOnlyList<string> texts, bool isQuery, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AiGatewayStatus> GetStatusAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public string? Prompt { get; private set; }
        public string? Context { get; private set; }

        public Task<AiGenerationResponse> GenerateAsync(string prompt, string context, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Prompt = prompt;
            Context = context;
            return fail
                ? Task.FromException<AiGenerationResponse>(new HttpRequestException("Gateway unavailable"))
                : Task.FromResult(new AiGenerationResponse("Scholarship recommendation", "gateway-test"));
        }
    }

    private sealed class DatabaseFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.That(name, Is.EqualTo("billingDatabase"));
            return new HttpClient(new DatabaseHandler()) { BaseAddress = new Uri("https://database.test/") };
        }
    }

    private sealed class DatabaseHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ScholarshipResponse[] scholarships =
            [
                new(1, "General Award", "Any major", 1000, 2, "Any", 1, new DateOnly(2030, 1, 1), true),
                new(2, "STEM Award", "STEM major", 2000, 3, "STEM", 2, new DateOnly(2030, 2, 1), true),
                new(3, "Excluded Award: year", "", 3000, 3, "Any", 4, new DateOnly(2030, 1, 1), true),
                new(4, "Excluded Award: inactive", "", 3000, 3, "Any", 1, new DateOnly(2030, 1, 1), false),
                new(5, "Excluded Award: major", "", 3000, 3, "Music", 1, new DateOnly(2030, 1, 1), true)
            ];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(scholarships) });
        }
    }
}
