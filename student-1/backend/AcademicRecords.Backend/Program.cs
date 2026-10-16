using AcademicRecords.Backend;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CampusPulse.Shared.Ai;
using AcademicRecords.Backend.Services;
using CampusPulse.Shared.Mcp.Client;
using CampusPulse.Shared.Rag.Assistant;
using CampusPulse.Shared.Rag.Client;
using CampusPulse.Shared.Rag.Generation;
using QuestPDF.Infrastructure;
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
builder.Services.AddHttpClient("academicRecordsDatabase", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:AcademicRecordsDatabase"] ?? "http://academicrecords.database:8080");
});
var academicRecordsCredentialsAddress = builder.Configuration["Services:AcademicRecordsCredentials"] ?? "http://localhost:55691";
builder.Services.AddHttpClient("academicRecordsCredentials", client =>
{
    client.BaseAddress = new Uri(academicRecordsCredentialsAddress, UriKind.Absolute);
}).ConfigurePrimaryHttpMessageHandler(() => CreateCredentialHttpHandler(
    builder.Configuration["Services:AcademicRecordsCredentialsCaCertificate"]));
builder.Services.AddHttpClient("courseManagementDatabase", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:CourseManagementDatabase"] ?? "http://coursemanagement.database:8080");
});
builder.Services.AddMcpClient(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:McpServer"] ?? "http://localhost:5268");
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddAiGateway(builder.Configuration);
builder.Services.AddMultiAgentCaller(builder.Configuration);
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
    .WithOrigins(
        "http://localhost:5062",
        "http://localhost:5186",
        "http://localhost:5254",
        "http://localhost:5128",
        "http://localhost:5236",
        "http://localhost:8080",
        "http://localhost:8081",
        "http://localhost:8082",
        "http://localhost:8083",
        "http://localhost:8084",
        "http://localhost:8085")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

QuestPDF.Settings.License = LicenseType.Community;

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();
app.MapControllers();
app.MapMultiAgentCaller("academicrecords", "courseManagementDatabase", "/api/courses");

app.Run();

static HttpMessageHandler CreateCredentialHttpHandler(string? certificateAuthorityPath)
{
    if (string.IsNullOrWhiteSpace(certificateAuthorityPath))
    {
        return new HttpClientHandler();
    }

    if (!File.Exists(certificateAuthorityPath))
    {
        throw new InvalidOperationException("The Academic Records credential CA certificate was not found.");
    }

    return new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (_, certificate, _, errors) =>
        {
            if (certificate is null
                || (errors & (SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateNotAvailable)) != 0)
            {
                return false;
            }

            using var serverCertificate = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
            using var rootCertificate = X509CertificateLoader.LoadCertificateFromFile(certificateAuthorityPath);
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(rootCertificate);
            chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            return chain.Build(serverCertificate);
        }
    };
}
