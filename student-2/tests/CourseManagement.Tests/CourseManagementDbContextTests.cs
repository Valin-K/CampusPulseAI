using CourseManagement.Backend.Data;
using CourseManagement.Backend.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

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
    public async Task MigrationUpgradesLegacyDatabaseWithoutLosingCourseData()
    {
        await using var connection = new SqliteConnection(
            "Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<CourseManagementDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var legacyDatabase = new CourseManagementDbContext(options))
        {
            var migrator = legacyDatabase.Database.GetService<IMigrator>();
            await migrator.MigrateAsync(
                CourseManagementDatabaseMigrator.InitialMigrationId);
            await legacyDatabase.Database.ExecuteSqlRawAsync(
                """DROP TABLE "__EFMigrationsHistory";""");

            legacyDatabase.Courses.Add(CreateCourse("CPAI5000"));
            await legacyDatabase.SaveChangesAsync();
        }

        await using var upgradedDatabase = new CourseManagementDbContext(options);
        await CourseManagementDataInitializer.InitializeAsync(upgradedDatabase);

        Assert.That(
            await upgradedDatabase.Courses.AnyAsync(
                course => course.Code == "CPAI5000"),
            Is.True);

        var upgradedCourse = await upgradedDatabase.Courses.SingleAsync(
            course => course.Code == "CPAI5000");
        upgradedCourse.Level = 0;

        Assert.ThrowsAsync<DbUpdateException>(
            async () => await upgradedDatabase.SaveChangesAsync());
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
