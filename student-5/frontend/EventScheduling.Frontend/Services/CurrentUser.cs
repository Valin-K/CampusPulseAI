using System.Globalization;
using EventScheduling.Frontend.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace EventScheduling.Frontend.Services;

public sealed class CurrentUser(EventSchedulingApiClient api, IJSRuntime js, NavigationManager navigation)
{
    private const string StorageKey = "campuspulse.event-scheduling.selected-user-id";
    private const int DefaultUserId = 4;

    public IReadOnlyList<UserProfile> Users { get; private set; } = [];
    public UserProfile? Profile { get; private set; }
    public bool IsInitialized { get; private set; }

    public bool IsDegraded { get; private set; }

    public int UserId => Profile?.UserId ?? DefaultUserId;

    public bool IsStaff => string.Equals(Profile?.Role, "Staff", StringComparison.OrdinalIgnoreCase);

    private Task<UserProfile?>? loadTask;

    public Task<UserProfile?> LoadAsync() => loadTask ??= InitializeAsync();

    private async Task<UserProfile?> InitializeAsync()
    {
        Users = await api.GetUsersAsync();
        IsDegraded = Users.Count == 0;

        int? storedId = null;
        try
        {
            var raw = await js.InvokeAsync<string?>("campusPulseUser.get", StorageKey);
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                storedId = parsed;
            }
        }
        catch (JSException)
        {
            // storage not available - fall back to the default
        }

        Profile =
            (storedId is { } id ? Users.FirstOrDefault(user => user.UserId == id) : null)
            ?? Users.FirstOrDefault(user => user.UserId == DefaultUserId)
            ?? Users.FirstOrDefault();

        IsInitialized = true;
        return Profile;
    }

    public async Task SelectAsync(int userId)
    {
        try
        {
            await js.InvokeVoidAsync("campusPulseUser.set", StorageKey,
                userId.ToString(CultureInfo.InvariantCulture));
        }
        catch (JSException)
        {
            return;
        }

        navigation.NavigateTo(navigation.Uri, forceLoad: true);
    }
}
