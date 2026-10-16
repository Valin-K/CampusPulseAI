using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using CampusPulse.Shared.Components.Models;
using Microsoft.AspNetCore.Components.Authorization;

namespace CampusPulse.Shared.Components.Services;

public sealed class AccountService : IDisposable
{
    private readonly HttpClient http;
    private readonly Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider authenticationStateProvider;

    public AccountService(HttpClient http, Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider authenticationStateProvider)
    {
        this.http = http;
        this.authenticationStateProvider = authenticationStateProvider;
        authenticationStateProvider.AuthenticationStateChanged += AuthenticationStateChanged;
    }

    public IReadOnlyList<CampusAccountUser> Users { get; private set; } = [];
    public CampusAccountUser? CurrentUser { get; private set; }
    public string? AuthenticatedEmail { get; private set; }
    public bool IsAuthenticated { get; private set; }
    public bool CanRegisterAccount { get; private set; }
    public bool IsInitialized { get; private set; }
    public string? ErrorMessage { get; private set; }

    public event Action? Changed;

    public async Task InitializeAsync(bool forceRefresh = false)
    {
        if (IsInitialized && !forceRefresh)
        {
            return;
        }

        IsInitialized = false;
        ErrorMessage = null;
        CurrentUser = null;
        Users = [];
        CanRegisterAccount = false;

        try
        {
            var principal = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
            IsAuthenticated = principal.Identity?.IsAuthenticated == true;
            AuthenticatedEmail = IsAuthenticated ? GetEmail(principal) : null;

            if (!IsAuthenticated)
            {
                return;
            }

            if (authenticationStateProvider is AuthenticationStateProvider demoAuthentication
                && demoAuthentication.CurrentSession is { } session)
            {
                ValidateUsers([session.User]);
                AuthenticatedEmail = session.User.Email;
                CurrentUser = session.User;
                Users = [session.User];
                return;
            }

            if (string.IsNullOrWhiteSpace(AuthenticatedEmail))
            {
                ErrorMessage = "The identity provider did not include an email address for this account.";
                return;
            }

            var directoryUsers = await http.GetFromJsonAsync<List<CampusAccountUser>>("api/users") ?? [];
            ValidateUsers(directoryUsers);
            var matches = directoryUsers
                .Where(user => string.Equals(user.Email, AuthenticatedEmail, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (matches.Length == 1)
            {
                var matchedUser = matches[0];
                CurrentUser = matchedUser;
                Users = [matchedUser];
            }
            else if (matches.Length == 0)
            {
                CanRegisterAccount = true;
                ErrorMessage = "This sign-in is not linked to a CampusPulse account. Create an account for the selected role or contact an administrator.";
            }
            else
            {
                ErrorMessage = "More than one CampusPulse account is linked to this email address. Contact an administrator.";
            }
        }
        catch (HttpRequestException exception)
        {
            ErrorMessage = $"The Academic Records directory is unavailable: {exception.Message}";
        }
        catch (JsonException)
        {
            ErrorMessage = "The Academic Records directory returned invalid data.";
        }
        finally
        {
            IsInitialized = true;
            Changed?.Invoke();
        }
    }

    public async Task<CampusCreatedUser> CreateStudentAsync(CampusStudentRegistration registration)
    {
        using var response = await http.PostAsJsonAsync("api/students/enroll", registration);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<CampusCreatedUser>()
            ?? throw new JsonException("The Academic Records API did not return the created student account.");
    }

    public async Task<CampusCreatedStaff> CreateStaffAsync(CampusStaffRegistration registration)
    {
        using var response = await http.PostAsJsonAsync("api/staff/enroll", registration);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<CampusCreatedStaff>()
            ?? throw new JsonException("The Academic Records API did not return the created staff account.");
    }

    public async Task<CampusUserProfile> GetCurrentProfileAsync()
    {
        var user = CurrentUser ?? throw new InvalidOperationException("Sign in before viewing a profile.");
        if (IsStudent(user))
        {
            var student = await http.GetFromJsonAsync<StudentProfileResponse>($"api/students/by-user/{user.UserId}")
                ?? throw new JsonException("The Academic Records API returned an empty student profile.");
            return new CampusUserProfile
            {
                UserId = student.UserId,
                StudentId = student.StudentId,
                FirstName = student.FirstName,
                LastName = student.LastName,
                Email = student.Email,
                Role = "Student",
                DateOfBirth = student.DateOfBirth,
                PhoneNumber = student.PhoneNumber,
                MailingAddress = student.MailingAddress,
                EmergencyContactName = student.EmergencyContactName,
                EmergencyContactPhone = student.EmergencyContactPhone,
                EnrollmentDate = student.EnrollmentDate,
                Degree = student.Degree,
                Major = student.Major,
                SubMajor = student.SubMajor
            };
        }

        var staff = await http.GetFromJsonAsync<StaffProfileResponse>($"api/staff/by-user/{user.UserId}")
            ?? throw new JsonException("The Academic Records API returned an empty staff profile.");
        return new CampusUserProfile
        {
            UserId = staff.UserId,
            StaffId = staff.StaffId,
            FirstName = staff.FirstName,
            LastName = staff.LastName,
            Email = staff.Email,
            Role = "Staff",
            DateOfBirth = staff.DateOfBirth,
            PhoneNumber = staff.PhoneNumber
        };
    }

    public async Task SaveCurrentProfileAsync(CampusUserProfile profile)
    {
        var user = CurrentUser ?? throw new InvalidOperationException("Sign in before editing a profile.");
        EnsureAuthenticatedEmail(profile.Email);
        if (profile.UserId != user.UserId)
        {
            throw new InvalidOperationException("Only the signed-in account can be updated.");
        }

        HttpResponseMessage response;
        if (IsStudent(user) && !string.IsNullOrWhiteSpace(profile.StudentId))
        {
            var request = new CampusUpdateStudentProfile(
                profile.FirstName,
                profile.LastName,
                profile.Email,
                profile.DateOfBirth,
                profile.MailingAddress ?? string.Empty,
                profile.EmergencyContactName ?? string.Empty,
                profile.EmergencyContactPhone ?? string.Empty,
                profile.PhoneNumber,
                profile.Degree,
                profile.Major,
                profile.SubMajor);
            response = await http.PutAsJsonAsync($"api/students/{profile.StudentId}", request);
        }
        else if (IsStaff(user) && !string.IsNullOrWhiteSpace(profile.StaffId))
        {
            var request = new CampusUpdateStaffProfile(
                profile.FirstName,
                profile.LastName,
                profile.Email,
                profile.DateOfBirth,
                profile.PhoneNumber);
            response = await http.PutAsJsonAsync($"api/staff/{profile.StaffId}", request);
        }
        else
        {
            throw new InvalidOperationException("The signed-in account profile is incomplete or has an unknown role.");
        }

        using (response)
        {
            await EnsureSuccessAsync(response);
        }

        CurrentUser = user with
        {
            FirstName = profile.FirstName,
            LastName = profile.LastName,
            Email = profile.Email
        };
        Users = Users.Select(candidate => candidate.UserId == user.UserId ? CurrentUser : candidate).ToArray();
        Changed?.Invoke();
    }

    private static string? GetEmail(ClaimsPrincipal principal) =>
        principal.FindFirst(ClaimTypes.Email)?.Value
        ?? principal.FindFirst("email")?.Value
        ?? principal.FindFirst("preferred_username")?.Value
        ?? principal.FindFirst("upn")?.Value;

    private void EnsureAuthenticatedEmail(string email)
    {
        if (!IsAuthenticated
            || string.IsNullOrWhiteSpace(AuthenticatedEmail)
            || !string.Equals(email, AuthenticatedEmail, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The account email must match the authenticated identity.");
        }
    }

    private void AuthenticationStateChanged(Task<AuthenticationState> authenticationState) =>
        _ = RefreshAuthenticationStateAsync(authenticationState);

    private async Task RefreshAuthenticationStateAsync(Task<AuthenticationState> authenticationState)
    {
        try
        {
            await authenticationState;
            await InitializeAsync(forceRefresh: true);
        }
        catch (HttpRequestException)
        {
            CurrentUser = null;
            Users = [];
            IsAuthenticated = false;
            IsInitialized = true;
            Changed?.Invoke();
        }
    }

    public void Dispose() =>
        authenticationStateProvider.AuthenticationStateChanged -= AuthenticationStateChanged;

    public async Task RefreshUsersAsync()
    {
        IsInitialized = false;
        await InitializeAsync(forceRefresh: true);
    }

    public static bool IsStudent(CampusAccountUser user) =>
        string.Equals(user.Role, "Student", StringComparison.OrdinalIgnoreCase);

    public static bool IsStaff(CampusAccountUser user) =>
        string.Equals(user.Role, "Staff", StringComparison.OrdinalIgnoreCase);

    private static void ValidateUsers(IReadOnlyList<CampusAccountUser> users)
    {
        if (users.Any(user => user.UserId <= 0
            || string.IsNullOrWhiteSpace(user.FirstName)
            || string.IsNullOrWhiteSpace(user.LastName)
            || string.IsNullOrWhiteSpace(user.Email)
            || (!IsStudent(user) && !IsStaff(user)))
            || users.Select(user => user.UserId).Distinct().Count() != users.Count)
        {
            throw new JsonException("The Academic Records directory contains invalid accounts.");
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = await response.Content.ReadAsStringAsync();
        throw new HttpRequestException(
            string.IsNullOrWhiteSpace(detail) ? response.ReasonPhrase : detail,
            null,
            response.StatusCode);
    }

    private sealed record StudentProfileResponse(
        int UserId,
        string StudentId,
        string FirstName,
        string LastName,
        string Email,
        DateOnly? DateOfBirth,
        string? PhoneNumber,
        string MailingAddress,
        string EmergencyContactName,
        string EmergencyContactPhone,
        DateOnly? EnrollmentDate,
        string? Degree,
        string? Major,
        string? SubMajor);

    private sealed record StaffProfileResponse(
        int UserId,
        string StaffId,
        string FirstName,
        string LastName,
        string Email,
        DateOnly? DateOfBirth,
        string? PhoneNumber);
}
