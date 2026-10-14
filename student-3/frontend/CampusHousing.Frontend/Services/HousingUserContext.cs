using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Net.Http.Json;

namespace CampusHousing.Frontend.Services;

public sealed record HousingUser(int UserId, string DisplayName, string StudentId, bool IsAdministrator);

public sealed class HousingUserContext(IJSRuntime js, NavigationManager navigation, HttpClient http)
{
    private const string StorageKey = "campuspulse.campus-housing.selected-user-id";

    public IReadOnlyList<HousingUser> Users { get; private set; } = [];

    public HousingUser? CurrentUser { get; private set; }

    public bool IsInitialized { get; private set; }

    public bool IsAvailable => CurrentUser is not null;

    public bool IsAdministrator => CurrentUser?.IsAdministrator == true;

    public string? ErrorMessage { get; private set; }

    public async Task InitializeAsync(bool forceRefresh = false)
    {
        if (IsInitialized && !forceRefresh)
        {
            return;
        }

        IsInitialized = false;
        CurrentUser = null;
        ErrorMessage = null;

        try
        {
            var directory = await http.GetFromJsonAsync<List<HousingDirectoryUser>>("api/housing/users");
            Users = (directory ?? [])
                .Where(user => string.Equals(user.Role, "Student", StringComparison.OrdinalIgnoreCase)
                    && user.UserId > 0 && !string.IsNullOrWhiteSpace(user.StudentId))
                .Select(user => new HousingUser(user.UserId, $"{user.FirstName} {user.LastName}", user.StudentId!, false))
                .Append(new HousingUser(0, "Housing Administrator", "", true))
                .ToList();

            var savedUserId = await js.InvokeAsync<string?>("campusPulseHousingUser.getSelectedUserId", StorageKey);
            var selectedUser = int.TryParse(savedUserId, out var userId)
                ? Users.FirstOrDefault(user => user.UserId == userId)
                : null;
            CurrentUser = selectedUser ?? Users.FirstOrDefault(user => !user.IsAdministrator);

            if (selectedUser is null && savedUserId is not null)
            {
                await js.InvokeVoidAsync("campusPulseHousingUser.removeSelectedUserId", StorageKey);
            }

        }
        catch (JSException)
        {
            ErrorMessage = "The selected Housing user could not be read from this browser.";
        }
        catch (HttpRequestException exception)
        {
            ErrorMessage = $"Housing users could not be loaded: {exception.Message}";
        }
        finally
        {
            IsInitialized = true;
        }
    }

    public async Task SelectUserAsync(int userId)
    {
        var user = Users.FirstOrDefault(candidate => candidate.UserId == userId)
            ?? throw new InvalidOperationException("The selected housing user is not available.");
        await js.InvokeVoidAsync("campusPulseHousingUser.setSelectedUserId", StorageKey, user.UserId);
        navigation.NavigateTo(navigation.Uri, forceLoad: true);
    }
}

file sealed record HousingDirectoryUser(int UserId, string FirstName, string LastName, string Email, string Role, string? StudentId, string? StaffId);

public sealed class HousingRoleHandler(HousingUserContext userContext) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Remove("X-Housing-Role");
        request.Headers.Remove("X-Housing-User");
        request.Headers.TryAddWithoutValidation("X-Housing-Role", userContext.IsAdministrator ? "Administrator" : "Student");
        request.Headers.TryAddWithoutValidation("X-Housing-User", userContext.CurrentUser?.StudentId ?? "");
        return base.SendAsync(request, cancellationToken);
    }
}
