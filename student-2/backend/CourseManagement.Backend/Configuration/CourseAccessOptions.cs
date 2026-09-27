namespace CourseManagement.Backend.Configuration;

public sealed class CourseAccessOptions
{
    public const string SectionName = "CourseAccess";

    public string DemoStudentId { get; set; } = "STU003";
    public string DemoStaffId { get; set; } = "STF001";
    public int DemoEnrollmentTermId { get; set; } = 15;
    public int DemoAcademicYear { get; set; } = 2026;
}
