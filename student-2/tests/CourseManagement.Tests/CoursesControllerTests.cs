using System.ComponentModel.DataAnnotations;
using CourseManagement.Backend.Contracts.Courses;
using CourseManagement.Backend.Controllers;
using CourseManagement.Database.Data;
using CourseManagement.Database.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CourseManagement.Tests;

public sealed class CoursesControllerTests
{
    [Test]
    public async Task CreateNormalizesCodeAndPersistsCourse()
    {
        await using var context = await CourseTestContext.CreateAsync();

        var result = await context.Controller.Create(
            CreateRequest(" cpai5000 "),
            CancellationToken.None);

        Assert.That(result.Result, Is.TypeOf<CreatedAtActionResult>());

        var course = await context.Database.Courses.SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(course.Code, Is.EqualTo("CPAI5000"));
            Assert.That(course.Name, Is.EqualTo("Test Course"));
            Assert.That(course.IsActive, Is.True);
        });
    }

    [Test]
    public async Task CreateReturnsConflictForDuplicateCode()
    {
        await using var context = await CourseTestContext.CreateAsync();
        await AddCourseAsync(context.Database, "CPAI5000");

        var result = await context.Controller.Create(
            CreateRequest("cpai5000"),
            CancellationToken.None);

        Assert.That(result.Result, Is.TypeOf<ConflictObjectResult>());
        Assert.That(await context.Database.Courses.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task UpdateChangesExistingCourse()
    {
        await using var context = await CourseTestContext.CreateAsync();
        var course = await AddCourseAsync(context.Database, "CPAI5000");
        var request = CreateRequest("CPAI5001");
        request.Name = "Updated Course";
        request.Level = 5;

        var result = await context.Controller.Update(
            course.Id,
            request,
            CancellationToken.None);

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());

        var updatedCourse = await context.Database.Courses.FindAsync(course.Id);
        Assert.Multiple(() =>
        {
            Assert.That(updatedCourse, Is.Not.Null);
            Assert.That(updatedCourse!.Code, Is.EqualTo("CPAI5001"));
            Assert.That(updatedCourse.Name, Is.EqualTo("Updated Course"));
            Assert.That(updatedCourse.Level, Is.EqualTo(5));
        });
    }

    [Test]
    public async Task SetStatusDeactivatesAndReactivatesCourse()
    {
        await using var context = await CourseTestContext.CreateAsync();
        var course = await AddCourseAsync(context.Database, "CPAI5000");

        var deactivateResult = await context.Controller.SetStatus(
            course.Id,
            new CourseStatusRequest { IsActive = false },
            CancellationToken.None);
        Assert.That(deactivateResult.Result, Is.TypeOf<OkObjectResult>());
        Assert.That(course.IsActive, Is.False);

        var reactivateResult = await context.Controller.SetStatus(
            course.Id,
            new CourseStatusRequest { IsActive = true },
            CancellationToken.None);
        Assert.That(reactivateResult.Result, Is.TypeOf<OkObjectResult>());
        Assert.That(course.IsActive, Is.True);
    }

    [Test]
    public async Task DeleteRemovesCourse()
    {
        await using var context = await CourseTestContext.CreateAsync();
        var course = await AddCourseAsync(context.Database, "CPAI5000");

        var result = await context.Controller.Delete(course.Id, CancellationToken.None);

        Assert.That(result, Is.TypeOf<NoContentResult>());
        Assert.That(await context.Database.Courses.AnyAsync(), Is.False);
    }

    [Test]
    public async Task DeleteReturnsConflictWhenCourseIsRequiredByAnotherCourse()
    {
        await using var context = await CourseTestContext.CreateAsync();
        var requiredCourse = await AddCourseAsync(context.Database, "CPAI1001");
        var dependentCourse = await AddCourseAsync(context.Database, "CPAI2001");
        context.Database.Prerequisites.Add(new Prerequisite
        {
            CourseId = dependentCourse.Id,
            RequiredCourseId = requiredCourse.Id,
            MinimumGrade = "Pass"
        });
        await context.Database.SaveChangesAsync();

        var result = await context.Controller.Delete(
            requiredCourse.Id,
            CancellationToken.None);

        Assert.That(result, Is.TypeOf<ConflictObjectResult>());
        Assert.That(await context.Database.Courses.CountAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task SetStatusReturnsNotFoundForUnknownCourse()
    {
        await using var context = await CourseTestContext.CreateAsync();

        var result = await context.Controller.SetStatus(
            999,
            new CourseStatusRequest { IsActive = false },
            CancellationToken.None);

        Assert.That(result.Result, Is.TypeOf<NotFoundObjectResult>());
    }

    [Test]
    public async Task SetStatusRejectsMissingStatusWithoutChangingCourse()
    {
        await using var context = await CourseTestContext.CreateAsync();
        var course = await AddCourseAsync(context.Database, "CPAI5000");

        var result = await context.Controller.SetStatus(
            course.Id,
            new CourseStatusRequest(),
            CancellationToken.None);

        Assert.That(result.Result, Is.TypeOf<BadRequestObjectResult>());
        var badRequestResult = (BadRequestObjectResult)result.Result;
        Assert.That(badRequestResult.Value, Is.TypeOf<ValidationProblemDetails>());
        var problemDetails = (ValidationProblemDetails)badRequestResult.Value;
        Assert.That(problemDetails.Status, Is.EqualTo(400));
        Assert.That(problemDetails.Errors, Contains.Key(nameof(CourseStatusRequest.IsActive)));
        Assert.That(course.IsActive, Is.True);
    }

    [Test]
    public void CourseRequestRejectsWhitespaceOnlyCode()
    {
        var request = CreateRequest("   ");
        var validationResults = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            validationResults,
            validateAllProperties: true);

        Assert.That(isValid, Is.False);
        Assert.That(
            validationResults.Select(result => result.ErrorMessage),
            Does.Contain("Course code is required."));
    }

    static CourseRequest CreateRequest(string code)
    {
        return new CourseRequest
        {
            Code = code,
            Name = "Test Course",
            Description = "Course created by a controller test.",
            CreditPoints = 6,
            Level = 4,
            IsActive = true
        };
    }

    static async Task<Course> AddCourseAsync(
        CourseManagementDbContext database,
        string code)
    {
        var course = new Course
        {
            Code = code,
            Name = $"{code} Course",
            Description = "Course created by a controller test.",
            CreditPoints = 6,
            Level = 1,
            IsActive = true
        };

        database.Courses.Add(course);
        await database.SaveChangesAsync();
        return course;
    }

    sealed class CourseTestContext(
        SqliteConnection connection,
        CourseManagementDbContext database) : IAsyncDisposable
    {
        public CourseManagementDbContext Database { get; } = database;
        public CoursesController Controller { get; } = new(database);

        public static async Task<CourseTestContext> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<CourseManagementDbContext>()
                .UseSqlite(connection)
                .Options;
            var database = new CourseManagementDbContext(options);
            await database.Database.EnsureCreatedAsync();

            return new CourseTestContext(connection, database);
        }

        public async ValueTask DisposeAsync()
        {
            await Database.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
