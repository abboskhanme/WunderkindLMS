namespace SchoolLms.Application.Dtos;

// Ikkita analitik hisobot shakllari (docs/modules/existing-module-gaps.md §4, #9 va #6).
// Alohida faylda — Dtos.cs ni shishirmaslik uchun; JSON camelCase'ga standart sozlama
// bilan aylanadi, frontend tiplari shu tartibda ko'chiriladi.

/* ==========================================================================
   #9 — Buyurtmalar voronkasi (lidlar)
   ========================================================================== */

/// <summary>
/// Voronkaning bitta bosqichi.
///
/// <para>
/// <b>MUHIM — bu SURAT (snapshot), oqim emas.</b> <c>Lead</c> da na yaratilgan vaqt,
/// na bosqich o'zgarishi tarixi saqlanadi (<c>SchoolLms.Domain/Entities.cs</c>), shuning
/// uchun "shu davrda nechta lid kirdi" degan savolga javob berib bo'lmaydi. Hisoblanadigan
/// narsa — lidlar HOZIR qaysi bosqichda turgani. "Yetib kelgan" (<see cref="ReachedCount"/>)
/// esa lid faqat oldinga siljiydi degan taxminga tayanadi: bosqich tartibi
/// (<c>LeadStage.Order</c>) bo'yicha shu bosqich yoki undan keyingilarida turganlar.
/// </para>
/// </summary>
/// <param name="StageId">Bosqich (ustun) id'si.</param>
/// <param name="Title">Ustun nomi — mijoz o'zi tahrirlaydi.</param>
/// <param name="Color">Doskadagi rang (bir xil ko'rinsin uchun).</param>
/// <param name="Order">Doskadagi tartib.</param>
/// <param name="CurrentCount">Hozir shu bosqichda turganlar.</param>
/// <param name="ReachedCount">Shu bosqichga yetganlar (shu va keyingi bosqichlar yig'indisi).</param>
/// <param name="MovedOnCount">Keyingi bosqichga o'tganlar. Oxirgi bosqichda — 0.</param>
/// <param name="SharePercent">Voronkadagi barcha lidlarga nisbatan ulush (%).</param>
/// <param name="StepConversionPercent">Keyingi bosqichga o'tish foizi. Oxirgi bosqichda — null.</param>
/// <param name="DropOffPercent">Shu bosqichda to'xtab qolganlar foizi. Oxirgi bosqichda — null.</param>
public record LeadFunnelStageDto(
    string StageId, string Title, string Color, int Order,
    int CurrentCount, int ReachedCount, int MovedOnCount,
    double SharePercent, double? StepConversionPercent, double? DropOffPercent);

/// <summary>
/// Yo'qotilgan lidlar bitta sabab (= "yo'qotildi" deb belgilangan ustun) bo'yicha.
/// Sabab matni — ustun nomi: bazada alohida "yo'qotish sababi" maydoni yo'q.
/// </summary>
public record LeadFunnelLossDto(
    string StageId, string Title, string Color, int Count, double SharePercent);

/// <summary>
/// Sinf (maqsadli daraja) kesimi — <c>Lead.SourceId</c> kabi manba maydoni bazada
/// YO'Q, shuning uchun manba o'rniga bor bo'lgan yagona mazmunli kesim.
/// </summary>
/// <param name="TargetGrade">Lid qaysi sinfga kelmoqchi (0–11).</param>
/// <param name="Total">Shu darajadagi barcha lidlar.</param>
/// <param name="InFunnelCount">Ulardan voronkada (yo'qotilmaganlari).</param>
/// <param name="ReachedFinalCount">Oxirgi bosqichga yetganlar.</param>
/// <param name="LostCount">Yo'qotilganlar.</param>
/// <param name="ConversionPercent">ReachedFinal / InFunnel (%). Voronkada lid bo'lmasa — null.</param>
public record LeadFunnelGradeDto(
    int TargetGrade, int Total, int InFunnelCount, int ReachedFinalCount, int LostCount,
    double? ConversionPercent);

/// <summary>
/// Buyurtmalar voronkasi (§4, #9). Bosqichlar ro'yxati va tartibi <c>LeadStage</c> dan
/// olinadi — kodda qattiq yozilgan ro'yxat yo'q, mijoz ustunlarni o'zi o'zgartiradi.
/// </summary>
/// <param name="TotalLeads">Bazadagi barcha lidlar.</param>
/// <param name="FunnelLeads">Voronka bosqichlarida turganlar (yo'qotilganlarsiz, bosqichsizlarsiz).</param>
/// <param name="LostCount">"Yo'qotildi" deb belgilangan ustunlardagilar.</param>
/// <param name="OrphanCount">Bosqichi o'chirilgan/noma'lum lidlar — hech qayerga qo'shilmaydi.</param>
/// <param name="OverallConversionPercent">Oxirgi bosqich / birinchi bosqichga yetganlar (%).</param>
public record LeadFunnelDto(
    int TotalLeads, int FunnelLeads, int LostCount, int OrphanCount,
    double? OverallConversionPercent,
    List<LeadFunnelStageDto> Stages,
    List<LeadFunnelLossDto> Losses,
    List<LeadFunnelGradeDto> Grades);

/* ==========================================================================
   #6 — Davomat intizomi bo'yicha hisobot
   ========================================================================== */

/// <summary>Bitta o'quvchi qatori.</summary>
/// <param name="StudentId">O'quvchi id'si.</param>
/// <param name="FullName">F.I.SH.</param>
/// <param name="ClassName">Sinf nomi.</param>
/// <param name="Opportunities">Shu davrda o'quvchiga tegishli o'tilgan darslar soni.</param>
/// <param name="Absences">Yo'qliklar ("kech keldi" BUNGA KIRMAYDI).</param>
/// <param name="Lates">"Kech keldi" belgilar soni.</param>
/// <param name="Unchecked">Davomati umuman belgilanmagan darslar — "keldi" deb hisoblanmaydi.</param>
/// <param name="AttendancePoints">Shu davrdagi davomat belgilaridan kelgan ball (manfiy = jazo).</param>
/// <param name="ManualPoints">Shu davrda qo'lda kiritilgan intizomiy ball.</param>
/// <param name="Remaining">Qoldi — 100 + BUTUN TARIX bo'yicha ballar (Ballar nazorati bilan bir xil).</param>
/// <param name="AttendancePercent">Davomat foizi; o'tilgan dars bo'lmasa — null.</param>
public record AttendanceDisciplineStudentDto(
    string StudentId, string FullName, string ClassName,
    int Opportunities, int Absences, int Lates, int Unchecked,
    int AttendancePoints, int ManualPoints, int Remaining,
    double? AttendancePercent);

/// <summary>Sinf kesimi — o'quvchilar qatorlarining yig'indisi.</summary>
/// <param name="ClassId">Sinf id'si.</param>
/// <param name="ClassName">Sinf nomi.</param>
/// <param name="Students">Faol o'quvchilar soni.</param>
/// <param name="Opportunities">O'tilgan darslar × o'quvchilar.</param>
/// <param name="Absences">Yo'qliklar.</param>
/// <param name="Lates">Kechikishlar.</param>
/// <param name="Unchecked">Belgilanmagan kataklar.</param>
/// <param name="AttendancePoints">Davomatdan yo'qotilgan/olingan ball.</param>
/// <param name="AttendancePercent">Sinf davomati (%); o'tilgan dars bo'lmasa — null.</param>
/// <param name="PenaltyPerStudent">Bitta o'quvchiga to'g'ri keladigan o'rtacha jazo balli (musbat son).</param>
public record AttendanceDisciplineClassDto(
    string ClassId, string ClassName, int Students,
    int Opportunities, int Absences, int Lates, int Unchecked,
    int AttendancePoints, double? AttendancePercent, double PenaltyPerStudent);

/// <summary>
/// Sabab kesimi. <c>Kind</c>: <c>attendance</c> — jurnaldagi davomat sababi
/// (<c>AbsenceReason</c>), <c>manual</c> — qo'lda kiritilgan intizomiy ball
/// (<c>DisciplinePoint</c>). <c>DisciplineController</c> dagi nomlar bilan bir xil.
/// </summary>
public record AttendanceDisciplineReasonDto(
    string ReasonId, string Name, string Kind, int PointsEach, int Count, int TotalPoints);

/// <summary>Butun hisobot bo'yicha jamlama.</summary>
public record AttendanceDisciplineTotalsDto(
    int Students, int Opportunities, int Absences, int Lates, int Unchecked,
    int AttendancePoints, int ManualPoints, double? AttendancePercent);

/// <summary>
/// Davomat intizomi bo'yicha hisobot (§4, #6): kim davomat orqali intizomiy ball
/// yo'qotmoqda va qaysi sinfda.
/// </summary>
/// <param name="From">Boshlanish sanasi (ISO), serverda normallashtirilgan.</param>
/// <param name="To">Tugash sanasi (ISO), serverda normallashtirilgan.</param>
public record AttendanceDisciplineReportDto(
    string From, string To,
    AttendanceDisciplineTotalsDto Totals,
    List<AttendanceDisciplineClassDto> Classes,
    List<AttendanceDisciplineStudentDto> Students,
    List<AttendanceDisciplineReasonDto> Reasons);
