using AcademicRecords.Database.Data;
using AcademicRecords.Database.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AcademicRecords.Tests
{
    public class Tests
    {
        [Test]
        public async Task StudentProfileRemainsLinkedAcrossTerms()
        {
            await using var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            await using var database = CreateDatabase(connection);
            await database.Database.EnsureCreatedAsync();

            var student = new Student
            {
                StudentId = "STU-PERMANENT-1",
                FirstName = "New",
                LastName = "Student",
                Email = "new.student@example.edu",
                DateOfBirth = new(2000, 1, 1),
                EnrollmentDate = new(2026, 1, 1),
                Status = "ACT",
                MailingAddress = "1 Campus Way",
                EmergencyContactName = "Contact Person",
                EmergencyContactPhone = "0400000000"
            };
            database.Students.Add(student);
            await database.SaveChangesAsync();

            database.Enrollments.AddRange(
                new Enrollment { StudentId = student.UserId, CourseCode = "CS101", AcademicTermId = 13, AcademicYear = 2026, Status = "Enrolled" },
                new Enrollment { StudentId = student.UserId, CourseCode = "CS102", AcademicTermId = 14, AcademicYear = 2026, Status = "Enrolled" });
            await database.SaveChangesAsync();

            var profile = await database.Students.Include(value => value.Enrollments)
                .SingleAsync(value => value.StudentId == "STU-PERMANENT-1");

            Assert.That(profile.StudentId, Is.EqualTo("STU-PERMANENT-1"));
            Assert.That(profile.Enrollments, Has.Count.EqualTo(2));
            Assert.That(profile.UserId, Is.GreaterThan(0));
        }

        [Test]
        public async Task DuplicateCourseEnrollmentInSameTermIsRejected()
        {
            await using var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            await using var database = CreateDatabase(connection);
            await database.Database.EnsureCreatedAsync();

            database.Enrollments.Add(new Enrollment { StudentId = 1, CourseCode = "ZZ999", AcademicTermId = 13, AcademicYear = 2026, Status = "Enrolled" });
            await database.SaveChangesAsync();
            database.Enrollments.Add(new Enrollment { StudentId = 1, CourseCode = "ZZ999", AcademicTermId = 13, AcademicYear = 2026, Status = "Enrolled" });

            Assert.ThrowsAsync<DbUpdateException>(async () => await database.SaveChangesAsync());
        }

        private static AcademicRecordsDbContext CreateDatabase(SqliteConnection connection) =>
            new(new DbContextOptionsBuilder<AcademicRecordsDbContext>().UseSqlite(connection).Options);
    }
}
