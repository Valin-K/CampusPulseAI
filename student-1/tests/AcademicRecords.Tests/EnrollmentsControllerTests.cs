using System.Globalization;
using AcademicRecords.Database.Contracts;
using AcademicRecords.Database.Controllers;
using AcademicRecords.Database.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcademicRecords.Tests;

public sealed class EnrollmentsControllerTests
{
    [TestCase(-1)]
    [TestCase(101)]
    public async Task UpdateGradeRejectsValuesOutsideRangeWithoutPersisting(int grade)
    {
        await using var testDatabase = await ControllerTestDatabase.CreateAsync();
        var database = testDatabase.Context;
        var enrollment = await AddEnrollmentAsync(database, CurrentTerm());
        enrollment.GradeValue = 75;
        await database.SaveChangesAsync();

        var result = await new EnrollmentsController(database).UpdateGrade(
            enrollment.EnrollmentId, new GradeEntryRequest(grade), CancellationToken.None);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        var stored = await database.Enrollments.AsNoTracking().SingleAsync();
        Assert.That(stored.GradeValue, Is.EqualTo(75));
    }

    [Test]
    public async Task UpdateGradeReturnsNotFoundForUnknownEnrollment()
    {
        await using var testDatabase = await ControllerTestDatabase.CreateAsync();

        var result = await new EnrollmentsController(testDatabase.Context).UpdateGrade(
            404, new GradeEntryRequest(75), CancellationToken.None);

        Assert.That(result, Is.InstanceOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task UpdateGradeRejectsEnrollmentWithoutAcademicTerm()
    {
        await using var testDatabase = await ControllerTestDatabase.CreateAsync();
        var database = testDatabase.Context;
        var enrollment = new Enrollment { CourseCode = "CS101", AcademicYear = DateTime.UtcNow.Year };
        database.Enrollments.Add(enrollment);
        await database.SaveChangesAsync();

        var result = await new EnrollmentsController(database).UpdateGrade(
            enrollment.EnrollmentId, new GradeEntryRequest(75), CancellationToken.None);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        var stored = await database.Enrollments.AsNoTracking().SingleAsync();
        Assert.That(stored.GradeValue, Is.Null);
    }

    [Test]
    public async Task UpdateGradeRejectsFutureSessionWithoutPersisting()
    {
        await using var testDatabase = await ControllerTestDatabase.CreateAsync();
        var database = testDatabase.Context;
        var enrollment = await AddEnrollmentAsync(database, new AcademicTerm
        {
            TermCode = "FUTURE",
            TermName = "Future Term",
            StartWeekOfYear = 1,
            EndWeekOfYear = 2,
            IsActive = true
        }, IsoYearToday() + 1);

        var result = await new EnrollmentsController(database).UpdateGrade(
            enrollment.EnrollmentId, new GradeEntryRequest(75), CancellationToken.None);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        var stored = await database.Enrollments.AsNoTracking().SingleAsync();
        Assert.That(stored.GradeValue, Is.Null);
        Assert.That(stored.GradeDate, Is.Null);
    }

    [Test]
    public async Task UpdateGradeRejectsEndedSessionWithoutPersisting()
    {
        await using var testDatabase = await ControllerTestDatabase.CreateAsync();
        var database = testDatabase.Context;
        var enrollment = await AddEnrollmentAsync(database, new AcademicTerm
        {
            TermCode = "ENDED",
            TermName = "Ended Term",
            StartWeekOfYear = 1,
            EndWeekOfYear = 2,
            IsActive = true
        }, IsoYearToday() - 1);

        var result = await new EnrollmentsController(database).UpdateGrade(
            enrollment.EnrollmentId, new GradeEntryRequest(75), CancellationToken.None);

        Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        var stored = await database.Enrollments.AsNoTracking().SingleAsync();
        Assert.That(stored.GradeValue, Is.Null);
        Assert.That(stored.GradeDate, Is.Null);
    }

    [Test]
    public async Task UpdateGradePersistsGradeAndAssignmentDateDuringSession()
    {
        await using var testDatabase = await ControllerTestDatabase.CreateAsync();
        var database = testDatabase.Context;
        var enrollment = await AddEnrollmentAsync(database, CurrentTerm());

        var result = await new EnrollmentsController(database).UpdateGrade(
            enrollment.EnrollmentId, new GradeEntryRequest(82.5m), CancellationToken.None);

        var response = result as OkObjectResult;
        Assert.That(response, Is.Not.Null);
        var stored = await database.Enrollments.AsNoTracking().SingleAsync();
        Assert.That(stored.GradeValue, Is.EqualTo(82.5m));
        Assert.That(stored.GradeDate, Is.EqualTo(DateOnly.FromDateTime(DateTime.UtcNow)));
    }

    private static AcademicTerm CurrentTerm()
    {
        var today = DateTime.UtcNow;
        var week = ISOWeek.GetWeekOfYear(today);
        return new AcademicTerm
        {
            TermCode = "CURRENT",
            TermName = "Current Term",
            StartWeekOfYear = week,
            EndWeekOfYear = week,
            IsActive = true
        };
    }

    private static int IsoYearToday() => ISOWeek.GetYear(DateTime.UtcNow);

    private static async Task<Enrollment> AddEnrollmentAsync(
        AcademicRecords.Database.Data.AcademicRecordsDbContext database,
        AcademicTerm term,
        int? academicYear = null)
    {
        database.AcademicTerms.Add(term);
        await database.SaveChangesAsync();
        var enrollment = new Enrollment
        {
            CourseCode = "CS101",
            AcademicTermId = term.TermId,
            AcademicYear = academicYear ?? IsoYearToday()
        };
        database.Enrollments.Add(enrollment);
        await database.SaveChangesAsync();
        return enrollment;
    }
}
