using EventScheduling.Backend.Models;

namespace EventScheduling.Backend.Services;

public interface IBookingAssistantService
{
    Task<AssistantChatResponse> ChatAsync(AssistantChatRequest request, CancellationToken cancellationToken);
}
