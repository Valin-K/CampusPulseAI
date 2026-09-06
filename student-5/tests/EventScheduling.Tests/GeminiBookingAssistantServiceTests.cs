using System.Net;
using System.Text;
using EventScheduling.Backend.Clients;
using EventScheduling.Backend.Models;
using EventScheduling.Backend.Services;
using Microsoft.Extensions.Configuration;

namespace EventScheduling.Tests;

public sealed class GeminiBookingAssistantServiceTests
{
    [Test]
    public async Task ChatReturnsReplyAndValidatesRoomProposal()
    {
        var gemini = new RecordingHttpMessageHandler(Ok(GeminiEnvelope(
            """{"message":"Study Room 101 works for you.","proposal":{"kind":"Room","resourceId":1,"startUtc":"2030-02-03T10:00:00","endUtc":"2030-02-03T12:00:00","rationale":"Seats 4 and is free then."}}""")));
        var service = CreateService(gemini);

        var response = await service.ChatAsync(Request(4, "book a quiet room for 4 on Feb 3 2030 10-12"), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(response.Reply, Is.EqualTo("Study Room 101 works for you."));
            Assert.That(response.Model, Is.EqualTo("gemini-test"));
            Assert.That(response.Proposal, Is.Not.Null);
            Assert.That(response.Proposal!.Kind, Is.EqualTo(AssistantResourceKind.Room));
            Assert.That(response.Proposal.ResourceId, Is.EqualTo(1));
            Assert.That(response.Proposal.ResourceName, Is.EqualTo("Study Room 101"));
            Assert.That(gemini.RequestBody, Does.Contain("STRICT SCOPE"));
            Assert.That(gemini.RequestBody, Does.Contain("Ryan"));
            Assert.That(gemini.RequestBody, Does.Contain("Study Room 101"));
            Assert.That(gemini.RequestBody, Does.Not.Contain("Computer Lab 1"));
            Assert.That(gemini.RequestBody, Does.Contain("book a quiet room for 4"));
        });
    }

    [Test]
    public async Task ChatGivesAStaffUserOnlyLabAndLectureRooms()
    {
        var gemini = new RecordingHttpMessageHandler(Ok(GeminiEnvelope("""{"message":"ok","proposal":null}""")));
        var service = CreateService(gemini);

        await service.ChatAsync(Request(14, "what can I book?"), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(gemini.RequestBody, Does.Contain("Computer Lab 1"));
            Assert.That(gemini.RequestBody, Does.Not.Contain("Study Room 101"));
        });
    }

    [Test]
    public async Task ChatDropsProposalForResourceOutsideTheUsersScope()
    {
        var service = CreateService(new RecordingHttpMessageHandler(Ok(GeminiEnvelope(
            """{"message":"Try this room.","proposal":{"kind":"Room","resourceId":9,"startUtc":"2030-02-03T10:00:00","endUtc":"2030-02-03T12:00:00","rationale":"x"}}"""))));

        var response = await service.ChatAsync(Request(4, "book Computer Lab 1"), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(response.Reply, Is.EqualTo("Try this room."));
            Assert.That(response.Proposal, Is.Null);
        });
    }

    [Test]
    public async Task ChatDropsProposalInThePast()
    {
        var service = CreateService(new RecordingHttpMessageHandler(Ok(GeminiEnvelope(
            """{"message":"Here you go.","proposal":{"kind":"Room","resourceId":1,"startUtc":"2020-01-01T10:00:00","endUtc":"2020-01-01T12:00:00","rationale":"x"}}"""))));

        var response = await service.ChatAsync(Request(4, "book it last year"), CancellationToken.None);

        Assert.That(response.Proposal, Is.Null);
    }

    [Test]
    public async Task ChatDropsEquipmentProposalWhenNoUnitsAvailable()
    {
        var service = CreateService(new RecordingHttpMessageHandler(Ok(GeminiEnvelope(
            """{"message":"Hire this.","proposal":{"kind":"Equipment","resourceId":7,"startUtc":"2030-02-03T10:00:00","endUtc":"2030-02-10T10:00:00","rationale":"x"}}"""))));

        var response = await service.ChatAsync(Request(4, "hire the sold-out kit"), CancellationToken.None);

        Assert.That(response.Proposal, Is.Null);
    }

    [Test]
    public async Task ChatIncludesTheUsersPastAndUpcomingBookingsInThePrompt()
    {
        var gemini = new RecordingHttpMessageHandler(Ok(GeminiEnvelope("""{"message":"ok","proposal":null}""")));
        var service = CreateService(gemini);

        await service.ChatAsync(Request(4, "what did I have booked in March 2020?"), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(gemini.RequestBody, Does.Contain("recent past bookings"));
            Assert.That(gemini.RequestBody, Does.Contain("2020-03-04 09:00"));
            Assert.That(gemini.RequestBody, Does.Contain("2030-05-06 14:00"));
        });
    }

    [Test]
    public async Task ChatToleratesFencedJsonFromTheModel()
    {
        var service = CreateService(new RecordingHttpMessageHandler(Ok(GeminiEnvelope(
            "```json\n{\"message\":\"fenced reply\",\"proposal\":null}\n```"))));

        var response = await service.ChatAsync(Request(4, "hi"), CancellationToken.None);

        Assert.That(response.Reply, Is.EqualTo("fenced reply"));
    }

    [Test]
    public void ChatThrowsWhenApiKeyIsMissing()
    {
        var gemini = new RecordingHttpMessageHandler(Ok(GeminiEnvelope("{}")));
        var service = CreateService(gemini, apiKey: null);

        Assert.Multiple(() =>
        {
            Assert.That(
                async () => await service.ChatAsync(Request(4, "hi"), CancellationToken.None),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(gemini.CallCount, Is.Zero);
        });
    }

    [Test]
    public void ChatThrowsWhenModelTextIsNotJson()
    {
        var service = CreateService(new RecordingHttpMessageHandler(Ok(GeminiEnvelope("just a sentence, not json"))));

        Assert.That(
            async () => await service.ChatAsync(Request(4, "hi"), CancellationToken.None),
            Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void ChatThrowsWhenGeminiReturnsAnError()
    {
        var gemini = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var service = CreateService(gemini);

        Assert.That(
            async () => await service.ChatAsync(Request(4, "hi"), CancellationToken.None),
            Throws.TypeOf<HttpRequestException>());
    }

    [Test]
    public async Task ChatReturnsFallbackWhenUserIsUnknown()
    {
        var gemini = new RecordingHttpMessageHandler(Ok(GeminiEnvelope("{}")));
        var service = CreateService(gemini);

        var response = await service.ChatAsync(Request(999, "hi"), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(response.Reply, Does.Contain("couldn't find"));
            Assert.That(response.Proposal, Is.Null);
            Assert.That(gemini.CallCount, Is.Zero);
        });
    }

    private static AssistantChatRequest Request(int userId, string message) => new()
    {
        UserId = userId,
        Messages = [new AssistantMessage { Role = "user", Content = message }]
    };

    private static readonly IReadOnlyList<AcademicRecordsUser> Users =
    [
        new(4, "Ryan", "Takayasu", "ryan@example.edu", "Student", "STU004", null),
        new(14, "Marcus", "Lee", "marcus@example.edu", "Staff", null, "STF004")
    ];

    private const string RoomsJson =
        """[{"id":1,"name":"Study Room 101","location":"Library","capacity":4,"description":"Quiet.","kind":0},{"id":9,"name":"Computer Lab 1","location":"Engineering","capacity":30,"description":"Lab.","kind":1}]""";

    private const string EquipmentJson =
        """[{"id":5,"name":"Camera Tripod","category":"Media","description":"Tripod.","availableCount":3},{"id":7,"name":"Wireless Presenter","category":"Presentation","description":"Clicker.","availableCount":0}]""";

    private const string BookingsJson =
        """[{"id":100,"studentId":4,"roomId":1,"equipmentId":null,"refEquipmentId":null,"resourceType":0,"resourceName":"Study Room 101","startUtc":"2020-03-04T09:00:00","endUtc":"2020-03-04T11:00:00","status":3},{"id":101,"studentId":4,"roomId":2,"equipmentId":null,"refEquipmentId":null,"resourceType":0,"resourceName":"Group Pod A","startUtc":"2030-05-06T14:00:00","endUtc":"2030-05-06T16:00:00","status":1}]""";

    private static string GeminiEnvelope(string modelText) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            candidates = new[] { new { content = new { parts = new[] { new { text = modelText } } } } }
        });

    private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static GeminiBookingAssistantService CreateService(
        RecordingHttpMessageHandler geminiHandler,
        string? apiKey = "test-key")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GEMINI_API_KEY"] = apiKey,
                ["Gemini:Model"] = "gemini-test"
            })
            .Build();

        var databaseHandler = new RoutingHttpMessageHandler(new Dictionary<string, string>
        {
            ["/api/rooms"] = RoomsJson,
            ["/api/refequipment"] = EquipmentJson,
            ["/api/bookings"] = BookingsJson
        });

        var factory = new NamedHttpClientFactory(new Dictionary<string, HttpMessageHandler>
        {
            ["eventDatabase"] = databaseHandler,
            ["gemini"] = geminiHandler
        });

        return new GeminiBookingAssistantService(factory, configuration, new StubAcademicRecordsClient());
    }

    private sealed class StubAcademicRecordsClient : IAcademicRecordsClient
    {
        public Task<IReadOnlyList<AcademicRecordsUser>> GetUsersAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Users);
    }

    private sealed class NamedHttpClientFactory(IReadOnlyDictionary<string, HttpMessageHandler> handlers) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handlers[name]) { BaseAddress = new Uri("https://service.test/") };
    }

    private sealed class RoutingHttpMessageHandler(IReadOnlyDictionary<string, string> byPath) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            return Task.FromResult(byPath.TryGetValue(path, out var json)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class RecordingHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return response;
        }
    }
}
