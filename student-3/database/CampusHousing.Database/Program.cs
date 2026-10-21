using CampusHousing.Database.Data;
using CampusHousing.Database.Models;
using CampusPulse.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Net.Http.Json;
using System.Security.Claims;

var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("CampusHousing")
    ?? "Data Source=/app/data/campus-housing.db";
// Azure Files (SMB) does not reliably support the byte-range locking SQLite needs to acquire
// and release a write lock per transaction - every repeated lock negotiation is a chance to see
// "database is locked". Instead, acquire the lock ONCE via a single connection kept open and
// exclusive for this container's entire lifetime, and have every DbContext reuse it.
var sharedConnection = new SqliteConnection(connectionString);
sharedConnection.Open();
using (var pragma = sharedConnection.CreateCommand())
{
    pragma.CommandText = "PRAGMA journal_mode=MEMORY; PRAGMA locking_mode=EXCLUSIVE;";
    pragma.ExecuteNonQuery();
}
builder.Services.AddSingleton(sharedConnection);
builder.Services.AddDbContext<CampusHousingDbContext>(options => options.UseSqlite(sharedConnection));
builder.Services.AddCampusPulseSecurity(builder.Configuration);
var academicRecordsUrl = builder.Configuration["Services:AcademicRecordsDatabase"] ?? "http://localhost:5021";
builder.Services.AddHttpClient("AcademicRecords", client => client.BaseAddress = new Uri(academicRecordsUrl.TrimEnd('/') + "/"));
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
var databasePath = connectionString.Replace("Data Source=", string.Empty).Split(';')[0];
var databaseDirectory = Path.GetDirectoryName(databasePath);
if (!string.IsNullOrWhiteSpace(databaseDirectory)) Directory.CreateDirectory(databaseDirectory);
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CampusHousingDbContext>();
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            await db.Database.EnsureCreatedAsync();
            break;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 5 && attempt < 10)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
    }
    var users = await GetAcademicRecordsUsersAsync(
        scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("AcademicRecords"));
    await CampusHousingSeedData.EnsureSeededAsync(db, users);
}
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { service = "campus-housing-database", status = " healthy".Trim() })).AllowAnonymous();
app.MapGet("/api/rooms", async (CampusHousingDbContext db) => Results.Ok(await db.DormRooms.AsNoTracking()
    .Select(room => new
    {
        room.Id, room.Building, room.RoomNumber, room.Capacity,
        OccupiedBeds = db.HousingAllocations.Count(allocation => allocation.DormRoomId == room.Id),
        IsAvailable = room.IsAvailable
            && db.HousingAllocations.Count(allocation => allocation.DormRoomId == room.Id) < room.Capacity
    }).ToListAsync()));
app.MapGet("/api/rooms/{id:int}", async (int id, CampusHousingDbContext db) =>
    await db.DormRooms.FindAsync(id) is { } room ? Results.Ok(room) : Results.NotFound());
app.MapPost("/api/rooms", async (DormRoom room, CampusHousingDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(room.Building) || string.IsNullOrWhiteSpace(room.RoomNumber) || room.Capacity <= 0)
        return Results.BadRequest(new { message = "Building, room number, and a positive capacity are required." });
    db.DormRooms.Add(room);
    try { await db.SaveChangesAsync(); } catch (DbUpdateException) { return Results.Conflict(new { message = "That room already exists." }); }
    return Results.Created($"/api/rooms/{room.Id}", room);
}).RequireAuthorization(CampusPulseSecurityExtensions.StaffOnlyPolicy);
app.MapPut("/api/rooms/{id:int}", async (int id, DormRoom input, CampusHousingDbContext db) =>
{
    var room = await db.DormRooms.FindAsync(id);
    if (room is null) return Results.NotFound();
    if (string.IsNullOrWhiteSpace(input.Building) || string.IsNullOrWhiteSpace(input.RoomNumber) || input.Capacity <= 0)
        return Results.BadRequest(new { message = "Building, room number, and a positive capacity are required." });
    if (await db.HousingAllocations.CountAsync(item => item.DormRoomId == id) > input.Capacity)
        return Results.Conflict(new { message = "Capacity cannot be lower than current occupancy." });
    room.Building = input.Building; room.RoomNumber = input.RoomNumber; room.Capacity = input.Capacity; room.IsAvailable = input.IsAvailable;
    try { await db.SaveChangesAsync(); } catch (DbUpdateException) { return Results.Conflict(new { message = "That room already exists." }); }
    return Results.Ok(room);
}).RequireAuthorization(CampusPulseSecurityExtensions.StaffOnlyPolicy);
app.MapDelete("/api/rooms/{id:int}", async (int id, CampusHousingDbContext db) =>
{
    var room = await db.DormRooms.FindAsync(id); if (room is null) return Results.NotFound();
    db.DormRooms.Remove(room); await db.SaveChangesAsync(); return Results.NoContent();
}).RequireAuthorization(CampusPulseSecurityExtensions.StaffOnlyPolicy);

app.MapGet("/api/allocations", async (CampusHousingDbContext db, ClaimsPrincipal user) =>
{
    var allocations = db.HousingAllocations.AsNoTracking();
    if (!user.IsInRole("Staff"))
    {
        var studentId = user.FindFirst("student_id")?.Value;
        if (string.IsNullOrWhiteSpace(studentId)) return Results.Forbid();
        allocations = allocations.Where(allocation => allocation.StudentId == studentId);
    }
    return Results.Ok(await allocations.ToListAsync());
});
app.MapPost("/api/allocations", async (HousingAllocation allocation, CampusHousingDbContext db, ClaimsPrincipal user) =>
{
    if (!user.CanAccessStudent(allocation.StudentId)) return Results.Forbid();
    var room = await db.DormRooms.FindAsync(allocation.DormRoomId);
    if (room is null) return Results.BadRequest(new { message = "Room does not exist." });
    if (string.IsNullOrWhiteSpace(allocation.StudentId) || string.IsNullOrWhiteSpace(allocation.StudentName))
        return Results.BadRequest(new { message = "Student ID and student name are required." });
    if (await db.HousingAllocations.AnyAsync(item => item.StudentId == allocation.StudentId))
        return Results.Conflict(new { message = "This student already has a room allocation. Confirm reallocation before changing rooms." });
    if (!room.IsAvailable) return Results.Conflict(new { message = "Room is not available for booking." });
    var occupied = await db.HousingAllocations.CountAsync(item => item.DormRoomId == allocation.DormRoomId);
    if (occupied >= room.Capacity) return Results.Conflict(new { message = "Room is at capacity." });
    allocation.AllocatedAt = DateTime.UtcNow; db.HousingAllocations.Add(allocation); await db.SaveChangesAsync();
    room.IsAvailable = occupied + 1 < room.Capacity;
    await db.SaveChangesAsync();
    return Results.Created($"/api/allocations/{allocation.Id}", allocation);
});
app.MapGet("/api/allocations/{id:int}", async (int id, CampusHousingDbContext db, ClaimsPrincipal user) =>
{
    var allocation = await db.HousingAllocations.FindAsync(id);
    return allocation is null ? Results.NotFound() : !user.CanAccessStudent(allocation.StudentId) ? Results.Forbid() : Results.Ok(allocation);
});
app.MapPut("/api/allocations/{id:int}", async (int id, HousingAllocation input, CampusHousingDbContext db, ClaimsPrincipal user) =>
{
    var allocation = await db.HousingAllocations.FindAsync(id);
    if (allocation is null) return Results.NotFound();
    if (!user.CanAccessStudent(allocation.StudentId) || !user.CanAccessStudent(input.StudentId)) return Results.Forbid();
    if (string.IsNullOrWhiteSpace(input.StudentId) || string.IsNullOrWhiteSpace(input.StudentName))
        return Results.BadRequest(new { message = "Student ID and student name are required." });
    var newRoom = await db.DormRooms.FindAsync(input.DormRoomId);
    if (newRoom is null) return Results.BadRequest(new { message = "Room does not exist." });
    if (await db.HousingAllocations.AnyAsync(item => item.Id != id && item.StudentId == input.StudentId))
        return Results.Conflict(new { message = "A student can only have one room allocation." });
    var occupied = await db.HousingAllocations.CountAsync(item => item.DormRoomId == input.DormRoomId && item.Id != id);
    if (!newRoom.IsAvailable || occupied >= newRoom.Capacity) return Results.Conflict(new { message = "The selected room is not available." });
    var oldRoomId = allocation.DormRoomId;
    allocation.DormRoomId = input.DormRoomId; allocation.StudentId = input.StudentId; allocation.StudentName = input.StudentName; allocation.RoommatePreference = input.RoommatePreference;
    if (oldRoomId != input.DormRoomId) allocation.AllocatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();
    if (oldRoomId != input.DormRoomId)
    {
        var oldRoom = await db.DormRooms.FindAsync(oldRoomId);
        if (oldRoom is not null) oldRoom.IsAvailable = await db.HousingAllocations.CountAsync(item => item.DormRoomId == oldRoomId) < oldRoom.Capacity;
        newRoom.IsAvailable = await db.HousingAllocations.CountAsync(item => item.DormRoomId == input.DormRoomId) < newRoom.Capacity;
        await db.SaveChangesAsync();
    }
    return Results.Ok(allocation);
});
app.MapDelete("/api/allocations/{id:int}", async (int id, CampusHousingDbContext db) =>
{
    var allocation = await db.HousingAllocations.FindAsync(id); if (allocation is null) return Results.NotFound();
    var room = await db.DormRooms.FindAsync(allocation.DormRoomId);
    db.HousingAllocations.Remove(allocation); await db.SaveChangesAsync();
    if (room is not null) { room.IsAvailable = await db.HousingAllocations.AnyAsync(item => item.DormRoomId == room.Id) == false || await db.HousingAllocations.CountAsync(item => item.DormRoomId == room.Id) < room.Capacity; await db.SaveChangesAsync(); }
    return Results.NoContent();
}).RequireAuthorization(CampusPulseSecurityExtensions.StaffOnlyPolicy);

app.MapGet("/api/tickets", async (CampusHousingDbContext db, ClaimsPrincipal user) =>
{
    var tickets = db.MaintenanceTickets.AsNoTracking();
    if (!user.IsInRole("Staff"))
    {
        var studentId = user.FindFirst("student_id")?.Value;
        if (string.IsNullOrWhiteSpace(studentId)) return Results.Forbid();
        tickets = tickets.Where(ticket => ticket.StudentId == studentId);
    }
    return Results.Ok(await tickets.OrderByDescending(ticket => ticket.CreatedAt).ToListAsync());
});
app.MapPost("/api/tickets", async (MaintenanceTicket ticket, CampusHousingDbContext db, ClaimsPrincipal user) =>
{
    if (!user.CanAccessStudent(ticket.StudentId)) return Results.Forbid();
    if (string.IsNullOrWhiteSpace(ticket.StudentId) || string.IsNullOrWhiteSpace(ticket.Description))
        return Results.BadRequest(new { message = "Student ID and description are required." });
    ticket.CreatedAt = DateTime.UtcNow; db.MaintenanceTickets.Add(ticket); await db.SaveChangesAsync();
    return Results.Created($"/api/tickets/{ticket.Id}", ticket);
});
app.MapGet("/api/tickets/{id:int}", async (int id, CampusHousingDbContext db, ClaimsPrincipal user) =>
{
    var ticket = await db.MaintenanceTickets.FindAsync(id);
    return ticket is null ? Results.NotFound() : !user.CanAccessStudent(ticket.StudentId) ? Results.Forbid() : Results.Ok(ticket);
});
app.MapPut("/api/tickets/{id:int}", async (int id, MaintenanceTicket input, CampusHousingDbContext db) =>
{
    var ticket = await db.MaintenanceTickets.FindAsync(id); if (ticket is null) return Results.NotFound();
    if (string.IsNullOrWhiteSpace(input.Status)) return Results.BadRequest(new { message = "Status is required." });
    ticket.Status = input.Status; ticket.Severity = string.IsNullOrWhiteSpace(input.Severity) ? ticket.Severity : input.Severity; ticket.RepairQueue = string.IsNullOrWhiteSpace(input.RepairQueue) ? ticket.RepairQueue : input.RepairQueue;
    ticket.ResolvedAt = ticket.Status.Equals("Resolved", StringComparison.OrdinalIgnoreCase) ? DateTime.UtcNow : null;
    await db.SaveChangesAsync(); return Results.Ok(ticket);
}).RequireAuthorization(CampusPulseSecurityExtensions.StaffOnlyPolicy);
app.MapDelete("/api/tickets/{id:int}", async (int id, CampusHousingDbContext db) =>
{
    var ticket = await db.MaintenanceTickets.FindAsync(id); if (ticket is null) return Results.NotFound();
    db.MaintenanceTickets.Remove(ticket); await db.SaveChangesAsync(); return Results.NoContent();
}).RequireAuthorization(CampusPulseSecurityExtensions.StaffOnlyPolicy);

await app.RunAsync();

static async Task<List<AcademicRecordsUser>?> GetAcademicRecordsUsersAsync(HttpClient client)
{
    const int maxAttempts = 15;

    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            return await client.GetFromJsonAsync<List<AcademicRecordsUser>>("api/users");
        }
        catch (HttpRequestException) when (attempt < maxAttempts)
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
        catch (JsonException) when (attempt < maxAttempts)
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }

    throw new InvalidOperationException("Academic Records did not become available while Campus Housing was starting.");
}

