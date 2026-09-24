using CampusPulse.Shared.Rag.Generation;

namespace CampusPulse.Shared.Rag.Client;

public interface IMcpServerClient
{
    Task ValidateIntegrationAsync(RagFeature feature, CancellationToken cancellationToken);
}
