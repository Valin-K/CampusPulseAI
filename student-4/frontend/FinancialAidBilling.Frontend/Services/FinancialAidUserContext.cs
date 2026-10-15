using System.Text.Json;
using CampusPulse.Shared.Components.Services;
using FinancialAidBilling.Frontend.Models;

namespace FinancialAidBilling.Frontend.Services;

public sealed class FinancialAidUserContext(CampusAccountService account)
{
    public StudentProfile? CurrentUser { get; private set; }
    public bool IsInitialized { get; private set; }
    public bool IsAvailable => CurrentUser is not null;
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
                    ?? "Sign in with a student account to access Financial Aid & Billing.";
                return;
            }

            if (!CampusAccountService.IsStudent(signedInUser))
            {
                ErrorMessage = "Financial Aid & Billing is available to student accounts.";
                return;
            }

            var profile = await account.GetCurrentProfileAsync();
            if (profile.UserId != signedInUser.UserId
                || string.IsNullOrWhiteSpace(profile.StudentId)
                || string.IsNullOrWhiteSpace(profile.FirstName)
                || string.IsNullOrWhiteSpace(profile.LastName))
            {
                throw new JsonException("The signed-in student profile is incomplete or mismatched.");
            }

            CurrentUser = new StudentProfile(
                profile.UserId,
                profile.StudentId,
                profile.FirstName,
                profile.LastName,
                profile.Email,
                profile.EnrollmentDate,
                profile.Degree,
                profile.Major,
                profile.SubMajor);
        }
        catch (HttpRequestException exception)
        {
            SetUnavailable($"Student information is unavailable: {exception.Message}");
        }
        catch (JsonException)
        {
            SetUnavailable("The signed-in student profile was returned in an unexpected format.");
        }
        catch (InvalidOperationException exception)
        {
            SetUnavailable(exception.Message);
        }
        finally
        {
            IsInitialized = true;
        }
    }

    private void SetUnavailable(string message)
    {
        CurrentUser = null;
        ErrorMessage = message;
    }
}
