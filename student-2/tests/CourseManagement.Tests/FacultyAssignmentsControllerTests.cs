using System.ComponentModel.DataAnnotations;
using CourseManagement.Backend.Contracts.Courses;
using CourseManagement.Backend.Contracts.FacultyAssignments;
using CourseManagement.Backend.Controllers;
using CourseManagement.Database.Data;
using CourseManagement.Database.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CourseManagement.Tests;

public sealed class FacultyAssignmentsControllerTests
{
    [Test]
    public async Task CreateNormalizesAndPersistsAssignment()
    {
        await using var context = await FacultyAssignmentTestContext.CreateAsync();
        var course = await AddCourseAsync(context.Database, "CPAI4000");

        var result = await context.Assignments.Create(
            CreateRequest(course.Id, " LECTURER@CampusPulse.edu "),
            CancellationToken.None);

        Assert.That(result.Result, Is.TypeOf<CreatedAtActionResult>());
        var assignment = await context.Database.FacultyAssignments.SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(assignment.StaffName, Is.EqualTo("Test Lecturer"));
            Assert.That(assignment.StaffEmail, Is.EqualTo("lecturer@campuspulse.edu"));
            Assert.That(assignment.Role, Is.EqualTo("Lecturer"));
            Assert.That(assignment.TeachingPeriod, Is.EqualTo("Autumn 2026"));
        });
    }

    [Test]
    public async Task GetAllFiltersAssignmentsByCourse()
    {
        await using var context = await FacultyAssignmentTestContext.CreateAsync();
        var selectedCourse = await AddCourseAsync(context.Database, "CPAI4000");
        var otherCourse = await AddCourseAsync(context.Database, "CPAI3000");
        await AddAssignmentAsync(context.Database, selectedCourse.Id, "selected@campuspulse.edu");
        await AddAssignmentAsync(context.Database, otherCourse.Id, "other@campuspulse.edu");

        var result = await context.Assignments.GetAll(
            selectedCourse.Id,
            CancellationToken.None);

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var response = (IReadOnlyList<FacultyAssignmentResponse>)
            ((OkObjectResult)result.Result).Value!;
        Assert.That(response, Has.Count.EqualTo(1));
        Assert.That(response[0].CourseId, Is.EqualTo(selectedCourse.Id));
    }

    [Test]
    public async Task CourseDetailsIncludeFacultyAssignments()
    {
        await using var context = await FacultyAssignmentTestContext.CreateAsync();
        var course = await AddCourseAsync(context.Database, "CPAI4000");
        await AddAssignmentAsync(context.Database, course.Id, "lecturer@campuspulse.edu");

        var result = await context.Courses.GetById(course.Id, CancellationToken.None);

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var response = (CourseDetailResponse)((OkObjectResult)result.Result).Value!;
        Assert.That(response.FacultyAssignments, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(response.FacultyAssignments[0].CourseCode, Is.EqualTo(course.Code));
            Assert.That(
                response.FacultyAssignments[0].StaffEmail,
                Is.EqualTo("lecturer@campuspulse.edu"));
        });
    }

    [Test]
    public async Task UpdateChangesAssignment()
    {
        await using var context = await FacultyAssignmentTestContext.CreateAsync();
        var course = await AddCourseAsync(context.Database, "CPAI4000");
        var assignment = await AddAssignmentAsync(
            context.Database,
            course.Id,
            "lecturer@campuspulse.edu");
        var request = CreateRequest(course.Id, "coordinator@campuspulse.edu");
        request.Role = "Course Coordinator";
        request.TeachingPeriod = "Spring 2026";

        var result = await context.Assignments.Update(
            assignment.Id,
            request,
            CancellationToken.None);

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        Assert.Multiple(() =>
        {
            Assert.That(assignment.StaffEmail, Is.EqualTo("coordinator@campuspulse.edu"));
            Assert.That(assignment.Role, Is.EqualTo("Course Coordinator"));
            Assert.That(assignment.TeachingPeriod, Is.EqualTo("Spring 2026"));
        });
    }

    [Test]
    public async Task DeleteRemovesAssignment()
    {
        await using var context = await FacultyAssignmentTestContext.CreateAsync();
        var course = await AddCourseAsync(context.Database, "CPAI4000");
        var assignment = await AddAssignmentAsync(
            context.Database,
            course.Id,
            "lecturer@campuspulse.edu");

        var result = await context.Assignments.Delete(
            assignment.Id,
            CancellationToken.None);

        Assert.That(result, Is.TypeOf<NoContentResult>());
        Assert.That(await context.Database.FacultyAssignments.AnyAsync(), Is.False);
    }

    [Test]
    public async Task CreateRejectsUnknownCourse()
    {
        await using var context = await FacultyAssignmentTestContext.CreateAsync();

        var result = await context.Assignments.Create(
            CreateRequest(999, "lecturer@campuspulse.edu"),
            CancellationToken.None);

        Assert.That(result.Result, Is.TypeOf<NotFoundObjectResult>());
        Assert.That(await context.Database.FacultyAssignments.AnyAsync(), Is.False);
    }

    [Test]
    public async Task CreateRejectsDuplicateAfterEmailNormalization()
    {
        await using var context = await FacultyAssignmentTestContext.CreateAsync();
        var course = await AddCourseAsync(context.Database, "CPAI4000");
        await AddAssignmentAsync(
            context.Database,
            course.Id,
            "lecturer@campuspulse.edu");

        var result = await context.Assignments.Create(
            CreateRequest(course.Id, " LECTURER@CAMPUSPULSE.EDU "),
            CancellationToken.None);

        Assert.That(result.Result, Is.TypeOf<ConflictObjectResult>());
        Assert.That(await context.Database.FacultyAssignments.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public void RequestRejectsInvalidEmail()
    {
        var request = CreateRequest(1, "not-an-email");
        var validationResults = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            validationResults,
            validateAllProperties: true);

        Assert.That(isValid, Is.False);
        Assert.That(
            validationResults.Select(result => result.MemberNames.Single()),
            Does.Contain(nameof(FacultyAssignmentRequest.StaffEmail)));
    }

    static FacultyAssignmentRequest CreateRequest(int courseId, string email)
    {
        return new FacultyAssignmentRequest
        {
            CourseId = courseId,
            StaffName = " Test Lecturer ",
            StaffEmail = email,
            Role = " Lecturer ",
            TeachingPeriod = " Autumn 2026 "
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
            Description = "Course created by a faculty assignment test.",
            CreditPoints = 6,
            Level = 1,
            IsActive = true
        };

        database.Courses.Add(course);
        await database.SaveChangesAsync();
        return course;
    }

    static async Task<FacultyAssignment> AddAssignmentAsync(
        CourseManagementDbContext database,
        int courseId,
        string email)
    {
        var assignment = new FacultyAssignment
        {
            CourseId = courseId,
            StaffName = "Test Lecturer",
            StaffEmail = email,
            Role = "Lecturer",
            TeachingPeriod = "Autumn 2026"
        };

        database.FacultyAssignments.Add(assignment);
        await database.SaveChangesAsync();
        return assignment;
    }

    sealed class FacultyAssignmentTestContext(
        SqliteConnection connection,
        CourseManagementDbContext database,
        TestSyllabusStorage syllabusStorage) : IAsyncDisposable
    {
        public CourseManagementDbContext Database { get; } = database;
        public FacultyAssignmentsController Assignments { get; } = new(database);
        public CoursesController Courses { get; } = new(database, syllabusStorage);

        public static async Task<FacultyAssignmentTestContext> CreateAsync()
        {
            var connection = new SqliteConnection(
                "Data Source=:memory:;Foreign Keys=True");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<CourseManagementDbContext>()
                .UseSqlite(connection)
                .Options;
            var database = new CourseManagementDbContext(options);
            await database.Database.EnsureCreatedAsync();

            return new FacultyAssignmentTestContext(
                connection,
                database,
                TestSyllabusStorage.Create());
        }

        public async ValueTask DisposeAsync()
        {
            await Database.DisposeAsync();
            await connection.DisposeAsync();
            await syllabusStorage.DisposeAsync();
        }
    }
}
