using System.Net.Http.Json;
using System.Text.Json;
using FinancialAidBilling.Frontend.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace FinancialAidBilling.Frontend.Services;

public sealed class FinancialAidUserContext(HttpClient http, IJSRuntime javaScript, NavigationManager navigation)
{
    private const string StorageKey = "campuspulse.financial-aid-billing.selected-user-id";

    public IReadOnlyList<StudentProfile> Users { get; private set; } = [];
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
        Users = [];
        CurrentUser = null;
        ErrorMessage = null;

        try
        {
            var users = await http.GetFromJsonAsync<List<StudentProfile>>("/api/students")
                ?? throw new JsonException("The student directory returned an empty response.");
            ValidateUsers(users);

            var storedValue = await javaScript.InvokeAsync<string?>("campusPulseFinancialAidUser.getSelectedUserId", StorageKey);
            var selectedUser = int.TryParse(storedValue, out var storedUserId)
                ? users.SingleOrDefault(user => user.UserId == storedUserId)
                : null;

            Users = users;
            CurrentUser = selectedUser ?? users[0];

            if (selectedUser is null && storedValue is not null)
            {
                await javaScript.InvokeVoidAsync("campusPulseFinancialAidUser.removeSelectedUserId", StorageKey);
            }
        }
        catch (HttpRequestException)
        {
            SetUnavailable("Student information is unavailable. Check that Academic Records and Financial Aid & Billing are running.");
        }
        catch (JsonException)
        {
            SetUnavailable("Student information was returned in an unexpected format.");
        }
        catch (JSException)
        {
            SetUnavailable("The selected Financial Aid user could not be read from this browser.");
        }
        finally
        {
            IsInitialized = true;
        }
    }

    public async Task SelectUserAsync(int userId)
    {
        var selectedUser = Users.SingleOrDefault(user => user.UserId == userId)
            ?? throw new InvalidOperationException("The selected student is unavailable.");

        await javaScript.InvokeVoidAsync("campusPulseFinancialAidUser.setSelectedUserId", StorageKey, selectedUser.UserId);
        navigation.NavigateTo(navigation.Uri, forceLoad: true);
    }

    private static void ValidateUsers(IReadOnlyList<StudentProfile> users)
    {
        if (users.Count == 0 || users.Any(user =>
            user.UserId <= 0 ||
            string.IsNullOrWhiteSpace(user.StudentId) ||
            string.IsNullOrWhiteSpace(user.FirstName) ||
            string.IsNullOrWhiteSpace(user.LastName)) ||
            users.Select(user => user.UserId).Distinct().Count() != users.Count)
        {
            throw new JsonException("Student information is incomplete or invalid.");
        }
    }

    private void SetUnavailable(string message)
    {
        Users = [];
        CurrentUser = null;
        ErrorMessage = message;
    }
}
