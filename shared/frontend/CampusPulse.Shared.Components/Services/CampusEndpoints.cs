using System.Net.Http.Json;

namespace CampusPulse.Shared.Components.Services;

public sealed class CampusEndpoints
{
    public bool UseSameOriginApi { get; set; }
    public Dictionary<string, string> Urls { get; set; } = new();
    public string[] AdditionalOrigins { get; set; } = [];

    private readonly HashSet<string> allowedOrigins = new(StringComparer.OrdinalIgnoreCase);

    public string Portal => Urls["Portal"];
    public string AcademicRecords => Urls["AcademicRecords"];
    public string CourseManagement => Urls["CourseManagement"];
    public string FinancialAidBilling => Urls["FinancialAidBilling"];
    public string EventScheduling => Urls["EventScheduling"];

    public static async Task<CampusEndpoints> LoadAsync(string baseAddress)
    {
        using var client = new HttpClient { BaseAddress = new Uri(baseAddress) };
        return await LoadAsync(client);
    }

    public static async Task<CampusEndpoints> LoadAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "campus-config.json");
        request.Headers.CacheControl = new() { NoCache = true };
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var endpoints = await response.Content.ReadFromJsonAsync<CampusEndpoints>()
            ?? throw new InvalidOperationException("Campus endpoint configuration is missing.");
        foreach (var name in new[] { "Portal", "AcademicRecords", "CourseManagement", "CampusHousing", "FinancialAidBilling", "EventScheduling" })
        {
            if (!endpoints.Urls.TryGetValue(name, out var address)
                || !Uri.TryCreate(address, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https")
                || !string.IsNullOrEmpty(uri.UserInfo))
            {
                throw new InvalidOperationException($"Invalid Campus endpoint: {name}.");
            }

            endpoints.allowedOrigins.Add(uri.GetLeftPart(UriPartial.Authority));
        }

        endpoints.allowedOrigins.Add((client.BaseAddress
            ?? throw new InvalidOperationException("The Campus configuration client requires a base address."))
            .GetLeftPart(UriPartial.Authority));
        foreach (var origin in endpoints.AdditionalOrigins)
        {
            endpoints.allowedOrigins.Add(new Uri(origin).GetLeftPart(UriPartial.Authority));
        }

        return endpoints;
    }

    public string AccountUrl(string path, string? returnUrl = null)
    {
        var address = new Uri(new Uri(AcademicRecords.TrimEnd('/') + "/"), path.TrimStart('/')).ToString();
        return returnUrl is null ? address : $"{address}?returnUrl={Uri.EscapeDataString(returnUrl)}";
    }

    public bool IsAllowedReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl)) return false;
        if (returnUrl.StartsWith('/'))
        {
            return !returnUrl.StartsWith("//", StringComparison.Ordinal)
                && !returnUrl.Contains('\\');
        }

        return Uri.TryCreate(returnUrl, UriKind.Absolute, out var destination)
            && destination.Scheme is "http" or "https"
            && string.IsNullOrEmpty(destination.UserInfo)
            && allowedOrigins.Contains(destination.GetLeftPart(UriPartial.Authority));
    }
}