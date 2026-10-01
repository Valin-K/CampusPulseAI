using McpServer.Tools.AcademicRecords;
using McpServer.Tools.CampusHousing;
using McpServer.Tools.CourseManagement;
using McpServer.Tools.Features;
using McpServer.Tools.Repository;

namespace McpServer.Tools;

public static class McpToolServiceCollectionExtensions
{
    public static IServiceCollection AddMcpTools(this IServiceCollection services)
    {
        services.AddScoped(serviceProvider => new CourseManagementApiClient(
            serviceProvider.GetRequiredService<IHttpClientFactory>()
                .CreateClient("courseManagement")));
        services.AddScoped(serviceProvider => new AcademicRecordsApiClient(
            serviceProvider.GetRequiredService<IHttpClientFactory>()
                .CreateClient("academicRecords")));
        services.AddScoped(serviceProvider => new CampusHousingApiClient(
            serviceProvider.GetRequiredService<IHttpClientFactory>()
                .CreateClient("campusHousing")));

        services.AddTransient<IMcpTool, StudentCountTool>();
        services.AddTransient<IMcpTool, StudentsBySubjectTool>();
        services.AddTransient<IMcpTool, ProjectFilesTool>();
        services.AddTransient<IMcpTool, CiReportTool>();
        services.AddTransient<IMcpTool, GetFeatureCapabilitiesTool>();
        services.AddTransient<IMcpTool, ValidateIntegrationTool>();
        services.AddTransient<IMcpTool, CalculateStudentAcademicStatisticsTool>();
        services.AddTransient<IMcpTool, GetStudentEnrollmentsTool>();
        services.AddTransient<IMcpTool, ListCoursesTool>();
        services.AddTransient<IMcpTool, GetCourseDetailsTool>();
        services.AddTransient<IMcpTool, GetCoursePrerequisitesTool>();
        services.AddTransient<IMcpTool, GetFacultyAssignmentsTool>();
        services.AddTransient<IMcpTool, CheckPrerequisiteEligibilityTool>();
        services.AddTransient<IMcpTool, RoomStatusTool>();
        services.AddTransient<IMcpTool, SearchRelatedTicketsTool>();

        return services;
    }
}
