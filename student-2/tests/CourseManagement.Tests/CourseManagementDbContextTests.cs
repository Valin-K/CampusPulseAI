using CourseManagement.Database.Data;
using CourseManagement.Database.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CourseManagement.Tests;

public sealed class CourseManagementDbContextTests
{
    [Test]
    public async Task SchemaRejectsDuplicateCourseCodes()
    {
        await using var context = await DatabaseTestContext.CreateAsync();
        context.Database.Courses.AddRange(
            CreateCourse("CPAI5000"),
            CreateCourse("CPAI5000"));

        Assert.ThrowsAsync<DbUpdateException>(
            async () => await context.Database.SaveChangesAsync());
    }

    [TestCase(0, 1)]
    [TestCase(49, 1)]
    [TestCase(6, 0)]
    [TestCase(6, 11)]
    public async Task SchemaRejectsCourseValuesOutsideAllowedRanges(
        int creditPoints,
        int level)
    {
        await using var context = await DatabaseTestContext.CreateAsync();
        var course = CreateCourse("CPAI5000");
        course.CreditPoints = creditPoints;
        course.Level = level;
        context.Database.Courses.Add(course);

        Assert.ThrowsAsync<DbUpdateException>(
            async () => await context.Database.SaveChangesAsync());
    }

    [Test]
    public async Task SchemaRejectsCourseAsItsOwnPrerequisite()
    {
        await using var context = await DatabaseTestContext.CreateAsync();
        var course = CreateCourse("CPAI5000");
        context.Database.Courses.Add(course);
        await context.Database.SaveChangesAsync();

        context.Database.Prerequisites.Add(new Prerequisite
        {
            CourseId = course.Id,
            RequiredCourseId = course.Id,
            MinimumGrade = "Pass"
        });

        Assert.ThrowsAsync<DbUpdateException>(
            async () => await context.Database.SaveChangesAsync());
    }

    [Test]
    public async Task SchemaRejectsDuplicatePrerequisiteRelationships()
    {
        await using var context = await DatabaseTestContext.CreateAsync();
        var course = CreateCourse("CPAI5000");
        var requiredCourse = CreateCourse("CPAI4000");
        context.Database.Courses.AddRange(course, requiredCourse);
        await context.Database.SaveChangesAsync();

        context.Database.Prerequisites.AddRange(
            CreatePrerequisite(course.Id, requiredCourse.Id),
            CreatePrerequisite(course.Id, requiredCourse.Id));

        Assert.ThrowsAsync<DbUpdateException>(
            async () => await context.Database.SaveChangesAsync());
    }

    [Test]
    public async Task SchemaRejectsPrerequisiteWithUnknownCourse()
    {
        await using var context = await DatabaseTestContext.CreateAsync();
        context.Database.Prerequisites.Add(
            CreatePrerequisite(999, 1000));

        Assert.ThrowsAsync<DbUpdateException>(
            async () => await context.Database.SaveChangesAsync());
    }

    [Test]
    public async Task SchemaRejectsDuplicateFacultyAssignments()
    {
        await using var context = await DatabaseTestContext.CreateAsync();
        var course = CreateCourse("CPAI5000");
        context.Database.Courses.Add(course);
        await context.Database.SaveChangesAsync();

        context.Database.FacultyAssignments.AddRange(
            CreateFacultyAssignment(course.Id),
            CreateFacultyAssignment(course.Id));

        Assert.ThrowsAsync<DbUpdateException>(
            async () => await context.Database.SaveChangesAsync());
    }

    [Test]
    public async Task SeedDataCreatesCourseCatalogueRelationships()
    {
        await using var context = await DatabaseTestContext.CreateEmptyAsync();

        await CourseManagementSeedData.InitializeAsync(context.Database);

        var courseCount = await context.Database.Courses.CountAsync();
        var prerequisiteCount = await context.Database.Prerequisites.CountAsync();
        var assignmentCount = await context.Database.FacultyAssignments.CountAsync();

        Assert.Multiple(() =>
        {
            Assert.That(courseCount, Is.EqualTo(10));
            Assert.That(prerequisiteCount, Is.EqualTo(10));
            Assert.That(assignmentCount, Is.EqualTo(15));
        });
    }

    [Test]
    public async Task SchemaUpgradeAddsMarkGradeTypeToExistingCourses()
    {
        await using var connection = new SqliteConnection(
            "Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE "Courses" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_Courses" PRIMARY KEY AUTOINCREMENT,
                    "Code" TEXT NOT NULL,
                    "Name" TEXT NOT NULL,
                    "Description" TEXT NOT NULL,
                    "CreditPoints" INTEGER NOT NULL,
                    "Level" INTEGER NOT NULL,
                    "IsActive" INTEGER NOT NULL,
                    "SyllabusFileName" TEXT NULL
                );
                INSERT INTO "Courses"
                    ("Code", "Name", "Description", "CreditPoints", "Level", "IsActive")
                VALUES
                    ('CPAI5000', 'Existing Course', 'Created before GradeType existed.', 6, 5, 1);
                """;
            await command.ExecuteNonQueryAsync();
        }

        var options = new DbContextOptionsBuilder<CourseManagementDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var database = new CourseManagementDbContext(options);

        await CourseManagementSchema.EnsureCurrentAsync(database);

        var course = await database.Courses.SingleAsync();
        Assert.That(course.GradeType, Is.EqualTo("Mark"));
    }

    static Course CreateCourse(string code)
    {
        return new Course
        {
            Code = code,
            Name = $"{code} Course",
            Description = "Course created by a database constraint test.",
            CreditPoints = 6,
            Level = 1,
            IsActive = true
        };
    }

    static Prerequisite CreatePrerequisite(int courseId, int requiredCourseId)
    {
        return new Prerequisite
        {
            CourseId = courseId,
            RequiredCourseId = requiredCourseId,
            MinimumGrade = "Pass"
        };
    }

    static FacultyAssignment CreateFacultyAssignment(int courseId)
    {
        return new FacultyAssignment
        {
            CourseId = courseId,
            StaffName = "Test Lecturer",
            StaffEmail = "lecturer@campuspulse.edu",
            Role = "Lecturer",
            TeachingPeriod = "Autumn 2026"
        };
    }

    sealed class DatabaseTestContext(
        SqliteConnection connection,
        CourseManagementDbContext database) : IAsyncDisposable
    {
        public CourseManagementDbContext Database { get; } = database;

        public static async Task<DatabaseTestContext> CreateAsync()
        {
            var context = await CreateEmptyAsync();
            await context.Database.Database.EnsureCreatedAsync();
            return context;
        }

        public static async Task<DatabaseTestContext> CreateEmptyAsync()
        {
            var connection = new SqliteConnection(
                "Data Source=:memory:;Foreign Keys=True");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<CourseManagementDbContext>()
                .UseSqlite(connection)
                .Options;
            var database = new CourseManagementDbContext(options);
            await database.Database.EnsureCreatedAsync();

            return new DatabaseTestContext(connection, database);
        }

        public async ValueTask DisposeAsync()
        {
            await Database.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
