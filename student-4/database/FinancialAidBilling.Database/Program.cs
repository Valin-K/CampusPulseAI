using FinancialAidBilling.Database.Data;
using FinancialAidBilling.Database.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("FinancialAidBilling")
    ?? "Data Source=/app/data/financial-aid-billing.db";

builder.Services.AddDbContext<FinancialAidBillingDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapOpenApi();

using (var scope = app.Services.CreateScope())
{
    var database = scope.ServiceProvider.GetRequiredService<FinancialAidBillingDbContext>();
    await database.Database.EnsureCreatedAsync();
    await InvoiceSeedData.InitializeAsync(database);
}

var invoices = app.MapGroup("/api/invoices");

invoices.MapGet("/", async (FinancialAidBillingDbContext database, CancellationToken cancellationToken) =>
    await database.Invoices
        .AsNoTracking()
        .OrderByDescending(invoice => invoice.IssueDate)
        .Select(invoice => invoice.ToResponse())
        .ToListAsync(cancellationToken));

invoices.MapGet("/{id:int}", async (int id, FinancialAidBillingDbContext database, CancellationToken cancellationToken) =>
{
    var invoice = await database.Invoices.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    return invoice is null ? Results.NotFound() : Results.Ok(invoice.ToResponse());
});

invoices.MapPost("/", async (InvoiceRequest request, FinancialAidBillingDbContext database, CancellationToken cancellationToken) =>
{
    var error = request.Validate();
    if (error is not null)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["invoice"] = [error] });
    }

    var invoice = request.ToInvoice();
    database.Invoices.Add(invoice);
    await database.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/invoices/{invoice.Id}", invoice.ToResponse());
});

invoices.MapPut("/{id:int}", async (int id, InvoiceRequest request, FinancialAidBillingDbContext database, CancellationToken cancellationToken) =>
{
    var error = request.Validate();
    if (error is not null)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["invoice"] = [error] });
    }

    var invoice = await database.Invoices.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (invoice is null)
    {
        return Results.NotFound();
    }

    invoice.UpdateFrom(request);
    await database.SaveChangesAsync(cancellationToken);

    return Results.Ok(invoice.ToResponse());
});

invoices.MapDelete("/{id:int}", async (int id, FinancialAidBillingDbContext database, CancellationToken cancellationToken) =>
{
    var invoice = await database.Invoices.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (invoice is null)
    {
        return Results.NotFound();
    }

    database.Invoices.Remove(invoice);
    await database.SaveChangesAsync(cancellationToken);

    return Results.NoContent();
});

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

await app.RunAsync();

public partial class Program;
