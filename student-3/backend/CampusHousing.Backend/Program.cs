using CampusPulse.Shared.Ai;
using CampusPulse.Shared.Mcp.Client;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
var databaseUrl = builder.Configuration["Services:CampusHousingDatabase"] ?? "http://localhost:5093";
builder.Services.AddHttpClient("CampusHousingDatabase", client => client.BaseAddress = new Uri(databaseUrl.TrimEnd('/') + "/"));
var academicRecordsUrl = builder.Configuration["Services:AcademicRecordsDatabase"] ?? "http://localhost:5021";
builder.Services.AddHttpClient("AcademicRecordsDatabase", client => client.BaseAddress = new Uri(academicRecordsUrl.TrimEnd('/') + "/"));
builder.Services.AddAiGateway(builder.Configuration);
builder.Services.AddTransient<CampusHousing.Backend.Services.MaintenanceClassifier>();
builder.Services.AddHttpClient<IMcpClient, McpClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:McpServer"] ?? "http://localhost:5268");
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient<CampusPulse.Shared.Rag.Client.IRagServerClient, CampusPulse.Shared.Rag.Client.RagServerClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:RagServer"] ?? "http://localhost:5267");
    client.Timeout = TimeSpan.FromSeconds(90);
});
builder.Services.AddSingleton<CampusPulse.Shared.Rag.Generation.RagCitationParser>();
builder.Services.AddScoped<CampusPulse.Shared.Rag.Assistant.RagKnowledgeService>();
builder.Services.AddScoped<CampusHousing.Backend.Services.IHousingKnowledgeService, CampusHousing.Backend.Services.HousingKnowledgeService>();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.UseCors();
app.UseAuthorization();

app.MapControllers();

app.Run();
