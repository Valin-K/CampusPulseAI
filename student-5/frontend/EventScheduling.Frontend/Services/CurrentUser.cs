using EventScheduling.Frontend.Models;

namespace EventScheduling.Frontend.Services;

// The signed-in student. There is no authentication yet, so this is fixed to
// Academic Records (student-1) UserId 4 - Ryan Takayasu / STU004. Bookings, the
// dashboard and the calendar are all scoped to this user.
public sealed class CurrentUser(EventSchedulingApiClient api)
{
    public const int UserId = 4;

    public UserProfile? Profile { get; private set; }

    private Task<UserProfile?>? loadTask;

    // Fetches the profile once and caches it for the lifetime of the session.
    public Task<UserProfile?> LoadAsync() => loadTask ??= FetchAsync();

    private async Task<UserProfile?> FetchAsync()
    {
        Profile = await api.GetUserAsync(UserId);
        return Profile;
    }
}
