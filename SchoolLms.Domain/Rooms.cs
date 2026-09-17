namespace SchoolLms.Domain;

// ===========================================================================
//  Xonalar — docs/modules/students-parity.md §2.6 (R-1).
// ===========================================================================
//
//  NEGA SHU MODULDA
//  ----------------
//  `rooms` uchta modulga kerak: xonalar reyestri (§2.6), ombordagi xona
//  jihozlari (`docs/modules/warehouse.md` §3.2) va kelajakdagi jadval
//  moduli (`docs/SPEC.md` §3.3). Qoida: KIM BIRINCHI KELSA, O'SHA yaratadi va
//  boshqalar TAKRORLAMAYDI. Birinchi shu modul keldi.
//
//  SHAKL — AYNAN `SPEC.md` §3.3 DAGIDEK. Ustun ham, tur ham, DEFAULT ham
//  o'zgartirilmagan, aks holda keyingi ikki modul boshqa shaklni kutib qolardi.
//
//  NIMA QO'SHILMAYDI (§2.6.3, warehouse.md §3.2):
//    · `buildings` jadvali — maktab bitta binoda (bino nomi matn ustunda);
//    · `classes.home_room_id` va jadvalga ulanish — jadval modulining ishi;
//    · xona jihozlari — WareHouse moduli.
//
//  `SchoolClass.Room` (erkin matn) JOYIDA qoladi va uni o'qiydigan ekranlar
//  o'zgarmaydi; reyestr o'sha matnlardan BIR MARTA to'ldiriladi (seed).
// ===========================================================================

/// <summary><see cref="Room.Kind"/> qiymatlari (SPEC §3.3 dagi izoh).</summary>
public static class RoomKind
{
    public const string Classroom = "classroom";
    public const string Lab = "lab";
    public const string Gym = "gym";
    public const string Hall = "hall";

    public static readonly string[] All = [Classroom, Lab, Gym, Hall];

    public static bool IsValid(string? value) => value is not null && All.Contains(value);
}

/// <summary>Xona — band qilinadigan resurs (SPEC §3.3, R-1).</summary>
public class Room
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Xona nomi/raqami ("101", "Sport zali"). Unikal.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Bino nomi (ixtiyoriy). Alohida `buildings` jadvali YO'Q.</summary>
    public string? Building { get; set; }

    public short? Floor { get; set; }

    /// <summary>Nechta o'quvchi sig'adi. SPEC §3.3: sukut 30.</summary>
    public short Capacity { get; set; } = 30;

    /// <summary>
    /// <see cref="RoomKind"/>. Baza CHECK bilan CHEKLAMAYDI — SPEC §3.3 da
    /// bu ro'yxat izoh, va WareHouse moduliga boshqa turdagi xona (ombor,
    /// kabinet) kerak bo'lishi mumkin; ro'yxatni migratsiya bilan kengaytirish
    /// o'sha modulning ishini bekorga sekinlashtirardi.
    /// </summary>
    public string Kind { get; set; } = RoomKind.Classroom;

    // =======================================================================
    //  §3.3 dan TASHQARI — R-1 slice'ining talabi (Batch C migratsiyasi).
    // =======================================================================

    /// <summary>
    /// Xona faolmi. <b>false = "ishlatilmaydi, lekin saqlanadi"</b>: yangi
    /// jadvalda va tanlovlarda ko'rinmaydi, lekin unga havola qilgan eski
    /// yozuvlar joyida qoladi.
    ///
    /// <para>
    /// <b>Nega kerak bo'ldi:</b> bugun reyestrda faqat O'CHIRISH bor, va
    /// sinf ko'rsatib turgan xonani o'chirib bo'lmaydi
    /// (<c>RoomTests.Sinf_korsatgan_xona_ochirilmaydi</c>) — ya'ni ta'mirga
    /// yopilgan xonani ro'yxatdan olib qo'yishning YO'LI YO'Q. Bu ustun
    /// <see cref="StudentStatus.IsActive"/> va
    /// <see cref="CertificateType.IsActive"/> bilan bir xil naqsh.
    /// </para>
    /// <para>
    /// Sukut <b>true</b> — mavjud har bir xona faol bo'lib qoladi, ya'ni
    /// bugungi ro'yxat va o'chirish qoidasi o'zgarmaydi.
    /// </para>
    /// </summary>
    public bool IsActive { get; set; } = true;
}
