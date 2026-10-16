using CampusPulse.Shared.Components.Models;
using CampusPulse.Shared.Components.Services;
using Microsoft.JSInterop;

namespace CampusHousing.Frontend.Services;

public sealed class HousingUserContext(AccountService account)
{
    public CampusAccountUser? CurrentUser { get; private set; }

    public bool IsInitialized { get; private set; }

    public bool IsAvailable => CurrentUser is not null;

    public bool IsAdministrator =>
        CurrentUser is not null && AccountService.IsStaff(CurrentUser);

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
            await account.InitializeAsync(forceRefresh);
            var signedInUser = account.CurrentUser;
            if (signedInUser is null)
            {
                ErrorMessage = account.ErrorMessage
                    ?? "Sign in with a student or staff account to access Campus Housing.";
                return;
            }

            if (!AccountService.IsStudent(signedInUser)
                && !AccountService.IsStaff(signedInUser))
            {
                ErrorMessage = "This CampusPulse account does not have Campus Housing access.";
                return;
            }

            CurrentUser = signedInUser;
        }
        catch (JSException)
        {
            ErrorMessage = "The CampusPulse sign-in session could not be read from this browser.";
        }
        finally
        {
            IsInitialized = true;
        }
    }
}

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
