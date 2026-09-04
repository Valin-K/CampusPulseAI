var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
var databaseUrl = builder.Configuration["Services:CampusHousingDatabase"] ?? "http://localhost:5093";
builder.Services.AddHttpClient("CampusHousingDatabase", client => client.BaseAddress = new Uri(databaseUrl.TrimEnd('/') + "/"));
var academicRecordsUrl = builder.Configuration["Services:AcademicRecordsDatabase"] ?? "http://localhost:5021";
builder.Services.AddHttpClient("AcademicRecordsDatabase", client => client.BaseAddress = new Uri(academicRecordsUrl.TrimEnd('/') + "/"));
builder.Services.AddHttpClient("gemini", client =>
{
    client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
    client.Timeout = TimeSpan.FromSeconds(90);

    var geminiApiKey = builder.Configuration["GEMINI_API_KEY"];
    if (!string.IsNullOrWhiteSpace(geminiApiKey))
    {
        client.DefaultRequestHeaders.Add("x-goog-api-key", geminiApiKey);
    }
});
builder.Services.AddTransient<CampusHousing.Backend.Services.MaintenanceClassifier>();
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
