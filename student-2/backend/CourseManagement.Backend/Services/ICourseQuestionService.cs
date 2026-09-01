using CourseManagement.Backend.Contracts.Ai;

namespace CourseManagement.Backend.Services;

public interface ICourseQuestionService
{
    Task<CourseQuestionResponse> AskAsync(
        CourseQuestionRequest request,
        CancellationToken cancellationToken);
}
