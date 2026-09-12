namespace SchoolLms.Domain;

// ===========================================================================
//  Telegram Mini App — akkaunt bog'lanishi. SPEC §6 Faza 3.
// ===========================================================================
//
//  NEGA BIR MARTALIK KOD, TELEFON EMAS
//  -----------------------------------
//  Telegram `request_contact` bergan raqam ISHONCHLI — uni Telegram o'zi
//  tasdiqlaydi. Ishonchsiz bo'lgani — raqamdan AKKAUNTGA o'tish: bu bazada
//  `students.parent_phone` (qo'lda kiritilgan, oilada bitta raqam, aka-uka
//  uchun bir xil) yoki `users.email` (login sifatidagi raqam). Ya'ni raqam
//  to'g'ri bo'lsa ham, u qaysi akkauntni ochishi noaniq va maktab bu
//  bog'lanishni ko'rmaydi.
//
//  Shuning uchun bog'lanishni MAKTAB boshlaydi: admin panelda kerakli
//  foydalanuvchi uchun bir martalik kod chiqariladi (<see cref="TelegramLinkCode"/>),
//  kod egasiga aytiladi, u Mini App'ga kiritadi. Natijada har bog'lanishning
//  aniq egasi, vaqti va uni bergan xodimi bor.
//
//  Batafsil (va rad etilgan variantlar): docs/ASSUMPTIONS.md.
// ===========================================================================

/// <summary>
/// Telegram foydalanuvchisi ↔ tizim akkaunti bog'lanishi. Bitta Telegram id —
/// bitta akkaunt; bitta akkaunt — bitta Telegram id.
/// </summary>
public class TelegramAccount
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Telegram `user.id` (`initData.user.id`) — unikal.</summary>
    public long TelegramUserId { get; set; }

    /// <summary>Bog'langan tizim akkaunti (<see cref="AppUser.Id"/>) — unikal.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Telegram `@username` (bo'lmasligi mumkin) — faqat ko'rsatish uchun.</summary>
    public string? Username { get; set; }

    /// <summary>Telegram profilidagi ism (ko'rsatish uchun).</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Kim bog'ladi — kodni bergan xodim (<see cref="AppUser.Id"/>).</summary>
    public string? LinkedByUserId { get; set; }

    public DateTime LinkedAt { get; set; } = AppClock.Now;

    /// <summary>Oxirgi muvaffaqiyatli Mini App kirishi — "kim ishlatyapti" ko'rinishi uchun.</summary>
    public DateTime LastSeenAt { get; set; } = AppClock.Now;
}

/// <summary>
/// Bir martalik bog'lanish kodi. Admin chiqaradi, foydalanuvchi Mini App'da
/// kiritadi. Muddati o'tsa yoki bir marta ishlatilsa — yaroqsiz.
/// </summary>
public class TelegramLinkCode
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Kodning O'ZI SAQLANMAYDI — faqat SHA-256 hash'i. Baza nusxasi sizib
    /// chiqsa ham tirik kodlar bilan akkaunt bog'lab bo'lmaydi. Ochiq kod
    /// yaratilgan lahzada bir marta qaytariladi va boshqa tiklanmaydi.
    /// </summary>
    public string CodeHash { get; set; } = string.Empty;

    /// <summary>Kod qaysi akkaunt uchun (<see cref="AppUser.Id"/>).</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Kodni bergan xodim (<see cref="AppUser.Id"/>).</summary>
    public string CreatedByUserId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = AppClock.Now;

    /// <summary>Shu vaqtdan keyin kod yaroqsiz.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Ishlatilgan vaqt (null = hali ishlatilmagan).</summary>
    public DateTime? UsedAt { get; set; }

    /// <summary>Kodni ishlatgan Telegram foydalanuvchisi (audit uchun).</summary>
    public long? UsedByTelegramUserId { get; set; }
}

/// <summary>
/// Chat kanali (sinf yoki "xodimlar") qachongacha o'qilgani — foydalanuvchi
/// bo'yicha. O'qilmagan xabarlar soni shundan hisoblanadi.
///
/// <para>
/// Alohida jadval kerak bo'ldi, chunki <c>UserSettings.NotificationsReadAt</c>
/// BUTUN admin qo'ng'irog'i uchun bitta vaqt belgisi — o'qituvchida esa har
/// sinf alohida kanal va ular alohida o'qiladi.
/// </para>
/// </summary>
public class ChatRead
{
    /// <summary><see cref="AppUser.Id"/>.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Kanal = sinf nomi yoki <c>ChatService.StaffChannel</c>.</summary>
    public string Channel { get; set; } = string.Empty;

    public DateTime ReadAt { get; set; } = AppClock.Now;
}
