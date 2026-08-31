using CourseManagement.Backend.Contracts.Courses;
using CourseManagement.Backend.Contracts.Prerequisites;
using CourseManagement.Backend.Controllers;
using CourseManagement.Database.Data;
using CourseManagement.Database.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CourseManagement.Tests;

public sealed class PrerequisitesControllerTests
{
    [Test]
    public async Task CreateAddsPrerequisiteWithMinimumGrade()
    {
        await using var context = await PrerequisiteTestContext.CreateAsync();
        var (course, requiredCourse) = await AddCoursesAsync(context.Database);

        var result = await context.Prerequisites.Create(
            CreateRequest(course.Id, requiredCourse.Id, "C"),
            CancellationToken.None);

        Assert.That(result.Result, Is.TypeOf<CreatedAtActionResult>());
        var prerequisite = await context.Database.Prerequisites.SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(prerequisite.CourseId, Is.EqualTo(course.Id));
            Assert.That(prerequisite.RequiredCourseId, Is.EqualTo(requiredCourse.Id));
            Assert.That(prerequisite.MinimumGrade, Is.EqualTo("Credit"));
        });
    }

    [Test]
    public async Task CourseDetailsIncludePrerequisites()
    {
        await using var context = await PrerequisiteTestContext.CreateAsync();
        var (course, requiredCourse) = await AddCoursesAsync(context.Database);
        await AddPrerequisiteAsync(
            context.Database,
            course.Id,
            requiredCourse.Id,
            "Credit");

        var result = await context.Courses.GetById(course.Id, CancellationToken.None);

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var response = (CourseDetailResponse)((OkObjectResult)result.Result).Value!;
        Assert.That(response.Prerequisites, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(
                response.Prerequisites[0].RequiredCourseCode,
                Is.EqualTo(requiredCourse.Code));
            Assert.That(
                response.Prerequisites[0].MinimumGrade,
                Is.EqualTo("Credit"));
        });
    }

    [Test]
    public async Task UpdateChangesRequiredCourseAndMinimumGrade()
    {
        await using var context = await PrerequisiteTestContext.CreateAsync();
        var (course, requiredCourse) = await AddCoursesAsync(context.Database);
        var replacementCourse = await AddCourseAsync(context.Database, "CPAI3000");
        var prerequisite = await AddPrerequisiteAsync(
            context.Database,
            course.Id,
            requiredCourse.Id,
            "Pass");

        var result = await context.Prerequisites.Update(
            prerequisite.Id,
            CreateRequest(course.Id, replacementCourse.Id, "Distinction"),
            CancellationToken.None);

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        Assert.Multiple(() =>
        {
            Assert.That(prerequisite.RequiredCourseId, Is.EqualTo(replacementCourse.Id));
            Assert.That(prerequisite.MinimumGrade, Is.EqualTo("Distinction"));
        });
    }

    [Test]
    public async Task DeleteRemovesPrerequisite()
    {
        await using var context = await PrerequisiteTestContext.CreateAsync();
        var (course, requiredCourse) = await AddCoursesAsync(context.Database);
        var prerequisite = await AddPrerequisiteAsync(
            context.Database,
            course.Id,
            requiredCourse.Id,
            "Pass");

        var result = await context.Prerequisites.Delete(
            prerequisite.Id,
            CancellationToken.None);

        Assert.That(result, Is.TypeOf<NoContentResult>());
        Assert.That(await context.Database.Prerequisites.AnyAsync(), Is.False);
    }

    [Test]
    public async Task CreateRejectsCourseAsItsOwnPrerequisite()
    {
        await using var context = await PrerequisiteTestContext.CreateAsync();
        var course = await AddCourseAsync(context.Database, "CPAI4000");

        var result = await context.Prerequisites.Create(
            CreateRequest(course.Id, course.Id, "Pass"),
            CancellationToken.None);

        Assert.That(result.Result, Is.TypeOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task CreateRejectsDuplicatePrerequisite()
    {
        await using var context = await PrerequisiteTestContext.CreateAsync();
        var (course, requiredCourse) = await AddCoursesAsync(context.Database);
        await AddPrerequisiteAsync(
            context.Database,
            course.Id,
            requiredCourse.Id,
            "Pass");

        var result = await context.Prerequisites.Create(
            CreateRequest(course.Id, requiredCourse.Id, "Credit"),
            CancellationToken.None);

        Assert.That(result.Result, Is.TypeOf<ConflictObjectResult>());
    }

    [Test]
    public async Task CreateRejectsUnsupportedMinimumGrade()
    {
        await using var context = await PrerequisiteTestContext.CreateAsync();
        var (course, requiredCourse) = await AddCoursesAsync(context.Database);

        var result = await context.Prerequisites.Create(
            CreateRequest(course.Id, requiredCourse.Id, "Excellent"),
            CancellationToken.None);

        Assert.That(result.Result, Is.TypeOf<BadRequestObjectResult>());
    }

    [Test]
    public async Task EligibilityReportsMissingCompletedCourse()
    {
        await using var context = await PrerequisiteTestContext.CreateAsync();
        var (course, requiredCourse) = await AddCoursesAsync(context.Database);
        await AddPrerequisiteAsync(
            context.Database,
            course.Id,
            requiredCourse.Id,
            "Credit");

        var response = await CheckEligibilityAsync(
            context.Courses,
            course.Id,
            []);

        Assert.That(response.IsEligible, Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(response.Requirements[0].IsMet, Is.False);
            Assert.That(response.Requirements[0].AchievedGrade, Is.Null);
            Assert.That(
                response.Requirements[0].Reason,
                Is.EqualTo($"{requiredCourse.Code} has not been completed."));
        });
    }

    [Test]
    public async Task EligibilityReportsGradeBelowMinimum()
    {
        await using var context = await PrerequisiteTestContext.CreateAsync();
        var (course, requiredCourse) = await AddCoursesAsync(context.Database);
        await AddPrerequisiteAsync(
            context.Database,
            course.Id,
            requiredCourse.Id,
            "Credit");

        var response = await CheckEligibilityAsync(
            context.Courses,
            course.Id,
            [new CompletedCourseRequest
            {
                CourseCode = requiredCourse.Code,
                Grade = "Pass"
            }]);

        Assert.That(response.IsEligible, Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(response.Requirements[0].IsMet, Is.False);
            Assert.That(response.Requirements[0].AchievedGrade, Is.EqualTo("Pass"));
            Assert.That(
                response.Requirements[0].Reason,
                Is.EqualTo("Achieved Pass; minimum grade Credit is required."));
        });
    }

    [Test]
    public async Task EligibilityReportsRequirementMet()
    {
        await using var context = await PrerequisiteTestContext.CreateAsync();
        var (course, requiredCourse) = await AddCoursesAsync(context.Database);
        await AddPrerequisiteAsync(
            context.Database,
            course.Id,
            requiredCourse.Id,
            "Credit");

        var response = await CheckEligibilityAsync(
            context.Courses,
            course.Id,
            [new CompletedCourseRequest
            {
                CourseCode = requiredCourse.Code,
                Grade = "Distinction"
            }]);

        Assert.That(response.IsEligible, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(response.Requirements[0].IsMet, Is.True);
            Assert.That(
                response.Requirements[0].Reason,
                Is.EqualTo("Achieved Distinction; minimum grade Credit is met."));
        });
    }

    static async Task<PrerequisiteEligibilityResponse> CheckEligibilityAsync(
        CoursesController controller,
        int courseId,
        IReadOnlyList<CompletedCourseRequest> completedCourses)
    {
        var result = await controller.CheckPrerequisiteEligibility(
            courseId,
            new PrerequisiteEligibilityRequest
            {
                CompletedCourses = completedCourses
            },
            CancellationToken.None);

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        return (PrerequisiteEligibilityResponse)((OkObjectResult)result.Result).Value!;
    }

    static PrerequisiteRequest CreateRequest(
        int courseId,
        int requiredCourseId,
        string minimumGrade)
    {
        return new PrerequisiteRequest
        {
            CourseId = courseId,
            RequiredCourseId = requiredCourseId,
            MinimumGrade = minimumGrade
        };
    }

    static async Task<(Course Course, Course RequiredCourse)> AddCoursesAsync(
        CourseManagementDbContext database)
    {
        var course = await AddCourseAsync(database, "CPAI4000");
        var requiredCourse = await AddCourseAsync(database, "CPAI2000");
        return (course, requiredCourse);
    }

    static async Task<Course> AddCourseAsync(
        CourseManagementDbContext database,
        string code)
    {
        var course = new Course
        {
            Code = code,
            Name = $"{code} Course",
            Description = "Course created by a prerequisite test.",
            CreditPoints = 6,
            Level = 1,
            IsActive = true
        };

        database.Courses.Add(course);
        await database.SaveChangesAsync();
        return course;
    }

    static async Task<Prerequisite> AddPrerequisiteAsync(
        CourseManagementDbContext database,
        int courseId,
        int requiredCourseId,
        string minimumGrade)
    {
        var prerequisite = new Prerequisite
        {
            CourseId = courseId,
            RequiredCourseId = requiredCourseId,
            MinimumGrade = minimumGrade
        };

        database.Prerequisites.Add(prerequisite);
        await database.SaveChangesAsync();
        return prerequisite;
    }

    sealed class PrerequisiteTestContext(
        SqliteConnection connection,
        CourseManagementDbContext database,
        TestSyllabusStorage syllabusStorage) : IAsyncDisposable
    {
        public CourseManagementDbContext Database { get; } = database;
        public PrerequisitesController Prerequisites { get; } = new(database);
        public CoursesController Courses { get; } = new(database, syllabusStorage);

        public static async Task<PrerequisiteTestContext> CreateAsync()
        {
            var connection = new SqliteConnection(
                "Data Source=:memory:;Foreign Keys=True");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<CourseManagementDbContext>()
                .UseSqlite(connection)
                .Options;
            var database = new CourseManagementDbContext(options);
            await database.Database.EnsureCreatedAsync();

            return new PrerequisiteTestContext(
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
