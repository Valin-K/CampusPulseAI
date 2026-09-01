using System.Net;
using System.Text;
using System.Text.Json;
using CourseManagement.Backend.Contracts.CourseAccess;
using CourseManagement.Backend.Clients;

namespace CourseManagement.Tests;

public sealed class CourseCatalogueClientTests
{
    [Test]
    public async Task GetByCodeFindsCourseWithoutCaseSensitivity()
    {
        const string json = """
            [{
              "id": 7,
              "code": "CPAI3001",
              "name": "Microservices",
              "isActive": true
            }]
            """;
        var client = CreateClient(HttpStatusCode.OK, json);

        var course = await client.GetByCodeAsync(
            "cpai3001",
            CancellationToken.None);

        Assert.That(
            course,
            Is.EqualTo(new CourseCatalogueResponse(
                7,
                "CPAI3001",
                true)));
    }

    [Test]
    public async Task GetByCodeReturnsNullWhenCourseDoesNotExist()
    {
        var client = CreateClient(HttpStatusCode.OK, "[]");

        var course = await client.GetByCodeAsync(
            "UNKNOWN",
            CancellationToken.None);

        Assert.That(course, Is.Null);
    }

    [Test]
    public void GetByCodeRejectsFailedCourseResponse()
    {
        var client = CreateClient(
            HttpStatusCode.ServiceUnavailable,
            "Unavailable");

        Assert.That(
            async () => await client.GetByCodeAsync(
                "CPAI3001",
                CancellationToken.None),
            Throws.TypeOf<CourseCatalogueResponseException>());
    }

    [Test]
    public async Task CheckPrerequisiteEligibilityPostsEvidenceAndReadsResult()
    {
        const string json = """
            {
              "courseId": 7,
              "courseCode": "CPAI3001",
              "isEligible": true,
              "requirements": []
            }
            """;
        var client = CreateClient(HttpStatusCode.OK, json, out var handler);

        var result = await client.CheckPrerequisiteEligibilityAsync(
            7,
            [new CompletedCourseRequest("CPAI2001", "Credit")],
            CancellationToken.None);

        using var body = JsonDocument.Parse(handler.RequestBody!);
        var completedCourse = body.RootElement
            .GetProperty("completedCourses")[0];
        Assert.Multiple(() =>
        {
            Assert.That(handler.Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(
                handler.RequestUri?.PathAndQuery,
                Is.EqualTo("/api/courses/7/prerequisite-eligibility"));
            Assert.That(
                completedCourse.GetProperty("courseCode").GetString(),
                Is.EqualTo("CPAI2001"));
            Assert.That(
                completedCourse.GetProperty("grade").GetString(),
                Is.EqualTo("Credit"));
            Assert.That(result.IsEligible, Is.True);
        });
    }

    [Test]
    public void CheckPrerequisiteEligibilityRejectsFailedResponse()
    {
        var client = CreateClient(
            HttpStatusCode.ServiceUnavailable,
            "Unavailable");

        Assert.That(
            async () => await client.CheckPrerequisiteEligibilityAsync(
                7,
                [],
                CancellationToken.None),
            Throws.TypeOf<CourseCatalogueResponseException>());
    }

    static CourseCatalogueClient CreateClient(
        HttpStatusCode statusCode,
        string content) =>
        CreateClient(statusCode, content, out _);

    static CourseCatalogueClient CreateClient(
        HttpStatusCode statusCode,
        string content,
        out StubHttpMessageHandler handler)
    {
        handler = new StubHttpMessageHandler(
            new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(
                    content,
                    Encoding.UTF8,
                    "application/json")
            });
        return new CourseCatalogueClient(
            new HttpClient(handler)
            {
                BaseAddress = new Uri("http://course-catalogue.test/")
            });
    }

    sealed class StubHttpMessageHandler(
        HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return response;
        }
    }
}
