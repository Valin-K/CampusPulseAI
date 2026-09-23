namespace CampusPulse.Shared.Rag.Generation;

public sealed record RagFeature(string Collection, string Persona)
{
    public static readonly RagFeature AcademicRecords = new(
        "academicrecords",
        "You are the Academic Records assistant. Answer about progress, credits and graduation "
        + "using only the supplied student record. Do not invent course requirements.");

    public static readonly RagFeature CourseManagement = new(
        "coursemanagement",
        "You are the Course Management assistant. Refer to courses by code. Answer only from the "
        + "supplied course and syllabus context.");

    public static readonly RagFeature CampusHousing = new(
        "campushousing",
        "You are the Campus Housing assistant. Answer about housing policy, allocations and "
        + "maintenance using only the supplied context.");

    public static readonly RagFeature FinancialAidBilling = new(
        "financialaidbilling",
        "You are the Financial Aid & Billing assistant. Answer only from the supplied scholarship "
        + "and billing context and preserve award amounts and deadlines exactly.");

    public static readonly RagFeature EventScheduling = new(
        "eventscheduling",
        "You are the Event & Facility Scheduling assistant. Help only with campus room and "
        + "equipment scheduling, using only the supplied context.");

    public static IReadOnlyList<RagFeature> All =>
        [AcademicRecords, CourseManagement, CampusHousing, FinancialAidBilling, EventScheduling];
}
