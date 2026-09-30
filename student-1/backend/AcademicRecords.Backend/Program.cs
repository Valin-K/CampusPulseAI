using AcademicRecords.Backend;
using CampusPulse.Shared.Ai;
using AcademicRecords.Backend.Services;
using CampusPulse.Shared.Mcp.Client;
using CampusPulse.Shared.Rag.Assistant;
using CampusPulse.Shared.Rag.Client;
using CampusPulse.Shared.Rag.Generation;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHttpClient("academicRecordsDatabase", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:AcademicRecordsDatabase"] ?? "http://academicrecords.database:8080");
});
builder.Services.AddHttpClient("courseManagementDatabase", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:CourseManagementDatabase"] ?? "http://coursemanagement.database:8080");
});
builder.Services.AddHttpClient<IMcpClient, McpClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:McpServer"] ?? "http://localhost:5268");
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddAiGateway(builder.Configuration);
builder.Services.AddSingleton<ITranscriptPdfGenerator, TranscriptPdfGenerator>();
builder.Services.AddScoped<GeminiAcademicProgressService>();
builder.Services.AddScoped<StudentCourseMetadataService>();
builder.Services.AddHttpClient<IRagServerClient, RagServerClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:RagServer"] ?? "http://localhost:5267");
    client.Timeout = TimeSpan.FromSeconds(90);
});
builder.Services.AddSingleton<RagCitationParser>();
builder.Services.AddScoped<RagKnowledgeService>();
builder.Services.AddScoped<IAcademicRagService, AcademicKnowledgeService>();
builder.Services.AddScoped<IAcademicKnowledgeService, AcademicKnowledgeOrchestrator>();
builder.Services.AddCors(options => options.AddPolicy("Frontend", policy => policy
    .WithOrigins("http://localhost:5062", "http://localhost:8081")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

QuestPDF.Settings.License = LicenseType.Community;

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthorization();
app.UseCors("Frontend");

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapControllers();

app.Run();
