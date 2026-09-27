using AcademicRecords.Database.Contracts;
using AcademicRecords.Database.Controllers;
using AcademicRecords.Database.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcademicRecords.Tests;

public sealed class StudentsControllerTests
{
    [Test]
    public async Task GetByUserReturnsStudentProfileAndTermEnrollment()
    {
        await using var testDatabase = await ControllerTestDatabase.CreateAsync();
        var database = testDatabase.Context;
        var student = CreateStudent("STU001", "Avery", "Student", "avery@example.edu");
        var term = CreateTerm("2026-T1");
        database.AddRange(student, term);
        await database.SaveChangesAsync();
        database.Enrollments.Add(new Enrollment
        {
            StudentId = student.UserId,
            CourseCode = "CS101",
            AcademicTermId = term.TermId,
            AcademicYear = 2026
        });
        await database.SaveChangesAsync();

        var result = await new StudentsController(database).GetByUser(student.UserId, CancellationToken.None);

        var response = result.Result as OkObjectResult;
        Assert.That(response, Is.Not.Null);
        var profile = response!.Value as StudentProfileResponse;
        Assert.That(profile, Is.Not.Null);
        Assert.That(profile!.StudentId, Is.EqualTo("STU001"));
        Assert.That(profile.Enrollments, Has.Count.EqualTo(1));
        Assert.That(profile.Enrollments[0].CourseCode, Is.EqualTo("CS101"));
        Assert.That(profile.Enrollments[0].TermCode, Is.EqualTo("2026-T1"));
    }

    [Test]
    public async Task GetStudentByIdAndGetAllReturnProfilesInNameOrder()
    {
        await using var testDatabase = await ControllerTestDatabase.CreateAsync();
        var database = testDatabase.Context;
        database.AddRange(
            CreateStudent("STU002", "Zoe", "Young", "zoe@example.edu"),
            CreateStudent("STU001", "Alex", "Adams", "alex@example.edu"));
        await database.SaveChangesAsync();
        var controller = new StudentsController(database);

        var listResult = await controller.GetAll(CancellationToken.None);
        var listResponse = listResult.Result as OkObjectResult;
        Assert.That(listResponse, Is.Not.Null);
        var profiles = (listResponse!.Value as IEnumerable<StudentProfileResponse>)?.ToList();
        Assert.That(profiles, Is.Not.Null);
        Assert.That(profiles!.Select(profile => profile.StudentId), Is.EqualTo(new[] { "STU001", "STU002" }));

        var singleResult = await controller.GetByStudentId("STU001", CancellationToken.None);
        var singleResponse = singleResult.Result as OkObjectResult;
        Assert.That(singleResponse, Is.Not.Null);
        Assert.That(((StudentProfileResponse)singleResponse!.Value!).Email, Is.EqualTo("alex@example.edu"));
    }

    [Test]
    public async Task GetUnknownStudentByUserAndStudentIdReturnsNotFound()
    {
        await using var testDatabase = await ControllerTestDatabase.CreateAsync();
        var controller = new StudentsController(testDatabase.Context);

        var userResult = await controller.GetByUser(999, CancellationToken.None);
        var studentIdResult = await controller.GetByStudentId("STU404", CancellationToken.None);

        Assert.That(userResult.Result, Is.InstanceOf<NotFoundResult>());
        Assert.That(studentIdResult.Result, Is.InstanceOf<NotFoundResult>());
    }

    [Test]
    public async Task EnrollStudentRejectsMissingRequiredFieldsWithoutPersisting()
    {
        await using var testDatabase = await ControllerTestDatabase.CreateAsync();
        var request = new EnrollStudentRequest(" ", "Student", "student@example.edu", new DateOnly(2000, 1, 1),
            "1 Campus Way", "Contact", "0400000000");

        var result = await new StudentsController(testDatabase.Context).Enroll(request, CancellationToken.None);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        Assert.That(await testDatabase.Context.Students.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task EnrollStudentRejectsCourseWithoutTermAndUnknownTerm()
    {
        await using var testDatabase = await ControllerTestDatabase.CreateAsync();
        var controller = new StudentsController(testDatabase.Context);
        var validRequest = CreateEnrollmentRequest();

        var missingCourse = await controller.Enroll(
            validRequest with { AcademicTermId = 1 }, CancellationToken.None);
        var unknownTerm = await controller.Enroll(
            validRequest with { AcademicTermId = 99, CourseCode = "CS101" }, CancellationToken.None);

        Assert.That(missingCourse, Is.InstanceOf<BadRequestObjectResult>());
        Assert.That(unknownTerm, Is.InstanceOf<NotFoundObjectResult>());
        Assert.That(await testDatabase.Context.Students.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task EnrollStudentPersistsProfileAndOptionalCourseEnrollment()
    {
        await using var testDatabase = await ControllerTestDatabase.CreateAsync();
        var database = testDatabase.Context;
        var term = CreateTerm("2026-T1");
        database.AcademicTerms.Add(term);
        await database.SaveChangesAsync();
        var request = CreateEnrollmentRequest() with
        {
            AcademicTermId = term.TermId,
            CourseCode = " CS101 ",
            AcademicYear = 2026
        };

        var result = await new StudentsController(database).Enroll(request, CancellationToken.None);

        var created = result as CreatedResult;
        Assert.That(created, Is.Not.Null);
        Assert.That(created!.Location, Is.EqualTo("/api/students/STU001"));
        var student = await database.Students.SingleAsync(value => value.StudentId == "STU001");
        Assert.That(student.FirstName, Is.EqualTo("Avery"));
        Assert.That(student.LastName, Is.EqualTo("Student"));
        Assert.That(student.Email, Is.EqualTo("avery@example.edu"));
        var enrollment = await database.Enrollments.SingleAsync();
        Assert.That(enrollment.StudentId, Is.EqualTo(student.UserId));
        Assert.That(enrollment.CourseCode, Is.EqualTo("CS101"));
        Assert.That(enrollment.AcademicTermId, Is.EqualTo(term.TermId));
        Assert.That(enrollment.AcademicYear, Is.EqualTo(2026));
    }

    [Test]
    public async Task AddEnrollmentPersistsEnrollmentForExistingStudent()
    {
        await using var testDatabase = await ControllerTestDatabase.CreateAsync();
        var database = testDatabase.Context;
        var student = CreateStudent("STU001", "Avery", "Student", "avery@example.edu");
        var term = CreateTerm("2026-T1");
        database.AddRange(student, term);
        await database.SaveChangesAsync();

        var result = await new StudentsController(database).AddEnrollment(
            student.StudentId, new AddEnrollmentRequest(" CS101 ", term.TermId, 2026), CancellationToken.None);

        var created = result as CreatedResult;
        Assert.That(created, Is.Not.Null);
        Assert.That(created!.Location, Does.StartWith("/api/students/STU001/enrollments/"));
        var enrollment = await database.Enrollments.SingleAsync();
        Assert.That(enrollment.StudentId, Is.EqualTo(student.UserId));
        Assert.That(enrollment.CourseCode, Is.EqualTo("CS101"));
        Assert.That(enrollment.AcademicTermId, Is.EqualTo(term.TermId));
    }

    [Test]
    public async Task AddEnrollmentReturnsNotFoundForMissingStudentOrTerm()
    {
        await using var testDatabase = await ControllerTestDatabase.CreateAsync();
        var database = testDatabase.Context;
        var student = CreateStudent("STU001", "Avery", "Student", "avery@example.edu");
        database.Students.Add(student);
        await database.SaveChangesAsync();
        var controller = new StudentsController(database);
        var request = new AddEnrollmentRequest("CS101", 99);

        var missingStudent = await controller.AddEnrollment("STU404", request, CancellationToken.None);
        var missingTerm = await controller.AddEnrollment(student.StudentId, request, CancellationToken.None);

        Assert.That(missingStudent, Is.InstanceOf<NotFoundObjectResult>());
        Assert.That(missingTerm, Is.InstanceOf<NotFoundObjectResult>());
        Assert.That(await database.Enrollments.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task UpdateStudentPersistsTrimmedProfileFields()
    {
        await using var testDatabase = await ControllerTestDatabase.CreateAsync();
        var database = testDatabase.Context;
        database.Students.Add(CreateStudent("STU001", "Old", "Name", "old@example.edu"));
        await database.SaveChangesAsync();
        var request = new UpdateStudentRequest(" New ", " Name ", "new@example.edu", new DateOnly(2001, 2, 3),
            " New Address ", " Emergency Contact ", "0400000001", "0400000002", " BSc ", " CS ", " Math ");

        var result = await new StudentsController(database).Update("STU001", request, CancellationToken.None);

        Assert.That(result, Is.InstanceOf<NoContentResult>());
        var updated = await database.Students.AsNoTracking().SingleAsync(student => student.StudentId == "STU001");
        Assert.That(updated.FirstName, Is.EqualTo("New"));
        Assert.That(updated.LastName, Is.EqualTo("Name"));
        Assert.That(updated.Email, Is.EqualTo("new@example.edu"));
        Assert.That(updated.MailingAddress, Is.EqualTo("New Address"));
        Assert.That(updated.EmergencyContactName, Is.EqualTo("Emergency Contact"));
        Assert.That(updated.EmergencyContactPhone, Is.EqualTo("0400000001"));
        Assert.That(updated.PhoneNumber, Is.EqualTo("0400000002"));
        Assert.That(updated.Degree, Is.EqualTo("BSc"));
        Assert.That(updated.Major, Is.EqualTo("CS"));
        Assert.That(updated.SubMajor, Is.EqualTo("Math"));
    }

    [Test]
    public async Task UpdateStudentRejectsInvalidEmergencyPhoneWithoutPersisting()
    {
        await using var testDatabase = await ControllerTestDatabase.CreateAsync();
        var database = testDatabase.Context;
        database.Students.Add(CreateStudent("STU001", "Old", "Name", "old@example.edu"));
        await database.SaveChangesAsync();
        var request = new UpdateStudentRequest("New", "Name", "new@example.edu", new DateOnly(2001, 2, 3),
            "New Address", "Contact", "bad-phone");

        var result = await new StudentsController(database).Update("STU001", request, CancellationToken.None);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        var unchanged = await database.Students.AsNoTracking().SingleAsync(student => student.StudentId == "STU001");
        Assert.That(unchanged.FirstName, Is.EqualTo("Old"));
        Assert.That(unchanged.Email, Is.EqualTo("old@example.edu"));
    }

    [Test]
    public async Task UpdateUnknownStudentReturnsNotFound()
    {
        await using var testDatabase = await ControllerTestDatabase.CreateAsync();
        var request = new UpdateStudentRequest("New", "Name", "new@example.edu", new DateOnly(2001, 2, 3),
            "New Address", "Contact", "0400000001");

        var result = await new StudentsController(testDatabase.Context).Update("STU404", request, CancellationToken.None);

        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task UpdateGraduationRejectsFutureDateAndPersistsPastDate()
    {
        await using var testDatabase = await ControllerTestDatabase.CreateAsync();
        var database = testDatabase.Context;
        database.Students.Add(CreateStudent("STU001", "Avery", "Student", "avery@example.edu"));
        await database.SaveChangesAsync();
        var controller = new StudentsController(database);
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

        var futureResult = await controller.UpdateGraduation(
            "STU001", new GraduationUpdateRequest(futureDate), CancellationToken.None);
        var validDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var validResult = await controller.UpdateGraduation(
            "STU001", new GraduationUpdateRequest(validDate), CancellationToken.None);

        Assert.That(futureResult, Is.InstanceOf<BadRequestObjectResult>());
        var response = validResult as OkObjectResult;
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Value, Is.EqualTo(new GraduationResponse("STU001", validDate)));
        var student = await database.Students.AsNoTracking().SingleAsync(value => value.StudentId == "STU001");
        Assert.That(student.GraduationDate, Is.EqualTo(validDate));
    }

    [Test]
    public async Task UpdateGraduationReturnsNotFoundForUnknownStudent()
    {
        await using var testDatabase = await ControllerTestDatabase.CreateAsync();

        var result = await new StudentsController(testDatabase.Context).UpdateGraduation(
            "STU404", new GraduationUpdateRequest(new DateOnly(2025, 12, 1)), CancellationToken.None);

        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    private static EnrollStudentRequest CreateEnrollmentRequest() => new(
        " Avery ", " Student ", "avery@example.edu", new DateOnly(2000, 1, 1),
        "1 Campus Way", "Emergency Contact", "0400000000");

    private static Student CreateStudent(string studentId, string firstName, string lastName, string email) => new()
    {
        StudentId = studentId,
        FirstName = firstName,
        LastName = lastName,
        Email = email,
        DateOfBirth = new DateOnly(2000, 1, 1),
        EnrollmentDate = new DateOnly(2020, 1, 1),
        Status = "ACT",
        MailingAddress = "1 Campus Way",
        EmergencyContactName = "Emergency Contact",
        EmergencyContactPhone = "0400000000"
    };

    private static AcademicTerm CreateTerm(string termCode) => new()
    {
        TermCode = termCode,
        TermName = "2026 Term 1",
        StartWeekOfYear = 1,
        EndWeekOfYear = 13,
        IsActive = true
    };
}
