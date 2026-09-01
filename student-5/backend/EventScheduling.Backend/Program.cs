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

var app = builder.Build();

app.MapOpenApi();
app.UseCors();
app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();
