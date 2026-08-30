using AcademicRecords.Database.Models;

namespace AcademicRecords.Database.Data;

public static class AcademicRecordsSeedData
{
    public static User[] Users =>
    [
        new() { UserId = 1, StudentId = "STU001", FirstName = "Alec", LastName = "Steele", Email = "alec.steele@example.edu", DateOfBirth = new(2001, 3, 12), EnrollmentDate = new(2023, 2, 20), Status = "ACT", PhoneNumber = "0400000001", Degree = "Bachelor of Computer Science", Major = "Software Engineering", SubMajor = "Artificial Intelligence" },
        new() { UserId = 2, StudentId = "STU002", FirstName = "Valin", LastName = "Kennedy", Email = "valin.kennedy@example.edu", DateOfBirth = new(2000, 7, 8), EnrollmentDate = new(2022, 8, 15), Status = "ACT", PhoneNumber = "0400000002", Degree = "Bachelor of Information Technology", Major = "Cybersecurity" },
        new() { UserId = 3, StudentId = "STU003", FirstName = "Joyce", LastName = "Yap", Email = "joyce.yap@example.edu", DateOfBirth = new(2002, 1, 24), EnrollmentDate = new(2024, 2, 19), Status = "ACT", PhoneNumber = "0400000003", Degree = "Bachelor of Data Science", Major = "Data Analytics" },
        new() { UserId = 4, StudentId = "STU004", FirstName = "Ryan", LastName = "Takayasu", Email = "ryan.takayasu@example.edu", DateOfBirth = new(2001, 11, 2), EnrollmentDate = new(2023, 8, 14), Status = "ACT", PhoneNumber = "0400000004", Degree = "Bachelor of Computer Science", Major = "Computer Science" },
        new() { UserId = 5, StudentId = "STU005", FirstName = "Jack", LastName = "Wang", Email = "jack.wang@example.edu", DateOfBirth = new(2000, 5, 17), EnrollmentDate = new(2022, 2, 21), Status = "ACT", PhoneNumber = "0400000005", Degree = "Bachelor of Business", Major = "Business Analytics" },
        new() { UserId = 6, StudentId = "STU006", FirstName = "Mia", LastName = "Chen", Email = "mia.chen@example.edu", DateOfBirth = new(2002, 9, 30), EnrollmentDate = new(2024, 8, 12), Status = "ACT", PhoneNumber = "0400000006", Degree = "Bachelor of Engineering", Major = "Software Engineering" },
        new() { UserId = 7, StudentId = "STU007", FirstName = "Noah", LastName = "Patel", Email = "noah.patel@example.edu", DateOfBirth = new(2001, 12, 6), EnrollmentDate = new(2023, 2, 20), Status = "ACT", PhoneNumber = "0400000007", Degree = "Bachelor of Science", Major = "Applied Mathematics" },
        new() { UserId = 8, StaffId = "STF001", FirstName = "Grace", LastName = "Brillis", Email = "grace.brillis@example.edu", Status = "ACT", PhoneNumber = "0400000008", Degree = "Academic Staff" },
        new() { UserId = 9, StaffId = "STF002", FirstName = "Georges", LastName = "Bou Ghantous", Email = "georges.boughantous@example.edu", Status = "ACT", PhoneNumber = "0400000009", Degree = "Academic Staff" },
        new() { UserId = 10, StaffId = "STF003", FirstName = "Olivia", LastName = "Davis", Email = "olivia.davis@example.edu", Status = "ACT", PhoneNumber = "0400000010", Degree = "Academic Staff" }
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
        .. Enumerable.Range(0, 7).SelectMany(studentIndex => Enumerable.Range(0, 5).Select(courseIndex => new Enrollment
        {
            EnrollmentId = studentIndex * 5 + courseIndex + 1,
            StudentId = studentIndex + 1,
            CourseCode = $"CS{101 + courseIndex:D3}",
            AcademicTermId = (studentIndex + courseIndex) % 3 + 13,
            AcademicYear = 2025,
            Status = courseIndex < 3 ? "Completed" : "Enrolled",
            GradeValue = courseIndex < 3 ? 65m + studentIndex + courseIndex * 5 : null,
            Credits = 3.0m,
            GradeDate = courseIndex < 3 ? new DateOnly(2025, 12, 20) : null,
            IsFinalized = courseIndex < 3
        }))
    ];
}
