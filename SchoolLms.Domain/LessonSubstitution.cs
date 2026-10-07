namespace SchoolLms.Domain;

// ===========================================================================
//  O'rindosh ustoz (client, 2026-10-07: "hodim dars o'tmagan holatda boshqa hodim bilan
//  almashtirish holatini qo'shib ketish"). One row = one lesson on one date taught by another
//  teacher. The timetable itself is a weekly template and stays untouched; a substitution only
//  overrides who teaches that lesson on that date:
//
//    * the substitute sees the lesson in their schedule / Mini App and may fill that class+subject
//      journal on that date;
//    * pay: the absent teacher loses that lesson (unless the whole day is already marked absent),
//      the substitute gains it at their own rate (TeacherSalaryCalc);
//    * the substitute hears about it from the bot.
//
//  Cancelled, never deleted — the pay of a closed month must stay explainable.
// ===========================================================================

public class LessonSubstitution
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly Date { get; set; }
    /// <summary>Dars raqami (ScheduleLesson.Period).</summary>
    public int Period { get; set; }
    /// <summary><see cref="LessonOwnerKind"/>: class | group.</summary>
    public string OwnerKind { get; set; } = LessonOwnerKind.Class;
    /// <summary>The class (or group) id — the journal's classId.</summary>
    public string OwnerId { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    /// <summary>0 = the whole class, 1/2 = one half (ScheduleLesson.SubGroup).</summary>
    public int SubGroup { get; set; }
    public string AbsentTeacherId { get; set; } = string.Empty;
    public string SubstituteTeacherId { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? CancelledBy { get; set; }
}
