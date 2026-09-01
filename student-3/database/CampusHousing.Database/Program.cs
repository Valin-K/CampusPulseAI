using CampusHousing.Database.Data;
using CampusHousing.Database.Models;
using Microsoft.EntityFrameworkCore;

var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("CampusHousing")
    ?? "Data Source=/app/data/campus-housing.db";
builder.Services.AddDbContext<CampusHousingDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
var databasePath = connectionString.Replace("Data Source=", string.Empty).Split(';')[0];
var databaseDirectory = Path.GetDirectoryName(databasePath);
if (!string.IsNullOrWhiteSpace(databaseDirectory)) Directory.CreateDirectory(databaseDirectory);
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CampusHousingDbContext>();
    await db.Database.EnsureCreatedAsync();
}
app.UseCors();

app.MapGet("/health", () => Results.Ok(new { service = "campus-housing-database", status = " healthy".Trim() }));
app.MapGet("/api/rooms", async (CampusHousingDbContext db) => Results.Ok(await db.DormRooms.AsNoTracking()
    .Select(room => new
    {
        room.Id, room.Building, room.RoomNumber, room.Capacity,
        OccupiedBeds = db.HousingAllocations.Count(allocation => allocation.DormRoomId == room.Id),
        room.IsAvailable
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
});
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
});
app.MapDelete("/api/rooms/{id:int}", async (int id, CampusHousingDbContext db) =>
{
    var room = await db.DormRooms.FindAsync(id); if (room is null) return Results.NotFound();
    db.DormRooms.Remove(room); await db.SaveChangesAsync(); return Results.NoContent();
});

app.MapGet("/api/allocations", async (CampusHousingDbContext db) => Results.Ok(await db.HousingAllocations.AsNoTracking().ToListAsync()));
app.MapPost("/api/allocations", async (HousingAllocation allocation, CampusHousingDbContext db) =>
{
    var room = await db.DormRooms.FindAsync(allocation.DormRoomId);
    if (room is null) return Results.BadRequest(new { message = "Room does not exist." });
    if (string.IsNullOrWhiteSpace(allocation.StudentId) || string.IsNullOrWhiteSpace(allocation.StudentName))
        return Results.BadRequest(new { message = "Student ID and student name are required." });
    if (!room.IsAvailable) return Results.Conflict(new { message = "Room is not available for booking." });
    var occupied = await db.HousingAllocations.CountAsync(item => item.DormRoomId == allocation.DormRoomId);
    if (occupied >= room.Capacity) return Results.Conflict(new { message = "Room is at capacity." });
    allocation.AllocatedAt = DateTime.UtcNow; db.HousingAllocations.Add(allocation); await db.SaveChangesAsync();
    room.IsAvailable = occupied + 1 < room.Capacity;
    await db.SaveChangesAsync();
    return Results.Created($"/api/allocations/{allocation.Id}", allocation);
});
app.MapGet("/api/allocations/{id:int}", async (int id, CampusHousingDbContext db) =>
    await db.HousingAllocations.FindAsync(id) is { } allocation ? Results.Ok(allocation) : Results.NotFound());
app.MapPut("/api/allocations/{id:int}", async (int id, HousingAllocation input, CampusHousingDbContext db) =>
{
    var allocation = await db.HousingAllocations.FindAsync(id);
    if (allocation is null) return Results.NotFound();
    if (string.IsNullOrWhiteSpace(input.StudentId) || string.IsNullOrWhiteSpace(input.StudentName))
        return Results.BadRequest(new { message = "Student ID and student name are required." });
    allocation.StudentId = input.StudentId; allocation.StudentName = input.StudentName; allocation.RoommatePreference = input.RoommatePreference;
    await db.SaveChangesAsync(); return Results.Ok(allocation);
});
app.MapDelete("/api/allocations/{id:int}", async (int id, CampusHousingDbContext db) =>
{
    var allocation = await db.HousingAllocations.FindAsync(id); if (allocation is null) return Results.NotFound();
    var room = await db.DormRooms.FindAsync(allocation.DormRoomId);
    db.HousingAllocations.Remove(allocation); await db.SaveChangesAsync();
    if (room is not null) { room.IsAvailable = await db.HousingAllocations.AnyAsync(item => item.DormRoomId == room.Id) == false || await db.HousingAllocations.CountAsync(item => item.DormRoomId == room.Id) < room.Capacity; await db.SaveChangesAsync(); }
    return Results.NoContent();
});

app.MapGet("/api/tickets", async (CampusHousingDbContext db) => Results.Ok(await db.MaintenanceTickets.AsNoTracking().OrderByDescending(t => t.CreatedAt).ToListAsync()));
app.MapPost("/api/tickets", async (MaintenanceTicket ticket, CampusHousingDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(ticket.StudentId) || string.IsNullOrWhiteSpace(ticket.Description))
        return Results.BadRequest(new { message = "Student ID and description are required." });
    ticket.CreatedAt = DateTime.UtcNow; db.MaintenanceTickets.Add(ticket); await db.SaveChangesAsync();
    return Results.Created($"/api/tickets/{ticket.Id}", ticket);
});
app.MapGet("/api/tickets/{id:int}", async (int id, CampusHousingDbContext db) =>
    await db.MaintenanceTickets.FindAsync(id) is { } ticket ? Results.Ok(ticket) : Results.NotFound());
app.MapPut("/api/tickets/{id:int}", async (int id, MaintenanceTicket input, CampusHousingDbContext db) =>
{
    var ticket = await db.MaintenanceTickets.FindAsync(id); if (ticket is null) return Results.NotFound();
    if (string.IsNullOrWhiteSpace(input.Status)) return Results.BadRequest(new { message = "Status is required." });
    ticket.Status = input.Status; ticket.Severity = string.IsNullOrWhiteSpace(input.Severity) ? ticket.Severity : input.Severity; ticket.RepairQueue = string.IsNullOrWhiteSpace(input.RepairQueue) ? ticket.RepairQueue : input.RepairQueue;
    ticket.ResolvedAt = ticket.Status.Equals("Resolved", StringComparison.OrdinalIgnoreCase) ? DateTime.UtcNow : null;
    await db.SaveChangesAsync(); return Results.Ok(ticket);
});
app.MapDelete("/api/tickets/{id:int}", async (int id, CampusHousingDbContext db) =>
{
    var ticket = await db.MaintenanceTickets.FindAsync(id); if (ticket is null) return Results.NotFound();
    db.MaintenanceTickets.Remove(ticket); await db.SaveChangesAsync(); return Results.NoContent();
});

await app.RunAsync();
