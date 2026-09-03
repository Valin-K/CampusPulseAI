using System.Text.Json;
using CourseManagement.Frontend.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace CourseManagement.Frontend.Services;

public sealed class CourseUserContext(
    CourseApiClient courseApi,
    IJSRuntime javaScript,
    NavigationManager navigation)
{
    const string SelectedUserStorageKey =
        "campuspulse.course-management.selected-user-id";

    public IReadOnlyList<CourseAccessUser> Users { get; private set; } = [];
    public CourseAccessUser? CurrentUser { get; private set; }
    public bool IsInitialized { get; private set; }
    public bool IsAvailable => CurrentUser is not null;
    public bool IsCourseAdministrator =>
        string.Equals(
            CurrentUser?.CourseRole,
            CourseAccessRoles.CourseAdministrator,
            StringComparison.Ordinal);
    public string? ErrorMessage { get; private set; }

    public async Task InitializeAsync(bool forceRefresh = false)
    {
        if (IsInitialized && !forceRefresh)
        {
            return;
        }

        IsInitialized = false;
        Users = [];
        CurrentUser = null;
        ErrorMessage = null;

        try
        {
            var users = await courseApi.GetCourseAccessUsersAsync();
            ValidateUsers(users);

            var student = users.Single(user =>
                string.Equals(
                    user.CourseRole,
                    CourseAccessRoles.Student,
                    StringComparison.Ordinal));
            var storedValue = await javaScript.InvokeAsync<string?>(
                "campusPulseCourseUser.getSelectedUserId",
                SelectedUserStorageKey);
            var selectedUser = int.TryParse(storedValue, out var storedUserId)
                ? users.SingleOrDefault(user => user.UserId == storedUserId)
                : null;

            Users = users;
            CurrentUser = selectedUser ?? student;

            if (selectedUser is null && storedValue is not null)
            {
                await javaScript.InvokeVoidAsync(
                    "campusPulseCourseUser.removeSelectedUserId",
                    SelectedUserStorageKey);
            }
        }
        catch (CourseApiException)
        {
            SetUnavailable(
                "Course access could not be verified. Check that Academic Records and Course Management are available.");
        }
        catch (HttpRequestException)
        {
            SetUnavailable(
                "Course access could not be verified. Check that the Course Management API is running.");
        }
        catch (JsonException)
        {
            SetUnavailable("Course access data was returned in an unexpected format.");
        }
        catch (InvalidOperationException)
        {
            SetUnavailable("Course access data did not contain the required Student and Staff users.");
        }
        catch (JSException)
        {
            SetUnavailable("The selected Course user could not be read from this browser.");
        }
        finally
        {
            IsInitialized = true;
        }
    }

    public async Task SelectUserAsync(int userId)
    {
        var selectedUser = Users.SingleOrDefault(user => user.UserId == userId)
            ?? throw new InvalidOperationException("The selected Course user is unavailable.");

        await javaScript.InvokeVoidAsync(
            "campusPulseCourseUser.setSelectedUserId",
            SelectedUserStorageKey,
            selectedUser.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        navigation.NavigateTo(navigation.Uri, forceLoad: true);
    }

    static void ValidateUsers(IReadOnlyList<CourseAccessUser> users)
    {
        if (users.Count != 2
            || users.Any(user =>
                user.UserId <= 0
                || string.IsNullOrWhiteSpace(user.ExternalId)
                || string.IsNullOrWhiteSpace(user.DisplayName)
                || string.IsNullOrWhiteSpace(user.Email))
            || users.Count(user =>
                string.Equals(
                    user.CourseRole,
                    CourseAccessRoles.Student,
                    StringComparison.Ordinal)) != 1
            || users.Count(user =>
                string.Equals(
                    user.CourseRole,
                    CourseAccessRoles.CourseAdministrator,
                    StringComparison.Ordinal)) != 1
            || users.Select(user => user.UserId).Distinct().Count() != users.Count)
        {
            throw new InvalidOperationException(
                "Course access users are incomplete or invalid.");
        }
    }

    void SetUnavailable(string message)
    {
        Users = [];
        CurrentUser = null;
        ErrorMessage = message;
    }
}
