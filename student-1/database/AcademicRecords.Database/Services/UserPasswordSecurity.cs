using AcademicRecords.Database.Models;
using Microsoft.AspNetCore.Identity;

namespace AcademicRecords.Database.Services;

public static class UserPasswordSecurity
{
    private const int MinimumPasswordLength = 12;
    private const int MaximumPasswordLength = 128;
    private static readonly PasswordHasher<User> Hasher = new();

    public static string HashPassword(User user, string password) => Hasher.HashPassword(user, password);

    public static bool VerifyPassword(User user, string password)
    {
        if (string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            return false;
        }

        return Hasher.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed;
    }

    public static bool IsValidNewPassword(string? password, string? confirmation) =>
        !string.IsNullOrWhiteSpace(password)
        && password.Length is >= MinimumPasswordLength and <= MaximumPasswordLength
        && string.Equals(password, confirmation, StringComparison.Ordinal);
}