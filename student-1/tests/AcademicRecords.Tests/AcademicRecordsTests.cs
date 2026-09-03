using AcademicRecords.Backend;
using AcademicRecords.Backend.Services;
using AcademicRecords.Database.Data;
using AcademicRecords.Database.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

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
                new Enrollment { StudentId = student.UserId, CourseCode = "CS101", AcademicTermId = 13, AcademicYear = 2026 },
                new Enrollment { StudentId = student.UserId, CourseCode = "CS102", AcademicTermId = 14, AcademicYear = 2026 });
            await database.SaveChangesAsync();

            var profile = await database.Students.Include(value => value.Enrollments)
                .SingleAsync(value => value.StudentId == "STU-PERMANENT-1");

            Assert.That(profile.StudentId, Is.EqualTo("STU-PERMANENT-1"));
            Assert.That(profile.Enrollments, Has.Count.EqualTo(2));
            Assert.That(profile.UserId, Is.GreaterThan(0));
        }

        [Test]
        public async Task AcademicProgressAnalysisReportsMissingApiKey()
        {
            var service = new GeminiAcademicProgressService(null!, new ConfigurationBuilder().Build());
            var profile = new StudentStatisticsProfile
            {
                StudentId = "AI-TEST-1",
                Enrollments =
                [
                    new StudentStatisticsEnrollment
                    {
                        AcademicYear = 2026,
                        TermCode = "T1",
                        CourseCode = "CS101",
                        Status = "Passed",
                        GradeValue = 75,
                        Credits = 12,
                        IsFinalized = true
                    }
                ]
            };

            var result = await service.AnalyzeAsync(profile, CancellationToken.None);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Status, Is.EqualTo("Failed"));
            Assert.That(result.Message, Does.Contain("GEMINI_API_KEY"));
            Assert.That(result.Metrics.EarnedCredits, Is.EqualTo(12));
            Assert.That(result.Metrics.CreditsRemaining, Is.EqualTo(132));
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

            database.Enrollments.Add(new Enrollment { StudentId = student.UserId, CourseCode = "ZZ999", AcademicTermId = 13, AcademicYear = 2026 });
            await database.SaveChangesAsync();
            database.Enrollments.Add(new Enrollment { StudentId = student.UserId, CourseCode = "ZZ999", AcademicTermId = 13, AcademicYear = 2026 });

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

        [Test]
        public void FinalStatusUsesTheEnrollmentGradeType()
        {
            Assert.That(EnrollmentStatusRules.GetFinalStatus(EnrollmentStatusRules.EnrollmentGradeTypes.Mark, 50m), Is.EqualTo(EnrollmentStatusRules.Passed));
            Assert.That(EnrollmentStatusRules.GetFinalStatus(EnrollmentStatusRules.EnrollmentGradeTypes.Mark, 49.99m), Is.EqualTo(EnrollmentStatusRules.Failed));
            Assert.That(EnrollmentStatusRules.GetFinalStatus(EnrollmentStatusRules.EnrollmentGradeTypes.Mark, null), Is.EqualTo(EnrollmentStatusRules.Completed));
            Assert.That(EnrollmentStatusRules.GetFinalStatus(EnrollmentStatusRules.EnrollmentGradeTypes.Ungraded, null), Is.EqualTo(EnrollmentStatusRules.Completed));
        }

        [Test]
        public async Task GradeAssignmentDoesNotFinalizeEnrollment()
        {
            await using var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            await using var database = CreateDatabase(connection);
            await database.Database.EnsureCreatedAsync();

            var enrollment = new Enrollment
            {
                CourseCode = "CS101",
                AcademicYear = 2026,
                AcademicTerm = new AcademicTerm { StartWeekOfYear = 1, EndWeekOfYear = 52 }
            };
            database.Enrollments.Add(enrollment);
            await database.SaveChangesAsync();

            enrollment.GradeValue = 75m;
            await database.SaveChangesAsync();

            var saved = await database.Enrollments.SingleAsync();
            Assert.That(saved.GradeValue, Is.EqualTo(75m));
            Assert.That(saved.Status, Is.EqualTo(EnrollmentStatusRules.InProgress));
            Assert.That(saved.IsFinalized, Is.False);
            Assert.That(saved.GradeDate, Is.Null);
        }

        [Test]
        public async Task FinalizingAssignedGradeUpdatesFinalStatus()
        {
            await using var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            await using var database = CreateDatabase(connection);
            await database.Database.EnsureCreatedAsync();

            var enrollment = new Enrollment
            {
                CourseCode = "CS101",
                AcademicYear = 2020,
                AcademicTerm = new AcademicTerm { StartWeekOfYear = 1, EndWeekOfYear = 2 }
            };
            database.Enrollments.Add(enrollment);
            await database.SaveChangesAsync();

            enrollment.GradeValue = 49.99m;
            enrollment.GradeDate = DateOnly.FromDateTime(DateTime.UtcNow);
            enrollment.IsFinalized = true;
            await database.SaveChangesAsync();

            var saved = await database.Enrollments.SingleAsync();
            Assert.That(saved.Status, Is.EqualTo(EnrollmentStatusRules.Failed));
            Assert.That(saved.IsFinalized, Is.True);
        }

        [Test]
        public void FinalizedEnrollmentInUnfinishedSessionRemainsInProgress()
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var enrollment = new Enrollment
            {
                AcademicYear = today.Year,
                AcademicTerm = new AcademicTerm
                {
                    StartWeekOfYear = 1,
                    EndWeekOfYear = ISOWeek.GetWeekOfYear(today.ToDateTime(TimeOnly.MinValue))
                },
                GradeValue = 95m,
                IsFinalized = true
            };

            Assert.That(enrollment.Status, Is.EqualTo(EnrollmentStatusRules.InProgress));
        }

        [Test]
        public void GradeEntryRejectsGradesOutsideTheSupportedRange()
        {
            Assert.That(IsValidGrade(-0.01m), Is.False);
            Assert.That(IsValidGrade(0m), Is.True);
            Assert.That(IsValidGrade(100m), Is.True);
            Assert.That(IsValidGrade(100.01m), Is.False);
        }

        [Test]
        public void GradeEditingWindowExcludesFutureAndEndedCourseDates()
        {
            var sessionDates = EnrollmentStatusRules.GetSessionDates(2026, 10, 20);

            Assert.That(IsWithinGradeEditingWindow(sessionDates.StartDate.AddDays(-1), sessionDates), Is.False);
            Assert.That(IsWithinGradeEditingWindow(sessionDates.StartDate, sessionDates), Is.True);
            Assert.That(IsWithinGradeEditingWindow(sessionDates.EndDate, sessionDates), Is.True);
            Assert.That(IsWithinGradeEditingWindow(sessionDates.EndDate.AddDays(1), sessionDates), Is.False);
        }

        [Test]
        public void CurrentCourseEnrollmentsCanBeGroupedBySession()
        {
            var enrollments = new[]
            {
                new TermEnrollmentResponse("STU001", "Alex Student", "CPAI1001", "T1", "Term 1", 2026, "In Progress"),
                new TermEnrollmentResponse("STU002", "Sam Student", "CPAI1001", "T1", "Term 1", 2026, "Enrolled"),
                new TermEnrollmentResponse("STU003", "Pat Student", "CPAI1001", "T2", "Term 2", 2026, "Completed")
            };

            var sessions = enrollments
                .Where(enrollment => enrollment.Status is EnrollmentStatusRules.Enrolled or EnrollmentStatusRules.InProgress)
                .GroupBy(enrollment => $"{enrollment.TermName} ({enrollment.TermCode}) - {enrollment.AcademicYear}")
                .ToList();

            Assert.That(sessions, Has.Count.EqualTo(1));
            Assert.That(sessions[0].Key, Is.EqualTo("Term 1 (T1) - 2026"));
            Assert.That(sessions[0].ToList(), Has.Count.EqualTo(2));
        }

        [Test]
        public void SeedEnrollmentsHaveStatusesConsistentWithTheirSessionDates()
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var terms = AcademicRecordsSeedData.AcademicTerms.ToDictionary(term => term.TermId);

            foreach (var enrollment in AcademicRecordsSeedData.Enrollments)
            {
                var term = terms[enrollment.AcademicTermId!.Value];
                var startDate = DateOnly.FromDateTime(ISOWeek.ToDateTime(enrollment.AcademicYear, term.StartWeekOfYear, DayOfWeek.Monday));
                var endYear = term.EndWeekOfYear < term.StartWeekOfYear ? enrollment.AcademicYear + 1 : enrollment.AcademicYear;
                var endDate = DateOnly.FromDateTime(ISOWeek.ToDateTime(endYear, term.EndWeekOfYear, DayOfWeek.Sunday));

                var expectedFinalized = enrollment.IsFinalized;
                var expectedStatus = startDate > today
                    ? EnrollmentStatusRules.Enrolled
                    : endDate >= today
                        ? EnrollmentStatusRules.InProgress
                    : expectedFinalized
                        ? EnrollmentStatusRules.GetFinalStatus(enrollment.GradeValue)
                        : EnrollmentStatusRules.Completed;
                var calculatedEnrollment = new Enrollment
                {
                    CourseCode = enrollment.CourseCode,
                    AcademicYear = enrollment.AcademicYear,
                    AcademicTerm = term,
                    GradeValue = enrollment.GradeValue,
                    IsFinalized = enrollment.IsFinalized
                };
                Assert.That(calculatedEnrollment.Status, Is.EqualTo(expectedStatus));
                Assert.That(enrollment.IsFinalized, Is.EqualTo(expectedFinalized));
                Assert.That(enrollment.GradeDate is null || enrollment.GradeDate <= today, Is.True);
            }

            var summer = terms[15];
            var summerEnd = ISOWeek.ToDateTime(2027, summer.EndWeekOfYear, DayOfWeek.Sunday);
            Assert.That(summerEnd.Year, Is.EqualTo(2027));
        }

        [Test]
        public async Task EnrollmentStatusIsCalculatedInsteadOfPersisted()
        {
            await using var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            await using var database = CreateDatabase(connection);
            await database.Database.EnsureCreatedAsync();

            var student = new Student
            {
                StudentId = "STU-CONSTRAINT-1",
                FirstName = "Constraint",
                LastName = "Test",
                Email = "constraint.test@example.edu",
                DateOfBirth = new(2000, 1, 1),
                Status = "ACT",
                MailingAddress = "1 Campus Way",
                EmergencyContactName = "Contact Person",
                EmergencyContactPhone = "0400000000"
            };
            database.Students.Add(student);
            await database.SaveChangesAsync();

            database.Enrollments.Add(new Enrollment
            {
                StudentId = student.UserId,
                CourseCode = "CS999",
                AcademicYear = 2026,
                IsFinalized = false
            });

            await database.SaveChangesAsync();

            var saved = await database.Enrollments.SingleAsync();
            Assert.That(saved.Status, Is.EqualTo(EnrollmentStatusRules.Enrolled));
            Assert.That((await database.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('Enrollments') WHERE name = 'status'").ToListAsync()), Is.Empty);
        }

        [Test]
        public void WamIsWeightedByCreditsAndIgnoresUngradedEnrollments()
        {
            var enrollments = new[]
            {
                new GradedEnrollment(70m, 6m, true),
                new GradedEnrollment(90m, 3m, true),
                new GradedEnrollment(100m, 6m, false),
                new GradedEnrollment(null, 6m, true)
            };

            var result = StudentStatisticsCalculator.CalculateWam(enrollments);

            Assert.That(result.Value, Is.EqualTo(76.6666666667m).Within(0.000000001m));
            Assert.That(result.GradedEnrollmentCount, Is.EqualTo(2));
            Assert.That(result.TotalCredits, Is.EqualTo(9m));
        }

        [Test]
        public void GpaUsesTheAustralianSevenPointScale()
        {
            var enrollments = new[]
            {
                new GradedEnrollment(85m, 6m, true),
                new GradedEnrollment(75m, 3m, true),
                new GradedEnrollment(65m, 1m, true),
                new GradedEnrollment(50m, 1m, true),
                new GradedEnrollment(49.99m, 1m, true)
            };

            var result = StudentStatisticsCalculator.CalculateGpa(enrollments);

            Assert.That(result.Value, Is.EqualTo(5.75m).Within(0.000000001m));
            Assert.That(StudentStatisticsCalculator.GradePoints(85m), Is.EqualTo(7m));
            Assert.That(StudentStatisticsCalculator.GradePoints(75m), Is.EqualTo(6m));
            Assert.That(StudentStatisticsCalculator.GradePoints(65m), Is.EqualTo(5m));
            Assert.That(StudentStatisticsCalculator.GradePoints(50m), Is.EqualTo(4m));
            Assert.That(StudentStatisticsCalculator.GradePoints(49.99m), Is.EqualTo(0m));
        }

        [Test]
        public void TranscriptDataFactoryBuildsSectionsAndDisplayResults()
        {
            var profile = new StudentStatisticsProfile
            {
                StudentId = "STU-TRANSCRIPT-1",
                FirstName = "Alec",
                LastName = "Steele",
                DegreeCode = "C10476",
                Degree = "Bachelor of Computing Science",
                Status = "Active",
                Major = "Enterprise Software Development",
                Enrollments =
                [
                    new StudentStatisticsEnrollment
                    {
                        CourseCode = "CPAI1001",
                        CourseTitle = "Programming Fundamentals",
                        AcademicYear = 2026,
                        TermCode = "SPR",
                        Status = "Enrolled",
                        Credits = 6,
                        IsFinalized = false
                    },
                    new StudentStatisticsEnrollment
                    {
                        CourseCode = "CPAI1002",
                        CourseTitle = "Database Foundations",
                        AcademicYear = 2025,
                        TermCode = "AUT",
                        Status = "Passed",
                        GradeType = "Pass/Fail",
                        GradeValue = 94,
                        Credits = 6,
                        IsFinalized = true
                    },
                    new StudentStatisticsEnrollment
                    {
                        CourseCode = "CPAI1003",
                        CourseTitle = "Web Development",
                        AcademicYear = 2025,
                        TermCode = "SPR",
                        Status = "Completed",
                        GradeType = "Ungraded",
                        Credits = 6,
                        IsFinalized = true
                    },
                    new StudentStatisticsEnrollment
                    {
                        CourseCode = "CPAI2001",
                        CourseTitle = "Software Design",
                        AcademicYear = 2025,
                        TermCode = "SUM",
                        Status = "Failed",
                        GradeType = "Mark",
                        GradeValue = 40,
                        Credits = 6,
                        IsFinalized = true
                    }
                ]
            };

            var transcript = TranscriptDataFactory.Create(profile, new DateTime(2026, 8, 25, 16, 5, 3));

            Assert.That(transcript.InstitutionName, Is.EqualTo("Campus Pulse AI"));
            Assert.That(transcript.StudentName, Is.EqualTo("Alec Steele"));
            Assert.That(transcript.EnrolledSubjects, Has.Count.EqualTo(1));
            Assert.That(transcript.EnrolledSubjects[0].Mark, Is.Null);
            Assert.That(transcript.CompletedSubjects, Has.Count.EqualTo(3));
            Assert.That(transcript.CompletedSubjects[0].Mark, Is.Null);
            Assert.That(transcript.CompletedSubjects[0].Result, Is.EqualTo("PASS - NOT GRADED"));
            Assert.That(transcript.CompletedSubjects[1].Result, Is.EqualTo("PASS - NOT GRADED"));
            Assert.That(transcript.CompletedSubjects[2].Result, Is.EqualTo("FAIL"));
            Assert.That(transcript.TotalCreditPointsGained, Is.EqualTo(12m));
             Assert.That(transcript.Gpa, Is.EqualTo(3.5m));
             Assert.That(transcript.Wam, Is.EqualTo(67m));
        }

        [Test]
        public void TranscriptPdfGeneratorProducesMultiPagePdfWithoutWatermark()
        {
            var subjects = Enumerable.Range(1, 24)
                .Select(index => new TranscriptSubject(
                    $"CPAI{index:0000}",
                    "A deliberately long subject title for checking transcript row wrapping and page breaks",
                    2026,
                    "AUT",
                    "COMPLETED",
                    "HIGH DISTINCTION",
                    90,
                    6,
                    6,
                    true))
                .ToList();
            var transcript = new TranscriptData(
                 "Campus Pulse AI",
                "Online Student Record",
                new DateTime(2026, 8, 25, 16, 5, 3),
                "STU-TRANSCRIPT-1",
                "Alec",
                "Steele",
                "C10476",
                "Bachelor of Computing Science",
                "Active",
                "Enterprise Software Development",
                [],
                subjects,
                6.71m,
                88.06m,
                144,
                "This is an e-record produced by this student for personal use.");

            var pdf = new TranscriptPdfGenerator().Generate(transcript);
            var pdfContent = Encoding.Latin1.GetString(pdf);
            var pageCount = Regex.Matches(pdfContent, @"/Type\s*/Page\b").Count;

            Assert.That(pdf, Is.Not.Empty);
            Assert.That(Encoding.ASCII.GetString(pdf, 0, 5), Is.EqualTo("%PDF-"));
            Assert.That(pageCount, Is.GreaterThanOrEqualTo(2));
            Assert.That(pdfContent.ToLowerInvariant(), Does.Not.Contain("watermark"));
        }

        [Test]
        public void InsufficientCreditsAreReportedBeforeActiveCourses()
        {
            var profile = CreateEligibilityProfile(
                ("CPAI1001", "Passed", 120, true),
                ("CPAI2001", "Enrolled", 6, false));

            var result = GraduationEligibilityCalculator.Calculate(profile);

            Assert.That(result.IsEligible, Is.False);
            Assert.That(result.EarnedCreditPoints, Is.EqualTo(120m));
            Assert.That(result.OutstandingRequirements, Has.Count.EqualTo(1));
            Assert.That(result.OutstandingRequirements[0], Does.StartWith("Insufficient credit points:"));
        }

        [Test]
        public void ActiveCoursesAreReportedAfterCreditRequirementIsMet()
        {
            var profile = CreateEligibilityProfile(
                ("CPAI1001", "Passed", 144, true),
                ("CPAI2001", "In Progress", 6, false));

            var result = GraduationEligibilityCalculator.Calculate(profile);

            Assert.That(result.IsEligible, Is.False);
            Assert.That(result.EarnedCreditPoints, Is.EqualTo(144m));
            Assert.That(result.OutstandingRequirements, Has.Count.EqualTo(1));
            Assert.That(result.OutstandingRequirements[0], Does.StartWith("Active courses must be completed:"));
        }

        [Test]
        public void FailedSubjectsDoNotContributeCreditPoints()
        {
            var profile = CreateEligibilityProfile(
                ("CPAI1001", "Passed", 138, true),
                ("CPAI2001", "Failed", 12, true));

            var result = GraduationEligibilityCalculator.Calculate(profile);

            Assert.That(result.IsEligible, Is.False);
            Assert.That(result.EarnedCreditPoints, Is.EqualTo(138m));
            Assert.That(result.OutstandingRequirements[0], Does.StartWith("Insufficient credit points:"));
        }

        [Test]
        public void Exactly144CreditsWithNoActiveCoursesIsEligible()
        {
            var result = GraduationEligibilityCalculator.Calculate(
                CreateEligibilityProfile(("CPAI1001", "Completed", 144, true)));

            Assert.That(result.IsEligible, Is.True);
            Assert.That(result.OutstandingRequirements, Is.Empty);
        }

        [Test]
        public async Task GraduationDatePersistsInStudentSchema()
        {
            await using var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            await using var database = CreateDatabase(connection);
            await database.Database.EnsureCreatedAsync();

            database.Students.Add(new Student
            {
                StudentId = "STU-GRAD-1",
                FirstName = "Graduating",
                LastName = "Student",
                Email = "graduating@example.edu",
                DateOfBirth = new(2000, 1, 1),
                Status = "ACT",
                MailingAddress = "1 Campus Way",
                EmergencyContactName = "Contact Person",
                EmergencyContactPhone = "0400000000",
                GraduationDate = new(2026, 12, 1)
            });
            await database.SaveChangesAsync();

            var saved = await database.Students.SingleAsync(value => value.StudentId == "STU-GRAD-1");

            Assert.That(saved.GraduationDate, Is.EqualTo(new DateOnly(2026, 12, 1)));
        }

        private static StudentStatisticsProfile CreateEligibilityProfile(
            params (string CourseCode, string Status, decimal Credits, bool IsFinalized)[] enrollments) =>
            new StudentStatisticsProfile
            {
                StudentId = "STU-ELIGIBILITY-1",
                Enrollments = enrollments.Select(enrollment => new StudentStatisticsEnrollment
                {
                    CourseCode = enrollment.CourseCode,
                    Status = enrollment.Status,
                    Credits = enrollment.Credits,
                    IsFinalized = enrollment.IsFinalized
                }).ToList()
            };

        private static AcademicRecordsDbContext CreateDatabase(SqliteConnection connection) =>
            new(new DbContextOptionsBuilder<AcademicRecordsDbContext>().UseSqlite(connection).Options);

        private static bool IsValidGrade(decimal? grade) => grade is >= 0 and <= 100;

        private static bool IsWithinGradeEditingWindow(DateOnly date, (DateOnly StartDate, DateOnly EndDate) sessionDates) =>
            date >= sessionDates.StartDate && date <= sessionDates.EndDate;

        private sealed record TermEnrollmentResponse(string StudentId, string StudentName, string CourseCode,
            string TermCode, string TermName, int AcademicYear, string Status);
    }
}
