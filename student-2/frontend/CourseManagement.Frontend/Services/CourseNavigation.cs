namespace CourseManagement.Frontend.Services;

public static class CourseNavigation
{
    public static string Dashboard(string tab) =>
        AddQueryParameter("/courses", "tab", tab);

    public static string WithReturnUrl(string destination, string returnUrl) =>
        AddQueryParameter(destination, "returnUrl", returnUrl);

    public static string AddQueryParameter(
        string destination,
        string name,
        string value)
    {
        var separator = destination.Contains('?')
            ? '&'
            : '?';
        return $"{destination}{separator}{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}";
    }

    public static string LocalReturnUrl(string? returnUrl, string fallback) =>
        IsLocalReturnUrl(returnUrl) ? returnUrl! : fallback;

    public static bool IsLocalReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl)
        && returnUrl.StartsWith("/", StringComparison.Ordinal)
        && !returnUrl.StartsWith("//", StringComparison.Ordinal)
        && !returnUrl.Contains('\r')
        && !returnUrl.Contains('\n')
        && Uri.TryCreate(returnUrl, UriKind.Relative, out _);
}
