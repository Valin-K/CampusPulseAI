using System.Net;
using System.Text;
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
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }
}
