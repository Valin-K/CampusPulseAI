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

rooms.MapGet("/", async (bool? includeInactive, EventSchedulingDbContext database, CancellationToken cancellationToken) =>
{
    var query = database.Rooms.AsNoTracking();
    if (includeInactive is not true)
    {
        query = query.Where(room => room.IsActive);
    }

    return await query
        .OrderBy(room => room.Name)
        .Select(room => room.ToResponse())
        .ToListAsync(cancellationToken);
});

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

static Task<int> AvailableCountAsync(EventSchedulingDbContext database, int refEquipmentId, CancellationToken cancellationToken) =>
    database.Equipment.CountAsync(
        unit => unit.RefEquipmentId == refEquipmentId && unit.Status == EquipmentStatus.AVL,
        cancellationToken);

static async Task ReleaseEquipmentUnitAsync(EventSchedulingDbContext database, Booking booking, CancellationToken cancellationToken)
{
    if (booking.EquipmentId is null)
    {
        return;
    }

    var unit = await database.Equipment.SingleOrDefaultAsync(item => item.Id == booking.EquipmentId, cancellationToken);
    if (unit is not null && unit.Status == EquipmentStatus.OCC)
    {
        unit.Status = EquipmentStatus.AVL;
    }

    booking.EquipmentId = null;
}

static Task<bool> RoomHasClashAsync(EventSchedulingDbContext database, int roomId, DateTime startUtc, DateTime endUtc,
    int? excludeBookingId, CancellationToken cancellationToken)
{
    var query = database.Bookings.Where(other =>
        other.RoomId == roomId
        && other.Status != BookingStatus.Cancelled
        && other.Status != BookingStatus.Completed
        && other.Status != BookingStatus.Returned
        && startUtc < other.EndUtc
        && other.StartUtc < endUtc);

    if (excludeBookingId is int excludeId)
    {
        query = query.Where(other => other.Id != excludeId);
    }

    return query.AnyAsync(cancellationToken);
}

var refEquipment = app.MapGroup("/api/refequipment");

refEquipment.MapGet("/", async (EventSchedulingDbContext database, CancellationToken cancellationToken) =>
{
    var types = await database.RefEquipment
        .AsNoTracking()
        .OrderBy(item => item.Name)
        .ToListAsync(cancellationToken);

    var counts = await database.Equipment
        .AsNoTracking()
        .Where(unit => unit.Status == EquipmentStatus.AVL)
        .GroupBy(unit => unit.RefEquipmentId)
        .Select(group => new { RefEquipmentId = group.Key, Count = group.Count() })
        .ToDictionaryAsync(entry => entry.RefEquipmentId, entry => entry.Count, cancellationToken);

    return types
        .Select(type => type.ToResponse(counts.GetValueOrDefault(type.Id)))
        .ToList();
});

refEquipment.MapGet("/{id:int}", async (int id, EventSchedulingDbContext database, CancellationToken cancellationToken) =>
{
    var type = await database.RefEquipment.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (type is null)
    {
        return Results.NotFound();
    }

    return Results.Ok(type.ToResponse(await AvailableCountAsync(database, id, cancellationToken)));
});

refEquipment.MapPost("/", async (RefEquipmentRequest request, EventSchedulingDbContext database, CancellationToken cancellationToken) =>
{
    var error = request.Validate();
    if (error is not null)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["refEquipment"] = [error] });
    }

    var type = request.ToRefEquipment();
    database.RefEquipment.Add(type);
    await database.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/refequipment/{type.Id}", type.ToResponse(0));
});

refEquipment.MapPut("/{id:int}", async (int id, RefEquipmentRequest request, EventSchedulingDbContext database, CancellationToken cancellationToken) =>
{
    var error = request.Validate();
    if (error is not null)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["refEquipment"] = [error] });
    }

    var type = await database.RefEquipment.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (type is null)
    {
        return Results.NotFound();
    }

    type.UpdateFrom(request);
    await database.SaveChangesAsync(cancellationToken);

    return Results.Ok(type.ToResponse(await AvailableCountAsync(database, id, cancellationToken)));
});

refEquipment.MapDelete("/{id:int}", async (int id, EventSchedulingDbContext database, CancellationToken cancellationToken) =>
{
    var type = await database.RefEquipment.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (type is null)
    {
        return Results.NotFound();
    }

    if (await database.Equipment.AnyAsync(unit => unit.RefEquipmentId == id, cancellationToken))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["refEquipment"] = ["Cannot delete an equipment type while units still reference it. Retire the units first."]
        });
    }

    database.RefEquipment.Remove(type);
    await database.SaveChangesAsync(cancellationToken);

    return Results.NoContent();
});

var equipment = app.MapGroup("/api/equipment");

equipment.MapGet("/", async (int? refEquipmentId, string? status, EventSchedulingDbContext database, CancellationToken cancellationToken) =>
{
    var query = database.Equipment.AsNoTracking();

    if (refEquipmentId is not null)
    {
        query = query.Where(item => item.RefEquipmentId == refEquipmentId);
    }

    if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<EquipmentStatus>(status, ignoreCase: true, out var parsedStatus))
    {
        query = query.Where(item => item.Status == parsedStatus);
    }

    return await query
        .OrderBy(item => item.Id)
        .Select(item => item.ToResponse())
        .ToListAsync(cancellationToken);
});

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

    if (!await database.RefEquipment.AnyAsync(type => type.Id == request.RefEquipmentId, cancellationToken))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["equipment"] = ["The specified equipment type does not exist."]
        });
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

    if (item.RefEquipmentId != request.RefEquipmentId
        && !await database.RefEquipment.AnyAsync(type => type.Id == request.RefEquipmentId, cancellationToken))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["equipment"] = ["The specified equipment type does not exist."]
        });
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

    if (await database.Bookings.AnyAsync(booking => booking.EquipmentId == id, cancellationToken))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["equipment"] = ["Cannot delete a unit that is referenced by a booking. Mark it as retired instead."]
        });
    }

    database.Equipment.Remove(item);
    await database.SaveChangesAsync(cancellationToken);

    return Results.NoContent();
});

var bookings = app.MapGroup("/api/bookings");

bookings.MapGet("/", async (EventSchedulingDbContext database, CancellationToken cancellationToken) =>
{
    var all = await database.Bookings
        .AsNoTracking()
        .OrderBy(booking => booking.StartUtc)
        .ToListAsync(cancellationToken);

    return all.Select(booking => booking.ToResponse()).ToList();
});

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

    var nowUtc = DateTime.UtcNow;

    if (request.StartUtc.Date < nowUtc.Date.AddDays(-1))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["booking"] = ["Bookings can't start on a past date."]
        });
    }

    if (request.RoomId is not null && !BookingRules.HasSufficientNotice(request.StartUtc, nowUtc))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["booking"] = [$"Room bookings need at least {Booking.MinBookingNotice.TotalMinutes:0} minutes' notice."]
        });
    }

    var activeBookingCount = await BookingRules.CountActiveBookingsAsync(database, request.StudentId, nowUtc, cancellationToken);
    if (activeBookingCount >= Booking.MaxActiveBookingsPerStudent)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["booking"] = [$"You already have {Booking.MaxActiveBookingsPerStudent} active bookings. Cancel or complete one before making another."]
        });
    }

    var booking = request.ToBooking();

    if (request.RoomId is not null)
    {
        var room = await database.Rooms.AsNoTracking().SingleOrDefaultAsync(item => item.Id == request.RoomId, cancellationToken);
        if (room is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["booking"] = ["The specified room does not exist."]
            });
        }

        if (room.IsStaffRoom != request.IsStaff)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["booking"] = [room.IsStaffRoom
                    ? "Lab and lecture rooms can only be booked by teaching staff."
                    : "Study rooms are booked by students, not teaching staff."]
            });
        }

        if (!BookingRules.HasCapacity(request.AttendeeCount, room.Capacity))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["booking"] = [$"This room seats {room.Capacity}, which is fewer than the {request.AttendeeCount} attendees given."]
            });
        }

        if (await RoomHasClashAsync(database, request.RoomId.Value, request.StartUtc, request.EndUtc, null, cancellationToken))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["booking"] = ["This room is already booked during part of that time."]
            });
        }
    }

    // FR-20: not an "else" - equipment can be requested alongside a room in the
    // same booking, or entirely on its own.
    if (request.RefEquipmentId is not null)
    {
        if (!await database.RefEquipment.AnyAsync(type => type.Id == request.RefEquipmentId, cancellationToken))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["booking"] = ["The specified equipment type does not exist."]
            });
        }

        var unit = await database.Equipment
            .Where(item => item.RefEquipmentId == request.RefEquipmentId && item.Status == EquipmentStatus.AVL)
            .OrderBy(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (unit is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["booking"] = ["No units of this equipment type are currently available."]
            });
        }

        unit.Status = EquipmentStatus.OCC;
        booking.EquipmentId = unit.Id;
    }

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

    var wasActive = booking.Status is not (BookingStatus.Cancelled or BookingStatus.Completed or BookingStatus.Returned);

    // FR-12: the cut off for changing or cancelling a still-active booking is its
    // own start time - once it has begun there is nothing left to give back.
    if (wasActive && !BookingRules.IsBeforeCutoff(booking.StartUtc, DateTime.UtcNow))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["booking"] = ["This booking has already started and can no longer be changed or cancelled."]
        });
    }

    if (request.RoomId is not null
        && await RoomHasClashAsync(database, request.RoomId.Value, request.StartUtc, request.EndUtc, id, cancellationToken))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["booking"] = ["This room is already booked during part of that time."]
        });
    }

    booking.UpdateFrom(request);

    if (request.Status == BookingStatus.Cancelled && wasActive)
    {
        await ReleaseEquipmentUnitAsync(database, booking, cancellationToken);
    }

    await database.SaveChangesAsync(cancellationToken);

    return Results.Ok(booking.ToResponse());
});

bookings.MapPost("/{id:int}/return", async (int id, ReturnRequest? request, EventSchedulingDbContext database, CancellationToken cancellationToken) =>
{
    if (request?.Validate() is { } returnError)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["booking"] = [returnError] });
    }

    var booking = await database.Bookings.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (booking is null)
    {
        return Results.NotFound();
    }

    if (booking.EquipmentId is null)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["booking"] = ["This booking has no equipment unit to return."]
        });
    }

    await ReleaseEquipmentUnitAsync(database, booking, cancellationToken);
    booking.ReturnConditionNotes = Booking.NormalizeNotes(request?.ConditionNotes);
    booking.Status = BookingStatus.Returned;
    await database.SaveChangesAsync(cancellationToken);

    return Results.Ok(booking.ToResponse());
});

bookings.MapPost("/{id:int}/end", async (int id, EventSchedulingDbContext database, CancellationToken cancellationToken) =>
{
    var booking = await database.Bookings.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
    if (booking is null)
    {
        return Results.NotFound();
    }

    if (booking.RoomId is null)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["booking"] = ["Only room bookings can be ended."]
        });
    }

    booking.Status = BookingStatus.Completed;
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

    await ReleaseEquipmentUnitAsync(database, booking, cancellationToken);
    database.Bookings.Remove(booking);
    await database.SaveChangesAsync(cancellationToken);

    return Results.NoContent();
});

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

await app.RunAsync();

public partial class Program;
