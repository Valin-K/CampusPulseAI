using FinancialAidBilling.Database.Data;
using FinancialAidBilling.Database.Models;
using CampusPulse.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("FinancialAidBilling")
    ?? "Data Source=/app/data/financial-aid-billing.db";

builder.Services.AddDbContext<FinancialAidBillingDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddOpenApi();
builder.Services.AddCampusPulseSecurity(builder.Configuration);

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();

using (var scope = app.Services.CreateScope())
{
    var database = scope.ServiceProvider.GetRequiredService<FinancialAidBillingDbContext>();
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            await database.Database.EnsureCreatedAsync();
            await InvoiceSeedData.InitializeAsync(database);
            break;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 5 && attempt < 10)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
    }
}

var invoices = app.MapGroup("/api/invoices");

invoices.MapGet("/", async (FinancialAidBillingDbContext database, ClaimsPrincipal user, CancellationToken cancellationToken) =>
{
    var query = database.Invoices.AsNoTracking();
    if (!user.IsInRole("Staff"))
    {
        if (!user.TryGetUserId(out var userId))
        {
            return Results.Forbid();
        }

        query = query.Where(invoice => invoice.StudentId == userId);
    }

    return Results.Ok(await query
        .OrderByDescending(invoice => invoice.IssueDate)
        .Select(invoice => invoice.ToResponse())
        .ToListAsync(cancellationToken));
});

invoices.MapGet("/{id:int}", async (int id, FinancialAidBillingDbContext database, ClaimsPrincipal user, CancellationToken cancellationToken) =>
{
    var invoice = await database.Invoices.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (invoice is null)
    {
        return Results.NotFound();
    }

    if (user.IsInRole("Staff"))
    {
        return Results.Ok(invoice.ToResponse());
    }

    if (!user.CanAccessUser(invoice.StudentId))
    {
        return Results.Forbid();
    }

    return Results.Ok(invoice.ToResponse());
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
}).RequireAuthorization(CampusPulseSecurityExtensions.StaffOnlyPolicy);

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
}).RequireAuthorization(CampusPulseSecurityExtensions.StaffOnlyPolicy);

invoices.MapDelete("/{id:int}", async (int id, FinancialAidBillingDbContext database, CancellationToken cancellationToken) =>
{
    var invoice = await database.Invoices.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (invoice is null)
    {
        return Results.NotFound();
    }

    var payments = await database.Payments.Where(payment => payment.InvoiceId == id).ToListAsync(cancellationToken);
    database.Payments.RemoveRange(payments);
    database.Invoices.Remove(invoice);
    await database.SaveChangesAsync(cancellationToken);

    return Results.NoContent();
}).RequireAuthorization(CampusPulseSecurityExtensions.StaffOnlyPolicy);

var payments = app.MapGroup("/api/payments");

payments.MapGet("/", async (FinancialAidBillingDbContext database, ClaimsPrincipal user, CancellationToken cancellationToken) =>
{
    var query = database.Payments.AsNoTracking();
    if (!user.IsInRole("Staff"))
    {
        if (!user.TryGetUserId(out var userId))
        {
            return Results.Forbid();
        }

        query = query.Where(payment => payment.StudentId == userId);
    }

    return Results.Ok(await query
        .OrderByDescending(payment => payment.PaidAtUtc)
        .Select(payment => payment.ToResponse())
        .ToListAsync(cancellationToken));
});

payments.MapGet("/{id:int}", async (int id, FinancialAidBillingDbContext database, ClaimsPrincipal user, CancellationToken cancellationToken) =>
{
    var payment = await database.Payments.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (payment is null)
    {
        return Results.NotFound();
    }

    if (user.IsInRole("Staff"))
    {
        return Results.Ok(payment.ToResponse());
    }

    if (!user.CanAccessUser(payment.StudentId))
    {
        return Results.Forbid();
    }

    return Results.Ok(payment.ToResponse());
});

payments.MapPost("/", async (PaymentRequest request, FinancialAidBillingDbContext database, ClaimsPrincipal user, CancellationToken cancellationToken) =>
{
    if (!user.IsInRole("Student") || !user.CanAccessUser(request.StudentId))
    {
        return Results.Forbid();
    }

    var error = request.Validate();
    if (error is not null)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["payment"] = [error] });
    }

    var invoice = await database.Invoices.SingleOrDefaultAsync(item => item.Id == request.InvoiceId, cancellationToken);
    if (invoice is null || invoice.StudentId != request.StudentId)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["invoice"] = ["The invoice does not belong to this student."] });
    }

    var previousPayments = await database.Payments
        .Where(payment => payment.InvoiceId == invoice.Id)
        .SumAsync(payment => (decimal?)payment.Amount, cancellationToken) ?? 0m;
    if (previousPayments + request.Amount > invoice.Amount)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["amount"] = ["Payment exceeds the remaining invoice balance."] });
    }

    var payment = request.ToPayment();
    database.Payments.Add(payment);
    if (previousPayments + request.Amount == invoice.Amount)
    {
        invoice.Status = InvoiceStatus.Paid;
    }

    await database.SaveChangesAsync(cancellationToken);
    return Results.Created($"/api/payments/{payment.Id}", payment.ToResponse());
}).RequireAuthorization(CampusPulseSecurityExtensions.StudentOnlyPolicy);

payments.MapPut("/{id:int}", async (int id, PaymentRequest request, FinancialAidBillingDbContext database, CancellationToken cancellationToken) =>
{
    var error = request.Validate();
    if (error is not null)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["payment"] = [error] });
    }

    var payment = await database.Payments.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    var invoice = await database.Invoices.SingleOrDefaultAsync(item => item.Id == request.InvoiceId, cancellationToken);
    if (payment is null)
    {
        return Results.NotFound();
    }

    if (invoice is null || invoice.StudentId != request.StudentId)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["invoice"] = ["The invoice does not belong to this student."] });
    }

    var otherPayments = await database.Payments
        .Where(item => item.InvoiceId == invoice.Id && item.Id != id)
        .SumAsync(item => (decimal?)item.Amount, cancellationToken) ?? 0m;
    if (otherPayments + request.Amount > invoice.Amount)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["amount"] = ["Payment exceeds the remaining invoice balance."] });
    }

    payment.UpdateFrom(request);
    invoice.Status = otherPayments + request.Amount == invoice.Amount ? InvoiceStatus.Paid : InvoiceStatus.Outstanding;
    await database.SaveChangesAsync(cancellationToken);
    return Results.Ok(payment.ToResponse());
}).RequireAuthorization(CampusPulseSecurityExtensions.StaffOnlyPolicy);

payments.MapDelete("/{id:int}", async (int id, FinancialAidBillingDbContext database, CancellationToken cancellationToken) =>
{
    var payment = await database.Payments.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (payment is null)
    {
        return Results.NotFound();
    }

    var invoice = await database.Invoices.SingleOrDefaultAsync(item => item.Id == payment.InvoiceId, cancellationToken);
    database.Payments.Remove(payment);
    if (invoice is not null)
    {
        invoice.Status = InvoiceStatus.Outstanding;
    }

    await database.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
}).RequireAuthorization(CampusPulseSecurityExtensions.StaffOnlyPolicy);

var scholarships = app.MapGroup("/api/scholarships");

scholarships.MapGet("/", async (FinancialAidBillingDbContext database, CancellationToken cancellationToken) =>
    await database.Scholarships
        .AsNoTracking()
        .OrderBy(scholarship => scholarship.ApplicationDeadline)
        .Select(scholarship => scholarship.ToResponse())
        .ToListAsync(cancellationToken));

scholarships.MapGet("/{id:int}", async (int id, FinancialAidBillingDbContext database, CancellationToken cancellationToken) =>
{
    var scholarship = await database.Scholarships.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    return scholarship is null ? Results.NotFound() : Results.Ok(scholarship.ToResponse());
});

scholarships.MapPost("/", async (ScholarshipRequest request, FinancialAidBillingDbContext database, CancellationToken cancellationToken) =>
{
    var error = request.Validate();
    if (error is not null)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["scholarship"] = [error] });
    }

    var scholarship = request.ToScholarship();
    database.Scholarships.Add(scholarship);
    await database.SaveChangesAsync(cancellationToken);
    return Results.Created($"/api/scholarships/{scholarship.Id}", scholarship.ToResponse());
}).RequireAuthorization(CampusPulseSecurityExtensions.StaffOnlyPolicy);

scholarships.MapPut("/{id:int}", async (int id, ScholarshipRequest request, FinancialAidBillingDbContext database, CancellationToken cancellationToken) =>
{
    var error = request.Validate();
    if (error is not null)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["scholarship"] = [error] });
    }

    var scholarship = await database.Scholarships.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (scholarship is null)
    {
        return Results.NotFound();
    }

    scholarship.UpdateFrom(request);
    await database.SaveChangesAsync(cancellationToken);
    return Results.Ok(scholarship.ToResponse());
}).RequireAuthorization(CampusPulseSecurityExtensions.StaffOnlyPolicy);

scholarships.MapDelete("/{id:int}", async (int id, FinancialAidBillingDbContext database, CancellationToken cancellationToken) =>
{
    var scholarship = await database.Scholarships.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (scholarship is null)
    {
        return Results.NotFound();
    }

    database.Scholarships.Remove(scholarship);
    await database.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
}).RequireAuthorization(CampusPulseSecurityExtensions.StaffOnlyPolicy);

app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();

await app.RunAsync();

public partial class Program;
