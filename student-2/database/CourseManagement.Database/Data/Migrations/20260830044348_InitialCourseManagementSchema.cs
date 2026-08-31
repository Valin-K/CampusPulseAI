using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourseManagement.Database.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCourseManagementSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Courses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    CreditPoints = table.Column<int>(type: "INTEGER", nullable: false),
                    Level = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    SyllabusFileName = table.Column<string>(type: "TEXT", maxLength: 260, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Courses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FacultyAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CourseId = table.Column<int>(type: "INTEGER", nullable: false),
                    StaffName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    StaffEmail = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    TeachingPeriod = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FacultyAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FacultyAssignments_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Prerequisites",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CourseId = table.Column<int>(type: "INTEGER", nullable: false),
                    RequiredCourseId = table.Column<int>(type: "INTEGER", nullable: false),
                    MinimumGrade = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Prerequisites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Prerequisites_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Prerequisites_Courses_RequiredCourseId",
                        column: x => x.RequiredCourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Courses_Code",
                table: "Courses",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FacultyAssignments_CourseId_StaffEmail_TeachingPeriod",
                table: "FacultyAssignments",
                columns: new[] { "CourseId", "StaffEmail", "TeachingPeriod" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Prerequisites_CourseId_RequiredCourseId",
                table: "Prerequisites",
                columns: new[] { "CourseId", "RequiredCourseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Prerequisites_RequiredCourseId",
                table: "Prerequisites",
                column: "RequiredCourseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FacultyAssignments");

            migrationBuilder.DropTable(
                name: "Prerequisites");

            migrationBuilder.DropTable(
                name: "Courses");
        }
    }
}
