namespace SchoolLms.Domain;

// ===========================================================================
//  Ishga qabul (client, 2026-10-07): "HR bo'limiga 1) vakansiya qoldirgan xodimlar
//  2) ishga olinish jarayonidagilar bosqichli holatda (Ma'lumot/CV -> Test -> Demo -> HR suhbat ->
//  Direktor suhbat) 3) Ishga olingan va adaptatsiya (on boarding) holatiga o'tganlar (adaptatsiya
//  30 kun davom etadi va 11 bosqichdan iborat)".
//
//  One applicant row walks: new (left an application) → the five hiring stages → onboarding
//  (30 days, a checklist copied from the 11 configurable steps) → adapted. Rejected can happen at
//  any point and records where. A CV is personal data: it lives under uploads/private, which is
//  never served statically — only through the gated admin endpoint.
//
//  Nothing here messages staff: the HR bot cut-over is on hold (old WunderkindHR bot still live).
// ===========================================================================

public static class VacancyStatus
{
    public const string Open = "open";
    public const string Closed = "closed";
}

public class Vacancy
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string? Department { get; set; }
    public string? Description { get; set; }
    public string? Requirements { get; set; }
    /// <summary>The public apply link: <c>/vakansiya/{slug}</c>.</summary>
    public string Slug { get; set; } = string.Empty;
    public string Status { get; set; } = VacancyStatus.Open;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
}

public static class ApplicantStage
{
    /// <summary>Left an application — not yet taken into the process (section 1).</summary>
    public const string New = "new";
    public const string Cv = "cv";
    public const string Test = "test";
    public const string Demo = "demo";
    public const string HrInterview = "hr_interview";
    public const string DirectorInterview = "director_interview";
    /// <summary>Hired, in the 30-day adaptation (section 3).</summary>
    public const string Onboarding = "onboarding";
    /// <summary>Passed the adaptation.</summary>
    public const string Adapted = "adapted";
    public const string Rejected = "rejected";

    /// <summary>The hiring stages, in order (section 2).</summary>
    public static readonly string[] Pipeline = [Cv, Test, Demo, HrInterview, DirectorInterview];
    public static readonly string[] All = [New, .. Pipeline, Onboarding, Adapted, Rejected];
    public static bool IsPipeline(string? s) => Array.IndexOf(Pipeline, s) >= 0;

    public static string Label(string s) => s switch
    {
        New => "Ariza",
        Cv => "Ma'lumot / CV",
        Test => "Test",
        Demo => "Demo dars",
        HrInterview => "HR suhbat",
        DirectorInterview => "Direktor suhbati",
        Onboarding => "Adaptatsiya",
        Adapted => "Ishga qabul qilingan",
        Rejected => "Rad etilgan",
        _ => s,
    };
}

public class JobApplicant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? VacancyId { get; set; }
    public string FullName { get; set; } = string.Empty;
    /// <summary>Canonical <c>+998901234567</c>.</summary>
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    /// <summary>The wanted position when there is no vacancy (manual add).</summary>
    public string? Position { get; set; }
    public string? Experience { get; set; }
    public string? About { get; set; }
    /// <summary>Path under <c>uploads/</c>: <c>private/recruitment/...</c>.</summary>
    public string? CvPath { get; set; }
    public string? CvName { get; set; }
    /// <summary>"public" (the apply form) or "manual".</summary>
    public string Source { get; set; } = "manual";
    public string Stage { get; set; } = ApplicantStage.New;
    public DateTimeOffset StageChangedAt { get; set; }
    public string? RejectReason { get; set; }
    public string? RejectedAtStage { get; set; }
    public DateOnly? HiredOn { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>What a stage showed — a test score, a demo lesson mark, an interview note. One per stage.</summary>
public class ApplicantStageResult
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ApplicantId { get; set; }
    public string Stage { get; set; } = string.Empty;
    /// <summary>0–100, optional.</summary>
    public int? Score { get; set; }
    public string? Comment { get; set; }
    public string By { get; set; } = string.Empty;
    public DateTimeOffset At { get; set; }
}

public class ApplicantEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ApplicantId { get; set; }
    public string? FromStage { get; set; }
    public string ToStage { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string By { get; set; } = string.Empty;
    public DateTimeOffset At { get; set; }
}

/// <summary>One of the adaptation steps every new hire goes through (11 to start with, editable).</summary>
public class OnboardingStepTemplate
{
    public const int Days = 30;
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Position { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>Day of the adaptation (1–30) the step is due.</summary>
    public int DueDay { get; set; } = 1;
    public bool IsActive { get; set; } = true;
}

/// <summary>A hired applicant's copy of a step — templates may change later, the plan does not.</summary>
public class ApplicantOnboardingStep
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ApplicantId { get; set; }
    public int Position { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateOnly DueOn { get; set; }
    public DateTimeOffset? DoneAt { get; set; }
    public string? DoneBy { get; set; }
    public string? Comment { get; set; }
}
