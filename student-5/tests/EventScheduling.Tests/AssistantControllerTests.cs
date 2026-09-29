using EventScheduling.Backend.Controllers;
using EventScheduling.Backend.Models;
using EventScheduling.Backend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace EventScheduling.Tests;

public sealed class AssistantControllerTests
{
    [Test]
    public async Task ChatReturnsOkWithTheAssistantResponse()
    {
        var expected = new AssistantChatResponse("hello", null, "gemini-test");
        var controller = Controller(_ => Task.FromResult(expected));

        var result = await controller.Chat(new AssistantChatRequest(), CancellationToken.None);

        Assert.That((result as OkObjectResult)?.Value, Is.SameAs(expected));
    }

    [Test]
    public async Task ChatMapsInvalidModelOutputTo503()
    {
        var controller = Controller(_ => throw new InvalidOperationException("The AI service returned an invalid reply."));

        var result = await controller.Chat(new AssistantChatRequest(), CancellationToken.None);

        Assert.That((result as ObjectResult)?.StatusCode, Is.EqualTo(503));
    }

    [Test]
    public async Task ChatMapsDependencyFailureTo503()
    {
        var controller = Controller(_ => throw new HttpRequestException("db down"));

        var result = await controller.Chat(new AssistantChatRequest(), CancellationToken.None);

        Assert.That((result as ObjectResult)?.StatusCode, Is.EqualTo(503));
    }

    private static AssistantController Controller(Func<AssistantChatRequest, Task<AssistantChatResponse>> handler) =>
        new(new StubAssistant(handler), NullLogger<AssistantController>.Instance);

    private sealed class StubAssistant(Func<AssistantChatRequest, Task<AssistantChatResponse>> handler)
        : IBookingAssistantService
    {
        public Task<AssistantChatResponse> ChatAsync(AssistantChatRequest request, CancellationToken cancellationToken) =>
            handler(request);
    }
}
