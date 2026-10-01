using System.Net.Http.Json;
using System.Text.Json;
using CampusPulse.Shared.Components.Models;
using Microsoft.JSInterop;

namespace CampusPulse.Shared.Components.Services;

public sealed class CampusAccountService(HttpClient http, IJSRuntime javaScript)
{
    public const string SessionStorageKey = "campuspulse.account.user-id";

    public IReadOnlyList<CampusAccountUser> Users { get; private set; } = [];
    public CampusAccountUser? CurrentUser { get; private set; }
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

        try
        {
            Users = await http.GetFromJsonAsync<List<CampusAccountUser>>("api/users") ?? [];
            ValidateUsers(Users);
        }
        catch (HttpRequestException exception)
        {
            Users = [];
            ErrorMessage = $"The Academic Records directory is unavailable: {exception.Message}";
            IsInitialized = true;
            Changed?.Invoke();
            return;
        }
        catch (JsonException)
        {
            Users = [];
            ErrorMessage = "The Academic Records directory returned invalid data.";
            IsInitialized = true;
            Changed?.Invoke();
            return;
        }

        try
        {
            var storedId = await javaScript.InvokeAsync<string?>("campusPulseAccountSession.getUserId");
            if (int.TryParse(storedId, out var userId))
            {
                CurrentUser = Users.SingleOrDefault(user => user.UserId == userId);
            }

            if (CurrentUser is null && storedId is not null)
            {
                await javaScript.InvokeVoidAsync("campusPulseAccountSession.removeUserId");
            }
        }
        catch (JSException)
        {
            ErrorMessage = "Browser session storage is unavailable. Sign-in cannot be saved in this tab.";
        }
        finally
        {
            IsInitialized = true;
            Changed?.Invoke();
        }
    }

    public async Task SignInAsync(int userId)
    {
        var user = Users.SingleOrDefault(candidate => candidate.UserId == userId)
            ?? throw new InvalidOperationException("The selected account is not in the Academic Records directory.");

        await javaScript.InvokeVoidAsync(
            "campusPulseAccountSession.setUserId",
            user.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        CurrentUser = user;
        ErrorMessage = null;
        Changed?.Invoke();
    }

    public async Task SignOutAsync()
    {
        await javaScript.InvokeVoidAsync("campusPulseAccountSession.removeUserId");
        CurrentUser = null;
        Changed?.Invoke();
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
