using AcademicRecords.Backend.Contracts;
using AcademicRecords.Backend.Services;
using CampusPulse.Shared.Ai;
using Microsoft.Extensions.Logging.Abstractions;

namespace AcademicRecords.Tests;

public sealed class GeminiAcademicProgressServiceTests
{
    [Test]
    public async Task AnalyzeSeparatesInstructionsAndStudentContext()
    {
        var gateway = new RecordingGateway("""
            {"expectedGraduationTerm":"T2 2028","expectedGraduationDate":"2028-11-01","explanation":"Observed pace","riskFlags":[]}
            """);
        var service = CreateService(gateway);

        var result = await service.AnalyzeAsync(new StudentStatisticsProfile
        {
            FirstName = "TestStudent",
            Major = "Computer Science",
            Enrollments = []
        }, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Prediction!.ExpectedGraduationTerm, Is.EqualTo("T2 2028"));
            Assert.That(gateway.Prompt, Does.Contain("Return JSON only"));
            Assert.That(gateway.Prompt, Does.Not.Contain("TestStudent"));
            Assert.That(gateway.Context, Does.Contain("TestStudent"));
            Assert.That(gateway.Context, Does.Contain("Computer Science"));
            Assert.That(result.Metrics.EarnedCredits, Is.Zero);
        });
    }

    [TestCase("not json")]
    [TestCase("null")]
    [TestCase("{}")]
    public async Task AnalyzeInvalidModelOutputPreservesFailureResponse(string text)
    {
        var result = await CreateService(new RecordingGateway(text))
            .AnalyzeAsync(new StudentStatisticsProfile(), CancellationToken.None);

        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.Status, Is.EqualTo("Failed"));
    }

    [Test]
    public void AnalyzePropagatesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.That(async () => await CreateService(new RecordingGateway("{}"))
            .AnalyzeAsync(new StudentStatisticsProfile(), cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    private static GeminiAcademicProgressService CreateService(IAiGatewayClient gateway) =>
        new(gateway, NullLogger<GeminiAcademicProgressService>.Instance);

    private sealed class RecordingGateway(string text) : IAiGatewayClient
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
            return Task.FromResult(new AiGenerationResponse(text, "gateway-test"));
        }
    }
}
