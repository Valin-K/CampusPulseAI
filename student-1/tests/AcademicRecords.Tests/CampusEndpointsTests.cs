using System.Net;
using System.Net.Http.Json;
using CampusPulse.Shared.Components.Services;

namespace AcademicRecords.Tests;

public sealed class CampusEndpointsTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task ConfiguredOriginsSupportAccountLinksAndReturnUrls(bool azure)
    {
        var urls = CreateUrls(azure);
        using var client = CreateClient(new { UseSameOriginApi = azure, Urls = urls, AdditionalOrigins = Array.Empty<string>() });
        var endpoints = await CampusEndpoints.LoadAsync(client);
        var returnUrl = urls["FinancialAidBilling"] + "invoices?student=1&status=due";

        Assert.That(endpoints.UseSameOriginApi, Is.EqualTo(azure));
        Assert.That(endpoints.Portal, Is.EqualTo(urls["Portal"]));
        Assert.That(endpoints.AccountUrl("/login/student", returnUrl),
            Is.EqualTo(urls["AcademicRecords"] + "login/student?returnUrl=" + Uri.EscapeDataString(returnUrl)));
        foreach (var address in urls.Values)
        {
            Assert.That(endpoints.IsAllowedReturnUrl(address + "profile"), Is.True, address);
        }
        Assert.That(endpoints.IsAllowedReturnUrl("/invoices?status=due"), Is.True);
    }

    [TestCase("https://attacker.example/")]
    [TestCase("https://academicrecords-web.example.azurecontainerapps.io.attacker.example/")]
    [TestCase("http://academicrecords-web.example.azurecontainerapps.io/")]
    [TestCase("https://academicrecords-web.example.azurecontainerapps.io:8443/")]
    [TestCase("https://attacker@academicrecords-web.example.azurecontainerapps.io/")]
    [TestCase("http://localhost:8081/")]
    [TestCase("//attacker.example/")]
    [TestCase("/\\attacker.example/")]
    [TestCase("javascript:alert(1)")]
    [TestCase("")]
    public async Task UnconfiguredReturnDestinationsAreRejected(string returnUrl)
    {
        using var client = CreateClient(new { Urls = CreateUrls(true) });
        var endpoints = await CampusEndpoints.LoadAsync(client);

        Assert.That(endpoints.IsAllowedReturnUrl(returnUrl), Is.False);
    }

    [Test]
    public async Task CurrentOriginAndExplicitDevelopmentOriginsAreAllowed()
    {
        using var client = CreateClient(new
        {
            Urls = CreateUrls(false),
            AdditionalOrigins = new[] { "http://localhost:5186" }
        });
        var endpoints = await CampusEndpoints.LoadAsync(client);

        Assert.That(endpoints.IsAllowedReturnUrl("https://current.example/profile"), Is.True);
        Assert.That(endpoints.IsAllowedReturnUrl("http://localhost:5186/courses"), Is.True);
        Assert.That(endpoints.IsAllowedReturnUrl("http://localhost:5187/courses"), Is.False);
    }

    [Test]
    public void MissingConfigurationDoesNotFallBackToLocalhost()
    {
        using var client = CreateClient(new { Urls = new Dictionary<string, string>() });

        Assert.ThrowsAsync<InvalidOperationException>(async () => await CampusEndpoints.LoadAsync(client));
    }

    private static Dictionary<string, string> CreateUrls(bool azure)
    {
        var services = new[] { "Portal", "AcademicRecords", "CourseManagement", "CampusHousing", "FinancialAidBilling", "EventScheduling" };
        var apps = new[] { "campuspulse-portal", "academicrecords-web", "coursemgmt-web", "campushousing-web", "finaid-web", "eventscheduling-web" };
        return services.Select((name, index) => new
        {
            Name = name,
            Url = azure ? $"https://{apps[index]}.example.azurecontainerapps.io/" : $"http://localhost:{8080 + index}/"
        }).ToDictionary(item => item.Name, item => item.Url);
    }

    private static HttpClient CreateClient(object config) => new(new ConfigurationHandler(config))
    {
        BaseAddress = new Uri("https://current.example/")
    };

    private sealed class ConfigurationHandler(object config) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.That(request.RequestUri!.AbsolutePath, Is.EqualTo("/campus-config.json"));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(config) });
        }
    }
}