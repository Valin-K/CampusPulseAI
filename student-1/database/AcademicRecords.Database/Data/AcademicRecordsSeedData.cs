using AcademicRecords.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AcademicRecords.Database.Data;

public static class AcademicRecordsSeedData
{
    public static async Task InitializeAsync(AcademicRecordsDbContext database)
    {
        if (!await database.Students.AnyAsync())
        {
            database.Students.AddRange(Students);
        }

        if (!await database.Set<Staff>().AnyAsync())
        {
            database.Set<Staff>().AddRange(StaffMembers);
        }

        if (!await database.AcademicTerms.AnyAsync())
        {
            database.AcademicTerms.AddRange(AcademicTerms);
            await database.SaveChangesAsync();
        }

        var seededEnrollments = Enrollments;
        var existingSeedEnrollments = await database.Enrollments
            .Where(enrollment => enrollment.EnrollmentId >= 1 && enrollment.EnrollmentId <= seededEnrollments.Length)
            .ToListAsync();

        if (existingSeedEnrollments.Count == 0)
        {
            database.Enrollments.AddRange(seededEnrollments);
        }
        else
        {
            var existingById = existingSeedEnrollments.ToDictionary(enrollment => enrollment.EnrollmentId);
            foreach (var existing in existingSeedEnrollments)
            {
                var expected = seededEnrollments[existing.EnrollmentId - 1];
                existing.CourseCode = expected.CourseCode;
                existing.GradeValue = expected.GradeValue;
                existing.GradeDate = expected.GradeDate;
                existing.IsFinalized = expected.IsFinalized;
            }

            database.Enrollments.AddRange(seededEnrollments
                .Where(expected => !existingById.ContainsKey(expected.EnrollmentId)));
        }

        await database.SaveChangesAsync();
    }

    public static Student[] Students =>
    [
        new() { UserId = 1, StudentId = "STU001", FirstName = "Alec", LastName = "Steele", Email = "alec.steele@example.edu", DateOfBirth = new(2001, 3, 12), EnrollmentDate = new(2023, 2, 20), Status = "ACT", PhoneNumber = "0400000001", MailingAddress = "1 Campus Way", EmergencyContactName = "Morgan Steele", EmergencyContactPhone = "0400100001", Degree = "Bachelor of Computer Science", Major = "Software Engineering", SubMajor = "Artificial Intelligence" },
        new() { UserId = 2, StudentId = "STU002", FirstName = "Valin", LastName = "Kennedy", Email = "valin.kennedy@example.edu", DateOfBirth = new(2000, 7, 8), EnrollmentDate = new(2022, 8, 15), Status = "ACT", PhoneNumber = "0400000002", MailingAddress = "2 Campus Way", EmergencyContactName = "Jordan Kennedy", EmergencyContactPhone = "0400100002", Degree = "Bachelor of Information Technology", Major = "Cybersecurity", SubMajor = "Network Security" },
        new() { UserId = 3, StudentId = "STU003", FirstName = "Joyce", LastName = "Yap", Email = "joyce.yap@example.edu", DateOfBirth = new(2002, 1, 24), EnrollmentDate = new(2024, 2, 19), Status = "ACT", PhoneNumber = "0400000003", MailingAddress = "3 Campus Way", EmergencyContactName = "Samuel Yap", EmergencyContactPhone = "0400100003", Degree = "Bachelor of Data Science", Major = "Data Analytics", SubMajor = "Statistics" },
        new() { UserId = 4, StudentId = "STU004", FirstName = "Ryan", LastName = "Takayasu", Email = "ryan.takayasu@example.edu", DateOfBirth = new(2001, 11, 2), EnrollmentDate = new(2023, 8, 14), Status = "ACT", PhoneNumber = "0400000004", MailingAddress = "4 Campus Way", EmergencyContactName = "Taylor Takayasu", EmergencyContactPhone = "0400100004", Degree = "Bachelor of Computer Science", Major = "Computer Science", SubMajor = "Cybersecurity" },
        new() { UserId = 5, StudentId = "STU005", FirstName = "Jack", LastName = "Wang", Email = "jack.wang@example.edu", DateOfBirth = new(2000, 5, 17), EnrollmentDate = new(2022, 2, 21), Status = "ACT", PhoneNumber = "0400000005", MailingAddress = "5 Campus Way", EmergencyContactName = "Casey Wang", EmergencyContactPhone = "0400100005", Degree = "Bachelor of Business", Major = "Business Analytics", SubMajor = "Finance" },
        new() { UserId = 6, StudentId = "STU006", FirstName = "Mia", LastName = "Chen", Email = "mia.chen@example.edu", DateOfBirth = new(2002, 9, 30), EnrollmentDate = new(2024, 8, 12), Status = "ACT", PhoneNumber = "0400000006", MailingAddress = "6 Campus Way", EmergencyContactName = "Alex Chen", EmergencyContactPhone = "0400100006", Degree = "Bachelor of Engineering", Major = "Software Engineering", SubMajor = "Embedded Systems" },
        new() { UserId = 7, StudentId = "STU007", FirstName = "Noah", LastName = "Patel", Email = "noah.patel@example.edu", DateOfBirth = new(2001, 12, 6), EnrollmentDate = new(2023, 2, 20), Status = "ACT", PhoneNumber = "0400000007", MailingAddress = "7 Campus Way", EmergencyContactName = "Riley Patel", EmergencyContactPhone = "0400100007", Degree = "Bachelor of Science", Major = "Applied Mathematics", SubMajor = "Statistics" },
        new() { UserId = 8, StudentId = "STU008", FirstName = "Ava", LastName = "Thompson", Email = "ava.thompson@example.edu", DateOfBirth = new(2002, 6, 14), EnrollmentDate = new(2024, 2, 19), Status = "ACT", PhoneNumber = "0400000008", MailingAddress = "8 Campus Way", EmergencyContactName = "Harper Thompson", EmergencyContactPhone = "0400100008", Degree = "Bachelor of Arts", Major = "Psychology", SubMajor = "Sociology" },
        new() { UserId = 9, StudentId = "STU009", FirstName = "Liam", LastName = "Nguyen", Email = "liam.nguyen@example.edu", DateOfBirth = new(2001, 10, 3), EnrollmentDate = new(2023, 8, 14), Status = "ACT", PhoneNumber = "0400000009", MailingAddress = "9 Campus Way", EmergencyContactName = "Mai Nguyen", EmergencyContactPhone = "0400100009", Degree = "Bachelor of Engineering", Major = "Mechanical Engineering", SubMajor = "Robotics" },
        new() { UserId = 10, StudentId = "STU010", FirstName = "Sophie", LastName = "Martinez", Email = "sophie.martinez@example.edu", DateOfBirth = new(2002, 3, 27), EnrollmentDate = new(2024, 8, 12), Status = "ACT", PhoneNumber = "0400000010", MailingAddress = "10 Campus Way", EmergencyContactName = "Diego Martinez", EmergencyContactPhone = "0400100010", Degree = "Bachelor of Commerce", Major = "Accounting", SubMajor = "Economics" },
    ];

    public static Staff[] StaffMembers =>
    [
        new() { UserId = 11, StaffId = "STF001", FirstName = "Grace", LastName = "Brillis", Email = "grace.brillis@example.edu", DateOfBirth = new(1985, 4, 18), Status = "ACT", PhoneNumber = "0400000011" },
        new() { UserId = 12, StaffId = "STF002", FirstName = "Georges", LastName = "Bou Ghantous", Email = "georges.boughantous@example.edu", DateOfBirth = new(1979, 9, 7), Status = "ACT", PhoneNumber = "0400000012" },
        new() { UserId = 13, StaffId = "STF003", FirstName = "Olivia", LastName = "Davis", Email = "olivia.davis@example.edu", DateOfBirth = new(1990, 1, 25), Status = "ACT", PhoneNumber = "0400000013" },
        new() { UserId = 14, StaffId = "STF004", FirstName = "Marcus", LastName = "Lee", Email = "marcus.lee@example.edu", DateOfBirth = new(1982, 11, 11), Status = "ACT", PhoneNumber = "0400000014" },
        new() { UserId = 15, StaffId = "STF005", FirstName = "Priya", LastName = "Sharma", Email = "priya.sharma@example.edu", DateOfBirth = new(1988, 2, 3), Status = "ACT", PhoneNumber = "0400000015" },
        new() { UserId = 16, StaffId = "STF006", FirstName = "Daniel", LastName = "Osei", Email = "daniel.osei@example.edu", DateOfBirth = new(1976, 7, 22), Status = "ACT", PhoneNumber = "0400000016" },
        new() { UserId = 17, StaffId = "STF007", FirstName = "Isabella", LastName = "Russo", Email = "isabella.russo@example.edu", DateOfBirth = new(1992, 5, 30), Status = "ACT", PhoneNumber = "0400000017" },
        new() { UserId = 18, StaffId = "STF008", FirstName = "Ethan", LastName = "Walker", Email = "ethan.walker@example.edu", DateOfBirth = new(1980, 12, 9), Status = "ACT", PhoneNumber = "0400000018" },
        new() { UserId = 19, StaffId = "STF009", FirstName = "Chloe", LastName = "Anderson", Email = "chloe.anderson@example.edu", DateOfBirth = new(1987, 8, 16), Status = "ACT", PhoneNumber = "0400000019" },
        new() { UserId = 20, StaffId = "STF010", FirstName = "Samuel", LastName = "O'Brien", Email = "samuel.obrien@example.edu", DateOfBirth = new(1983, 3, 5), Status = "ACT", PhoneNumber = "0400000020" }
    ];

    public static AcademicTerm[] AcademicTerms =>
    [
        new() { TermId = 1, TermCode = "JAN", TermName = "January", StartWeekOfYear = 1, EndWeekOfYear = 5, IsActive = false },
        new() { TermId = 2, TermCode = "FEB", TermName = "February", StartWeekOfYear = 5, EndWeekOfYear = 9, IsActive = false },
        new() { TermId = 3, TermCode = "MAR", TermName = "March", StartWeekOfYear = 9, EndWeekOfYear = 13, IsActive = false },
        new() { TermId = 4, TermCode = "APR", TermName = "April", StartWeekOfYear = 14, EndWeekOfYear = 17, IsActive = false },
        new() { TermId = 5, TermCode = "MAY", TermName = "May", StartWeekOfYear = 18, EndWeekOfYear = 22, IsActive = false },
        new() { TermId = 6, TermCode = "JUN", TermName = "June", StartWeekOfYear = 23, EndWeekOfYear = 26, IsActive = false },
        new() { TermId = 7, TermCode = "JUL", TermName = "July", StartWeekOfYear = 27, EndWeekOfYear = 31, IsActive = false },
        new() { TermId = 8, TermCode = "AUG", TermName = "August", StartWeekOfYear = 32, EndWeekOfYear = 35, IsActive = false },
        new() { TermId = 9, TermCode = "SEP", TermName = "September", StartWeekOfYear = 36, EndWeekOfYear = 39, IsActive = false },
        new() { TermId = 10, TermCode = "OCT", TermName = "October", StartWeekOfYear = 40, EndWeekOfYear = 44, IsActive = false },
        new() { TermId = 11, TermCode = "NOV", TermName = "November", StartWeekOfYear = 44, EndWeekOfYear = 48, IsActive = false },
        new() { TermId = 12, TermCode = "DEC", TermName = "December", StartWeekOfYear = 48, EndWeekOfYear = 52, IsActive = false },
        new() { TermId = 13, TermCode = "AUT", TermName = "Autumn", StartWeekOfYear = 5, EndWeekOfYear = 22, IsActive = false },
        new() { TermId = 14, TermCode = "SPR", TermName = "Spring", StartWeekOfYear = 32, EndWeekOfYear = 48, IsActive = false },
        new() { TermId = 15, TermCode = "SUM", TermName = "Summer", StartWeekOfYear = 48, EndWeekOfYear = 9, IsActive = true }
    ];

    public static Enrollment[] Enrollments =>
    [
        .. Enumerable.Range(0, 10).SelectMany(studentIndex => Enumerable.Range(0, 5).Select(courseIndex =>
        {
            var academicTermId = (studentIndex + courseIndex) % 3 + 13;
            var academicYear = 2026;
            var term = AcademicTerms.Single(value => value.TermId == academicTermId);
            var (startDate, endDate) = EnrollmentStatusRules.GetSessionDates(academicYear, term.StartWeekOfYear, term.EndWeekOfYear);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var status = EnrollmentStatusRules.GetStatus(startDate, endDate, today);
            var isCompleted = studentIndex == 0 || status == EnrollmentStatusRules.Completed;

            return new Enrollment
            {
                EnrollmentId = studentIndex * 5 + courseIndex + 1,
                StudentId = studentIndex + 1,
                CourseCode = CourseCodes[courseIndex],
                AcademicTermId = academicTermId,
                AcademicYear = academicYear,
                GradeValue = isCompleted && courseIndex != 2
                    ? courseIndex is 1 or 4
                        ? studentIndex % 2 == 0 ? 50m : 49m
                        : 65m + studentIndex + courseIndex * 5
                    : null,
                GradeDate = isCompleted ? studentIndex == 0 ? today : endDate : null,
                IsFinalized = isCompleted
            };
        })),
        .. Enumerable.Range(0, 5).Select(courseIndex =>
        {
            var academicTermId = (courseIndex + 2) % 3 + 13;
            var academicYear = 2025;
            var term = AcademicTerms.Single(value => value.TermId == academicTermId);
            var (_, endDate) = EnrollmentStatusRules.GetSessionDates(academicYear, term.StartWeekOfYear, term.EndWeekOfYear);

            return new Enrollment
            {
                EnrollmentId = 51 + courseIndex,
                StudentId = 1,
                CourseCode = AdditionalCourseCodes[courseIndex],
                AcademicTermId = academicTermId,
                AcademicYear = academicYear,
                GradeValue = courseIndex == 2 ? null : 70m + courseIndex,
                GradeDate = DateOnly.FromDateTime(DateTime.UtcNow),
                IsFinalized = true
            };
        })
    ];

    private static readonly string[] CourseCodes =
    [
        "CPAI1001",
        "CPAI1002",
        "CPAI1003",
        "CPAI2001",
        "CPAI2002"
    ];

    private static readonly string[] AdditionalCourseCodes =
    [
        "CPAI2003",
        "CPAI3001",
        "CPAI3002",
        "CPAI3003",
        "CPAI4001"
    ];

}
