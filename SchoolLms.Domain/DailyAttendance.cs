namespace SchoolLms.Domain;

// ===========================================================================
//  Kunlik davomat belgilash — BITTA MAS'UL XODIM, BARCHA SINFLAR
//  ---------------------------------------------------------------------------
//  Mijoz, 2026-09-18: "davomat bo'limida bir kishi doimiy sinflar davomatini
//  qiloladigan bo'lsin, yani bitta mas'ul xodimga shu davomat menusini bersak
//  barcha sinflarni eng qulay usulda davomatini qilolsin."
//
//  DAVOMATNING O'ZI YANGI JOYGA YOZILMAYDI. Yo'qlik hamon JURNALGA tushadi
//  (<see cref="JournalEntry.ReasonId"/>) — o'qituvchi qo'li bilan yozgani
//  bilan bir xil qatorga. Sabab: davomat foizi, intizomiy ball, ota-onaga
//  ko'rinadigan jurnal va barcha hisobotlar SHU qatordan o'qiydi; ikkinchi
//  manba ochilsa, ikkitasi bir kun kelib bir-biriga zid javob berardi.
//
//  BU JADVAL ESA — ISH JARAYONI BELGISI, pul yoki baho emas: "shu sinfning
//  shu DARSI belgilab bo'lindi". Usiz "hech kim yo'q" (hammasi keldi) bilan
//  "hali belgilanmagan" ni FARQLAB BO'LMAYDI — ikkalasida ham jurnalda bitta
//  ham qator yo'q. Mas'ul xodim esa aynan shu farqni ko'rishi kerak: qaysi
//  soat qoldi. Shu bilan birga "kim, qachon belgiladi" izi ham qoladi.
// ===========================================================================

/// <summary>
/// Bitta sinfning bitta DARSI belgilab bo'lingani.
/// Kalit: <see cref="Date"/> + <see cref="ClassId"/> + <see cref="SubjectId"/> + <see cref="Period"/>.
///
/// <para>
/// Mijoz, 2026-09-18 (ikkinchi xat): "sinf tanlansa o'sha soatda darsiga ko'ra
/// sinfni davomat qilish mumkin bo'lsin" — ya'ni belgilash butun kun uchun
/// emas, HAR BIR DARS SOATI uchun. Shuning uchun belgi ham dars bo'yicha
/// yuritiladi: qaysi soat belgilangan, qaysi biri qolgani ko'rinib tursin.
/// </para>
/// </summary>
public class DailyAttendanceMark
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Ega id'si: sinf (<see cref="SchoolClass"/>) YOKI yo'nalish guruhi
    /// (<see cref="StudyGroup"/>, 2026-09-26) — jurnal qatorlaridagi <c>class_id</c> bilan bir xil
    /// ma'no. Guruh id'si uuid, sinf id'si bilan to'qnashmaydi; FK yo'q.</summary>
    public string ClassId { get; set; } = string.Empty;

    /// <summary>Kun, "YYYY-MM-DD" — jurnal qatorlaridagi <c>date</c> bilan bir xil shakl.</summary>
    public string Date { get; set; } = string.Empty;

    /// <summary>Qaysi fanning darsi (<see cref="Subject"/>).</summary>
    public string SubjectId { get; set; } = string.Empty;

    /// <summary>Nechanchi dars (1-10) — jadvaldagi soat.</summary>
    public int Period { get; set; }

    /// <summary>Kim belgiladi (<c>users.id</c>) — JWT'dan, so'rov tanasidan EMAS.</summary>
    public string MarkedBy { get; set; } = string.Empty;

    /// <summary>Oxirgi marta qachon saqlandi (qayta saqlansa yangilanadi).</summary>
    public DateTimeOffset MarkedAt { get; set; }

    /// <summary>Saqlash paytidagi yo'qlar soni (shu dars uchun) — ro'yxatda darrov ko'rsatish uchun.</summary>
    public int AbsentCount { get; set; }

    /// <summary>
    /// Saqlash paytidagi "kech keldi" soni (<see cref="AbsenceReason.IsLate"/>).
    /// Yo'qlikka kirmaydi, alohida sanaladi.
    /// </summary>
    public int LateCount { get; set; }
}
