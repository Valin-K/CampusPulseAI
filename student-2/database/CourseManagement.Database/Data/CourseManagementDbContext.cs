using CourseManagement.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace CourseManagement.Database.Data;

public sealed class CourseManagementDbContext(
    DbContextOptions<CourseManagementDbContext> options) : DbContext(options)
{
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<Prerequisite> Prerequisites => Set<Prerequisite>();
    public DbSet<FacultyAssignment> FacultyAssignments => Set<FacultyAssignment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Course>(entity =>
        {
            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_Courses_CreditPoints",
                    "\"CreditPoints\" BETWEEN 1 AND 48");
                table.HasCheckConstraint(
                    "CK_Courses_Level",
                    "\"Level\" BETWEEN 1 AND 10");
            });

            entity.HasKey(course => course.Id);
            entity.HasIndex(course => course.Code).IsUnique();

            entity.Property(course => course.Code).IsRequired().HasMaxLength(20);
            entity.Property(course => course.Name).IsRequired().HasMaxLength(200);
            entity.Property(course => course.Description).IsRequired().HasMaxLength(2000);
            entity.Property(course => course.SyllabusFileName).HasMaxLength(260);
        });

        modelBuilder.Entity<Prerequisite>(entity =>
        {
            entity.ToTable(table =>
                table.HasCheckConstraint(
                    "CK_Prerequisites_DifferentCourses",
                    "\"CourseId\" <> \"RequiredCourseId\""));

            entity.HasKey(prerequisite => prerequisite.Id);
            entity.HasIndex(prerequisite =>
                new { prerequisite.CourseId, prerequisite.RequiredCourseId }).IsUnique();

            entity.Property(prerequisite => prerequisite.MinimumGrade).HasMaxLength(20);

            entity.HasOne(prerequisite => prerequisite.Course)
                .WithMany(course => course.Prerequisites)
                .HasForeignKey(prerequisite => prerequisite.CourseId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(prerequisite => prerequisite.RequiredCourse)
                .WithMany(course => course.RequiredForCourses)
                .HasForeignKey(prerequisite => prerequisite.RequiredCourseId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FacultyAssignment>(entity =>
        {
            entity.HasKey(assignment => assignment.Id);
            entity.HasIndex(assignment =>
                new { assignment.CourseId, assignment.StaffEmail, assignment.TeachingPeriod })
                .IsUnique();

            entity.Property(assignment => assignment.StaffName).IsRequired().HasMaxLength(200);
            entity.Property(assignment => assignment.StaffEmail).IsRequired().HasMaxLength(320);
            entity.Property(assignment => assignment.Role).IsRequired().HasMaxLength(100);
            entity.Property(assignment => assignment.TeachingPeriod).IsRequired().HasMaxLength(50);

            entity.HasOne(assignment => assignment.Course)
                .WithMany(course => course.FacultyAssignments)
                .HasForeignKey(assignment => assignment.CourseId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
