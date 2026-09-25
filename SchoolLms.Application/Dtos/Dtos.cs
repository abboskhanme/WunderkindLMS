namespace SchoolLms.Application.Dtos;

// Frontend servislari kutadigan so'rov (request) va javob (response) shakllari.
// JSON camelCase'ga ASP.NET Core standart sozlamasi orqali aylantiriladi.

/* ---------- Auth ---------- */
public record LoginRequest(string Email, string Password);
public record UserDto(
    string Id, string FullName, string Role, string Email, string? AvatarUrl,
    List<string>? Permissions = null);
public record LoginResponse(string Token, UserDto User);
/// <summary>O'quvchi/o'qituvchiga biriktirilgan tizim akkaunti ma'lumotlari (admin uchun).</summary>
public record CredentialsDto(string Login, string Password, string Role);
/// <summary>Joriy foydalanuvchi o'z login (email) va/yoki parolini o'zgartirishi uchun.
/// NewPassword bo'sh bo'lsa — parol o'zgarmaydi. CurrentPassword har doim talab qilinadi.</summary>
public record UpdateAccountRequest(string? Email, string CurrentPassword, string? NewPassword);
/// <summary>O'quvchi/ota-ona ilova ichida o'z parolini almashtirishi uchun.
/// Joriy parol bilan tasdiqlanadi; yangi parol kamida 8 belgi.</summary>
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/* ---------- Students ---------- */
/// <summary>
/// O'quvchi yaratish/tahrirlash so'rovi. FISH alohida-alohida kiritiladi (LastName/FirstName/MiddleName);
/// agar bo'lsa, ulardan FullName yig'iladi. Ota-ona FISH ham alohida.
/// FullName/ParentFullName ixtiyoriy — yo'q bo'lsa parts'dan yig'iladi.
/// </summary>
/// <param name="Phone">
/// §2.3 (S-8) — o'quvchining O'Z telefoni. <c>null</c> = TEGMA (tahrirda
/// maydon yuborilmasa eskisi qoladi), bo'sh satr = tozala.
/// </param>
/// <param name="Language">§2.3 (S-8) — o'qish tili: uz | ru | en | kaa. <c>null</c> = tegma.</param>
/// <param name="DocumentUrl">
/// §2.3 (S-8) — hujjat NUSXASI (metrika/pasport skani). Rasm emas: rasm
/// hamon <see cref="BirthCertificateUrl"/> da (nomi aldamchi, ma'nosi
/// o'zgarmadi). <c>null</c> = tegma.
/// </param>
/// <param name="Guardians">
/// §2.3 (S-8) — vasiylar (1–2 ta). <c>null</c> yoki bo'sh ro'yxat = bugungi
/// xatti-harakat: faqat <c>ParentPhone</c> dan <c>GuardianSync</c> ishlaydi.
/// Ro'yxat berilsa BIRINCHI (yoki <c>isPrimary</c>) yozuv ASOSIY vasiy bo'ladi
/// va u eski <c>parent_*</c> ustunlari bilan bir qadamda ushlab turiladi.
/// Bu yerdan vasiy O'CHIRILMAYDI — uzish alohida endpoint
/// (<c>DELETE /api/admin/students/{id}/guardians/{guardianId}</c>).
/// </param>
public record StudentPayload(
    string FullName, string BirthDate, string Address, string Gender,
    string ParentFullName, string ParentPhone, string ClassName, string? EnrollmentDate,
    string? NewPassword = null,
    int? SubGroup = null,
    string? LastName = null, string? FirstName = null, string? MiddleName = null,
    string? BirthCertificateUrl = null,
    string? ParentLastName = null, string? ParentFirstName = null, string? ParentMiddleName = null,
    string? ParentPassportUrl = null,
    string? Phone = null, string? Language = null, string? DocumentUrl = null,
    List<StudentGuardianInput>? Guardians = null,
    // §3.3 (Batch C, S-9) — sinfi hali yo'q o'quvchining mo'ljaldagi sinf
    // darajasi. `ClassName` bo'sh bo'lganda talab qilinadi, aks holda
    // e'tiborsiz qoldiriladi (StudentsController tozalaydi).
    short? TargetGrade = null);
public record PaymentRequest(decimal Amount, string? Month);

/* ---------- Excel'dan ommaviy import ---------- */
public record StudentImportRowErrorDto(int Row, string Message);
public record StudentImportResultDto(int Created, int Failed, int Skipped, List<StudentImportRowErrorDto> Errors);

/* ---------- Teachers ---------- */
public record TeacherPayload(
    string FullName, string BirthDate, string Address, string Gender,
    string HomeroomClass, List<string> SubjectIds, decimal Salary, string? SalaryStartMonth,
    string? NewPassword = null, List<string>? Permissions = null, string? Phone = null,
    string? PhotoUrl = null, string? Category = null, string? SalaryStartDate = null);
/// <summary>Maosh berish so'rovi.</summary>
/// <param name="Method">
/// Pul qaysi hisobdan chiqdi: <c>cash</c> kassadan, qolgani bankdan
/// (<see cref="SchoolLms.Domain.PaymentMethod"/>). Berilmasa — <c>transfer</c>:
/// maktab maoshni bank orqali to'laydi, va uni sukut bo'yicha naqd deb yozish
/// kassa hisobini minusga tortib, pul oqimi hisobotini buzardi.
/// </param>
public record SalaryPaymentRequest(decimal Amount, string? Note, string? Method = null);
public record SalaryHistoryDto(
    string TeacherId, string FullName, decimal Salary, decimal TotalPaid, List<PaymentDto> Payments);
public record MonthSalaryDto(string Month, decimal Expected, decimal Paid, decimal Remaining, string Status);
public record SalaryLedgerDto(
    string TeacherId, string FullName, decimal Salary,
    decimal TotalExpected, decimal TotalPaid, decimal Remaining,
    List<MonthSalaryDto> Months, List<PaymentDto> Payments);
public record SalaryReportRowDto(
    string TeacherId, string TeacherName, decimal Salary, decimal TotalPaid, int PaymentsCount,
    int Months, decimal Expected, decimal Remaining);

/// <summary>"Dars jadvali → Oylik hisoblash": toifa soat narxlari + har o'qituvchining hisoblangan oyligi.</summary>
public record SalaryRatesDto(
    decimal Oliy, decimal T1, decimal T2, decimal Mutaxasis,
    int WeeksPerMonth, string Month, List<TeacherPayrollRowDto> Teachers);
public record TeacherPayrollRowDto(
    string Id, string FullName, string Category, int WeeklyLessons, int MonthlyLessons,
    int MissedLessons, decimal BonusPct, decimal MonthlySalary);
public record SalaryRatesRequest(decimal Oliy, decimal T1, decimal T2, decimal Mutaxasis);
public record SetTeacherBonusRequest(decimal BonusPct);
public record SetBonusBulkRequest(List<string> TeacherIds, decimal BonusPct);

/// <summary>Bitta o'qituvchining tanlangan oydagi maosh tafsiloti (kelmagan kunlar + ustama bilan).</summary>
public record AbsentDayDto(string Date, int Lessons, string Note);
public record TeacherSalaryDetailDto(
    string TeacherId, string FullName, string Category, string Month, string StartDate, bool PartialMonth,
    decimal HourlyRate, int WeeklyLessons, int MonthlyLessons,
    decimal PlannedSalary, int MissedLessons, decimal Deduction,
    decimal BaseSalary, decimal BonusPct, decimal BonusAmount, decimal NetSalary,
    decimal Paid, decimal Remaining, List<AbsentDayDto> AbsentDays);

/// <summary>O'qituvchilar davomati — oylik board (o'qituvchilar + belgilangan kunlar).</summary>
public record TeacherNameDto(string Id, string FullName, string StartDate = "");
public record TeacherAttendanceDto(string TeacherId, string Date, string Status, string Note);
public record DateRangeDto(string Start, string End);
public record TeacherAttendanceBoardDto(
    List<TeacherNameDto> Teachers, List<TeacherAttendanceDto> Entries, List<DateRangeDto> Quarters);
public record SetTeacherAttendanceRequest(string TeacherId, string Date, string? Status, string? Note);
/// <summary>Bitta kun uchun BARCHA faol o'qituvchini belgilash (status bo'sh = o'sha kun tozalanadi).</summary>
public record SetTeacherAttendanceDayRequest(string Date, string? Status);

// ---------- Turniket/FaceID: o'qituvchilar davomati dashboard ----------
/// <summary>Dashboard bitta o'qituvchi qatori (kunlik).</summary>
public record TeacherDashboardRowDto(
    string TeacherId, string FullName, string? PhotoUrl, string DeviceUserId,
    string Status, string CheckIn, string CheckOut, string Expected, int LateMinutes, string Source);
/// <summary>Kunlik davomat jamlamasi.</summary>
public record AttendanceSummaryDto(int Total, int Present, int Late, int Absent, int NotArrived);
/// <summary>O'qituvchilar davomati dashboard (tanlangan kun).</summary>
public record TeacherAttendanceDashboardDto(
    string Date, bool TurnstileEnabled, string LastSync, bool InTeachingPeriod,
    AttendanceSummaryDto Summary, List<TeacherDashboardRowDto> Rows);
/// <summary>Sinxronlash natijasi.</summary>
public record TurnstileSyncResultDto(bool Ok, string Message, int EventsFetched, int Updated, string LastSync);

// ---------- Turniket: o'quvchilar kirgan/chiqqan vaqti ----------
/// <summary>O'quvchi turniket qatori: FISH, sinf, qurilma ID, kirgan/chiqqan vaqt (tanlangan kun).</summary>
public record StudentTurnstileRowDto(
    string StudentId, string FullName, string ClassName, string DeviceUserId,
    string CheckIn, string CheckOut, int Passes);
/// <summary>O'quvchilar turniket dashboard (tanlangan kun).</summary>
public record StudentTurnstileDashboardDto(
    string Date, bool TurnstileEnabled, string LastSync, int Present, int Total,
    List<StudentTurnstileRowDto> Rows);
/// <summary>O'quvchiga qurilma (turniket) ID biriktirish.</summary>
public record SetStudentDeviceRequest(string StudentId, string? DeviceUserId);
/// <summary>SignalR jonli avtobus joylashuvi (LiveHub "gps" → busLocation).</summary>
public record BusLivePingDto(string BusId, double Latitude, double Longitude, double Speed, string RecordedAt);

// ---------- GPS: maktab avtobuslari ----------
public record BusDto(
    string Id, string Name, string PlateNumber, string DriverName, string DriverPhone,
    string DeviceId, string Route, bool IsActive, string Note);
public record SaveBusRequest(
    string Name, string? PlateNumber, string? DriverName, string? DriverPhone,
    string? DeviceId, string? Route, bool IsActive = true, string? Note = null);
/// <summary>Avtobus + so'nggi joylashuvi (umumiy xarita uchun).</summary>
public record BusLiveDto(
    BusDto Bus, double? Lat, double? Lng, double? Speed, string? LastSeen, bool Online);
/// <summary>Iz nuqtasi.</summary>
public record TrackPointDto(double Lat, double Lng, double Speed, string Time);
/// <summary>To'xtash joyi (radiusda turib qolgan davr).</summary>
public record BusStopDto(double Lat, double Lng, string ArrivedAt, string DepartedAt, int DurationMin);
/// <summary>Bir kunlik iz + to'xtashlar + jamlama.</summary>
public record BusTrackDto(
    string Date, List<TrackPointDto> Points, List<BusStopDto> Stops,
    double DistanceKm, int MovingMin, int StoppedMin);
/// <summary>GPS tracker'dan kelgan bitta signal (ingest).</summary>
public record GpsPingRequest(string DeviceId, double Lat, double Lng, double? Speed, string? Time, string? Token);

/// <summary>O'quvchi/ota-ona uchun avtobus + so'nggi joylashuvi. XAVFSIZLIK: tracker IMEI'si (DeviceId)
/// va ichki izoh berilmaydi.</summary>
public record StudentBusDto(
    string Id, string Name, string PlateNumber, string DriverName, string DriverPhone,
    string Route, double? Lat, double? Lng, double? Speed, string? LastSeen, bool Online);
/// <summary>Avtobuslar jonli joylashuvi (o'quvchi/ota-ona ilovasi). Faqat ertalabki oynada ko'rinadi
/// (FromHour–ToHour, Asia/Tashkent); oynadan tashqarida Available=false va Buses bo'sh bo'ladi.</summary>
public record StudentBusesDto(
    bool Available, int FromHour, int ToHour, string ServerTime, IReadOnlyList<StudentBusDto> Buses);

/// <summary>GPS integratsiya sozlamasi.</summary>
public record GpsSettingsDto(
    bool Enabled, string IngestToken, int OnlineMinutes, int StopRadiusM, int StopMinMinutes, int BusCount);
public record SaveGpsSettingsRequest(
    bool Enabled, string? IngestToken, int? OnlineMinutes, int? StopRadiusM, int? StopMinMinutes);

// ---------- Kamera (videokuzatuv) ----------
public record CameraDto(
    string Id, string Name, string Location, string RtspUrl, string RtspSubUrl,
    int RetentionDays, bool IsActive, string Note);
public record SaveCameraRequest(
    string Name, string? Location, string RtspUrl, string? RtspSubUrl,
    int RetentionDays = 7, bool IsActive = true, string? Note = null);
/// <summary>Kamera integratsiya sozlamasi.</summary>
public record CameraSettingsDto(bool Enabled, int CameraCount);
public record SaveCameraSettingsRequest(bool Enabled);

/* ---------- Subjects ---------- */
/// <summary>
/// Fan formasi. <paramref name="IsGroupable"/> — "guruhlarga bo'linadi"
/// (students-parity.md §2.5, G-9): faqat shunday fanga o'quv guruhi ochiladi.
/// Sukut qiymati <c>false</c> ataylab: eski chaqiruvchi bayroqni yubormasa
/// fan guruhli BO'LIB QOLMAYDI.
///
/// <para>
/// <paramref name="Color"/> va <paramref name="IsActive"/> — F-3
/// (students-parity.md §2.5.3): jadval katakchasini bo'yaydigan rang va
/// o'chirish o'rniga arxivlash bayrog'i. Forma <c>IsGroupable</c> bilan bir
/// xil naqshda HAR DOIM to'liq obyekt yuboradi — shuning uchun bu yerda ham
/// qisman ("berilmasa tegilmaydi") yangilash yo'q, Update() hammasini
/// almashtiradi.
/// </para>
/// </summary>
public record SubjectPayload(
    string Name,
    bool IsGroupable = false,
    string? Color = null,
    bool IsActive = true);

/* ---------- Classes ---------- */
/// <summary>
/// Sinf formasi. <paramref name="Capacity"/> — C-4 (students-parity.md §2.2.3): sinfga
/// nechta o'quvchi sig'adi. <c>null</c> = chek yo'q (sukut, migratsiyadan keyingi hamma
/// sinf shunday). Bu OGOHLANTIRISH chegarasi — <see cref="SchoolLms.Application.Services.ClassMembershipService.CapacityWarningAsync"/>
/// qo'shish/o'tkazishdan keyin oshib ketganini aytadi, lekin taqiqlamaydi.
/// </summary>
public record ClassPayload(
    string Name, int Grade, string Language, decimal MonthlyFee, string? Room, short? Capacity = null);

/* ---------- Sinf rahbarlari (C-5) ---------- */
/// <summary>
/// Sinfga biriktirilgan sinf rahbari — <c>teachers.homeroom_class</c> ustunidan o'qiladi
/// (haqiqat manbai o'qituvchi tomonida qoladi; sinf formasi endi shu qiymatni O'QIYDI VA
/// YOZADI, ilgari faqat o'qituvchi kartochkasidan yozilardi).
/// </summary>
public record HomeroomTeacherDto(string Id, string FullName);

/// <summary>
/// Sinf rahbari(lari)ni belgilash. Ro'yxatda YO'Q, lekin hozir shu sinfga biriktirilgan
/// o'qituvchilar — bo'shatiladi (<c>HomeroomClass = ""</c>). Ro'yxatda bor, lekin BOSHQA
/// sinfga biriktirilgan o'qituvchi — shu sinfga "o'g'irlanadi" (bugungi teacher-tarafdagi
/// forma ham xuddi shunday ishlaydi: bitta o'qituvchi faqat bitta sinfning rahbari bo'la oladi).
/// </summary>
public record SetHomeroomTeachersRequest(List<string>? TeacherIds);

/* ---------- Leads ---------- */
public record LeadCreateRequest(
    string FullName, string Gender, string BirthDate, string ParentFullName,
    string ParentPhone, int TargetGrade, string? Note, string Stage);
public record LeadUpdateRequest(
    string FullName, string Gender, string BirthDate, string ParentFullName,
    string ParentPhone, int TargetGrade, string? Note);
public record LeadStageRequest(string Stage);

/* ---------- Lead stages ---------- */
public record StagePayload(string Title, string Color);
public record ReorderRequest(List<string> Ids);

/* ---------- Canteen ---------- */
public record DishPayload(string Name, string Ingredients, string? ImageUrl);
public record DishDto(string Id, string Name, string Ingredients, string? ImageUrl);
public record DayMenuDto(string Date, Dictionary<string, List<DishDto>> Meals);

/* ---------- Journal ---------- */
/// <summary>
/// Jurnal ustuni — bir dars (sana + dars raqami + guruh). SubGroup: 0 = butun sinf,
/// 1 = 1-guruh, 2 = 2-guruh. Bo'lingan darsda har guruh o'z ustunini oladi.
/// </summary>
public record JournalColumnDto(string Date, int Period, int SubGroup = 0);
/// <summary>Mavzular Excel importidagi xato qator (Excel qator raqami + sabab).</summary>
public record TopicImportRowErrorDto(int Row, string Reason);
/// <summary>Mavzular Excel import natijasi: to'ldirilgan / o'tkazib yuborilgan (bo'sh) / xato qatorlar.</summary>
public record TopicImportResultDto(int Imported, int Skipped, int Errors, List<TopicImportRowErrorDto> RowErrors);
public record JournalEntryDto(
    string StudentId, string Date, int Period, int? Grade, string? ReasonId,
    int Homework, int Behavior, int? Mastery);
public record SetJournalEntryRequest(
    string ClassId, string SubjectId, int Quarter, string StudentId, string Date, int Period,
    int? Grade, string? ReasonId, int Homework = 0, int Behavior = 0, int? Mastery = null);
public record JournalTopicDto(string Date, int Period, string Topic, string? Homework, bool Conducted, int SubGroup = 0);
/// <summary>Berilgan sanada o'tilgan (conducted) darslar — ega+fan+dars raqami+guruh.</summary>
/// <param name="OwnerKind">
/// Darsning egasi sinfmi yoki o'quv guruhimi (<c>LessonOwnerKind</c>;
/// students-parity.md §2.1.4). Oxirida turibdi va sukuti <c>class</c> —
/// eski mijoz kodi buzilmaydi.
/// </param>
public record ConductedLessonDto(
    string ClassId, string SubjectId, int Period, int SubGroup = 0,
    string OwnerKind = SchoolLms.Domain.LessonOwnerKind.Class);

/// <summary>
/// Jurnal tanlagichi uchun bitta EGA — sinf yoki o'quv guruhi (G-12).
///
/// <para>
/// <paramref name="Id"/> jurnal endpointlarining <c>classId</c> parametriga
/// beriladi: guruh darsi mavjud <c>class_id</c> ustunida guruh id'sini
/// saqlaydi, shuning uchun so'rovlar bir xil qoladi. Guruhlar ro'yxatda
/// FAQAT cut-over o'chirgichi yoqilganda paydo bo'ladi (§4.3).
/// </para>
/// </summary>
/// <param name="Kind"><c>class</c> | <c>group</c>.</param>
/// <param name="Grade">Sinf darajasi; guruh uchun 0.</param>
/// <param name="SubjectId">Guruhning fani; sinf uchun null.</param>
/// <param name="SubjectName">Guruhning fan nomi; sinf uchun null.</param>
/// <param name="StudentCount">Arxivlanmagan o'quvchilar soni.</param>
/// <param name="Language">Sinfning ta'lim tili (uz / ru / en ...); guruh uchun null.</param>
/// <param name="HomeroomTeacher">Sinf rahbari (o'qituvchilar ro'yxatidagi "sinf rahbari" belgisi); bo'lmasa null.</param>
public record JournalOwnerDto(
    string Id, string Name, string Kind, int Grade,
    string? SubjectId, string? SubjectName, int StudentCount,
    string? Language = null, string? HomeroomTeacher = null);

/// <summary>Jurnal → sinf → fanlar ro'yxatining bitta qatori (EduSchool oqimi, 2026-09-23).</summary>
/// <param name="Teachers">Shu sinfda shu fanni o'tadigan o'qituvchilar — dars jadvalidan.</param>
public record JournalSubjectDto(string SubjectId, string SubjectName, List<string> Teachers);
public record SetLessonNoteRequest(
    string ClassId, string SubjectId, int Quarter, string Date, int Period, string Topic, string? Homework, bool Conducted,
    int SubGroup = 0);
/// <summary>O'quvchining chorak bahosi: Grade = o'qituvchi qo'ygan rasmiy baho (yo'q bo'lsa null),
/// Recommended = kunlik baholar o'rtachasidan tavsiya (baho yo'q bo'lsa null).</summary>
public record QuarterGradeRowDto(string StudentId, int? Grade, double? Recommended);
/// <summary>Chorak bahosini belgilash; Grade null bo'lsa — mavjud baho o'chiriladi.</summary>
public record SetQuarterGradeRequest(string ClassId, string SubjectId, int Quarter, string StudentId, int? Grade);

/* ---------- Settings ---------- */
/// <summary>GradesOpen — o'qituvchilarga shu chorak bahosini kiritish ochiqmi (admin boshqaradi).</summary>
public record QuarterPeriodDto(int Quarter, string StartDate, string EndDate, bool GradesOpen);
public record LessonTimeDto(int Period, string StartTime, string EndTime);
public record AbsenceReasonDto(string Id, string Name, string Short, bool IsLate);
public record SchoolSettingsDto(
    List<QuarterPeriodDto> Quarters, List<LessonTimeDto> LessonTimes, List<AbsenceReasonDto> AbsenceReasons);
public record SaveQuartersRequest(List<QuarterPeriodDto> Quarters);
public record SaveLessonTimesRequest(List<LessonTimeDto> LessonTimes);
public record SaveAbsenceReasonsRequest(List<AbsenceReasonDto> AbsenceReasons);

/* ---------- Schedule templates ---------- */
/// <summary>Bitta dars katagi. SubGroup: 0 = butun sinf, 1 = 1-guruh, 2 = 2-guruh.</summary>
public record ScheduleLessonDto(int Day, int Period, string SubjectId, string TeacherId, int SubGroup = 0);
/// <summary>
/// Jadval varianti. <paramref name="ClassId"/> — EGAning id'si: sinf yoki
/// o'quv guruhi, <paramref name="OwnerKind"/> qaysi ekanini aytadi
/// (students-parity.md §2.1.4).
/// </summary>
public record ScheduleTemplateDto(
    string Id, string ClassId, string Name, List<ScheduleLessonDto> Lessons,
    string OwnerKind = "class");
public record CreateTemplateRequest(string Name);
public record RenameTemplateRequest(string Name);
/// <summary>
/// Bir (Day, Period) katakni to'liq holatga o'rnatish: bo'sh (Lessons=[]),
/// butun sinf (1 ta lesson, SubGroup=0) yoki bo'lingan (2 ta lesson, SubGroup=1/2).
/// </summary>
public record SetCellRequest(int Day, int Period, List<ScheduleLessonDto> Lessons);

/* ---------- Sinf guruhlari ---------- */
public record GroupAssignmentDto(string StudentId, int SubGroup);
/// <summary>Sinfdagi bitta o'quvchining guruhdagi pozitsiyasi.</summary>
public record GroupStudentDto(string Id, string FullName, int SubGroup);
/// <summary>
/// Sinf guruhlari holati. Locked=true bo'lsa — o'quv yili allaqachon boshlangan (jurnalda
/// yozuv bor). <see cref="CanEdit"/> joriy foydalanuvchining tahrirlash huquqi:
/// admin'larda Locked'ga teskari, superadmin'da har doim true (qulflangan bo'lsa ham override).
/// </summary>
public record ClassGroupsDto(
    string ClassId, string ClassName, bool Locked, string? LockReason, bool CanEdit,
    int UngroupedCount, int Group1Count, int Group2Count, List<GroupStudentDto> Students);
/// <summary>Guruh tayinlashni saqlash so'rovi. Berilmagan o'quvchilar o'zgarmaydi.</summary>
public record SaveGroupsRequest(List<GroupAssignmentDto> Assignments);

/* ---------- Week assignments ---------- */
public record WeekAssignmentDto(int Week, string? TemplateId);
public record SaveWeekAssignmentsRequest(int Quarter, List<WeekAssignmentDto> Assignments);

/* ---------- Bildirishnomalar (topbar qo'ng'irog'i) ---------- */

/// <summary>Bitta bildirishnoma. <c>Kind</c>: suggestion | complaint | pickup | chat | birthday.
/// <c>Link</c> — bosilganda ochiladigan admin sahifasi. <c>IsNew</c> — oxirgi o'qishdan keyin paydo bo'lgan.</summary>
public record NotificationDto(
    string Id, string Kind, string Title, string Text, DateTime CreatedAt, string Link,
    bool IsNew = false);

/// <summary>Tanlangan bildirishnomalarni o'chirish so'rovi (id'lar — <c>NotificationDto.Id</c>).</summary>
public record DismissNotificationsRequest(List<string>? Ids);

/// <summary>O'qildi — <c>Ids</c> bo'sh bo'lsa hammasi, aks holda faqat shular.</summary>
public record MarkNotificationsReadRequest(List<string>? Ids);

/// <summary>Bildirishnomalar ro'yxati + o'qilmaganlar soni.</summary>
public record NotificationListDto(List<NotificationDto> Items, int UnreadCount);

/// <summary>Faqat o'qilmaganlar soni (qo'ng'iroq nishoni uchun).</summary>
public record UnreadCountDto(int UnreadCount);

/* ---------- Dashboard ---------- */
/// <summary>
/// Bosh sahifadagi raqamlar.
///
/// <para>
/// Pul bilan bog'liq uchtasi — <see cref="CreditCount"/>, <see cref="DebtorCount"/>,
/// <see cref="PaidAtLeastOnceCount"/> — <c>StudentBalanceQuery</c> dan
/// HISOBLANADI. O'quvchi qatorida saqlangan qoldiq YO'Q (P1-21): u bir marta
/// haqiqatdan ajralib ketgan va shu sababli o'chirilgan.
/// </para>
/// </summary>
/// <param name="StudentsCount">Faol (arxivlanmagan) o'quvchilar.</param>
/// <param name="UnassignedCount">Sinfga biriktirilmagan faol o'quvchilar.</param>
/// <param name="ClassesCount">Sinflar soni.</param>
/// <param name="ArchivedCount">Arxivdagi o'quvchilar.</param>
/// <param name="CreditCount">Avansi bor (qoldig'i musbat) o'quvchilar — "haqdorlar".</param>
/// <param name="DebtorCount">Qarzdor (qoldig'i manfiy) o'quvchilar.</param>
/// <param name="PaidAtLeastOnceCount">Hech bo'lmasa bitta to'lov qilganlar.</param>
/// <param name="ActiveCount">Sinfda o'qiyotgan faol o'quvchilar (jami − sinfsiz).</param>
/// <param name="LeftFromClassCount">
/// Sinfdan CHIQARILGAN va boshqa sinfga qo'yilmagan faol o'quvchilar: hozir sinfsiz,
/// lekin kamida bitta yopilgan <c>class_memberships</c> yozuvi bor. Ko'chirilgan bola
/// bu yerga tushmaydi — uning yangi sinfda faol a'zoligi bor.
/// </param>
/// <param name="WaitingCount">Qabul qilingan, sinfi hali hal qilinmagan (sinfsiz va
/// <c>target_grade</c> ko'rsatilgan) o'quvchilar.</param>
/// <param name="FirstPaymentThisMonthCount">Birinchi (storno bo'lmagan) to'lovi joriy oyga
/// to'g'ri kelgan o'quvchilar.</param>
public record AdminStatsDto(
    int StudentsCount, int TeachersCount, double AverageGrade, double? AttendanceRate,
    int UnassignedCount, int ClassesCount, int ArchivedCount,
    int CreditCount, int DebtorCount, int PaidAtLeastOnceCount,
    int ActiveCount = 0, int LeftFromClassCount = 0, int WaitingCount = 0,
    int FirstPaymentThisMonthCount = 0, int MaleCount = 0, int FemaleCount = 0);

/// <summary>Bitta sinfdagi faol o'quvchilar — bosh sahifadagi "Sinflar kesimi" va
/// "Kontingent tarkibi" vidjetlari uchun.</summary>
/// <param name="Capacity">Sinf sig'imi; ko'rsatilmagan bo'lsa null.</param>
public record ClassHeadcountDto(
    string ClassId, string ClassName, int Grade, int StudentsCount, int MaleCount, int FemaleCount,
    int? Capacity);

/// <summary>Bitta dars soati kesimidagi davomat (bosh sahifadagi jadval va diagramma).</summary>
/// <param name="Period">Dars raqami (1..10).</param>
/// <param name="Expected">Shu soatda darsi bo'lgan o'quvchilar soni.</param>
/// <param name="Absent">Sababli yoki sababsiz kelmaganlar.</param>
/// <param name="Unchecked">Davomati umuman belgilanmaganlar.</param>
public record AttendanceByPeriodDto(int Period, int Expected, int Present, int Absent, int Unchecked);

/// <summary>Dars qoldirayotgan o'quvchi — bosh sahifadagi ro'yxat.</summary>
/// <param name="MissedDays">Oxirgi 30 kunda sababsiz qoldirgan kunlari.</param>
/// <param name="LastSeen">Oxirgi kelgan sanasi (hech qachon kelmagan bo'lsa — null).</param>
public record AbsentStudentDto(
    string StudentId, string FullName, string ClassName, int MissedDays, string? LastSeen);
public record ClassPerformanceItemDto(string ClassId, string ClassName, double AverageGrade, double? AttendanceRate);
public record TopClassDto(string Id, string Name, int StudentsCount, double AverageGrade);
public record AdminDashboardDto(
    AdminStatsDto Stats, List<ClassPerformanceItemDto> ClassPerformance, List<TopClassDto> TopClasses,
    List<AttendanceByPeriodDto> AttendanceByPeriod, List<AbsentStudentDto> AbsentStudents,
    List<ClassHeadcountDto>? ClassHeadcounts = null);

/// <summary>Fan qayerlarda ishlatilayotgani. <c>CanDelete</c> = hech qayerda.</summary>
/// <param name="UsedIn">"2 ta o'quv guruhi", "14 ta dars jadvali katagi" ...</param>
public record SubjectUsageDto(bool CanDelete, List<string> UsedIn);

/* ---------- Class performance / rating ---------- */
public record SubjectDto(string Id, string Name);
/// <summary>
/// O'quvchi — ro'yxat va tanlov ekranlari uchun.
/// </summary>
/// <param name="Balance">
/// Pul qoldig'i (manfiy = qarz, musbat = avans) — <c>StudentBalanceQuery</c> dan
/// HISOBLANADI, o'quvchi qatorida saqlanmaydi (P1-21).
///
/// <para>
/// <b><c>null</c> = "bu ro'yxatda pul ko'rsatilmaydi"</b>, nol emas. Jurnal,
/// davomat va reyting ro'yxatlari aynan shunday qaytadi: u yerda qoldiq
/// ko'rsatilmaydi va o'qituvchiga ko'rsatilmasligi ham kerak (SPEC §4.3).
/// Nol bilan to'ldirish "qarzi yo'q" degan yolg'on ma'no berardi.
/// </para>
/// </param>
public record StudentDto(
    string Id, string FullName, string BirthDate, string Address, string Gender,
    string ParentFullName, string ParentPhone, string ClassName, string EnrollmentDate,
    decimal? Balance = null, int SubGroup = 0,
    string LastName = "", string FirstName = "", string MiddleName = "",
    string? BirthCertificateUrl = null,
    string ParentLastName = "", string ParentFirstName = "", string ParentMiddleName = "",
    string? ParentPassportUrl = null,
    bool IsArchived = false, string? ArchivedAt = null, string? ArchiveReason = null,
    // §2.3 (S-8) — oxiriga QO'SHILDI: mavjud o'quvchilarda null bo'lgani uchun
    // birorta ekran o'zgarishini sezmaydi, formaga esa ular kerak.
    string? Phone = null, string? Language = null, string? DocumentUrl = null,
    // §3.3 (Batch C, S-9) — sinfi hali yo'q o'quvchining mo'ljaldagi sinf
    // darajasi (0-11). null = sinfga biriktirilgan yoki mo'ljal ko'rsatilmagan.
    short? TargetGrade = null);

/// <summary>
/// O'quvchini arxivlash so'rovi. <c>Reason</c> — erkin matn, MAJBURIY (tafsilot);
/// <c>ArchiveReasonId</c> — katalog qatori, ixtiyoriy (guruhlash uchun, §2.2);
/// <c>Force</c> — qarzdorlik to'sig'ini chetlab o'tish, faqat superadmin (§9 Q4).
/// </summary>
public record ArchiveStudentRequest(string Reason, Guid? ArchiveReasonId = null, bool Force = false);

/// <summary>Bayram/dam olish kuni (butun maktab). Date — "YYYY-MM-DD".</summary>
public record HolidayDto(string Date, string Name);
/// <summary>Bayram kunini qo'shish/yangilash so'rovi.</summary>
public record SaveHolidayRequest(string Date, string? Name);

/* ---------- Intizomiy ball ---------- */
/// <summary>
/// Intizomiy ball sababi (nomi + ball). <c>Kind</c>: "other" — mustaqil intizomiy sabab;
/// "attendance" — davomat sababi (jurnalda ishlatiladi, manbai bitta).
/// </summary>
/// <param name="NotifyParent">
/// Shu sabab bilan ball qo'yilganda ota-onaga TELEGRAM xabari ketadimi (§6.3, 4-qadam).
/// Davomat sabablarida ("attendance") har doim <c>false</c>: ular jurnalda ishlatiladi
/// va o'z xabar yo'liga ega.
/// </param>
/// <param name="Description">Sabab izohi — qachon qo'yiladi, nimani anglatadi (§6.3, 5-qadam).</param>
/// <param name="IsActive">false = yangi ball qo'yishda tanlanmaydi; eski yozuvlar joyida qoladi.</param>
public record DisciplineReasonDto(
    string Id, string Name, int Points, string Kind,
    bool NotifyParent = false, string? Description = null, bool IsActive = true);

/// <summary>
/// Sababni yaratish/tahrirlash. Yangi maydonlar NULL bo'lishi mumkin — eski mijoz
/// (yoki eski test) ularni yubormasa, sukut qiymat qo'llanadi: xabar O'CHIQ, sabab FAOL.
/// </summary>
public record SaveDisciplineReasonRequest(
    string Name, int Points,
    bool? NotifyParent = null, string? Description = null, bool? IsActive = null);
/// <summary>Davomat sababiga ball belgilash so'rovi.</summary>
public record SetReasonPointsRequest(int Points);

/// <summary>O'quvchilarni baholash turi (admin xohlagancha qo'shadi).</summary>
public record EvaluationTypeDto(string Id, string Name, string Description);
public record SaveEvaluationTypeRequest(string Name, string? Description);

/// <summary>Bitta davomat sababidan o'quvchida necha marta bo'lgani (jurnal belgilaridan).</summary>
public record AttendanceReasonCountDto(string ReasonId, string Name, string Short, bool IsLate, int Count);

/// <summary>
/// Baholash jadvalidagi bitta o'quvchi qatori: qatnashish (o'tilgan/qatnashgan), davomat sabablari
/// bo'yicha taqsimot va baholash turlari bo'yicha baholar (typeId → 1-5).
/// </summary>
public record EvaluationRowDto(
    string StudentId,
    string FullName,
    string ClassName,
    int Conducted,
    int Attended,
    IReadOnlyList<AttendanceReasonCountDto> Reasons,
    Dictionary<string, int> Grades,
    double AvgGrade);

/// <summary>
/// Baholash jadvali: mavjud oylar katalogi, joriy (tanlangan) oy/hafta, ustun turlari va qatorlar.
/// Qatnashish/davomat tanlangan davr (oy yoki hafta) bo'yicha, baholar tanlangan oy bo'yicha.
/// </summary>
public record EvaluationBoardDto(
    IReadOnlyList<string> Months,
    string Month,
    int Week,
    IReadOnlyList<EvaluationTypeDto> Types,
    IReadOnlyList<EvaluationRowDto> Rows,
    string SubjectId = "",
    IReadOnlyList<SubjectDto>? Subjects = null);

/// <summary>
/// Baho qo'yish/yangilash/tozalash so'rovi (oy bo'yicha; Score null yoki 1-5 dan tashqari = tozalash).
/// <c>SubjectId</c> — qaysi fan ("" = umumiy). <c>ClassId</c> — o'qituvchi chaqiruvida egalik tekshiruvi uchun.
/// </summary>
public record SetEvaluationGradeRequest(
    string StudentId, string TypeId, string Month, int Week, int? Score,
    string? SubjectId = null, string? ClassId = null);

/// <summary>O'quvchining bitta fan bo'yicha oylik baholashlari (shaxsiy daftarda "fan kesimida").</summary>
public record SubjectEvaluationDto(string SubjectId, string SubjectName, double Avg, List<MonthlyEvaluationDto> Evaluations);
/// <summary>Ballar nazorati qatori: o'quvchi, sinf, plus (rag'bat), minus (jazo), qoldi (100+plus−minus).</summary>
public record DisciplineScoreRowDto(
    string StudentId, string FullName, string ClassName, int Plus, int Minus, int Remaining);
/// <summary>O'quvchiga ball kiritish so'rovi (sabab bo'yicha).</summary>
public record AddDisciplinePointRequest(string StudentId, string ReasonId, string? Note);
/// <summary>
/// Butun sinfga bitta intizomiy ball kiritish (C-6, students-parity.md §2.2.3): sinfning HAR
/// BIR faol o'quvchisiga bir xil sabab bilan alohida yozuv qo'shiladi — EduSchool'dagi
/// <c>editBehaviorIncidents</c> / <c>POST /behavior-incidents/class</c> naqshi. Faqat mustaqil
/// intizomiy sabab ("other") — davomat sababi jurnal orqali qo'yiladi, bu yerdan emas.
/// </summary>
public record AddClassDisciplinePointRequest(string ClassId, string ReasonId, string? Note);
/// <summary>Sinf bo'ylab ball kiritish natijasi: nechta o'quvchiga yozildi, nechtasiga ota-onaga xabar ketdi.</summary>
public record ClassDisciplinePointResultDto(int Applied, int NotifiedParents, List<DisciplinePointDto> Items);
/// <summary>Bitta intizomiy ball yozuvi (tarix). <c>Source</c>: "manual" (qo'lda, o'chirsa bo'ladi) yoki "attendance" (jurnal davomati, faqat ko'rish).</summary>
/// <param name="NotifiedParents">
/// Shu ball haqida ota-onaga HAQIQATAN yuborilgan Telegram xabarlari soni (§6.3, 4-qadam).
/// Faqat yangi ball qo'yilganda ma'noga ega; tarixni o'qiyotganda har doim 0.
/// </param>
public record DisciplinePointDto(
    string Id, string StudentId, string ReasonName, int Points, string Note, string CreatedAt,
    string CreatedBy, string Source, int NotifiedParents = 0);
/// <summary>O'quvchi/ota-ona ilovasi uchun intizomiy ball: qoldi + plus/minus + tarix (100 dan boshlanadi).</summary>
public record StudentDisciplineDto(int Remaining, int Plus, int Minus, List<DisciplinePointDto> Items);

/// <summary>
/// "Harakatlar" lentasidagi bitta qator — maktab bo'ylab, o'quvchi nomi va sinfi bilan.
/// <c>ReasonName</c> va <c>Points</c> — YOZUV PAYTIDAGI nusxa (sabab keyin o'zgarsa ham tarix
/// o'zgarmaydi), shuning uchun sabab bo'yicha filtr <c>ReasonId</c> ustidan ishlaydi, ekranda esa
/// nusxa ko'rinadi. <c>Source</c>: "manual" (qo'lda kiritilgan) yoki "attendance" (jurnal davomati).
/// </summary>
public record DisciplineFeedRowDto(
    string Id, string StudentId, string FullName, string ClassName, string ReasonName, int Points,
    string Note, string CreatedAt, string CreatedBy, string Source);

/// <summary>
/// "Harakatlar" lentasi: bitta sahifa + filtrlangan to'plamning jamlamasi + filtr ro'yxatlari.
/// <c>Authors</c> va <c>ClassNames</c> filtrga BOG'LIQ EMAS (butun bazadan) — tanlangan filtr
/// ro'yxatni qisqartirib, foydalanuvchini qamalda qoldirmasligi uchun.
/// </summary>
public record DisciplineFeedDto(
    IReadOnlyList<DisciplineFeedRowDto> Items,
    int Total, int Page, int PageSize,
    int PlusCount, int MinusCount, int PointsSum,
    IReadOnlyList<string> Authors, IReadOnlyList<string> ClassNames);
/// <summary>O'quvchini arxivdan qaytarish — ixtiyoriy yangi parol (arxivlanganda parol bloklangan edi).</summary>
public record RestoreStudentRequest(string? NewPassword);

/// <summary>O'qituvchini arxivlash so'rovi — sababini saqlaydi.</summary>
public record ArchiveTeacherRequest(string Reason);
/// <summary>O'qituvchini arxivdan qaytarish — ixtiyoriy yangi parol (arxivlanganda parol bloklangan edi).</summary>
public record RestoreTeacherRequest(string? NewPassword);

/// <summary>
/// O'qituvchi faollik hisoboti — bitta qator (umumiy ko'rinish). Expected = jadvaldan kelib
/// chiqib bugungacha bo'lishi kerak bo'lgan darslar; Conducted = jurnal "o'tildi" belgilari;
/// foizlar bajarilgan/mavzu yozilgan/uy vazifa berilgan ulushini bildiradi. Status: active|low|none.
/// </summary>
public record TeacherReportRowDto(
    string TeacherId, string FullName, bool IsArchived,
    int Expected, int Conducted, int? DonePct,
    int Grades, int? TopicPct, int? HomeworkPct,
    string? LastActivity, string Status);

/// <summary>O'qituvchi hisoboti — sinf/fan kesimida bitta qator (batafsil ko'rinish).</summary>
public record TeacherReportBreakdownDto(
    string ClassName, string SubjectName, int SubGroup,
    int Expected, int Conducted, int? DonePct,
    int Grades, int? TopicPct, int? HomeworkPct,
    // G-15: qator sinf darsimi yoki GURUH darsimi (LessonOwnerKind).
    string OwnerKind = "class");

/// <summary>Bitta o'qituvchining batafsil hisoboti: umumiy ko'rsatkichlar + sinf/fan yoyilmasi.</summary>
public record TeacherReportDetailDto(
    string TeacherId, string FullName, bool IsArchived,
    int Expected, int Conducted, int? DonePct,
    int Grades, int? TopicPct, int? HomeworkPct,
    string? LastActivity, string Status,
    List<TeacherReportBreakdownDto> Rows);

/// <summary>O'quvchi (mobil ilova) o'z joylashuvini yangilash so'rovi — GPS dan keladi.</summary>
public record UpdateLocationRequest(double Latitude, double Longitude, string? Address);
/// <summary>Joriy saqlangan joylashuvni o'qish (ilova xaritada ko'rsatishi uchun). Hali yo'q bo'lsa null'lar.</summary>
public record StudentLocationDto(double? Latitude, double? Longitude, string? Address, string? UpdatedAt);
/// <summary>Admin xarita uchun — joylashuvi bor o'quvchi qatori.</summary>
public record StudentLocationRowDto(
    string StudentId, string FullName, string ClassName,
    double Latitude, double Longitude, string? Address, string? UpdatedAt);

/// <summary>
/// Admin xarita uchun — bitta o'quvchining BITTA turdagi pin'i (§2.8, L-2).
/// Bitta o'quvchida uchtagacha pin bo'lishi mumkin (home/school/pickup).
/// </summary>
public record StudentLocationPinDto(
    string StudentId, string FullName, string ClassName, string Kind,
    double Latitude, double Longitude, string? Name,
    string? PickupFrom, string? PickupTo);

/// <summary>Ota-ona bo'limidagi bitta farzand (qisqacha) + qurilma ma'lumoti.</summary>
public record ParentChildDto(
    string StudentId, string FullName, string ClassName,
    string? FirstLoginAt, string? LastLoginAt,
    string DeviceName = "", string Platform = "", string AppId = "");

/// <summary>
/// Admin "Ota-onalar" bo'limidagi bir ota-ona qatori — telefon bo'yicha guruhlangan.
/// IsActivated = farzandlardan kamida bittasi ilovaga kirgan (FirstLoginAt mavjud).
/// ActivatedAt = farzandlar orasida eng erta FirstLoginAt; LastSeenAt = eng kech LastLoginAt.
/// DeviceName/Platform = oxirgi faol qurilma (farzandlar bo'yicha).
/// </summary>
public record ParentRowDto(
    string FullName, string Phone, int ChildrenCount,
    bool IsActivated, string? ActivatedAt, string? LastSeenAt,
    List<ParentChildDto> Children, string DeviceName = "", string Platform = "");

/// <summary>Admin "Ilova → O'qituvchilar" bo'limidagi bir o'qituvchi qatori (ilova faolligi + qurilma).</summary>
public record TeacherAppRowDto(
    string TeacherId, string FullName, string Phone,
    bool IsActivated, string? ActivatedAt, string? LastSeenAt,
    string DeviceName, string Platform, string AppId);
/// <summary>Sinf hisobotidagi bitta o'quvchi qatori. Studentga SubGroup ham kiradi (StudentDto orqali).</summary>
public record ClassStudentRowDto(StudentDto Student, Dictionary<string, double> Grades, double Average, double? Attendance);
public record ClassPerformanceDataDto(List<SubjectDto> Subjects, List<ClassStudentRowDto> Rows);
public record ClassStatsDto(int StudentsCount, double AverageGrade, double? Attendance);
public record StudentRatingRowDto(StudentDto Student, string ClassName, int Grade, double Average, double? Attendance);
/// <summary>
/// O'zlashtirish hisobotidagi bitta qator: sinf, parallel (daraja), ta'lim bosqichi yoki maktab.
/// Kind: class | parallel | level | school. ShowCategories=false bo'lsa kategoriya kataklari bo'sh.
/// </summary>
public record GradesProgressRowDto(
    string Kind, string Label, string Language, int Total, bool ShowCategories,
    int ExcellentCount, double ExcellentPct, string ExcellentNames,
    int GoodCount, double GoodPct,
    int SatisfactoryCount, double SatisfactoryPct,
    int PoorCount, double PoorPct, string PoorNames,
    double AvgRating, double QualityPct, double OtmPct);

/// <summary>Maktab bo'yicha o'zlashtirish hisoboti (tanlangan sinflar + choraklar).</summary>
public record GradesProgressReportDto(int TotalStudents, int NoGradesCount, List<GradesProgressRowDto> Rows);

/// <summary>Sinf bo'yicha hisobot uchun bitta o'quvchi: fan→chorak→o'rtacha baho (faqat mavjudlari).</summary>
public record ClassReportStudentDto(
    string Id, string FullName, Dictionary<string, Dictionary<int, double>> Averages);
/// <summary>Sinf bo'yicha hisobotning xom ma'lumoti (o'quvchilar × fanlar × choraklar o'rtacha baholari).</summary>
public record ClassReportDto(
    string ClassId, string ClassName, int Grade, string Language, string HomeroomTeacher,
    List<SubjectDto> Subjects, List<ClassReportStudentDto> Students);

/// <summary>O'quvchi davomati — har metrika chorak (1-4) → son ko'rinishida.</summary>
public record StudentAttendanceDto(
    Dictionary<int, int> MissedDays, Dictionary<int, int> IllnessDays,
    Dictionary<int, int> MissedLessons, Dictionary<int, int> IllnessLessons,
    Dictionary<int, int> LateCount);
/// <summary>Bitta o'quvchining o'zlashtirish va qatnashish hisoboti.</summary>
public record StudentReportDto(
    string StudentId, string FullName, string ClassName, string HomeroomTeacher, string ParentFullName,
    List<SubjectDto> Subjects, Dictionary<string, Dictionary<int, double>> Grades,
    StudentAttendanceDto Attendance);

/// <summary>O'quvchining bitta oydagi baholash turlari bo'yicha baholari.</summary>
public record MonthlyEvaluationDto(string Month, Dictionary<string, int> Grades, double Avg);
/// <summary>O'quvchining bitta chorakdagi uy vazifa/xulq jamlamasi.</summary>
public record QuarterMarksDto(int Quarter, int HomeworkDone, int HomeworkMissed, int BehaviorGood, int BehaviorBad);

/// <summary>
/// O'quvchi shaxsiy daftari — bitta o'quvchi haqida BARCHA ma'lumot (profil, o'zlashtirish,
/// davomat, intizom, topshiriqlar, oylik baholash, uy vazifa va xulq).
/// </summary>
public record StudentNotebookDto(
    // Profil
    string Id, string FullName, string ClassName, string HomeroomTeacher,
    string ParentFullName, string ParentPhone, string Gender, string BirthDate,
    string EnrollmentDate, decimal Balance, string? PhotoUrl,
    // Shaxsiy ma'lumotlar. Chegirma bu yerda YO'Q (P1-21): u endi `discounts`
    // jadvalida, direktor tasdig'i bilan — "Moliya → Chegirmalar" ekranida.
    string Address,
    int SubGroup, string? ParentPassportUrl,
    // O'zlashtirish
    List<SubjectDto> Subjects, Dictionary<string, Dictionary<int, double>> Grades, double AvgGrade,
    // Davomat
    StudentAttendanceDto Attendance, int Conducted, int Attended, int AttendancePct,
    List<AttendanceReasonCountDto> Reasons,
    // Intizom
    int DisciplineScore, int DisciplinePlus, int DisciplineMinus, List<DisciplinePointDto> DisciplinePoints,
    // Topshiriqlar
    StudentAssignmentScoresDto Assignments,
    // Oylik baholash — umumiy (fanlar o'rtachasi) + fan kesimida
    List<EvaluationTypeDto> EvaluationTypes, List<MonthlyEvaluationDto> Evaluations,
    List<SubjectEvaluationDto> EvaluationsBySubject,
    // Uy vazifa + xulq (choraklik)
    int HomeworkDone, int HomeworkMissed, int BehaviorGood, int BehaviorBad, List<QuarterMarksDto> MarksTrend);

/// <summary>Portal reytingidagi bitta qator (o'quvchi/parent ko'rinishi — shaxsiy ma'lumotsiz: telefon/balans/manzil yo'q).</summary>
public record PortalRatingRowDto(
    int Rank, string StudentId, string FullName, string ClassName, double Average, double? Attendance);

/// <summary>
/// O'quvchi/parent reytingi (adminniki bilan bir xil hisob, o'rtacha baho bo'yicha): o'z sinfi TO'LIQ
/// ranglangan, maktab bo'yicha esa faqat TOP 15. `MeStudentId` — o'z qatorini ajratish uchun;
/// `MeSchoolRank` top 15 dan tashqarida bo'lsa ham o'quvchining maktab o'rnini beradi (`SchoolSize` — jami).
/// </summary>
public record PortalRatingDto(
    string MeStudentId,
    List<PortalRatingRowDto> ClassRows,
    List<PortalRatingRowDto> SchoolRows,
    int? MeSchoolRank, int SchoolSize);

/* ---------- Yangi o'quv yiliga o'tish ---------- */
public record AcademicYearInfoDto(
    string CurrentYear, int Students, int Classes, int JournalEntries,
    int WeekAssignments, int Payments);
public record ArchiveListItemDto(
    string Id, string Year, string CreatedAt,
    int StudentsCount, int ClassesCount, int JournalCount, int FinanceCount);
public record RolloverRequest(
    string NewYear, bool PromoteStudents,
    bool ClearGrades, bool ClearSchedule, bool ClearQuarters, bool ClearFinance);
public record RolloverResultDto(string OldYear, string NewYear, int Promoted, int Graduated);

/// <summary>Maktab ma'lumotlari (profil sozlamasi).</summary>
public record SchoolInfoDto(
    string Name, string Director, string Phone, string Email,
    string Address, string Region, string District);
/// <summary>Maktab nomi (brending — barcha foydalanuvchilar uchun).</summary>
public record SchoolNameDto(string Name);
/// <summary>Telegram bot sozlamasi (admin). Configured = token bo'sh emasligini bildiradi.</summary>
public record TelegramSettingsDto(string BotToken, string BotUsername, string BotName, bool Configured);
/// <summary>Telegram bot sozlamasini saqlash so'rovi.</summary>
public record SaveTelegramSettingsRequest(string? BotToken, string? BotUsername, string? BotName);
/// <summary>Firebase (FCM push) sozlamasi. Configured = service account JSON to'g'ri kiritilgan.</summary>
public record FirebaseSettingsDto(
    string ServiceAccountJson, bool Configured,
    string WebConfigJson, string VapidKey, bool WebConfigured);
public record SaveFirebaseSettingsRequest(
    string? ServiceAccountJson, string? WebConfigJson, string? VapidKey);
/// <summary>Web (PWA) push uchun klient konfiguratsiyasi — brauzer FCM token olishi uchun.</summary>
public record PushClientConfigDto(bool Enabled, string WebConfigJson, string VapidKey);

/// <summary>Turniket/FaceID integratsiya sozlamasi (o'qituvchilar davomati avtomatik).
/// Parol javobda BO'SH qaytadi (xavfsizlik); HasPassword saqlanganini bildiradi.</summary>
public record TurnstileSettingsDto(
    bool Enabled, string Vendor, string Host, int Port, string Username, bool HasPassword,
    string WorkStartTime, int LateGraceMinutes, string LastSync,
    List<TeacherDeviceMapDto> Teachers);
/// <summary>O'qituvchi ↔ qurilma ID moslamasi.</summary>
public record TeacherDeviceMapDto(string TeacherId, string FullName, string DeviceUserId);
/// <summary>Turniket sozlamasini saqlash so'rovi. Password null/bo'sh = o'zgartirilmaydi (eski saqlanadi).</summary>
public record SaveTurnstileSettingsRequest(
    bool Enabled, string? Vendor, string? Host, int? Port, string? Username, string? Password,
    string? WorkStartTime, int? LateGraceMinutes, List<TeacherDeviceMapDto>? Teachers);

/* ---------- Finance (Moliya) ----------
   P1-21: `FinanceTransactionDto`, `FinanceTransactionPayload`, `FinanceSummaryDto`,
   `FinanceMonthlyDto`, `CategoryAmountDto`, `AccrueResultDto` va
   `StudentFinanceRowDto` o'chirildi — ular `finance_transactions` va o'quvchi
   qatoridagi qoldiqqa tayanardi. O'rniga jurnaldan hisoblanadigan hisobotlar:
     · Foyda va zarar  -> GET /api/admin/finance/pnl
     · Pul oqimi       -> GET /api/admin/finance/cashflow
     · Qarzdorlar      -> GET /api/admin/finance/debtors
     · Yig'ilish foizi -> GET /api/admin/finance/collection-rate
     · Pul aylanmasi   -> GET /api/admin/finance/money-flow
   Maosh hisoboti (`SalaryReportRowDto`) qoldi — u endi `expenses` dan o'qiydi. */

/* ---------- O'quvchi to'lov tarixi (ledger) ---------- */
/// <summary>Bitta oyning hisobi — o'sha oyning BARCHA toifadagi hisob-fakturalari yig'indisi.
/// Charged = to'liq summa (chegirmasiz); Discount = qo'llangan chegirma;
/// Paid = shu oy hisob-fakturalariga taqsimlangan pul; Remaining = Charged − Discount − Paid
/// (manfiy bo'lsa 0). Manba: `invoices` + `payment_allocations` (P1-21).</summary>
public record MonthLedgerDto(
    string Month, decimal Charged, decimal Discount, decimal Paid, decimal Remaining, string Status);
/// <summary>
/// Daftar (ledger) ko'rinishidagi bitta to'lov qatori.
///
/// <para>
/// <b>Storno holati IZOHDA emas, bayroqda.</b> Ilgari <c>StudentLedger</c> izoh
/// matniga <c>[STORNO]</c> deb yozib qo'yardi: ekran uni ajratib ko'rsata
/// olmasdi, filtrlay olmasdi, va kassir izohga o'sha so'zni yozsa qator yolg'on
/// ko'rinardi. Endi ikkita bayroq bor va izoh — faqat izoh.
/// </para>
/// </summary>
/// <param name="IsReversal">Bu qatorning O'ZI storno (bekor qiluvchi yozuv).</param>
/// <param name="Reversed">Bu to'lov keyinchalik storno qilingan.</param>
public record PaymentDto(
    string Date, decimal Amount, string? Note, string? Month,
    bool IsReversal = false, bool Reversed = false);
public record StudentLedgerDto(
    StudentDto Student, decimal Balance, decimal MonthlyFee,
    decimal TotalCharged, decimal TotalDiscount, decimal TotalPaid,
    List<MonthLedgerDto> Months, List<PaymentDto> Payments);

/* ---------- O'zgarishlar tarixi (audit) ---------- */
public record AuditLogDto(
    string Id, string EntityType, string EntityId, string Action, string Timestamp,
    string? ActorName, string Summary, string? Before, string? After,
    string? StudentId, string? TeacherId);

/* ---------- Teacher portal (ilova) ---------- */
/// <summary>O'qituvchining o'z profili (ilovada ko'rsatish uchun).</summary>
public record TeacherProfileDto(
    string Id, string FullName, string Email, string HomeroomClass, List<SubjectDto> Subjects,
    List<string> Permissions, string? PhotoUrl = null);
/// <summary>
/// O'qituvchi yetadigan bitta EGA — sinf yoki o'quv guruhi (qaysi fanlarni
/// o'qitadi va sinf rahbarimi / guruhga biriktirilganmi).
/// </summary>
/// <param name="OwnerKind">
/// <c>class</c> | <c>group</c> (<c>LessonOwnerKind</c>; students-parity.md
/// §2.1.4). Guruhda <paramref name="ClassId"/> guruh id'sini,
/// <paramref name="ClassName"/> guruh nomini, <paramref name="Grade"/> esa 0 ni
/// saqlaydi. Oxirida turibdi va sukuti <c>class</c> — eski mijoz kodi buzilmaydi.
/// </param>
/// <param name="IsHomeroom">
/// Sinfda — sinf rahbari; guruhda — guruhga biriktirilgan o'qituvchi.
/// </param>
public record TeacherClassDto(
    string ClassId, string ClassName, int Grade, bool IsHomeroom, List<SubjectDto> Subjects,
    string OwnerKind = SchoolLms.Domain.LessonOwnerKind.Class);
/// <summary>
/// O'qituvchining O'Z o'quv guruhi — "Guruhlarim" sahifasi (X-3,
/// students-parity.md §2.11). <c>/teacher/classes</c> ham guruhlarni beradi (ro'yxat
/// uchun), bu DTO esa guruh ro'yxatini (roster) boshqarish sahifasiga xos maydonlar bilan.
/// </summary>
/// <param name="CanEditRoster">
/// Ro'yxatni TAHRIRLASH (qo'shish/chiqarish) huquqi bormi. X-3 qarori: bunday alohida
/// ruxsat kaliti (TeacherPermissions) hali yo'q, shuning uchun ENG XAVFSIZ o'qish
/// tanlandi — FAQAT guruhga BIRIKTIRILGAN o'qituvchiga (<c>study_group_teachers</c>;
/// <c>TeacherOwner.IsHomeroom</c> guruh uchun aynan shu ma'noni bildiradi) true.
/// Faqat jadvalda darsi bor, biriktirilmagan o'qituvchi ro'yxatni FAQAT ko'radi — xuddi
/// sinf rahbarligi/dars beruvchi nomutanosibligiga o'xshab (<c>TeacherOwnerAccess.cs</c>
/// fayl boshidagi izoh).
/// </param>
public record TeacherGroupDto(
    Guid Id, string Name, string SubjectId, string SubjectName, string? Gender,
    List<StudyGroupClassRefDto> Classes, int MemberCount, bool CanEditRoster);
/// <summary>
/// O'qituvchi jadvalidagi bitta dars (qaysi sinf, fan, kun, dars raqami, vaqt, guruh).
///
/// <para>
/// <paramref name="OwnerKind"/> — darsning EGASI sinfmi yoki o'quv guruhimi
/// (<c>LessonOwnerKind</c>; students-parity.md §2.1.4). Guruh darsida
/// <paramref name="ClassId"/> guruh id'sini, <paramref name="ClassName"/> esa
/// guruh nomini saqlaydi — jadval ustunlari o'zgarmasligi uchun. Oxirida
/// turibdi va sukut qiymati bor: eski mijoz kodini buzmaydi.
/// </para>
/// </summary>
public record TeacherLessonDto(
    int Day, int Period, string? StartTime, string? EndTime,
    string ClassId, string ClassName, string SubjectId, string SubjectName, int SubGroup = 0,
    string OwnerKind = "class");

/* ---------- Student portal (ilova) ---------- */
/// <summary>O'quvchining o'z profili (ilovada ko'rsatish uchun).</summary>
public record StudentProfileDto(
    string Id, string FullName, string ClassName, string BirthDate, string Gender,
    string ParentFullName, string ParentPhone, string EnrollmentDate,
    string? PhotoUrl = null, string? ParentPhotoUrl = null);
/// <summary>O'quvchi jadvalidagi bitta dars (fan, o'qituvchi, kun, dars raqami, vaqt).</summary>
public record StudentLessonDto(
    int Day, int Period, string? StartTime, string? EndTime,
    string SubjectId, string SubjectName, string TeacherId, string TeacherName, int SubGroup = 0,
    // G-18: dars sinfniki yoki GURUHniki (LessonOwnerKind). Guruh darsida ekran
    // guruh nomini yorliq qilib ko'rsatadi; eski mijozlar maydonni e'tiborsiz
    // qoldiradi va bugungidek ishlaydi.
    string OwnerKind = "class", string? OwnerName = null);
/// <summary>O'quvchi uchun dars mavzusi va uyga vazifa (sana + fan bo'yicha).
/// Shu o'quvchining o'sha (sana + dars raqami) jurnal yozuvi bo'lsa — Grade va Reason ham
/// bog'lab qaytariladi (bugungi/haftalik baholarni alohida endpoint'siz ko'rsatish uchun).</summary>
public record HomeworkItemDto(
    string Date, int Period, string SubjectId, string SubjectName,
    string Topic, string? Homework, bool Conducted,
    int? Grade, string? ReasonId, string? ReasonName, bool IsLate,
    // G-18: dars egasi — sinf yoki guruh.
    string OwnerKind = "class", string? OwnerName = null);

/// <summary>O'quvchi jurnali — bitta dars qatori (sana + dars raqami + fan + o'qituvchi + mavzu/uyga vazifa + baho/sabab).</summary>
public record StudentJournalRowDto(
    string Date, int Period, int Quarter, int Week,
    string? StartTime, string? EndTime,
    string SubjectId, string SubjectName,
    string? TeacherId, string? TeacherName,
    string Topic, string? Homework, bool Conducted,
    int? Grade, string? ReasonId, string? ReasonName, bool IsLate,
    // G-18: dars egasi — sinf yoki guruh.
    string OwnerKind = "class", string? OwnerName = null);

/// <summary>O'quvchining bitta davomatsizlik (yoki kech qolish) yozuvi.</summary>
public record StudentAbsenceRowDto(
    string Date, int Period, int Quarter,
    string SubjectId, string SubjectName,
    string ReasonId, string ReasonName, bool IsLate, bool IsIll);

/// <summary>O'quvchi davomati — chorak bo'yicha umumiy + kunlik ro'yxat.</summary>
public record StudentAttendanceFullDto(
    StudentAttendanceDto Summary, List<StudentAbsenceRowDto> Rows);

/// <summary>Bosh sahifa uchun yagona payload — bir chaqiruvda hammasi.</summary>
public record StudentDashboardDto(
    StudentProfileDto Profile, PortalMetaDto Meta,
    List<StudentLessonDto> TodayLessons, List<HomeworkItemDto> TodayGrades,
    int PendingAssignmentsCount, decimal Balance, decimal MonthlyFee);

/// <summary>O'quvchi/foydalanuvchi shaxsiy sozlamasi (til, tema, bildirishnoma).</summary>
public record UserSettingsDto(string Language, string Theme, bool NotificationsEnabled);

/* ---------- Farzandni olib ketish (pickup) ---------- */
/// <summary>Ota-ona "Farzandimni olishga keldim" so'rovi (ixtiyoriy studentId — bir nechta farzand bo'lsa).</summary>
public record CreatePickupRequest(string? StudentId);
/// <summary>Pickup so'rovi holati. Status: "pending" | "accepted".</summary>
public record PickupRequestDto(
    string Id, string StudentId, string StudentName, string ClassName, string Status,
    string CreatedAt, string? AcceptedAt, string? AcceptedByName);
/// <summary>Sinf rahbarligi ro'yxatidagi bitta o'quvchi — ota-onasi kelgan (pending) bo'lsa belgilanadi.</summary>
public record HomeroomStudentDto(
    string StudentId, string FullName, bool HasPendingPickup, string? Status, string? RequestedAt);
/// <summary>Sinf rahbari farzandni ota-onasiga topshirish so'rovi.</summary>
public record HandoverRequest(string StudentId);
/// <summary>Sozlamani yangilash so'rovi. Berilgan maydonlar yangilanadi, qolganlari saqlanadi.</summary>
public record SaveUserSettingsRequest(string? Language, string? Theme, bool? NotificationsEnabled);

/// <summary>Push qurilma tokenini ro'yxatdan o'tkazish so'rovi.</summary>
public record RegisterDeviceRequest(string Token, string? Platform, string? DeviceName, string? AppId);

/// <summary>Portal umumiy konteksti: choraklar, dars vaqtlari, davomat sabablari + joriy chorak/hafta.</summary>
public record PortalMetaDto(
    List<QuarterPeriodDto> Quarters, List<LessonTimeDto> LessonTimes,
    List<AbsenceReasonDto> AbsenceReasons, int CurrentQuarter, int CurrentWeek);

/* ---------- O'quvchi: topshiriqlar/testlar (xavfsiz — to'g'ri javob OSHKOR QILINMAYDI) ---------- */

/// <summary>Test savoli o'quvchi uchun — to'g'ri javob indeksi BERILMAYDI.</summary>
public record StudentTestQuestionDto(string Id, string Text, List<string> Options);
/// <summary>O'quvchi topshiriqlar ro'yxatidagi element (o'z holati bilan).</summary>
public record StudentAssignmentDto(
    string Id, string SubjectName, string Title, string Description, string Format,
    string? StartDate, string? DueDate, bool LateAccept, int LatePenaltyPct, int MaxScore,
    int QuestionCount, List<AssignmentMaterialDto> Materials,
    bool Completed, string? SubmittedAt, int? Score);
/// <summary>O'quvchi topshiriq tafsiloti (test bo'lsa — javobsiz savollar bilan).</summary>
public record StudentAssignmentDetailDto(
    string Id, string SubjectName, string Title, string Description, string Format,
    string? StartDate, string? DueDate, bool LateAccept, int LatePenaltyPct, int MaxScore,
    List<AssignmentMaterialDto> Materials, List<StudentTestQuestionDto> Questions,
    bool Completed, string? SubmittedAt, int? Score, string? AnswerText, string? FileUrl);
/// <summary>Test javobi: savol id + tanlangan variant indeksi.</summary>
public record TestAnswerInput(string QuestionId, int SelectedIndex);
/// <summary>Topshiriqni topshirish: test uchun Answers; yozma uchun AnswerText; fayl/video uchun FileUrl.</summary>
public record SubmitAssignmentRequest(
    List<TestAnswerInput>? Answers, string? AnswerText, string? FileUrl);
/// <summary>Topshirish natijasi: test bo'lsa ball + to'g'ri/jami.</summary>
public record SubmitResultDto(bool Completed, int? Score, int? CorrectCount, int? Total);

/* ---------- Attendance ---------- */
public record ReasonCountDto(string Name, int Count);
/// <param name="Marked">
/// Davomat shu dars uchun BELGILANGANMI (`daily_attendance_marks`). Hisobotda
/// kerak: yo'qlar soni 0 bo'lgan dars "hammasi keldi" ni ham, "hali hech kim
/// belgilamagan" ni ham bildirishi mumkin edi — zavuch uchun bu ikkisi
/// butunlay boshqa narsa (izoh: `SchoolLms.Domain/DailyAttendance.cs`).
/// </param>
public record SubjectAttendanceDto(
    string SubjectId, string SubjectName, int Period, int Total, int Present, int Absent,
    List<ReasonCountDto> Reasons, bool Marked = false);
public record DailyAttendanceDto(int Total, List<SubjectAttendanceDto> Subjects);
public record StudentStatusDto(StudentDto Student, bool Absent, string? ReasonName);

/* ---------- Messaging (chat + e'lon + telegram) ---------- */

/// <summary>Sinf guruh chatidagi bitta xabar. CreatedAt — ISO 8601 ("o" formati).</summary>
public record ChatMessageDto(
    string Id, string ClassName, string SenderUserId, string SenderName,
    string SenderRole, string Text, string CreatedAt);

/// <summary>Chatga xabar yuborish so'rovi (sinf URL'dan keladi).</summary>
public record SendChatRequest(string Text);

/// <summary>Admin uchun sinf chat/e'lon ro'yxati elementi.</summary>
public record ChatClassDto(
    string Name, int Grade, int StudentCount, int ParentCount, string? LastMessageAt);

/// <summary>
/// Bitta chat kanali — kalit va odam ko'radigan nom (G-17).
///
/// <para>
/// Sinf kanalida <paramref name="Key"/> sinf NOMI ("5-A"), ya'ni nom bilan bir
/// xil. O'quv guruhida esa kalit <c>grp:&lt;guruh id&gt;</c> — u ekranda
/// ko'rsatilmaydi, foydalanuvchi <paramref name="Label"/> (guruh nomi) ni
/// ko'radi. Xodimlar kanali — <c>__xodimlar__</c>.
/// </para>
/// </summary>
/// <param name="Kind"><c>class</c> | <c>group</c> | <c>staff</c>.</param>
public record ChatChannelDto(string Key, string Label, string Kind);

/// <summary>Yuborilgan e'lon (Telegram). CreatedAt — ISO 8601.</summary>
public record BroadcastDto(
    string Id, string ClassName, string Text, string SenderName, string CreatedAt,
    int RecipientCount, int SentCount);

/// <summary>
/// E'lon yuborish so'rovi. <c>Scope</c>: "class" (ClassName sinfi), "group" (GroupId o'quv
/// guruhining FAOL a'zolari), "all" (barcha sinf), "selected" (StudentIds tanlangan
/// o'quvchilar), "filter" (Filter'ga mos BARCHA o'quvchi, S-6). <c>OnlyDebtors</c> — faqat
/// balansi manfiylar.
/// <c>Text</c> ichida o'rinbosarlar bo'lishi mumkin: {fish} {sinf} {qarzdorlik} {balans} {ota-ona} {telefon}.
///
/// <para>
/// <c>GroupId</c> ATAYLAB oxirgi va ixtiyoriy: e'lon HAQIQIY ota-onalarga Telegram
/// xabari yuboradi, shuning uchun guruh qamrovi faqat chaqiruvchi uni ANIQ
/// so'raganda (<c>scope: "group"</c> + guruh id'si) ishlaydi — sukut bo'yicha hech
/// narsa o'zgarmaydi.
/// </para>
/// </summary>
public record SendBroadcastRequest(
    string? Scope, string? ClassName, bool OnlyDebtors, List<string>? StudentIds, string Text,
    string? GroupId = null,
    // S-6 (students-parity.md §2.3.3) — scope === "filter" bo'lganda ro'yxat
    // ekranidagi JORIY filtr shu yerdan keladi; qamrov `StudentListQuery`
    // orqali hisoblanadi, ya'ni ekran nechta o'quvchini ko'rsatsa, xabar ham
    // AYNAN o'shalarga boradi (tanlangan qatorlardan mustaqil).
    StudentListFilter? Filter = null);

/// <summary>
/// Telegramda ro'yxatdan o'tgan ota-ona. ChatId string (JS aniqligi uchun).
/// Balance — qarz aniqlash uchun; moliya ruxsati bo'lmagan chaqiruvchi uchun
/// <c>null</c> (nol emas — nol "qarzi yo'q" degan yolg'on ma'no berardi).
/// </summary>
public record TelegramParentDto(
    string StudentId, string StudentName, string ClassName, decimal? Balance,
    string ParentName, string Phone, string ChatId, string CreatedAt);

/// <summary>
/// Ilovaga push yuborish so'rovi. Audience: "parents" (ClassName ixtiyoriy) | "teachers" |
/// "selected" (UserIds tanlangan foydalanuvchilar).
/// </summary>
public record SendPushRequest(string Audience, string? ClassName, List<string>? UserIds, string Title, string Body);
/// <summary>Push uchun tanlanadigan oluvchi. UserId — akkaunt id; HasDevice = qurilma ulangan (push yetadi).</summary>
public record PushRecipientDto(string UserId, string Name, string Group, string Detail, bool HasDevice);
/// <summary>Yuborilgan push (tarix). CreatedAt — ISO.</summary>
public record PushMessageDto(
    string Id, string Audience, string Title, string Body, string SenderName, string CreatedAt,
    int RecipientCount, int SentCount);

/* ---------- Assignments (qo'shimcha topshiriqlar) ---------- */

/// <summary>Topshiriqqa biriktirilgan material (yuklangan fayl yoki havola).</summary>
public record AssignmentMaterialDto(string Id, string Name, string Url, long Size, string ContentType);
/// <summary>Test savoli (format=test).</summary>
public record TestQuestionDto(string Id, string Text, List<string> Options, int CorrectIndex, int Order);
/// <summary>
/// Topshiriq/test (to'liq). Format: written|file|test|video. CreatedAt/Start/Due — ISO.
/// </summary>
/// <param name="ClassIds">
/// G-20: <see cref="OwnerKind"/>="group" bo'lsa — o'quv GURUH id'lari (§2.1.4
/// naqshi: guruh id'si xuddi shu ustunda saqlanadi, alohida ustun yo'q).
/// </param>
/// <param name="ClassNames">
/// Ko'rsatiladigan nomlar — <see cref="OwnerKind"/> qaysi bo'lsa, o'sha
/// turdagi (sinf yoki guruh) nomlar. Chaqiruvchi tomonda ikkalasi ham bir xil
/// "yorliqlar ro'yxati" sifatida chiziladi.
/// </param>
/// <param name="OwnerKind"><see cref="SchoolLms.Domain.LessonOwnerKind"/> — "class" | "group".</param>
public record AssignmentDto(
    string Id, string CreatedByUserId, string SubjectId, string SubjectName, string Title,
    string Description, string Format, List<string> ClassIds, List<string> ClassNames,
    string? StartDate, string? DueDate, bool LateAccept, int LatePenaltyPct, int MaxScore,
    bool AutoGrade, string CreatedAt,
    List<AssignmentMaterialDto> Materials, List<TestQuestionDto> Questions,
    string OwnerKind = "class");
public record MaterialInput(string Name, string Url, long Size, string ContentType);
public record QuestionInput(string Text, List<string> Options, int CorrectIndex);
/// <summary>
/// Topshiriq yaratish/tahrirlash so'rovi (ham create, ham update).
/// </summary>
/// <param name="OwnerKind">
/// G-20: topshiriq SINFGA beriladimi yoki o'quv GURUHIGA —
/// <see cref="SchoolLms.Domain.LessonOwnerKind"/> ("class" | "group").
/// null/bo'sh/noma'lum qiymat — "class" (bugungi xatti-harakat, eski
/// mijoz — o'qituvchi portali — bu maydonni umuman yubormaydi).
/// </param>
public record SaveAssignmentRequest(
    string SubjectId, string Title, string? Description, string Format, List<string> ClassIds,
    string? StartDate, string? DueDate, bool LateAccept, int LatePenaltyPct, int MaxScore,
    bool AutoGrade, List<MaterialInput>? Materials, List<QuestionInput>? Questions,
    string? OwnerKind = null);
/// <summary>Yuklangan fayl haqida ma'lumot (upload javobida).</summary>
public record UploadedFileDto(string Name, string Url, long Size, string ContentType);

/* ---------- Shartnomalar ---------- */

/// <summary>Yuklangan Word andoza (ota-ona/xodim).</summary>
public record ContractTemplateDto(string Id, string Target, string Name, string FileUrl, string FileName, string UploadedAt);
/// <summary>Shartnoma andozasini yaratish so'rovi (fayl avval /api/admin/uploads orqali yuklanadi).</summary>
public record CreateContractTemplateRequest(string Target, string Name, string FileUrl, string FileName);
/// <summary>Ota-ona oluvchi qatori (telefon bo'yicha guruhlangan).</summary>
public record ParentRecipientDto(
    string Key, string ParentName, string Phone, List<string> Children, bool Registered, int? LastNumber);
/// <summary>Xodim oluvchi qatori.</summary>
public record StaffRecipientDto(
    string TeacherId, string FullName, string Phone, bool Registered, int? LastNumber);
/// <summary>Shartnoma yuborish so'rovi.</summary>
public record SendContractsRequest(string Target, string TemplateId, List<string> RecipientKeys);
/// <summary>Bitta oluvchi uchun yuborish natijasi.</summary>
public record SendResultDto(string RecipientKey, bool Ok, int? Number, string Message);

/* ---------- Boshqaruv ---------- */

/// <summary>Filial (branch).</summary>
public record BranchDto(
    string Id, string Name, string Address, double Latitude, double Longitude,
    int RadiusMeters, string CreatedAt);
public record BranchPayload(
    string Name, string Address, double Latitude, double Longitude, int RadiusMeters);

/// <summary>Xodim (o'qituvchi bo'lmagan ishchi) — admin akkaunti bilan.</summary>
/// <param name="AvatarUrl">Profil rasmi (<c>/uploads/…</c>) — admin qo'yadi yoki xodimning o'zi.</param>
public record StaffDto(string Id, string FullName, string Position, string Login, List<string> Permissions,
    string? AvatarUrl = null, Guid? AccessRoleId = null, string? AccessRoleName = null, string? LastLoginAt = null);
/// <param name="AvatarUrl">null — o'zgarmaydi; "" — olib tashlanadi; "/uploads/…" — yangi rasm.</param>
public record StaffPayload(string FullName, string Position, string? NewPassword = null, string? AvatarUrl = null);
/// <summary>Xodimning admin bo'lim ruxsatlari (faqat superadmin o'zgartiradi).</summary>
public record SetStaffPermissionsRequest(List<string> Permissions);
/// <summary>Assign an access role to a staff member; null removes it (and its permissions).</summary>
public record SetStaffRoleRequest(Guid? AccessRoleId);

/// <summary>Staff access role (Boshqaruv → Rollar).</summary>
public record AccessRoleDto(Guid Id, string Name, string Description, List<string> Permissions, int StaffCount);
public record AccessRolePayload(string Name, string? Description, List<string>? Permissions);

/// <summary>Taklif/shikoyat — admin ko'rinishi uchun (yuboruvchi roli/ismi + ixtiyoriy rasm bilan).</summary>
public record FeedbackDto(
    string Id, string StudentName, string ParentName, string ClassName,
    string Type, string Text, string CreatedAt, string Status,
    string SenderRole, string SenderName, string? ImageUrl);
/// <summary>Ota-ona ilovasidan taklif/shikoyat yuborish (matn; rasm multipart `image` orqali).</summary>
public record SubmitFeedbackRequest(string Type, string Text);
/// <summary>
/// Topshiriq natijasi — bitta o'quvchining holati: bajardimi, qachon, qancha ball (Score) hamda
/// yuborgan javobi (AnswerText — yozma) yoki fayli (FileUrl — fayl/video). Bularni o'qituvchi
/// ko'rib baholaydi; test esa avto-baholangan ball bilan keladi.
/// </summary>
public record SubmissionRowDto(
    string StudentId, string StudentName, string ClassName, bool Completed, string? SubmittedAt,
    int? Score, string? AnswerText, string? FileUrl);
/// <summary>
/// Topshiriq bo'yicha natijalar: jami / bajarganlar soni / har o'quvchi holati.
/// Format + MaxScore ballni to'g'ri ko'rsatish va javob turini (matn/fayl) bilish uchun.
/// </summary>
public record AssignmentResultDto(
    string AssignmentId, string Title, string Format, int MaxScore,
    int Total, int CompletedCount, List<SubmissionRowDto> Rows);
/// <summary>O'quvchi holatini belgilash (o'qituvchi).</summary>
public record SetSubmissionRequest(bool Completed, int? Score);
/// <summary>Topshiriq turi (Sozlamalarda boshqariladi — kategoriya; yangi forma ishlatmaydi).</summary>
public record AssignmentTypeDto(string Id, string Name);
public record SaveAssignmentTypesRequest(List<AssignmentTypeDto> Types);

/* ---------- Topshiriqlar bali (admin: sinf bo'yicha ball jadvali) ---------- */

/// <summary>Ball jadvalidagi ustun — bitta topshiriq.</summary>
public record AssignmentScoreColumnDto(
    string AssignmentId, string Title, string SubjectName, string Format, int MaxScore, string? DueDate);
/// <summary>Bitta katak — o'quvchining shu topshiriqdagi holati/bali.</summary>
public record AssignmentScoreCellDto(string AssignmentId, bool Completed, int? Score);
/// <summary>Bitta qator — o'quvchi va uning barcha topshiriqlardagi ballari.</summary>
public record AssignmentScoreRowDto(
    string StudentId, string FullName, string ClassName,
    List<AssignmentScoreCellDto> Cells, int TotalScore, int TotalMax, int GradedCount);
/// <summary>
/// Ega (sinf yoki guruh, G-20) bo'yicha topshiriqlar ball jadvali (ustunlar = topshiriqlar,
/// qatorlar = o'quvchilar). <paramref name="ClassId"/>/<paramref name="ClassName"/> nomiga
/// qaramay — <c>AssignmentService.GetScoreboardAsync</c> guruh id berilsa guruhning
/// id'si/nomini qaytaradi (ustun nomlari o'zgartirilmadi — FE hech narsa buzmasin).
/// </summary>
public record AssignmentScoreboardDto(
    string ClassId, string ClassName,
    List<AssignmentScoreColumnDto> Assignments, List<AssignmentScoreRowDto> Students);

/* ---------- Topshiriq ballari (o'quvchi/ota-ona ko'rinishi) ---------- */

/// <summary>O'quvchining bitta topshiriqdagi bali.</summary>
public record StudentAssignmentScoreDto(
    string AssignmentId, string SubjectName, string Title, string Format,
    int MaxScore, int? Score, bool Completed, string? DueDate, string? SubmittedAt);
/// <summary>O'quvchining barcha topshiriqlari bo'yicha ballari + yig'ma.</summary>
public record StudentAssignmentScoresDto(
    int Count, int GradedCount, int TotalScore, int TotalMax,
    List<StudentAssignmentScoreDto> Items);

/* ---------- LMS (Ta'lim) ---------- */

/// <summary>LMS fani (admin ro'yxati va batafsil ko'rinish).</summary>
public record LmsSubjectDto(
    string Id, string ClassId, string ClassName,
    string Title, string Description,
    string UnlockMode, int BatchSize,
    int TopicsCount, string CreatedAt);

/// <summary>LMS moduli (admin) — fan ichidagi mavzular guruhi.</summary>
public record LmsModuleDto(
    string Id, string SubjectId, string Title, string Description, int Order, int TopicsCount);

/// <summary>LMS moduli yaratish/tahrirlash so'rovi.</summary>
public record SaveLmsModuleRequest(string Title, string? Description);

/// <summary>LMS modullar tartibini qayta belgilash.</summary>
public record ReorderLmsModulesRequest(List<string> ModuleIds);

/// <summary>LMS mavzusi (admin). Endi modulga tegishli (ModuleId).</summary>
public record LmsTopicDto(
    string Id, string ModuleId, string Title, string Description,
    string? VideoUrl, string? TextContent, int Order,
    List<LmsMaterialRowDto> Materials,
    int CompletedCount);

/// <summary>LMS material satri. Id so'rovda kelmasligi mumkin (yangi yuklangan fayl) —
/// server saqlashda o'zi yangi Id beradi, shu sabab nullable.</summary>
public record LmsMaterialRowDto(string? Id, string Name, string Url, long Size, string ContentType);

/// <summary>LMS fani yaratish/tahrirlash so'rovi.</summary>
public record SaveLmsSubjectRequest(
    string? ClassId, string Title, string? Description,
    string UnlockMode, int BatchSize);

/// <summary>LMS mavzusi yaratish/tahrirlash so'rovi.</summary>
public record SaveLmsTopicRequest(
    string Title, string? Description, string? VideoUrl, string? TextContent,
    List<LmsMaterialRowDto>? Materials);

/// <summary>LMS mavzular tartibini qayta belgilash.</summary>
public record ReorderLmsTopicsRequest(List<string> TopicIds);

/* ---------- Jadval — band soatlar (o'qituvchi mojarosi tekshiruvi) ---------- */

/// <summary>
/// O'qituvchining bitta band qilingan soati (boshqa template/sinf ichida).
/// Jadval yaratishda ziddiyat (conflict) tekshiruvi uchun ishlatiladi.
/// </summary>
public record OccupiedSlotDto(
    int Day, int Period, string ClassName, string TemplateName, string OwnerKind = "class");

/// <summary>O'quvchi uchun LMS mavzu (ochilganmi, tugallanganmi). Endi modulga tegishli (ModuleId).</summary>
public record StudentLmsTopicDto(
    string Id, string ModuleId, string Title, string Description,
    string? VideoUrl, string? TextContent, int Order,
    List<LmsMaterialRowDto> Materials,
    bool IsUnlocked, bool IsCompleted);

/// <summary>O'quvchi uchun LMS moduli — ichidagi mavzular (ochilish/progress bilan).</summary>
public record StudentLmsModuleDto(
    string Id, string Title, string Description, int Order,
    int TopicsCount, int CompletedCount, List<StudentLmsTopicDto> Topics);

/// <summary>O'quvchi uchun LMS fani ro'yxatdagi element.</summary>
public record StudentLmsSubjectDto(
    string Id, string Title, string Description,
    string UnlockMode, int BatchSize, int TopicsCount, int CompletedCount);

/* ---------- Fan progresi (dars o'tilishiga qarab — LMS'siz) ----------
   Reja (Planned) = chorakdagi sinf jadvalidagi shu fan dars kataklari soni.
   O'tilgan (Conducted) = o'qituvchi "dars o'tildi" deb belgilagan (LessonNote.Conducted) darslar.
   Progress = Conducted / Planned. */

/// <summary>O'quvchi/ota-ona uchun bitta fan progresi.</summary>
public record SubjectProgressDto(
    string SubjectId, string SubjectName,
    int Planned,          // chorakdagi jami reja darslar
    int Conducted,        // o'tilgan (belgilangan) darslar
    int Remaining,        // qolgan = Planned - Conducted
    int Percent,          // Conducted/Planned (0..100)
    int ExpectedByToday,  // shu kungacha bo'lishi kerak edi (orqada/oldinda aniqlash uchun)
    string? NextLessonDate,  // keyingi hali o'tilmagan reja dars sanasi (ISO) yoki null
    string? LastLessonDate); // chorakdagi oxirgi reja dars sanasi (ISO) yoki null

/// <summary>O'quvchining barcha fanlari bo'yicha umumiy + har bir fan progresi.</summary>
public record StudentSubjectsProgressDto(
    int Quarter,
    int TotalPlanned, int TotalConducted, int TotalPercent,
    List<SubjectProgressDto> Subjects);

/// <summary>Fan ichidagi bitta dars (yashil = o'tilgan, qizil = hali yo'q).</summary>
public record SubjectLessonDto(
    string Date, int Period, string? StartTime, string? EndTime,
    string Topic, string? Homework, bool Conducted, bool IsPast);

/// <summary>Fanga kirilganda — darslar ro'yxati va yig'ma sonlar.</summary>
public record SubjectProgressDetailDto(
    string SubjectId, string SubjectName, int Quarter,
    int Planned, int Conducted, int Remaining, int Percent,
    List<SubjectLessonDto> Lessons);

/// <summary>O'qituvchi progresi — bitta (sinf, fan, guruh) kesimi.</summary>
public record TeacherSubjectProgressDto(
    string ClassId, string ClassName, string SubjectId, string SubjectName, int SubGroup,
    int Planned, int Conducted, int Remaining, int Percent, int ExpectedByToday,
    // G-15: kesim sinfniki yoki GURUHniki (LessonOwnerKind).
    string OwnerKind = "class");

/// <summary>O'qituvchining umumiy o'tilgan darslar progresi + kesimlar bo'yicha yoyilma.</summary>
public record TeacherProgressDto(
    int Quarter,
    int TotalPlanned, int TotalConducted, int TotalPercent,
    List<TeacherSubjectProgressDto> Items);

/* ---------- LMS o'qituvchi progress hisoboti (faqat ko'rish) ---------- */

/// <summary>Progress jadvalidagi ustun — mavzu (qisqacha).</summary>
public record LmsTopicBriefDto(string Id, string Title, int Order);
/// <summary>Progress jadvalidagi qator — o'quvchi va u tugatgan mavzular.</summary>
public record LmsStudentProgressDto(
    string StudentId, string FullName, List<string> CompletedTopicIds,
    int CompletedCount, int TotalCount);
/// <summary>O'qituvchi LMS progress hisoboti: mavzular (ustun) × o'quvchilar (qator) matritsasi.</summary>
public record LmsProgressReportDto(
    List<LmsTopicBriefDto> Topics, List<LmsStudentProgressDto> Students);
