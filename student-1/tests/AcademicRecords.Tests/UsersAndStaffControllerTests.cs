using AcademicRecords.Database.Contracts;
using AcademicRecords.Database.Controllers;
using AcademicRecords.Database.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DatabaseUserResponse = AcademicRecords.Database.Contracts.UserResponse;

namespace AcademicRecords.Tests;

public sealed class UsersAndStaffControllerTests
{
    [Test]
    public async Task GetUsersReturnsProfilesSortedByLastNameAndRoleIdentifiers()
    {
        await using var testDatabase = await ControllerTestDatabase.CreateAsync();
        var database = testDatabase.Context;
        database.AddRange(
            new Student
            {
                StudentId = "STU002",
                FirstName = "Zoe",
                LastName = "Young",
                Email = "zoe@example.edu",
                Status = "ACT",
                MailingAddress = "2 Campus Way",
                EmergencyContactName = "Contact Two",
                EmergencyContactPhone = "0400000002"
            },
            new Staff
            {
                StaffId = "STF001",
                FirstName = "Alex",
                LastName = "Adams",
                Email = "alex@example.edu",
                Status = "ACT"
            });
        await database.SaveChangesAsync();

        var result = await new UsersController(database).GetAll(CancellationToken.None);

        var response = result.Result as OkObjectResult;
        Assert.That(response, Is.Not.Null);
        var users = (response!.Value as IEnumerable<DatabaseUserResponse>)?.ToList();
        Assert.That(users, Is.Not.Null, $"Unexpected users response type: {response.Value?.GetType().FullName ?? "null"}");
        Assert.That(users!.Select(user => user.LastName), Is.EqualTo(new[] { "Adams", "Young" }));
        Assert.That(users[0].Role, Is.EqualTo("Staff"));
        Assert.That(users[0].StaffId, Is.EqualTo("STF001"));
        Assert.That(users[0].StudentId, Is.Null);
        Assert.That(users[1].Role, Is.EqualTo("Student"));
        Assert.That(users[1].StudentId, Is.EqualTo("STU002"));
        Assert.That(users[1].StaffId, Is.Null);
    }

    [Test]
    public async Task GetStaffByUserReturnsProfile()
    {
        await using var testDatabase = await ControllerTestDatabase.CreateAsync();
        var database = testDatabase.Context;
        var staff = CreateStaff("STF001", "Taylor", "Ng", "taylor@example.edu");
        database.Add(staff);
        await database.SaveChangesAsync();

        var result = await new StaffController(database).GetByUser(staff.UserId, CancellationToken.None);

        var response = result.Result as OkObjectResult;
        Assert.That(response, Is.Not.Null);
        var profile = response!.Value as StaffProfileResponse;
        Assert.That(profile, Is.Not.Null);
        Assert.That(profile!.StaffId, Is.EqualTo("STF001"));
        Assert.That(profile.UserId, Is.EqualTo(staff.UserId));
        Assert.That(profile.Email, Is.EqualTo("taylor@example.edu"));
    }

    [Test]
    public async Task GetStaffByUnknownUserReturnsNotFound()
    {
        await using var testDatabase = await ControllerTestDatabase.CreateAsync();

        var result = await new StaffController(testDatabase.Context).GetByUser(999, CancellationToken.None);

        Assert.That(result.Result, Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public async Task GetAllStaffReturnsNamesSortedByLastName()
    {
        await using var testDatabase = await ControllerTestDatabase.CreateAsync();
        var database = testDatabase.Context;
        database.AddRange(
            CreateStaff("STF002", "Morgan", "Young", "morgan@example.edu"),
            CreateStaff("STF001", "Alex", "Adams", "alex@example.edu"));
        await database.SaveChangesAsync();

        var result = await new StaffController(database).GetAll(CancellationToken.None);

        var response = result.Result as OkObjectResult;
        Assert.That(response, Is.Not.Null);
        var profiles = response!.Value as IReadOnlyList<StaffProfileResponse>;
        Assert.That(profiles, Is.Not.Null);
        Assert.That(profiles!.Select(profile => profile.StaffId), Is.EqualTo(new[] { "STF001", "STF002" }));
    }

    [Test]
    public async Task EnrollStaffRejectsInvalidEmailAndDoesNotPersist()
    {
        await using var testDatabase = await ControllerTestDatabase.CreateAsync();
        var request = new EnrollStaffRequest("Sam", "Staff", "not-an-email", new DateOnly(1990, 1, 1));

        var result = await new StaffController(testDatabase.Context).Enroll(request, CancellationToken.None);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        Assert.That(await testDatabase.Context.Set<Staff>().CountAsync(), Is.Zero);
    }

    [Test]
    public async Task EnrollStaffRejectsInvalidPhoneNumberAndDoesNotPersist()
    {
        await using var testDatabase = await ControllerTestDatabase.CreateAsync();
        var request = new EnrollStaffRequest("Sam", "Staff", "sam@example.edu", new DateOnly(1990, 1, 1), "12345");

        var result = await new StaffController(testDatabase.Context).Enroll(request, CancellationToken.None);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        Assert.That(await testDatabase.Context.Set<Staff>().CountAsync(), Is.Zero);
    }

    [Test]
    public async Task EnrollStaffCreatesTrimmedRecordWithNextStaffId()
    {
        await using var testDatabase = await ControllerTestDatabase.CreateAsync();
        var database = testDatabase.Context;
        database.Add(CreateStaff("STF001", "Existing", "Staff", "existing@example.edu"));
        await database.SaveChangesAsync();
        var request = new EnrollStaffRequest(" Sam ", " Staff ", "sam@example.edu", new DateOnly(1990, 1, 1), "0400000000");

        var result = await new StaffController(database).Enroll(request, CancellationToken.None);

        var created = result as CreatedResult;
        Assert.That(created, Is.Not.Null);
        Assert.That(created!.Location, Is.EqualTo("/api/staff/STF002"));
        var persisted = await database.Set<Staff>().SingleAsync(staff => staff.StaffId == "STF002");
        Assert.That(persisted.FirstName, Is.EqualTo("Sam"));
        Assert.That(persisted.LastName, Is.EqualTo("Staff"));
        Assert.That(persisted.PhoneNumber, Is.EqualTo("0400000000"));
    }

    [Test]
    public async Task UpdateStaffPersistsTrimmedValues()
    {
        await using var testDatabase = await ControllerTestDatabase.CreateAsync();
        var database = testDatabase.Context;
        database.Add(CreateStaff("STF001", "Old", "Name", "old@example.edu"));
        await database.SaveChangesAsync();
        var request = new UpdateStaffRequest(" New ", " Name ", "new@example.edu", new DateOnly(1988, 2, 3), "0400000001");

        var result = await new StaffController(database).Update("STF001", request, CancellationToken.None);

        Assert.That(result, Is.InstanceOf<NoContentResult>());
        var updated = await database.Set<Staff>().AsNoTracking().SingleAsync(staff => staff.StaffId == "STF001");
        Assert.That(updated.FirstName, Is.EqualTo("New"));
        Assert.That(updated.LastName, Is.EqualTo("Name"));
        Assert.That(updated.Email, Is.EqualTo("new@example.edu"));
        Assert.That(updated.DateOfBirth, Is.EqualTo(new DateOnly(1988, 2, 3)));
        Assert.That(updated.PhoneNumber, Is.EqualTo("0400000001"));
    }

    [Test]
    public async Task UpdateStaffRejectsInvalidEmailWithoutChangingRecord()
    {
        await using var testDatabase = await ControllerTestDatabase.CreateAsync();
        var database = testDatabase.Context;
        database.Add(CreateStaff("STF001", "Old", "Name", "old@example.edu"));
        await database.SaveChangesAsync();
        var request = new UpdateStaffRequest("New", "Name", "invalid", new DateOnly(1988, 2, 3));

        var result = await new StaffController(database).Update("STF001", request, CancellationToken.None);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        var unchanged = await database.Set<Staff>().AsNoTracking().SingleAsync(staff => staff.StaffId == "STF001");
        Assert.That(unchanged.Email, Is.EqualTo("old@example.edu"));
    }

    [Test]
    public async Task UpdateUnknownStaffReturnsNotFound()
    {
        await using var testDatabase = await ControllerTestDatabase.CreateAsync();
        var request = new UpdateStaffRequest("New", "Name", "new@example.edu", new DateOnly(1988, 2, 3));

        var result = await new StaffController(testDatabase.Context).Update("STF404", request, CancellationToken.None);

        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    private static Staff CreateStaff(string staffId, string firstName, string lastName, string email) => new()
    {
        StaffId = staffId,
        FirstName = firstName,
        LastName = lastName,
        Email = email,
        DateOfBirth = new DateOnly(1985, 1, 1),
        Status = "ACT"
    };
}
