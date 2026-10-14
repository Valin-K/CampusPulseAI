using CampusPulse.Shared.Ai;
using CampusPulse.Shared.Mcp.Client;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
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
var databaseUrl = builder.Configuration["Services:CampusHousingDatabase"] ?? "http://localhost:5093";
builder.Services.AddHttpClient("CampusHousingDatabase", client => client.BaseAddress = new Uri(databaseUrl.TrimEnd('/') + "/"));
builder.Services.AddHttpClient("CampusHousingDatabaseHistory", client => client.BaseAddress = new Uri(databaseUrl.TrimEnd('/') + "/"));
var academicRecordsUrl = builder.Configuration["Services:AcademicRecordsDatabase"] ?? "http://localhost:5021";
builder.Services.AddHttpClient("AcademicRecordsDatabase", client => client.BaseAddress = new Uri(academicRecordsUrl.TrimEnd('/') + "/"));
builder.Services.AddAiGateway(builder.Configuration);
builder.Services.AddMultiAgentCaller(builder.Configuration);
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
