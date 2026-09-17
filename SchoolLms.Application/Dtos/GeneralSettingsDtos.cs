namespace SchoolLms.Application.Dtos;

// ===========================================================================
//  §5.5 — umumiy sozlamaning TO'RTTA bayrog'i.
//
//  Bittasi ham "ko'rinadigan, lekin hech narsa qilmaydigan" tugma emas — har
//  biri aniq bir joyda ishlaydi:
//    · ArchiveOnlyNonDebtorStudents          → StudentArchiveService
//    · MakeAttendanceReasonRequired          → JournalService.SetEntryAsync
//    · IsStudentGradeRequired                → JournalService.SetNoteAsync
//    · ShowLearningProgressInParentDashboard → StudentPortalController,
//                                              TelegramParentController,
//                                              GET /api/tg/me (Mini App)
// ===========================================================================

/// <summary>
/// Umumiy sozlama bayroqlari. GET va PUT bir xil shakldan foydalanadi — bu yerda
/// yashiriladigan (parol kabi) maydon yo'q.
/// </summary>
public record GeneralSettingsDto(
    bool ArchiveOnlyNonDebtorStudents,
    bool MakeAttendanceReasonRequired,
    bool IsStudentGradeRequired,
    bool ShowLearningProgressInParentDashboard);
