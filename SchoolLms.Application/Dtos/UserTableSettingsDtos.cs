using System.Text.Json;

namespace SchoolLms.Application.Dtos;

// ===========================================================================
//  Jadval ko'rinishi sozlamalari — docs/modules/students-parity.md §2.11 (X-1).
//
//  `Settings` NEGA `JsonElement`. Ekran ustunlar ro'yxati, tartibi va qadalgan
//  (pinned) ustunlarni O'ZI biladi — server buning ICHIGA qaramaydi, faqat
//  saqlaydi (UserTableSettings.cs dagi izoh). `JsonElement` ASP.NET'ga har
//  qanday JSON obyektni O'ZGARISHSIZ qabul qilish/qaytarishga imkon beradi;
//  server tomonda qat'iy shakl yo'q, shuning uchun yangi ekran (yangi ustun
//  to'plami) uchun DTO o'zgarmaydi.
// ===========================================================================

/// <summary>Bitta ekrandagi jadval ko'rinishi — GET javobi.</summary>
public record UserTableSettingsDto(string Page, JsonElement Settings, DateTimeOffset? UpdatedAt);

/// <summary>Ko'rinishni saqlash so'rovi.</summary>
public record SaveUserTableSettingsRequest(JsonElement Settings);
