using CampusPulse.Shared.Components.Services;
using EventScheduling.Frontend.Models;

namespace EventScheduling.Frontend.Services;

public sealed class CurrentUser(AccountService account)
{
    public UserProfile? Profile { get; private set; }
    public bool IsInitialized { get; private set; }

    public int UserId => Profile?.UserId ?? 0;

    public bool IsStaff => string.Equals(Profile?.Role, "Staff", StringComparison.OrdinalIgnoreCase);

    private Task<UserProfile?>? loadTask;

    public Task<UserProfile?> LoadAsync() => loadTask ??= InitializeAsync();

    private async Task<UserProfile?> InitializeAsync()
    {
        try
        {
            await account.InitializeAsync();
            if (account.CurrentUser is { } signedInUser
                && (AccountService.IsStudent(signedInUser)
                    || AccountService.IsStaff(signedInUser)))
            {
                Profile = new UserProfile(
                    signedInUser.UserId,
                    signedInUser.FirstName,
                    signedInUser.LastName,
                    signedInUser.Email,
                    signedInUser.Role,
                    signedInUser.StudentId,
                    signedInUser.StaffId);
            }

            return Profile;
        }
        finally
        {
            IsInitialized = true;
        }
    }
}
