using CampusPulse.Shared.Mcp.Client;
using CampusPulse.Shared.Ai;
using CampusPulse.Shared.Rag.Assistant;
using CampusPulse.Shared.Rag.Client;
using CampusPulse.Shared.Rag.Generation;
using EventScheduling.Backend.Clients;
using EventScheduling.Backend.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
var issuer = builder.Configuration["Authentication:Issuer"] ?? "CampusPulse.Dev";
var audience = builder.Configuration["Authentication:Audience"] ?? "CampusPulse.Api";
var signingKey = builder.Configuration["Authentication:SigningKey"]
    ?? (builder.Environment.IsDevelopment()
        ? "campuspulse-demo-only-signing-key-change-before-any-use"
        : throw new InvalidOperationException("The token signing key is not configured."));
var keyBytes = System.Text.Encoding.UTF8.GetBytes(signingKey);
if (keyBytes.Length < 32)
{
    throw new InvalidOperationException("The token signing key must contain at least 32 bytes.");
}
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(keyBytes),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .RequireClaim("scope", "CampusPulse.Access")
        .Build();
});
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

builder.Services.AddMcpClient(client =>
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
builder.Services.AddMultiAgentCaller(builder.Configuration);

var app = builder.Build();

app.MapOpenApi();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapMultiAgentCaller("eventscheduling", "eventDatabase", "/api/rooms");

app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();

app.Run();
