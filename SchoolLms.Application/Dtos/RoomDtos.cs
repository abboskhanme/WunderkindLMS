namespace SchoolLms.Application.Dtos;

// ===========================================================================
//  Xonalar reyestri — docs/modules/students-parity.md §2.6 (R-1).
//
//  Bu KATALOG. Bandlik, dars, jihoz — bu yerda emas:
//    · xona darsga biriktirilishi va to'qnashuvlar — jadval moduli;
//    · xona jihozlari — WareHouse moduli (`docs/modules/warehouse.md` §3.2).
//
//  `SchoolClass.Room` (erkin matn) O'ZGARMAYDI: uni o'qiydigan ekranlar
//  bugungidek ishlayveradi. Reyestr o'sha matnlardan BIR MARTA to'ldirilishi
//  mumkin (`POST rooms/import-from-classes`).
// ===========================================================================

/// <summary>Reyestrdagi bitta xona.</summary>
/// <param name="UsedByClasses">
/// Nechta sinf shu xonani (nomi bo'yicha) ko'rsatgan. 0 dan katta bo'lsa
/// xona o'chirilmaydi — sinflar "xonasiz" qolib ketmasligi uchun.
/// </param>
public record RoomDto(
    Guid Id,
    string Name,
    string? Building,
    short? Floor,
    short Capacity,
    string Kind,
    int UsedByClasses);

/// <summary>Xona yaratish/tahrirlash.</summary>
public record SaveRoomRequest(
    string? Name,
    string? Building,
    short? Floor,
    short? Capacity,
    string? Kind);

/// <summary>
/// Ommaviy yaratish (EduSchool'dagi <c>rooms/multiple</c>, §2.6.1):
/// bir binoda ketma-ket raqamlangan N ta xona.
/// </summary>
/// <param name="Count">1..50 (EduSchool ham 50 da to'xtatadi).</param>
/// <param name="StartFrom">
/// Birinchi raqam. Berilmasa — mavjud raqamli nomlarning eng kattasidan keyingisi.
/// </param>
/// <param name="Prefix">Raqam oldidan qo'shiladigan matn ("A-" → "A-101").</param>
public record BulkCreateRoomsRequest(
    int? Count,
    int? StartFrom,
    string? Prefix,
    string? Building,
    short? Floor,
    short? Capacity,
    string? Kind);

/// <summary>
/// Ommaviy yaratish natijasi. Nomi allaqachon band bo'lgan xonalar
/// YARATILMAYDI va <paramref name="Skipped"/> ga tushadi — butun so'rov
/// bitta takroriy nom tufayli yiqilmasligi kerak.
/// </summary>
public record BulkCreateRoomsResultDto(
    IReadOnlyList<RoomDto> Created,
    IReadOnlyList<string> Skipped);

/// <summary>Sinflardagi erkin matnli xona nomlaridan reyestrni to'ldirish natijasi (§2.6.3 "seeded").</summary>
public record RoomImportResultDto(
    IReadOnlyList<RoomDto> Created,
    IReadOnlyList<string> Skipped);
