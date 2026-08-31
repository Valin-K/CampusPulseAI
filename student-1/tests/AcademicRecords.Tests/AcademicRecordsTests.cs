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
            database.AcademicTerms.AddRange(
                new AcademicTerm { TermId = 13, TermCode = "2026-T1", TermName = "2026 Term 1", StartWeekOfYear = 1, EndWeekOfYear = 13, IsActive = true },
                new AcademicTerm { TermId = 14, TermCode = "2026-T2", TermName = "2026 Term 2", StartWeekOfYear = 14, EndWeekOfYear = 26, IsActive = true });
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

            var student = new Student
            {
                StudentId = "STU-DUP-1",
                FirstName = "Dup",
                LastName = "Student",
                Email = "dup.student@example.edu",
                DateOfBirth = new(2000, 1, 1),
                Status = "ACT",
                MailingAddress = "1 Campus Way",
                EmergencyContactName = "Contact Person",
                EmergencyContactPhone = "0400000000"
            };
            database.Students.Add(student);
            database.AcademicTerms.Add(new AcademicTerm { TermId = 13, TermCode = "2026-T1", TermName = "2026 Term 1", StartWeekOfYear = 1, EndWeekOfYear = 13, IsActive = true });
            await database.SaveChangesAsync();

            database.Enrollments.Add(new Enrollment { StudentId = student.UserId, CourseCode = "ZZ999", AcademicTermId = 13, AcademicYear = 2026, Status = "Enrolled" });
            await database.SaveChangesAsync();
            database.Enrollments.Add(new Enrollment { StudentId = student.UserId, CourseCode = "ZZ999", AcademicTermId = 13, AcademicYear = 2026, Status = "Enrolled" });

            Assert.ThrowsAsync<DbUpdateException>(async () => await database.SaveChangesAsync());
        }

        [Test]
        public async Task ValidEmailIsAccepted()
        {
            await using var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            await using var database = CreateDatabase(connection);
            await database.Database.EnsureCreatedAsync();

            var student = new Student
            {
                StudentId = "TEST001",
                FirstName = "Test",
                LastName = "Student",
                Email = "valid.email@example.edu",
                DateOfBirth = new(2000, 1, 1),
                Status = "ACT",
                MailingAddress = "123 Test St",
                EmergencyContactName = "Emergency Contact",
                EmergencyContactPhone = "0400000000"
            };

            database.Students.Add(student);
            await database.SaveChangesAsync();

            var saved = await database.Students.SingleAsync(s => s.StudentId == "TEST001");
            Assert.That(saved.Email, Is.EqualTo("valid.email@example.edu"));
        }

        [Test]
        public async Task StudentProfileCanBeUpdated()
        {
            await using var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            await using var database = CreateDatabase(connection);
            await database.Database.EnsureCreatedAsync();

            var student = new Student
            {
                StudentId = "UPDATE001",
                FirstName = "Original",
                LastName = "Name",
                Email = "original@example.edu",
                DateOfBirth = new(2000, 1, 1),
                Status = "ACT",
                PhoneNumber = "0400111111",
                MailingAddress = "Old Address",
                EmergencyContactName = "Old Contact",
                EmergencyContactPhone = "0400222222",
                Degree = "Old Degree",
                Major = "Old Major",
                SubMajor = "Old Minor"
            };
            database.Students.Add(student);
            await database.SaveChangesAsync();

            student.FirstName = "Updated";
            student.LastName = "Student";
            student.Email = "updated@example.edu";
            student.PhoneNumber = "0400333333";
            student.MailingAddress = "New Address";
            student.EmergencyContactName = "New Contact";
            student.EmergencyContactPhone = "0400444444";
            student.Degree = "New Degree";
            student.Major = "New Major";
            student.SubMajor = "New Minor";
            await database.SaveChangesAsync();

            var updated = await database.Students.SingleAsync(s => s.StudentId == "UPDATE001");
            Assert.That(updated.FirstName, Is.EqualTo("Updated"));
            Assert.That(updated.Email, Is.EqualTo("updated@example.edu"));
            Assert.That(updated.PhoneNumber, Is.EqualTo("0400333333"));
            Assert.That(updated.Major, Is.EqualTo("New Major"));
            Assert.That(updated.SubMajor, Is.EqualTo("New Minor"));
        }

        [Test]
        public void ValidEmailFormats()
        {
            Assert.That(IsValidEmail("test@example.com"), Is.True);
            Assert.That(IsValidEmail("user.name@example.edu"), Is.True);
            Assert.That(IsValidEmail("invalid@"), Is.False);
            Assert.That(IsValidEmail("@example.com"), Is.False);
            Assert.That(IsValidEmail("notanemail"), Is.False);
        }

        [Test]
        public void ValidPhoneNumberFormats()
        {
            Assert.That(IsValidPhoneNumber("0400000000"), Is.True);
            Assert.That(IsValidPhoneNumber("0412345678"), Is.True);
            Assert.That(IsValidPhoneNumber("+61400000000"), Is.True);
            Assert.That(IsValidPhoneNumber("+61412345678"), Is.True);
            Assert.That(IsValidPhoneNumber("1234"), Is.False);
            Assert.That(IsValidPhoneNumber("abcd"), Is.False);
            Assert.That(IsValidPhoneNumber("123456789"), Is.False);
        }

        private static bool IsValidEmail(string email)
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsValidPhoneNumber(string phoneNumber)
        {
            var cleaned = new string(phoneNumber.Where(char.IsDigit).ToArray());
            if (phoneNumber.StartsWith("+61"))
            {
                return cleaned.Length == 11 && cleaned.StartsWith("61");
            }
            return cleaned.Length == 10 && cleaned.StartsWith("0");
        }

        [Test]
        public async Task AllStudentsCanBeRetrieved()
        {
            await using var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            await using var database = CreateDatabase(connection);
            await database.Database.EnsureCreatedAsync();

            database.Students.AddRange(
                new Student
                {
                    StudentId = "STU-ALL-1",
                    FirstName = "Alice",
                    LastName = "Anderson",
                    Email = "alice.anderson@example.edu",
                    DateOfBirth = new(2000, 1, 1),
                    EnrollmentDate = new(2026, 1, 1),
                    Status = "ACT",
                    MailingAddress = "1 Campus Way",
                    EmergencyContactName = "Contact Person",
                    EmergencyContactPhone = "0400000000"
                },
                new Student
                {
                    StudentId = "STU-ALL-2",
                    FirstName = "Bob",
                    LastName = "Baker",
                    Email = "bob.baker@example.edu",
                    DateOfBirth = new(2001, 2, 2),
                    EnrollmentDate = new(2026, 1, 1),
                    Status = "ACT",
                    MailingAddress = "2 Campus Way",
                    EmergencyContactName = "Contact Person",
                    EmergencyContactPhone = "0400000001"
                });
            await database.SaveChangesAsync();

            var students = await database.Students.AsNoTracking().ToListAsync();

            Assert.That(students, Has.Count.EqualTo(2));
            Assert.That(students.Select(value => value.StudentId), Is.EquivalentTo(new[] { "STU-ALL-1", "STU-ALL-2" }));
        }

        [Test]
        public async Task AllStaffCanBeRetrieved()
        {
            await using var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            await using var database = CreateDatabase(connection);
            await database.Database.EnsureCreatedAsync();

            database.Set<Staff>().AddRange(
                new Staff
                {
                    StaffId = "STA-ALL-1",
                    FirstName = "Carla",
                    LastName = "Carter",
                    Email = "carla.carter@example.edu",
                    DateOfBirth = new(1980, 3, 3),
                    Status = "ACT"
                },
                new Staff
                {
                    StaffId = "STA-ALL-2",
                    FirstName = "David",
                    LastName = "Diaz",
                    Email = "david.diaz@example.edu",
                    DateOfBirth = new(1981, 4, 4),
                    Status = "ACT"
                });
            await database.SaveChangesAsync();

            var staff = await database.Set<Staff>().AsNoTracking().ToListAsync();

            Assert.That(staff, Has.Count.EqualTo(2));
            Assert.That(staff.Select(value => value.StaffId), Is.EquivalentTo(new[] { "STA-ALL-1", "STA-ALL-2" }));
        }

        private static AcademicRecordsDbContext CreateDatabase(SqliteConnection connection) =>
            new(new DbContextOptionsBuilder<AcademicRecordsDbContext>().UseSqlite(connection).Options);
    }
}
