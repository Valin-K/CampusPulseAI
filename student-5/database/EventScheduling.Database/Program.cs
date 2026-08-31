using EventScheduling.Database.Data;
using EventScheduling.Database.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("EventScheduling")
    ?? "Data Source=/app/data/event-scheduling.db";

builder.Services.AddDbContext<EventSchedulingDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapOpenApi();

using (var scope = app.Services.CreateScope())
{
    var database = scope.ServiceProvider.GetRequiredService<EventSchedulingDbContext>();
    await database.Database.EnsureCreatedAsync();
    await EventSeedData.InitializeAsync(database);
}

var rooms = app.MapGroup("/api/rooms");

rooms.MapGet("/", async (EventSchedulingDbContext database, CancellationToken cancellationToken) =>
    await database.Rooms
        .AsNoTracking()
        .OrderBy(room => room.Name)
        .Select(room => room.ToResponse())
        .ToListAsync(cancellationToken));

rooms.MapGet("/{id:int}", async (int id, EventSchedulingDbContext database, CancellationToken cancellationToken) =>
{
    var room = await database.Rooms.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    return room is null ? Results.NotFound() : Results.Ok(room.ToResponse());
});

rooms.MapPost("/", async (RoomRequest request, EventSchedulingDbContext database, CancellationToken cancellationToken) =>
{
    var error = request.Validate();
    if (error is not null)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["room"] = [error] });
    }

    var room = request.ToRoom();
    database.Rooms.Add(room);
    await database.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/rooms/{room.Id}", room.ToResponse());
});

rooms.MapPut("/{id:int}", async (int id, RoomRequest request, EventSchedulingDbContext database, CancellationToken cancellationToken) =>
{
    var error = request.Validate();
    if (error is not null)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["room"] = [error] });
    }

    var room = await database.Rooms.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (room is null)
    {
        return Results.NotFound();
    }

    room.UpdateFrom(request);
    await database.SaveChangesAsync(cancellationToken);

    return Results.Ok(room.ToResponse());
});

rooms.MapDelete("/{id:int}", async (int id, EventSchedulingDbContext database, CancellationToken cancellationToken) =>
{
    var room = await database.Rooms.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (room is null)
    {
        return Results.NotFound();
    }

    database.Rooms.Remove(room);
    await database.SaveChangesAsync(cancellationToken);

    return Results.NoContent();
});

var equipment = app.MapGroup("/api/equipment");

equipment.MapGet("/", async (EventSchedulingDbContext database, CancellationToken cancellationToken) =>
    await database.Equipment
        .AsNoTracking()
        .OrderBy(item => item.Name)
        .Select(item => item.ToResponse())
        .ToListAsync(cancellationToken));

equipment.MapGet("/{id:int}", async (int id, EventSchedulingDbContext database, CancellationToken cancellationToken) =>
{
    var item = await database.Equipment.AsNoTracking().SingleOrDefaultAsync(entity => entity.Id == id, cancellationToken);
    return item is null ? Results.NotFound() : Results.Ok(item.ToResponse());
});

equipment.MapPost("/", async (EquipmentRequest request, EventSchedulingDbContext database, CancellationToken cancellationToken) =>
{
    var error = request.Validate();
    if (error is not null)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["equipment"] = [error] });
    }

    var item = request.ToEquipment();
    database.Equipment.Add(item);
    await database.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/equipment/{item.Id}", item.ToResponse());
});

equipment.MapPut("/{id:int}", async (int id, EquipmentRequest request, EventSchedulingDbContext database, CancellationToken cancellationToken) =>
{
    var error = request.Validate();
    if (error is not null)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["equipment"] = [error] });
    }

    var item = await database.Equipment.SingleOrDefaultAsync(entity => entity.Id == id, cancellationToken);
    if (item is null)
    {
        return Results.NotFound();
    }

    item.UpdateFrom(request);
    await database.SaveChangesAsync(cancellationToken);

    return Results.Ok(item.ToResponse());
});

equipment.MapDelete("/{id:int}", async (int id, EventSchedulingDbContext database, CancellationToken cancellationToken) =>
{
    var item = await database.Equipment.SingleOrDefaultAsync(entity => entity.Id == id, cancellationToken);
    if (item is null)
    {
        return Results.NotFound();
    }

    database.Equipment.Remove(item);
    await database.SaveChangesAsync(cancellationToken);

    return Results.NoContent();
});

var bookings = app.MapGroup("/api/bookings");

bookings.MapGet("/", async (EventSchedulingDbContext database, CancellationToken cancellationToken) =>
    await database.Bookings
        .AsNoTracking()
        .OrderBy(booking => booking.StartUtc)
        .Select(booking => booking.ToResponse())
        .ToListAsync(cancellationToken));

bookings.MapGet("/{id:int}", async (int id, EventSchedulingDbContext database, CancellationToken cancellationToken) =>
{
    var booking = await database.Bookings.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    return booking is null ? Results.NotFound() : Results.Ok(booking.ToResponse());
});

bookings.MapPost("/", async (BookingRequest request, EventSchedulingDbContext database, CancellationToken cancellationToken) =>
{
    var error = request.Validate();
    if (error is not null)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["booking"] = [error] });
    }

    var booking = request.ToBooking();
    database.Bookings.Add(booking);
    await database.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/bookings/{booking.Id}", booking.ToResponse());
});

bookings.MapPut("/{id:int}", async (int id, BookingRequest request, EventSchedulingDbContext database, CancellationToken cancellationToken) =>
{
    var error = request.Validate();
    if (error is not null)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["booking"] = [error] });
    }

    var booking = await database.Bookings.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (booking is null)
    {
        return Results.NotFound();
    }

    booking.UpdateFrom(request);
    await database.SaveChangesAsync(cancellationToken);

    return Results.Ok(booking.ToResponse());
});

bookings.MapDelete("/{id:int}", async (int id, EventSchedulingDbContext database, CancellationToken cancellationToken) =>
{
    var booking = await database.Bookings.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (booking is null)
    {
        return Results.NotFound();
    }

    database.Bookings.Remove(booking);
    await database.SaveChangesAsync(cancellationToken);

    return Results.NoContent();
});

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

await app.RunAsync();

public partial class Program;
