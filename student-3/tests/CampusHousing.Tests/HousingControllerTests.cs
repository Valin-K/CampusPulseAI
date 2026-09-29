using System.Net;
using System.Net.Http.Json;
using CampusHousing.Backend.Controllers;
using CampusHousing.Backend.Models;
using CampusHousing.Backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using CampusPulse.Shared.Ai;
using Microsoft.Extensions.Logging.Abstractions;

namespace CampusHousing.Tests;

public sealed class HousingControllerTests
{
    [Test]
    public async Task Rooms_ForwardsDatabaseResponse()
    {
        var client = new RecordingHttpClient(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new[] { new { id = 1, building = "Maple Hall" } })
        });
        var controller = CreateController(client);

        var result = await controller.Rooms(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.TypeOf<ContentResult>());
            Assert.That(((ContentResult)result).StatusCode, Is.EqualTo(200));
            Assert.That(client.LastRequest!.RequestUri!.ToString(), Is.EqualTo("https://database.test/api/rooms"));
        });
    }

    [Test]
    public async Task Room_ForwardsNotFoundResponseWithId()
    {
        var client = new RecordingHttpClient(new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await CreateController(client).Room(42, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(((ContentResult)result).StatusCode, Is.EqualTo(404));
            Assert.That(client.LastRequest!.RequestUri!.ToString(), Does.EndWith("/api/rooms/42"));
        });
    }

    [Test]
    public async Task CreateRoom_RequiresAdministrator()
    {
        var client = new RecordingHttpClient(new HttpResponseMessage(HttpStatusCode.Created));
        var controller = CreateController(client, studentId: "STU-1");

        var result = await controller.CreateRoom(new CreateRoomRequest("Maple Hall", "103", 2), CancellationToken.None);

        Assert.That(result, Is.TypeOf<ForbidResult>());
        Assert.That(client.LastRequest, Is.Null);
    }

    [Test]
    public async Task CreateRoom_AdministratorForwardsJsonRequest()
    {
        var client = new RecordingHttpClient(new HttpResponseMessage(HttpStatusCode.Created));
        var controller = CreateController(client, administrator: true);

        var result = await controller.CreateRoom(new CreateRoomRequest("Maple Hall", "103", 2), CancellationToken.None);

        Assert.That(((ContentResult)result).StatusCode, Is.EqualTo(201));
        Assert.That(client.LastRequest!.Method, Is.EqualTo(HttpMethod.Post));
        Assert.That(client.LastRequest.RequestUri!.ToString(), Does.EndWith("/api/rooms"));
        Assert.That(client.LastBody, Does.Contain("Maple Hall"));
    }

    [Test]
    public async Task UpdateRoom_AdministratorUsesPutAndForwardsNotFound()
    {
        var client = new RecordingHttpClient(new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await CreateController(client, administrator: true).UpdateRoom(8, new CreateRoomRequest("Oak Hall", "208", 4), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(((ContentResult)result).StatusCode, Is.EqualTo(404));
            Assert.That(client.LastRequest!.Method, Is.EqualTo(HttpMethod.Put));
            Assert.That(client.LastRequest.RequestUri!.ToString(), Does.EndWith("/api/rooms/8"));
        });
    }

    [Test]
    public async Task DeleteRoom_RequiresAdministratorAndForwardsDelete()
    {
        var client = new RecordingHttpClient(new HttpResponseMessage(HttpStatusCode.NoContent));

        var forbidden = await CreateController(client, studentId: "STU-1").DeleteRoom(1, CancellationToken.None);
        var result = await CreateController(client, administrator: true).DeleteRoom(1, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(forbidden, Is.TypeOf<ForbidResult>());
            Assert.That(((ContentResult)result).StatusCode, Is.EqualTo(204));
            Assert.That(client.LastRequest!.Method, Is.EqualTo(HttpMethod.Delete));
        });
    }

    [Test]
    public async Task Allocations_StudentReceivesOnlyOwnAllocations()
    {
        var allocations = new[]
        {
            new HousingAllocation(1, 1, "STU-1", "One", "Quiet", DateTime.UtcNow),
            new HousingAllocation(2, 2, "STU-2", "Two", "Social", DateTime.UtcNow)
        };
        var client = new RecordingHttpClient(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(allocations) });
        var controller = CreateController(client, studentId: "stu-1");

        var result = await controller.Allocations(CancellationToken.None);

        var values = ((OkObjectResult)result).Value as IEnumerable<HousingAllocation>;
        Assert.That(values!.Select(item => item.StudentId), Is.EqualTo(new[] { "STU-1" }));
    }

    [Test]
    public async Task Allocations_AdministratorReceivesDatabasePayloadUnfiltered()
    {
        var client = new RecordingHttpClient(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new[] { new { studentId = "STU-1" }, new { studentId = "STU-2" } })
        });

        var result = await CreateController(client, administrator: true).Allocations(CancellationToken.None);

        Assert.That(((ContentResult)result).StatusCode, Is.EqualTo(200));
        Assert.That(client.LastRequest!.RequestUri!.ToString(), Does.EndWith("/api/allocations"));
    }

    [Test]
    public async Task UpdateAllocation_StudentGetsDatabaseFailureWithoutPutting()
    {
        var client = new RecordingHttpClient(new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await CreateController(client, studentId: "STU-1").UpdateAllocation(12, new UpdateAllocationRequest(1, "STU-1", "One", null), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(((ContentResult)result).StatusCode, Is.EqualTo(404));
            Assert.That(client.LastRequest!.Method, Is.EqualTo(HttpMethod.Get));
        });
    }

    [Test]
    public async Task UpdateAllocation_StudentCannotUpdateAnotherStudentsAllocation()
    {
        var existing = new HousingAllocation(12, 1, "STU-2", "Two", null, DateTime.UtcNow);
        var client = new RecordingHttpClient(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(existing) });

        var result = await CreateController(client, studentId: "STU-1").UpdateAllocation(12, new UpdateAllocationRequest(1, "STU-1", "One", null), CancellationToken.None);

        Assert.That(result, Is.TypeOf<ForbidResult>());
    }

    [Test]
    public async Task UpdateAllocation_AdministratorForwardsPut()
    {
        var client = new RecordingHttpClient(new HttpResponseMessage(HttpStatusCode.OK));

        var result = await CreateController(client, administrator: true).UpdateAllocation(12, new UpdateAllocationRequest(2, "STU-2", "Two", "Quiet"), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(((ContentResult)result).StatusCode, Is.EqualTo(200));
            Assert.That(client.LastRequest!.Method, Is.EqualTo(HttpMethod.Put));
            Assert.That(client.LastRequest.RequestUri!.ToString(), Does.EndWith("/api/allocations/12"));
        });
    }

    [Test]
    public async Task DeleteAllocation_RequiresAdministratorAndForwardsDelete()
    {
        var client = new RecordingHttpClient(new HttpResponseMessage(HttpStatusCode.NoContent));

        var forbidden = await CreateController(client, studentId: "STU-1").DeleteAllocation(1, CancellationToken.None);
        var result = await CreateController(client, administrator: true).DeleteAllocation(1, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(forbidden, Is.TypeOf<ForbidResult>());
            Assert.That(((ContentResult)result).StatusCode, Is.EqualTo(204));
        });
    }

    [Test]
    public async Task Allocate_StudentCannotAllocateForAnotherStudent()
    {
        var client = new RecordingHttpClient(new HttpResponseMessage(HttpStatusCode.Created));
        var controller = CreateController(client, studentId: "STU-1");

        var result = await controller.Allocate(new CreateAllocationRequest(1, "STU-2", "Two", null), CancellationToken.None);

        Assert.That(result, Is.TypeOf<ForbidResult>());
        Assert.That(client.LastRequest, Is.Null);
    }

    [Test]
    public async Task CreateTicket_StudentCannotCreateForAnotherStudent()
    {
        var client = new RecordingHttpClient(new HttpResponseMessage(HttpStatusCode.Created));

        var result = await CreateController(client, studentId: "STU-1").CreateTicket(new CreateMaintenanceRequest("STU-2", 1, "Broken light"), CancellationToken.None);

        Assert.That(result, Is.TypeOf<ForbidResult>());
    }

    [Test]
    public async Task Tickets_StudentReceivesOnlyOwnTickets()
    {
        var tickets = new[]
        {
            new MaintenanceTicket(1, "STU-1", 1, "Light", "Low", "Electrical", "Open", DateTime.UtcNow, null),
            new MaintenanceTicket(2, "STU-2", 1, "Lock", "Low", "Security", "Open", DateTime.UtcNow, null)
        };
        var client = new RecordingHttpClient(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(tickets) });

        var result = await CreateController(client, studentId: "STU-1").Tickets(CancellationToken.None);

        var values = ((OkObjectResult)result).Value as IEnumerable<MaintenanceTicket>;
        Assert.That(values!.Select(ticket => ticket.StudentId), Is.EqualTo(new[] { "STU-1" }));
    }

    [Test]
    public async Task Allocate_AdministratorCanAllocateForAnotherStudent()
    {
        var client = new RecordingHttpClient(new HttpResponseMessage(HttpStatusCode.Conflict));

        var result = await CreateController(client, administrator: true).Allocate(new CreateAllocationRequest(1, "STU-2", "Two", null), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(((ContentResult)result).StatusCode, Is.EqualTo(409));
            Assert.That(client.LastRequest!.Method, Is.EqualTo(HttpMethod.Post));
        });
    }

    [Test]
    public async Task UpdateTicket_AdministratorForwardsPutResponse()
    {
        var client = new RecordingHttpClient(new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await CreateController(client, administrator: true).UpdateTicket(99, new UpdateTicketRequest("Resolved", "High", "Plumbing"), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(((ContentResult)result).StatusCode, Is.EqualTo(404));
            Assert.That(client.LastRequest!.Method, Is.EqualTo(HttpMethod.Put));
            Assert.That(client.LastRequest.RequestUri!.ToString(), Does.EndWith("/api/tickets/99"));
        });
    }

    [Test]
    public async Task DeleteTicket_RequiresAdministratorAndForwardsDelete()
    {
        var client = new RecordingHttpClient(new HttpResponseMessage(HttpStatusCode.NoContent));

        var forbidden = await CreateController(client, studentId: "STU-1").DeleteTicket(1, CancellationToken.None);
        var result = await CreateController(client, administrator: true).DeleteTicket(1, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(forbidden, Is.TypeOf<ForbidResult>());
            Assert.That(((ContentResult)result).StatusCode, Is.EqualTo(204));
        });
    }

    [Test]
    public async Task CreateTicket_RequiresDescriptionBeforeCallingClassifier()
    {
        var client = new RecordingHttpClient(new HttpResponseMessage(HttpStatusCode.Created));
        var controller = CreateController(client, studentId: "STU-1");

        var result = await controller.CreateTicket(new CreateMaintenanceRequest("STU-1", 1, " "), CancellationToken.None);

        Assert.That(result, Is.TypeOf<BadRequestObjectResult>());
        Assert.That(client.LastRequest, Is.Null);
    }

    [Test]
    public async Task CreateTicket_ClassifiesAndForwardsTicket()
    {
        var client = new RecordingHttpClient(new HttpResponseMessage(HttpStatusCode.Created));
        var controller = CreateController(client, studentId: "STU-1");

        var result = await controller.CreateTicket(new CreateMaintenanceRequest("STU-1", 1, "The gas line is leaking"), CancellationToken.None);

        Assert.That(((ContentResult)result).StatusCode, Is.EqualTo(201));
        Assert.That(client.LastBody, Does.Contain("Emergency"));
        Assert.That(client.LastBody, Does.Contain("Plumbing"));
        Assert.That(client.LastBody, Does.Contain("Open"));
    }

    [Test]
    public async Task UpdateTicket_RequiresAdministratorAndStatus()
    {
        var client = new RecordingHttpClient(new HttpResponseMessage(HttpStatusCode.OK));
        var studentController = CreateController(client, studentId: "STU-1");
        var forbidden = await studentController.UpdateTicket(1, new UpdateTicketRequest("Resolved"), CancellationToken.None);
        var adminController = CreateController(client, administrator: true);
        var invalid = await adminController.UpdateTicket(1, new UpdateTicketRequest(" "), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(forbidden, Is.TypeOf<ForbidResult>());
            Assert.That(invalid, Is.TypeOf<BadRequestObjectResult>());
        });
    }

    private static HousingController CreateController(RecordingHttpClient client, bool administrator = false, string? studentId = null)
    {
        var controller = new HousingController(
            new TestHttpClientFactory(client),
            new MaintenanceClassifier(new UnavailableGateway(), NullLogger<MaintenanceClassifier>.Instance));
        var context = new DefaultHttpContext();
        if (administrator) context.Request.Headers["X-Housing-Role"] = "Administrator";
        if (studentId is not null) context.Request.Headers["X-Housing-User"] = studentId;
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        return controller;
    }

    private sealed class TestHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class UnavailableGateway : IAiGatewayClient
    {
        public Task<AiEmbeddingResponse> EmbedAsync(IReadOnlyList<string> texts, bool isQuery, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AiGatewayStatus> GetStatusAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AiGenerationResponse> GenerateAsync(string prompt, string context, CancellationToken cancellationToken = default) =>
            Task.FromException<AiGenerationResponse>(new HttpRequestException("Gateway unavailable"));
    }

    private sealed class RecordingHttpClient : HttpClient
    {
        public RecordingHttpClient(HttpResponseMessage response) : this(new RecordingHandler(response))
        {
        }

        private RecordingHttpClient(RecordingHandler handler) : base(handler)
        {
            baseHandler = handler;
            BaseAddress = new Uri("https://database.test/");
        }

        public HttpRequestMessage? LastRequest => ((RecordingHandler)baseHandler).LastRequest;

        public string? LastBody => ((RecordingHandler)baseHandler).LastBody;

        private readonly HttpMessageHandler baseHandler;

        private sealed class RecordingHandler(HttpResponseMessage response) : HttpMessageHandler
        {
            public HttpRequestMessage? LastRequest { get; private set; }
            public string? LastBody { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastRequest = request;
                LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
                request.RequestUri = new Uri(new Uri("https://database.test/"), request.RequestUri!);
                return response;
            }
        }
    }
}
