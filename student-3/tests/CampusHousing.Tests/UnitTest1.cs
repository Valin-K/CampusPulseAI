using CampusHousing.Backend.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace CampusHousing.Tests;

public class MaintenanceClassifierTests
{
    [TestCase("There is a gas leak in the kitchen", "Emergency", "Plumbing")]
    [TestCase("The hallway light is broken", "Low", "Electrical")]
    [TestCase("The door lock is not working", "Low", "Security")]
    public async Task ClassifyAsync_WithoutGemini_UsesRuleBasedClassification(string description, string severity, string queue)
    {
        var classifier = new MaintenanceClassifier(
            new EmptyHttpClientFactory(),
            new ConfigurationBuilder().Build(),
            NullLogger<MaintenanceClassifier>.Instance);

        var result = await classifier.ClassifyAsync(description, CancellationToken.None);

        Assert.That(result.Severity, Is.EqualTo(severity));
        Assert.That(result.RepairQueue, Is.EqualTo(queue));
    }

    private sealed class EmptyHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new NotSupportedException();
    }
}
