using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourseManagement.Database.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDatabaseIntegrityConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_Prerequisites_DifferentCourses",
                table: "Prerequisites",
                sql: "\"CourseId\" <> \"RequiredCourseId\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Courses_CreditPoints",
                table: "Courses",
                sql: "\"CreditPoints\" BETWEEN 1 AND 48");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Courses_Level",
                table: "Courses",
                sql: "\"Level\" BETWEEN 1 AND 10");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Prerequisites_DifferentCourses",
                table: "Prerequisites");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Courses_CreditPoints",
                table: "Courses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Courses_Level",
                table: "Courses");
        }
    }
}
