using AcademicRecords.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AcademicRecords.Database.Data;

public sealed class AcademicRecordsDbContext(DbContextOptions<AcademicRecordsDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<AcademicTerm> AcademicTerms => Set<AcademicTerm>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(user => user.UserId);
            entity.Property(user => user.UserId).HasColumnName("user_id").ValueGeneratedOnAdd();
            entity.Property(user => user.StudentId).HasColumnName("student_id").HasMaxLength(20);
            entity.Property(user => user.StaffId).HasColumnName("staff_id").HasMaxLength(20);
            entity.Property(user => user.FirstName).HasColumnName("first_name").HasMaxLength(100).IsRequired();
            entity.Property(user => user.LastName).HasColumnName("last_name").HasMaxLength(100).IsRequired();
            entity.Property(user => user.Email).HasColumnName("email").HasMaxLength(255).IsRequired();
            entity.Property(user => user.DateOfBirth).HasColumnName("date_of_birth").HasColumnType("DATE");
            entity.Property(user => user.EnrollmentDate).HasColumnName("enrollment_date").HasColumnType("DATE");
            entity.Property(user => user.Status).HasColumnName("status").HasMaxLength(3).IsRequired();
            entity.Property(user => user.PhoneNumber).HasColumnName("phone_number").HasMaxLength(10);
            entity.Property(user => user.Degree).HasColumnName("degree").HasMaxLength(100);
            entity.Property(user => user.Major).HasColumnName("major").HasMaxLength(100);
            entity.Property(user => user.SubMajor).HasColumnName("sub_major").HasMaxLength(100);
        });

        modelBuilder.Entity<AcademicTerm>(entity =>
        {
            entity.ToTable("AcademicTerms");
            entity.HasKey(term => term.TermId);
            entity.Property(term => term.TermId).HasColumnName("term_id").ValueGeneratedOnAdd();
            entity.Property(term => term.TermCode).HasColumnName("term_code").HasMaxLength(20).IsRequired();
            entity.Property(term => term.TermName).HasColumnName("term_name").HasMaxLength(100).IsRequired();
            entity.Property(term => term.StartDate).HasColumnName("start_date").HasColumnType("DATE").IsRequired();
            entity.Property(term => term.EndDate).HasColumnName("end_date").HasColumnType("DATE").IsRequired();
            entity.Property(term => term.GradeLockDate).HasColumnName("grade_lock_date").HasColumnType("DATE");
            entity.Property(term => term.CensusDate).HasColumnName("census_date").HasColumnType("DATE");
            entity.Property(term => term.IsActive).HasColumnName("is_active").IsRequired();
        });

        modelBuilder.Entity<Enrollment>(entity =>
        {
            entity.ToTable("Enrollments");
            entity.HasKey(enrollment => enrollment.EnrollmentId);
            entity.Property(enrollment => enrollment.EnrollmentId).HasColumnName("enrollment_id").ValueGeneratedOnAdd();
            entity.Property(enrollment => enrollment.StudentId).HasColumnName("student_id");
            entity.Property(enrollment => enrollment.CourseCode).HasColumnName("course_code").HasMaxLength(20).IsRequired();
            entity.Property(enrollment => enrollment.AcademicTermId).HasColumnName("academic_term_id");
            entity.Property(enrollment => enrollment.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            entity.Property(enrollment => enrollment.GradeValue).HasColumnName("grade_value").HasPrecision(4, 2);
            entity.Property(enrollment => enrollment.Credits).HasColumnName("credits").HasPrecision(4, 1);
            entity.Property(enrollment => enrollment.GradeDate).HasColumnName("grade_date").HasColumnType("DATE");
            entity.Property(enrollment => enrollment.IsFinalized).HasColumnName("is_finalized").IsRequired();

            entity.HasOne(enrollment => enrollment.Student)
                .WithMany(user => user.Enrollments)
                .HasForeignKey(enrollment => enrollment.StudentId)
                .HasPrincipalKey(user => user.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(enrollment => enrollment.AcademicTerm)
                .WithMany(term => term.Enrollments)
                .HasForeignKey(enrollment => enrollment.AcademicTermId)
                .HasPrincipalKey(term => term.TermId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
