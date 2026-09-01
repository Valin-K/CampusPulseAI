namespace EventScheduling.Backend.Services;

public sealed class DatabaseStartupRetryHandler(ILogger<DatabaseStartupRetryHandler> logger) : DelegatingHandler
{
    private const int MaxAttempts = 30;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            using var retryRequest = Clone(request);

            try
            {
                return await base.SendAsync(retryRequest, cancellationToken);
            }
            catch (HttpRequestException) when (attempt < MaxAttempts)
            {
                if (attempt == 1)
                {
                    logger.LogInformation("Event Scheduling database is starting; waiting to complete {Method} {Path}.", request.Method, request.RequestUri?.AbsolutePath);
                }

                await Task.Delay(RetryDelay, cancellationToken);
            }
        }

        throw new InvalidOperationException("Database request retry loop exited unexpectedly.");
    }

    private static HttpRequestMessage Clone(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy
        };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }
}
