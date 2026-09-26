namespace CourseManagement.Backend.Infrastructure;

public sealed class DatabaseApiProxy(IHttpClientFactory clients)
{
    public const string ClientName = "courseManagementDatabaseProxy";

    public async Task ForwardAsync(
        HttpContext context,
        string databasePath,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            new HttpMethod(context.Request.Method),
            databasePath + context.Request.QueryString);

        if (context.Request.ContentLength > 0
            || context.Request.Headers.ContainsKey("Transfer-Encoding"))
        {
            request.Content = new StreamContent(context.Request.Body);
        }

        foreach (var header in context.Request.Headers)
        {
            if (string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!request.Headers.TryAddWithoutValidation(
                    header.Key,
                    header.Value.ToArray())
                && request.Content is not null)
            {
                request.Content.Headers.TryAddWithoutValidation(
                    header.Key,
                    header.Value.ToArray());
            }
        }

        HttpResponseMessage response;
        try
        {
            response = await clients
                .CreateClient(ClientName)
                .SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
        }
        catch (HttpRequestException)
        {
            await WriteUnavailableProblemAsync(context, cancellationToken);
            return;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await WriteUnavailableProblemAsync(context, cancellationToken);
            return;
        }

        using (response)
        {
            context.Response.StatusCode = (int)response.StatusCode;
            foreach (var header in response.Headers)
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }

            foreach (var header in response.Content.Headers)
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }

            context.Response.Headers.Remove("transfer-encoding");
            await response.Content.CopyToAsync(
                context.Response.Body,
                cancellationToken);
        }
    }

    static async Task WriteUnavailableProblemAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(
            new
            {
                status = StatusCodes.Status503ServiceUnavailable,
                title = "Course Management Database unavailable",
                detail = "The Course request could not be completed because the Course database service is unavailable."
            },
            cancellationToken);
    }
}
