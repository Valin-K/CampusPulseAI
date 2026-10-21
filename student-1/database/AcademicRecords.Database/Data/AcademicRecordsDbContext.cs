using AcademicRecords.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace AcademicRecords.Database.Data;

public sealed class AcademicRecordsDbContext(DbContextOptions<AcademicRecordsDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<AcademicTerm> AcademicTerms => Set<AcademicTerm>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.ToTable("Users", table => table.HasCheckConstraint(
                "CK_Users_ExactlyOneRole",
                "(student_id IS NOT NULL AND staff_id IS NULL) OR (student_id IS NULL AND staff_id IS NOT NULL)"));
            entity.HasDiscriminator<string>("user_type")
                .HasValue<Student>("Student")
                .HasValue<Staff>("Staff");
            entity.HasKey(user => user.UserId);
            entity.Property(user => user.UserId).HasColumnName("user_id").ValueGeneratedOnAdd();
            entity.Property(user => user.FirstName).HasColumnName("first_name").HasMaxLength(100).IsRequired();
            entity.Property(user => user.LastName).HasColumnName("last_name").HasMaxLength(100).IsRequired();
            entity.Property(user => user.Email).HasColumnName("email").HasMaxLength(255).IsRequired();
            entity.Property(user => user.PasswordHash).HasColumnName("password_hash").HasMaxLength(512).IsRequired();
            entity.Property(user => user.DateOfBirth).HasColumnName("date_of_birth").HasColumnType("DATE");
            entity.Property(user => user.Status).HasColumnName("status").HasMaxLength(3).IsRequired();
            entity.Property(user => user.PhoneNumber).HasColumnName("phone_number").HasMaxLength(10);
        });

        modelBuilder.Entity<Student>(entity =>
        {
            entity.Property(student => student.StudentId).HasColumnName("student_id").HasMaxLength(20).IsRequired();
            entity.Property(student => student.MailingAddress).HasColumnName("mailing_address").HasMaxLength(500).IsRequired();
            entity.Property(student => student.EmergencyContactName).HasColumnName("emergency_contact_name").HasMaxLength(200).IsRequired();
            entity.Property(student => student.EmergencyContactPhone).HasColumnName("emergency_contact_phone").HasMaxLength(30).IsRequired();
            entity.Property(student => student.EnrollmentDate).HasColumnName("enrollment_date").HasColumnType("DATE");
            entity.Property(student => student.GraduationDate).HasColumnName("graduation_date").HasColumnType("DATE");
            entity.Property(student => student.Degree).HasColumnName("degree").HasMaxLength(100);
            entity.Property(student => student.Major).HasColumnName("major").HasMaxLength(100);
            entity.Property(student => student.SubMajor).HasColumnName("sub_major").HasMaxLength(100);
            entity.HasIndex(student => student.StudentId).IsUnique();
        });

        modelBuilder.Entity<Staff>(entity =>
        {
            entity.Property(staff => staff.StaffId).HasColumnName("staff_id").HasMaxLength(20).IsRequired();
        });

        modelBuilder.Entity<AcademicTerm>(entity =>
        {
            entity.ToTable("AcademicTerms");
            entity.HasKey(term => term.TermId);
            entity.Property(term => term.TermId).HasColumnName("term_id").ValueGeneratedOnAdd();
            entity.Property(term => term.TermCode).HasColumnName("term_code").HasMaxLength(20).IsRequired();
            entity.Property(term => term.StartWeekOfYear).HasColumnName("start_week_of_year").IsRequired();
            entity.Property(term => term.EndWeekOfYear).HasColumnName("end_week_of_year").IsRequired();
            entity.Property(term => term.TermName).HasColumnName("term_name").HasMaxLength(100).IsRequired();
            entity.Property(term => term.IsActive).HasColumnName("is_active").IsRequired();
            entity.HasIndex(term => term.TermCode).IsUnique();
            entity.ToTable("AcademicTerms", table => table.HasCheckConstraint(
                "CK_AcademicTerms_WeekRange",
                "start_week_of_year BETWEEN 1 AND 53 AND end_week_of_year BETWEEN 1 AND 53"));
        });

        modelBuilder.Entity<Enrollment>(entity =>
        {
            entity.ToTable("Enrollments", table =>
            {
                table.HasCheckConstraint("CK_Enrollments_GradeRange", "grade_value IS NULL OR CAST(grade_value AS REAL) BETWEEN 0 AND 100");
            });
            entity.HasKey(enrollment => enrollment.EnrollmentId);
            entity.Property(enrollment => enrollment.EnrollmentId).HasColumnName("enrollment_id").ValueGeneratedOnAdd();
            entity.Property(enrollment => enrollment.StudentId).HasColumnName("student_id");
            entity.Property(enrollment => enrollment.CourseCode).HasColumnName("course_code").HasMaxLength(20).IsRequired();
            entity.Property(enrollment => enrollment.AcademicTermId).HasColumnName("academic_term_id");
            entity.Property(enrollment => enrollment.AcademicYear).HasColumnName("academic_year").IsRequired();
            entity.Ignore(enrollment => enrollment.Status);
            entity.Ignore(enrollment => enrollment.IsFinalized);
            entity.Property(enrollment => enrollment.GradeValue).HasColumnName("grade_value").HasPrecision(5, 2);
            entity.Property(enrollment => enrollment.GradeDate).HasColumnName("grade_date").HasColumnType("DATE");

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
            entity.HasIndex(enrollment => new { enrollment.StudentId, enrollment.AcademicTermId, enrollment.CourseCode }).IsUnique();
        });

    }
}
