using CourseManagement.Backend.Contracts.Ai;
using CourseManagement.Backend.Controllers;
using CourseManagement.Backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace CourseManagement.Tests;

public sealed class CourseQuestionsControllerTests
{
    [Test]
    public async Task AskReturnsGeminiAnswer()
    {
        var expected = new CourseQuestionResponse(
            "A concise Course answer.",
            "gemini-test",
            IsSyllabusGrounded: false);
        var controller = CreateController(new StubCourseQuestionService(expected));

        var result = await controller.Ask(
            Request(),
            CancellationToken.None);

        var ok = result.Result as OkObjectResult;
        Assert.That(ok, Is.Not.Null);
        Assert.That(ok!.Value, Is.EqualTo(expected));
    }

    [Test]
    public void LegacyRequestDeserializesWithoutCourses()
    {
        const string json = """
            {
              "courseCode": "CPAI3001",
              "courseName": "Microservices Architecture",
              "question": "What should I understand before taking this Course?"
            }
            """;

        var request = JsonSerializer.Deserialize<CourseQuestionRequest>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Multiple(() =>
        {
            Assert.That(request, Is.Not.Null);
            Assert.That(request!.Courses, Is.Empty);
            Assert.That(request.CourseCode, Is.EqualTo("CPAI3001"));
            Assert.That(
                request.CourseName,
                Is.EqualTo("Microservices Architecture"));
        });
    }

    [TestCase(typeof(GeminiConfigurationException), StatusCodes.Status503ServiceUnavailable)]
    [TestCase(typeof(HttpRequestException), StatusCodes.Status503ServiceUnavailable)]
    [TestCase(typeof(GeminiResponseException), StatusCodes.Status502BadGateway)]
    public async Task AskMapsGeminiFailure(
        Type exceptionType,
        int expectedStatus)
    {
        var exception = exceptionType == typeof(GeminiResponseException)
            ? new GeminiResponseException(
                "Gemini failure.",
                new JsonException("Invalid response."))
            : (Exception)Activator.CreateInstance(
                exceptionType,
                "Gemini failure.")!;
        var controller = CreateController(
            new StubCourseQuestionService(exception));

        var result = await controller.Ask(
            Request(),
            CancellationToken.None);

        var problem = result.Result as ObjectResult;
        Assert.Multiple(() =>
        {
            Assert.That(problem, Is.Not.Null);
            Assert.That(problem!.StatusCode, Is.EqualTo(expectedStatus));
            Assert.That(problem.Value, Is.TypeOf<ProblemDetails>());
        });
    }

    static CourseQuestionsController CreateController(
        ICourseQuestionService service) =>
        new(
            service,
            NullLogger<CourseQuestionsController>.Instance);

    static CourseQuestionRequest Request() =>
        new()
        {
            Question = "What should I understand before taking this Course?",
            CoursesExplicitlySelected = true,
            Courses =
            [
                new CourseQuestionContext
                {
                    Code = "CPAI3001",
                    Name = "Microservices Architecture",
                    Description = "Design independently deployable services.",
                    Level = 3,
                    CreditPoints = 6
                }
            ]
        };

    sealed class StubCourseQuestionService : ICourseQuestionService
    {
        readonly CourseQuestionResponse? response;
        readonly Exception? exception;

        public StubCourseQuestionService(CourseQuestionResponse response)
        {
            this.response = response;
        }

        public StubCourseQuestionService(Exception exception)
        {
            this.exception = exception;
        }

        public Task<CourseQuestionResponse> AskAsync(
            CourseQuestionRequest request,
            CancellationToken cancellationToken) =>
            exception is null
                ? Task.FromResult(response!)
                : Task.FromException<CourseQuestionResponse>(exception);
    }
}
