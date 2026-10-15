namespace SchoolLms.Domain;

// ===========================================================================
//  Homework completion — docs/modules/homework-completion.md (client, 2026-10-15).
// ===========================================================================
//
//  The value lives in `journal_entries.homework` (int, no CHECK constraint), the same journal cell
//  that holds the grade and the attendance reason. 1 and 2 were written long before this feature
//  and production rows carry them, so their meaning is frozen: 1 = done, 2 = not done at all.
//  The three new statuses take the next free numbers.
//
//  The symbols follow the client's literal order "+ / - / *" = bajardi / chala / umuman yo'q;
//  K and Y are the initials of "kech qoldi" and "kelmadi". Every screen shows a legend with the
//  words, so the meaning never depends on the symbol alone. The TypeScript twin of this class is
//  `schoollms.client/src/lib/homeworkStatus.ts` (the Mini App keeps a JS copy in
//  `ui-tg/src/lib/homework.js`) — change all three together.
// ===========================================================================

/// <summary>
/// Homework completion status of one pupil in one lesson (<see cref="JournalEntry.Homework"/>).
/// </summary>
public static class HomeworkStatus
{
    /// <summary>Not marked.</summary>
    public const int None = 0;

    /// <summary><c>+</c> Bajardi — done. Stored value predates this class (the old "qildi").</summary>
    public const int Done = 1;

    /// <summary><c>*</c> Umuman yo'q — not done at all. Stored value predates this class (the old "qilmadi").</summary>
    public const int NotDone = 2;

    /// <summary><c>−</c> Chala — partly done.</summary>
    public const int Partial = 3;

    /// <summary><c>K</c> Kech qoldi — handed in late.</summary>
    public const int Late = 4;

    /// <summary><c>Y</c> Kelmadi — the pupil was absent. Also derived (never written) from a non-late absence reason.</summary>
    public const int Absent = 5;

    /// <summary>Largest valid value; anything outside <c>0..Max</c> is rejected.</summary>
    public const int Max = Absent;

    /// <summary>Error shown when a write carries an unknown value.</summary>
    public const string InvalidMessage =
        "Uy vazifasi holati noto'g'ri — 0 (belgilanmagan), 1 (+ bajardi), 2 (* umuman yo'q), "
        + "3 (− chala), 4 (K kech qoldi) yoki 5 (Y kelmadi) bo'lishi kerak.";

    public static bool IsValid(int value) => value is >= None and <= Max;

    /// <summary>Short mark shown in a table cell; empty for <see cref="None"/>.</summary>
    public static string Symbol(int value) => value switch
    {
        Done => "+",
        NotDone => "*",
        Partial => "−",
        Late => "K",
        Absent => "Y",
        _ => "",
    };

    /// <summary>The word for the legend and exports; empty for <see cref="None"/>.</summary>
    public static string Label(int value) => value switch
    {
        Done => "Bajardi",
        NotDone => "Umuman yo'q",
        Partial => "Chala",
        Late => "Kech qoldi",
        Absent => "Kelmadi",
        _ => "",
    };

    /// <summary>Legend order — the client's order: + − * K Y.</summary>
    public static readonly IReadOnlyList<int> Ordered = [Done, Partial, NotDone, Late, Absent];

    /// <summary>
    /// Counts towards the completion rate's denominator. <see cref="Absent"/> does not: a pupil who
    /// missed the lesson is an attendance matter (the journal already counts it), not a homework failure.
    /// </summary>
    public static bool CountsTowardsRate(int value) => value is Done or NotDone or Partial or Late;

    /// <summary>
    /// Credit one status earns in the completion rate: done 1, partial and late ½, not done 0.
    /// Rate = Σ credit ÷ number of statuses that <see cref="CountsTowardsRate"/>.
    /// </summary>
    public static decimal Credit(int value) => value switch
    {
        Done => 1m,
        Partial or Late => 0.5m,
        _ => 0m,
    };
}
