using CourseManagement.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace CourseManagement.Database.Data;

public static class CourseManagementDataInitializer
{
    public static async Task InitializeAsync(CourseManagementDbContext database)
    {
        await CourseManagementDatabaseMigrator.MigrateAsync(database);

        if (await database.Courses.AnyAsync()
            || await database.Prerequisites.AnyAsync()
            || await database.FacultyAssignments.AnyAsync())
        {
            return;
        }

        await using var transaction = await database.Database.BeginTransactionAsync();

        var courses = new[]
        {
            new Course
            {
                Code = "CPAI1001",
                Name = "Programming Fundamentals",
                Description = "Introduces programming concepts, problem solving, and basic software development.",
                CreditPoints = 6,
                Level = 1
            },
            new Course
            {
                Code = "CPAI1002",
                Name = "Database Foundations",
                Description = "Covers relational data modelling, SQL, constraints, and database design.",
                CreditPoints = 6,
                Level = 1
            },
            new Course
            {
                Code = "CPAI1003",
                Name = "Web Development",
                Description = "Introduces browser applications, HTTP, frontend development, and web APIs.",
                CreditPoints = 6,
                Level = 1
            },
            new Course
            {
                Code = "CPAI2001",
                Name = "Software Design",
                Description = "Applies object-oriented design, architecture principles, and reusable patterns.",
                CreditPoints = 6,
                Level = 2
            },
            new Course
            {
                Code = "CPAI2002",
                Name = "Data Structures",
                Description = "Examines common data structures, algorithms, complexity, and implementation trade-offs.",
                CreditPoints = 6,
                Level = 2
            },
            new Course
            {
                Code = "CPAI2003",
                Name = "Cloud Fundamentals",
                Description = "Introduces cloud services, deployment models, containers, and scalable infrastructure.",
                CreditPoints = 6,
                Level = 2
            },
            new Course
            {
                Code = "CPAI3001",
                Name = "Microservices Architecture",
                Description = "Designs independently deployable services with explicit APIs and data ownership.",
                CreditPoints = 6,
                Level = 3
            },
            new Course
            {
                Code = "CPAI3002",
                Name = "DevOps Engineering",
                Description = "Covers CI/CD, automated validation, container delivery, and operational feedback.",
                CreditPoints = 6,
                Level = 3
            },
            new Course
            {
                Code = "CPAI3003",
                Name = "Applied Artificial Intelligence",
                Description = "Applies machine learning and language models to practical software problems.",
                CreditPoints = 6,
                Level = 3
            },
            new Course
            {
                Code = "CPAI4001",
                Name = "Agentic Software Engineering",
                Description = "Builds controlled agentic systems using tools, retrieval, review, and human oversight.",
                CreditPoints = 6,
                Level = 4
            }
        };

        database.Courses.AddRange(courses);
        await database.SaveChangesAsync();

        var coursesByCode = courses.ToDictionary(course => course.Code);

        database.Prerequisites.AddRange(
            CreatePrerequisite(coursesByCode, "CPAI2001", "CPAI1001", "Pass"),
            CreatePrerequisite(coursesByCode, "CPAI2002", "CPAI1001", "Pass"),
            CreatePrerequisite(coursesByCode, "CPAI2003", "CPAI1003", "Pass"),
            CreatePrerequisite(coursesByCode, "CPAI3001", "CPAI2001", "Credit"),
            CreatePrerequisite(coursesByCode, "CPAI3001", "CPAI1002", "Pass"),
            CreatePrerequisite(coursesByCode, "CPAI3002", "CPAI2001", "Pass"),
            CreatePrerequisite(coursesByCode, "CPAI3003", "CPAI2002", "Credit"),
            CreatePrerequisite(coursesByCode, "CPAI3003", "CPAI1002", "Pass"),
            CreatePrerequisite(coursesByCode, "CPAI4001", "CPAI3001", "Credit"),
            CreatePrerequisite(coursesByCode, "CPAI4001", "CPAI3003", "Credit"));

        database.FacultyAssignments.AddRange(
            CreateAssignment(coursesByCode, "CPAI1001", "Maya Chen", "maya.chen@campuspulse.edu", "Course Coordinator", "Autumn 2026"),
            CreateAssignment(coursesByCode, "CPAI1002", "Liam Patel", "liam.patel@campuspulse.edu", "Lecturer", "Autumn 2026"),
            CreateAssignment(coursesByCode, "CPAI1003", "Sofia Nguyen", "sofia.nguyen@campuspulse.edu", "Course Coordinator", "Spring 2026"),
            CreateAssignment(coursesByCode, "CPAI2001", "Noah Williams", "noah.williams@campuspulse.edu", "Lecturer", "Autumn 2026"),
            CreateAssignment(coursesByCode, "CPAI2002", "Emma Thompson", "emma.thompson@campuspulse.edu", "Course Coordinator", "Spring 2026"),
            CreateAssignment(coursesByCode, "CPAI2003", "Ethan Brooks", "ethan.brooks@campuspulse.edu", "Lecturer", "Spring 2026"),
            CreateAssignment(coursesByCode, "CPAI3001", "Olivia Martin", "olivia.martin@campuspulse.edu", "Course Coordinator", "Autumn 2026"),
            CreateAssignment(coursesByCode, "CPAI3002", "Lucas Reed", "lucas.reed@campuspulse.edu", "Lecturer", "Spring 2026"),
            CreateAssignment(coursesByCode, "CPAI3003", "Ava Robinson", "ava.robinson@campuspulse.edu", "Course Coordinator", "Autumn 2026"),
            CreateAssignment(coursesByCode, "CPAI4001", "Mia Taylor", "mia.taylor@campuspulse.edu", "Course Coordinator", "Spring 2026"));

        await database.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    static Prerequisite CreatePrerequisite(
        IReadOnlyDictionary<string, Course> courses,
        string courseCode,
        string requiredCourseCode,
        string minimumGrade)
    {
        return new Prerequisite
        {
            CourseId = courses[courseCode].Id,
            RequiredCourseId = courses[requiredCourseCode].Id,
            MinimumGrade = minimumGrade
        };
    }

    static FacultyAssignment CreateAssignment(
        IReadOnlyDictionary<string, Course> courses,
        string courseCode,
        string staffName,
        string staffEmail,
        string role,
        string teachingPeriod)
    {
        return new FacultyAssignment
        {
            CourseId = courses[courseCode].Id,
            StaffName = staffName,
            StaffEmail = staffEmail,
            Role = role,
            TeachingPeriod = teachingPeriod
        };
    }
}
