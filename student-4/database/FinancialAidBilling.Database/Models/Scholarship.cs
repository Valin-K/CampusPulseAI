namespace FinancialAidBilling.Database.Models;

public sealed class Scholarship
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal AwardAmount { get; set; }
    public decimal MinimumGpa { get; set; }
    public string EligibleMajor { get; set; } = string.Empty;
    public int MinimumYearLevel { get; set; }
    public DateOnly ApplicationDeadline { get; set; }
    public bool IsActive { get; set; }

    public void UpdateFrom(ScholarshipRequest request)
    {
        Name = request.Name.Trim();
        Description = request.Description.Trim();
        AwardAmount = request.AwardAmount;
        MinimumGpa = request.MinimumGpa;
        EligibleMajor = request.EligibleMajor.Trim();
        MinimumYearLevel = request.MinimumYearLevel;
        ApplicationDeadline = request.ApplicationDeadline;
        IsActive = request.IsActive;
    }

    public ScholarshipResponse ToResponse() => new(
        Id,
        Name,
        Description,
        AwardAmount,
        MinimumGpa,
        EligibleMajor,
        MinimumYearLevel,
        ApplicationDeadline,
        IsActive);
}

public sealed record ScholarshipRequest(
    string Name,
    string Description,
    decimal AwardAmount,
    decimal MinimumGpa,
    string EligibleMajor,
    int MinimumYearLevel,
    DateOnly ApplicationDeadline,
    bool IsActive)
{
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 120)
        {
            return "Name is required and must be 120 characters or fewer.";
        }

        if (string.IsNullOrWhiteSpace(Description) || Description.Trim().Length > 500)
        {
            return "Description is required and must be 500 characters or fewer.";
        }

        if (AwardAmount <= 0 || MinimumGpa is < 0 or > 4 || MinimumYearLevel is < 1 or > 8)
        {
            return "Award amount, GPA, or year-level eligibility is invalid.";
        }

        if (string.IsNullOrWhiteSpace(EligibleMajor) || EligibleMajor.Trim().Length > 100)
        {
            return "Eligible major is required and must be 100 characters or fewer.";
        }

        return null;
    }

    public Scholarship ToScholarship()
    {
        var scholarship = new Scholarship();
        scholarship.UpdateFrom(this);
        return scholarship;
    }
}

public sealed record ScholarshipResponse(
    int Id,
    string Name,
    string Description,
    decimal AwardAmount,
    decimal MinimumGpa,
    string EligibleMajor,
    int MinimumYearLevel,
    DateOnly ApplicationDeadline,
    bool IsActive);