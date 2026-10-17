using CampusPulse.Shared.Ai;
using CampusPulse.Shared.Mcp.Client;
using CampusPulse.Shared.Security;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddCampusPulseSecurity(builder.Configuration, builder.Environment);
var databaseUrl = builder.Configuration["Services:CampusHousingDatabase"] ?? "http://localhost:5093";
builder.Services.AddHttpClient("CampusHousingDatabase", client => client.BaseAddress = new Uri(databaseUrl.TrimEnd('/') + "/"))
    .ForwardCampusPulseAuthorization();
builder.Services.AddHttpClient("CampusHousingDatabaseHistory", client => client.BaseAddress = new Uri(databaseUrl.TrimEnd('/') + "/"))
    .ForwardCampusPulseAuthorization();
var academicRecordsUrl = builder.Configuration["Services:AcademicRecordsDatabase"] ?? "http://localhost:5021";
builder.Services.AddHttpClient("AcademicRecordsDatabase", client => client.BaseAddress = new Uri(academicRecordsUrl.TrimEnd('/') + "/"))
    .ForwardCampusPulseAuthorization();
builder.Services.AddAiGateway(builder.Configuration);
builder.Services.AddMultiAgentCaller(builder.Configuration);
builder.Services.AddTransient<CampusHousing.Backend.Services.MaintenanceClassifier>();
builder.Services.AddMcpClient(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:McpServer"] ?? "http://localhost:5268");
    client.Timeout = TimeSpan.FromSeconds(30);
}).ForwardCampusPulseAuthorization();
builder.Services.AddHttpClient<CampusPulse.Shared.Rag.Client.IRagServerClient, CampusPulse.Shared.Rag.Client.RagServerClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:RagServer"] ?? "http://localhost:5267");
    client.Timeout = TimeSpan.FromSeconds(90);
});
builder.Services.AddSingleton<CampusPulse.Shared.Rag.Generation.RagCitationParser>();
builder.Services.AddScoped<CampusPulse.Shared.Rag.Assistant.RagKnowledgeService>();
builder.Services.AddScoped<CampusHousing.Backend.Services.IHousingRagService, CampusHousing.Backend.Services.HousingKnowledgeService>();
builder.Services.AddScoped<CampusHousing.Backend.Services.IHousingKnowledgeService, CampusHousing.Backend.Services.HousingKnowledgeOrchestrator>();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();
app.MapControllers();
app.MapMultiAgentCaller("campushousing", "CampusHousingDatabase", "/api/rooms");

app.Run();
