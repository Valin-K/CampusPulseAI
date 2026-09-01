using System.Net;
using System.Text;
using System.Text.Json;
using CourseManagement.Backend.Clients;

namespace CourseManagement.Tests;

public sealed class AcademicRecordsClientTests
{
    [Test]
    public async Task GetUsersDeserializesPublishedContract()
    {
        const string json = """
            [{
              "userId": 3,
              "firstName": "Joyce",
              "lastName": "Yap",
              "email": "joyce.yap@example.edu",
              "role": "Student",
              "studentId": "STU003",
              "staffId": null
            }]
            """;
        var client = CreateClient(HttpStatusCode.OK, json);

        var users = await client.GetUsersAsync(CancellationToken.None);

        Assert.That(
            users.Single(),
            Is.EqualTo(new AcademicRecordsUserResponse(
                3,
                "Joyce",
                "Yap",
                "joyce.yap@example.edu",
                "Student",
                "STU003",
                null)));
    }

    [Test]
    public void GetUsersRejectsMalformedJson()
    {
        var client = CreateClient(HttpStatusCode.OK, "{not-json");

        Assert.That(
            async () => await client.GetUsersAsync(CancellationToken.None),
            Throws.TypeOf<AcademicRecordsResponseException>());
    }

    [Test]
    public void GetUsersRejectsNonSuccessResponse()
    {
        var client = CreateClient(HttpStatusCode.InternalServerError, "Database unavailable");

        Assert.That(
            async () => await client.GetUsersAsync(CancellationToken.None),
            Throws.TypeOf<AcademicRecordsResponseException>());
    }

    [Test]
    public async Task GetStudentDeserializesEnrollmentSummary()
    {
        const string json = """
            {
              "userId": 3,
              "studentId": "STU003",
              "firstName": "Joyce",
              "enrollments": [{
                "enrollmentId": 12,
                "courseCode": "CPAI3001",
                "academicTermId": 15,
                "status": "Enrolled",
                "credits": 6,
                "isFinalized": false
              }]
            }
            """;
        var client = CreateClient(HttpStatusCode.OK, json);

        var student = await client.GetStudentAsync(
            "STU003",
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(student.StudentId, Is.EqualTo("STU003"));
            Assert.That(student.Enrollments.Single(), Is.EqualTo(
                new AcademicRecordsEnrollmentResponse(
                    12,
                    "CPAI3001",
                    15,
                    "Enrolled",
                    6,
                    false)));
        });
    }

    [Test]
    public async Task CreateEnrollmentPostsPublishedAcademicRecordsContract()
    {
        var handler = new StubHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.Created));
        var client = new AcademicRecordsClient(
            new HttpClient(handler)
            {
                BaseAddress = new Uri("http://academic-records.test/")
            });

        await client.CreateEnrollmentAsync(
            "STU003",
            new AcademicRecordsEnrollmentRequest("CPAI3001", 15, 2026),
            CancellationToken.None);

        using var requestJson = JsonDocument.Parse(handler.RequestBody!);
        Assert.Multiple(() =>
        {
            Assert.That(handler.RequestMethod, Is.EqualTo(HttpMethod.Post));
            Assert.That(
                handler.RequestUri,
                Is.EqualTo(new Uri(
                    "http://academic-records.test/api/students/STU003/enrollments")));
            Assert.That(
                requestJson.RootElement.GetProperty("courseCode").GetString(),
                Is.EqualTo("CPAI3001"));
            Assert.That(
                requestJson.RootElement.GetProperty("academicTermId").GetInt32(),
                Is.EqualTo(15));
            Assert.That(
                requestJson.RootElement.GetProperty("academicYear").GetInt32(),
                Is.EqualTo(2026));
        });
    }

    static AcademicRecordsClient CreateClient(HttpStatusCode statusCode, string content)
    {
        var handler = new StubHttpMessageHandler(
            new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });
        return new AcademicRecordsClient(
            new HttpClient(handler)
            {
                BaseAddress = new Uri("http://academic-records.test/")
            });
    }

    sealed class StubHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpMethod? RequestMethod { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestMethod = request.Method;
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return response;
        }
    }
}
