namespace SchoolLms.Application.Dtos;

/// <summary>One pupil in the top or bottom list of a test (docs/modules/exam-awards.md).</summary>
/// <param name="Rank">Place: 1 = best in the top list, 1 = lowest in the bottom list; equal results share a place.</param>
/// <param name="Chats">Telegram chats linked to the pupil — 0 means the message will not reach anyone.</param>
/// <param name="AlreadySent">This kind was already sent to the pupil for this exam — it is skipped.</param>
public record ExamAwardRowDto(
    string StudentId, string FullName, string ClassName, int Rank,
    decimal? TotalPoints, decimal? MaxPoints, decimal? Percent, int Chats, bool AlreadySent);

/// <summary>The lists for the whole exam (<paramref name="ClassName"/> null) or for one class.</summary>
public record ExamAwardGroupDto(string? ClassName, int Scored, List<ExamAwardRowDto> Top, List<ExamAwardRowDto> Bottom);

public record ExamAwardPreviewDto(
    string ExamId, string ExamTitle, string? ExamDate, int Scored, int Top, int Bottom, bool PerClass,
    List<ExamAwardGroupDto> Groups, string TopText, string BottomText, IReadOnlyList<string> Tokens,
    bool TelegramConfigured);

/// <summary>
/// Send the thank-you message to the best <paramref name="Top"/> and the warning to the lowest
/// <paramref name="Bottom"/> (both typed in by hand, 0–50), over the whole exam or per class.
/// </summary>
/// <param name="Diploma">Also send the best ones a "Tashakkurnoma" PDF.</param>
/// <param name="ExcludeStudentIds">Pupils unticked in the preview.</param>
public record ExamAwardSendRequest(
    int Top, int Bottom, bool PerClass, string? TopText, string? BottomText, bool Diploma,
    List<string>? ExcludeStudentIds);

/// <param name="Delivered">Telegram chats reached.</param>
/// <param name="NoTelegram">Pupils with no linked chat (the message still sits in their app feed).</param>
/// <param name="Failed">Pupils none of whose chats could be reached.</param>
/// <param name="Skipped">Already sent before for this exam.</param>
public record ExamAwardSendResultDto(int TopSent, int BottomSent, int Delivered, int NoTelegram, int Failed, int Skipped);

/// <summary>A message already sent for the exam.</summary>
public record ExamAwardNoticeDto(
    Guid Id, string StudentId, string FullName, string ClassName, string Kind, int Rank, decimal? Percent,
    int Delivered, bool WithDiploma, string SentByName, DateTimeOffset SentAt);
