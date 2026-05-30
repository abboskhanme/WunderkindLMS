namespace SchoolLms.Server.Dtos;

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

/* ---------- Students ---------- */
/// <summary>
/// O'quvchi yaratish/tahrirlash so'rovi. FISH alohida-alohida kiritiladi (LastName/FirstName/MiddleName);
/// agar bo'lsa, ulardan FullName yig'iladi. Ota-ona FISH ham alohida.
/// FullName/ParentFullName ixtiyoriy — yo'q bo'lsa parts'dan yig'iladi.
/// </summary>
public record StudentPayload(
    string FullName, string BirthDate, string Address, string Gender,
    string ParentFullName, string ParentPhone, string ClassName, string? EnrollmentDate,
    string? NewPassword = null,
    int? DiscountPct = null, decimal? DiscountAmount = null, string? DiscountNote = null,
    int? SubGroup = null,
    string? LastName = null, string? FirstName = null, string? MiddleName = null,
    string? BirthCertificateUrl = null,
    string? ParentLastName = null, string? ParentFirstName = null, string? ParentMiddleName = null,
    string? ParentPassportUrl = null);
public record PaymentRequest(decimal Amount, string? Month);

/* ---------- Teachers ---------- */
public record TeacherPayload(
    string FullName, string BirthDate, string Address, string Gender,
    string HomeroomClass, List<string> SubjectIds, decimal Salary, string? SalaryStartMonth,
    string? NewPassword = null, List<string>? Permissions = null, string? Phone = null);
public record SalaryPaymentRequest(decimal Amount, string? Note);
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
/// <summary>Moliyada o'quvchi qatori. Charged = jami to'liq oylik (chegirmasiz);
/// Discount = jami berilgan chegirma; Paid = haqiqiy naqd to'lovlar yig'indisi (turli oylar uchun);
/// Debt / Advance — joriy holatdan (balans). DiscountPct/Amount — qoidani ko'rsatish uchun.</summary>
public record StudentFinanceRowDto(
    string StudentId, string FullName, string ClassName,
    decimal Charged, decimal Discount, decimal Paid, decimal Debt, decimal Advance,
    int DiscountPct = 0, decimal DiscountAmount = 0);

/* ---------- Subjects ---------- */
public record SubjectPayload(string Name);

/* ---------- Classes ---------- */
public record ClassPayload(string Name, int Grade, string Language, decimal MonthlyFee, string? Room);

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
public record JournalEntryDto(string StudentId, string Date, int Period, int? Grade, string? ReasonId);
public record SetJournalEntryRequest(
    string ClassId, string SubjectId, int Quarter, string StudentId, string Date, int Period,
    int? Grade, string? ReasonId);
public record JournalTopicDto(string Date, int Period, string Topic, string? Homework, bool Conducted, int SubGroup = 0);
/// <summary>Berilgan sanada o'tilgan (conducted) darslar — sinf+fan+dars raqami+guruh.</summary>
public record ConductedLessonDto(string ClassId, string SubjectId, int Period, int SubGroup = 0);
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
public record ScheduleTemplateDto(string Id, string ClassId, string Name, List<ScheduleLessonDto> Lessons);
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

/* ---------- Dashboard ---------- */
public record AdminStatsDto(int StudentsCount, int TeachersCount, double AverageGrade, double? AttendanceRate);
public record ClassPerformanceItemDto(string ClassId, string ClassName, double AverageGrade, double? AttendanceRate);
public record TopClassDto(string Id, string Name, int StudentsCount, double AverageGrade);
public record AdminDashboardDto(
    AdminStatsDto Stats, List<ClassPerformanceItemDto> ClassPerformance, List<TopClassDto> TopClasses);

/* ---------- Class performance / rating ---------- */
public record SubjectDto(string Id, string Name);
public record StudentDto(
    string Id, string FullName, string BirthDate, string Address, string Gender,
    string ParentFullName, string ParentPhone, string ClassName, string EnrollmentDate, decimal Balance,
    int DiscountPct = 0, decimal DiscountAmount = 0, string DiscountNote = "", int SubGroup = 0,
    string LastName = "", string FirstName = "", string MiddleName = "",
    string? BirthCertificateUrl = null,
    string ParentLastName = "", string ParentFirstName = "", string ParentMiddleName = "",
    string? ParentPassportUrl = null,
    bool IsArchived = false, string? ArchivedAt = null, string? ArchiveReason = null);

/// <summary>O'quvchini arxivlash so'rovi — sababini saqlaydi.</summary>
public record ArchiveStudentRequest(string Reason);
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
    int Grades, int? TopicPct, int? HomeworkPct);

/// <summary>Bitta o'qituvchining batafsil hisoboti: umumiy ko'rsatkichlar + sinf/fan yoyilmasi.</summary>
public record TeacherReportDetailDto(
    string TeacherId, string FullName, bool IsArchived,
    int Expected, int Conducted, int? DonePct,
    int Grades, int? TopicPct, int? HomeworkPct,
    string? LastActivity, string Status,
    List<TeacherReportBreakdownDto> Rows);

/// <summary>O'quvchi (mobil ilova) o'z joylashuvini yangilash so'rovi — GPS dan keladi.</summary>
public record UpdateLocationRequest(double Latitude, double Longitude, string? Address);
/// <summary>Admin xarita uchun — joylashuvi bor o'quvchi qatori.</summary>
public record StudentLocationRowDto(
    string StudentId, string FullName, string ClassName,
    double Latitude, double Longitude, string? Address, string? UpdatedAt);

/// <summary>Ota-ona bo'limidagi bitta farzand (qisqacha).</summary>
public record ParentChildDto(
    string StudentId, string FullName, string ClassName,
    string? FirstLoginAt, string? LastLoginAt);

/// <summary>
/// Admin "Ota-onalar" bo'limidagi bir ota-ona qatori — telefon bo'yicha guruhlangan.
/// IsActivated = farzandlardan kamida bittasi ilovaga kirgan (FirstLoginAt mavjud).
/// ActivatedAt = farzandlar orasida eng erta FirstLoginAt; LastSeenAt = eng kech LastLoginAt.
/// </summary>
public record ParentRowDto(
    string FullName, string Phone, int ChildrenCount,
    bool IsActivated, string? ActivatedAt, string? LastSeenAt,
    List<ParentChildDto> Children);
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

/* ---------- Yangi o'quv yiliga o'tish ---------- */
public record AcademicYearInfoDto(
    string CurrentYear, int Students, int Classes, int JournalEntries,
    int WeekAssignments, int FinanceTransactions);
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
public record TelegramSettingsDto(string BotToken, string BotUsername, bool Configured);
/// <summary>Telegram bot sozlamasini saqlash so'rovi.</summary>
public record SaveTelegramSettingsRequest(string? BotToken, string? BotUsername);

/* ---------- Finance (Moliya) ---------- */
public record FinanceTransactionDto(
    string Id, string Date, string Direction, string Category, decimal Amount,
    string? Note, string? StudentId, string? StudentName, string? TeacherId, string? TeacherName,
    string? Month);
public record FinanceTransactionPayload(
    string Date, string Direction, string Category, decimal Amount, string? Note,
    string? StudentId, string? TeacherId);
public record CategoryAmountDto(string Category, decimal Amount);
public record FinanceSummaryDto(
    decimal TotalIncome, decimal TotalExpense, decimal Net,
    decimal TuitionIncome, decimal OtherIncome,
    List<CategoryAmountDto> IncomeByCategory, List<CategoryAmountDto> ExpenseByCategory,
    decimal StudentDebt, decimal StudentAdvance, int TransactionsCount);
public record FinanceMonthlyDto(string Month, decimal Income, decimal Expense);
public record AccrueResultDto(List<string> Months, int Count, decimal Total);

/* ---------- O'quvchi to'lov tarixi (ledger) ---------- */
/// <summary>Bitta oyning hisobi.
/// Charged = to'liq oylik (sinf narxi); Discount = shu oy uchun berilgan chegirma;
/// Paid = haqiqiy naqd to'lov (tx); Remaining = Charged − Discount − Paid (manfiy bo'lsa 0).</summary>
public record MonthLedgerDto(
    string Month, decimal Charged, decimal Discount, decimal Paid, decimal Remaining, string Status);
public record PaymentDto(string Date, decimal Amount, string? Note, string? Month);
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
    List<string> Permissions);
/// <summary>O'qituvchi dars beradigan bitta sinf (qaysi fanlarni va sinf rahbarimi).</summary>
public record TeacherClassDto(
    string ClassId, string ClassName, int Grade, bool IsHomeroom, List<SubjectDto> Subjects);
/// <summary>O'qituvchi jadvalidagi bitta dars (qaysi sinf, fan, kun, dars raqami, vaqt, guruh).</summary>
public record TeacherLessonDto(
    int Day, int Period, string? StartTime, string? EndTime,
    string ClassId, string ClassName, string SubjectId, string SubjectName, int SubGroup = 0);

/* ---------- Student portal (ilova) ---------- */
/// <summary>O'quvchining o'z profili (ilovada ko'rsatish uchun).</summary>
public record StudentProfileDto(
    string Id, string FullName, string ClassName, string BirthDate, string Gender,
    string ParentFullName, string ParentPhone, string EnrollmentDate);
/// <summary>O'quvchi jadvalidagi bitta dars (fan, o'qituvchi, kun, dars raqami, vaqt).</summary>
public record StudentLessonDto(
    int Day, int Period, string? StartTime, string? EndTime,
    string SubjectId, string SubjectName, string TeacherId, string TeacherName, int SubGroup = 0);
/// <summary>O'quvchi uchun dars mavzusi va uyga vazifa (sana + fan bo'yicha).
/// Shu o'quvchining o'sha (sana + dars raqami) jurnal yozuvi bo'lsa — Grade va Reason ham
/// bog'lab qaytariladi (bugungi/haftalik baholarni alohida endpoint'siz ko'rsatish uchun).</summary>
public record HomeworkItemDto(
    string Date, int Period, string SubjectId, string SubjectName,
    string Topic, string? Homework, bool Conducted,
    int? Grade, string? ReasonId, string? ReasonName, bool IsLate);

/// <summary>O'quvchi jurnali — bitta dars qatori (sana + dars raqami + fan + o'qituvchi + mavzu/uyga vazifa + baho/sabab).</summary>
public record StudentJournalRowDto(
    string Date, int Period, int Quarter, int Week,
    string? StartTime, string? EndTime,
    string SubjectId, string SubjectName,
    string? TeacherId, string? TeacherName,
    string Topic, string? Homework, bool Conducted,
    int? Grade, string? ReasonId, string? ReasonName, bool IsLate);

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
/// <summary>Sozlamani yangilash so'rovi. Berilgan maydonlar yangilanadi, qolganlari saqlanadi.</summary>
public record SaveUserSettingsRequest(string? Language, string? Theme, bool? NotificationsEnabled);

/// <summary>Push qurilma tokenini ro'yxatdan o'tkazish so'rovi.</summary>
public record RegisterDeviceRequest(string Token, string? Platform);

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
public record SubjectAttendanceDto(
    string SubjectId, string SubjectName, int Period, int Total, int Present, int Absent,
    List<ReasonCountDto> Reasons);
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

/// <summary>Yuborilgan e'lon (Telegram). CreatedAt — ISO 8601.</summary>
public record BroadcastDto(
    string Id, string ClassName, string Text, string SenderName, string CreatedAt,
    int RecipientCount, int SentCount);

/// <summary>Sinf ota-onalariga e'lon yuborish so'rovi.</summary>
public record SendBroadcastRequest(string ClassName, string Text);

/// <summary>Telegramda ro'yxatdan o'tgan ota-ona (sinf bo'yicha). ChatId string (JS aniqligi uchun).</summary>
public record TelegramParentDto(
    string StudentId, string StudentName, string ParentName, string Phone,
    string ChatId, string CreatedAt);

/* ---------- Assignments (qo'shimcha topshiriqlar) ---------- */

/// <summary>Topshiriqqa biriktirilgan material (yuklangan fayl yoki havola).</summary>
public record AssignmentMaterialDto(string Id, string Name, string Url, long Size, string ContentType);
/// <summary>Test savoli (format=test).</summary>
public record TestQuestionDto(string Id, string Text, List<string> Options, int CorrectIndex, int Order);
/// <summary>Topshiriq/test (to'liq). Format: written|file|test|video. CreatedAt/Start/Due — ISO.</summary>
public record AssignmentDto(
    string Id, string CreatedByUserId, string SubjectId, string SubjectName, string Title,
    string Description, string Format, List<string> ClassIds, List<string> ClassNames,
    string? StartDate, string? DueDate, bool LateAccept, int LatePenaltyPct, int MaxScore,
    bool AutoGrade, string CreatedAt,
    List<AssignmentMaterialDto> Materials, List<TestQuestionDto> Questions);
public record MaterialInput(string Name, string Url, long Size, string ContentType);
public record QuestionInput(string Text, List<string> Options, int CorrectIndex);
/// <summary>Topshiriq yaratish/tahrirlash so'rovi (ham create, ham update).</summary>
public record SaveAssignmentRequest(
    string SubjectId, string Title, string? Description, string Format, List<string> ClassIds,
    string? StartDate, string? DueDate, bool LateAccept, int LatePenaltyPct, int MaxScore,
    bool AutoGrade, List<MaterialInput>? Materials, List<QuestionInput>? Questions);
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
public record StaffDto(string Id, string FullName, string Position, string Login, List<string> Permissions);
public record StaffPayload(string FullName, string Position, string? NewPassword = null);
/// <summary>Xodimning admin bo'lim ruxsatlari (faqat superadmin o'zgartiradi).</summary>
public record SetStaffPermissionsRequest(List<string> Permissions);

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
/// <summary>Sinf bo'yicha topshiriqlar ball jadvali (ustunlar = topshiriqlar, qatorlar = o'quvchilar).</summary>
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

/// <summary>LMS mavzusi (admin).</summary>
public record LmsTopicDto(
    string Id, string SubjectId, string Title, string Description,
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
public record OccupiedSlotDto(int Day, int Period, string ClassName, string TemplateName);

/// <summary>O'quvchi uchun LMS mavzu (ochilganmi, tugallanganmi).</summary>
public record StudentLmsTopicDto(
    string Id, string SubjectId, string Title, string Description,
    string? VideoUrl, string? TextContent, int Order,
    List<LmsMaterialRowDto> Materials,
    bool IsUnlocked, bool IsCompleted);

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
    int Planned, int Conducted, int Remaining, int Percent, int ExpectedByToday);

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
