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
//    · TeachersMarkAttendance (2026-10-15)   → TeacherPortalController journal
//                                              writes (JournalService keepReason)
// ===========================================================================

/// <summary>
/// Umumiy sozlama bayroqlari. GET va PUT bir xil shakldan foydalanadi — bu yerda
/// yashiriladigan (parol kabi) maydon yo'q.
/// </summary>
/// <param name="TeachersMarkAttendance">
/// Teachers mark pupils' attendance in their journal. GET always fills it; on PUT a missing
/// (null) value leaves the stored flag UNCHANGED — an admin client older than the flag must not
/// switch teachers' attendance marking back on or off by saving the other four.
/// </param>
public record GeneralSettingsDto(
    bool ArchiveOnlyNonDebtorStudents,
    bool MakeAttendanceReasonRequired,
    bool IsStudentGradeRequired,
    bool ShowLearningProgressInParentDashboard,
    bool? TeachersMarkAttendance = null);
