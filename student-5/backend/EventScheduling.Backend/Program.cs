using CampusPulse.Shared.Mcp.Client;
using CampusPulse.Shared.Ai;
using CampusPulse.Shared.Rag.Assistant;
using CampusPulse.Shared.Rag.Client;
using CampusPulse.Shared.Rag.Generation;
using EventScheduling.Backend.Clients;
using EventScheduling.Backend.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .AllowAnyOrigin()
    .AllowAnyHeader()
    .AllowAnyMethod()));
builder.Services.AddTransient<DatabaseStartupRetryHandler>();
builder.Services.AddHttpClient("eventDatabase", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:EventDatabase"] ?? "http://eventscheduling.database:8080");
}).AddHttpMessageHandler<DatabaseStartupRetryHandler>();

builder.Services.AddHttpClient<IAcademicRecordsClient, AcademicRecordsClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:AcademicRecordsDatabase"]
        ?? "http://academicrecords.database:8080");
});

builder.Services.AddHttpClient<IRagServerClient, RagServerClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:RagServer"] ?? "http://localhost:5267");
    client.Timeout = TimeSpan.FromSeconds(90);
});

builder.Services.AddHttpClient<IMcpClient, McpClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:McpServer"] ?? "http://localhost:5268");
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddScoped<IBookingAssistantService, GeminiBookingAssistantService>();
builder.Services.AddSingleton<RagCitationParser>();
builder.Services.AddScoped<RagKnowledgeService>();
builder.Services.AddScoped<IEventRagService, EventKnowledgeService>();
builder.Services.AddScoped<IEventKnowledgeService, LocalEventAiOrchestrator>();

builder.Services.AddAiGateway(builder.Configuration);

var app = builder.Build();

app.MapOpenApi();
app.UseCors();
app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();
