using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

/// <summary>
/// The paper checklist "DARS KUZATUVI - NAZORAT CHECKLISTI", transcribed — verbatim from the
/// old app's <c>backend/app/db/criteria_data.py</c>. The printed form numbers its rows 1..35
/// but skips 24, so it holds 34 rows in 8 sections.
///
/// <para>
/// Seeded at start-up <b>only when the sections table is empty</b> (old rule): once an admin
/// has renamed, deactivated or added a criterion, a restart must not bring the original back.
/// The one-time import maps old criteria onto these by (section order, criterion text).
/// </para>
/// </summary>
public static class LessonAnalysisSeeder
{
    public sealed record SeedSection(string Title, string Color, string[] Criteria);

    public static readonly IReadOnlyList<SeedSection> Sections =
    [
        new("1-BO'LIM: DARSGA TAYYORGARLIK", "#7a9a2e",
        [
            "Dars rejasi (lesson plan) oldindan tuzilgan va taqdim etilgan",
            "Texnik vositalar (proyektor, audio, kompyuter) vositalardan dars davomida samarali foydalanildi",
        ]),
        new("2-BO'LIM: DARS BOSHLANISHI - Motivatsiya", "#1f8fd0",
        [
            "Dars o'z vaqtida boshlandi, sinf xonasi toza va tartibli",
            "O'quvchilarning diqqati jalb qilindi",
            "Oldingi dars mavzusini qisqacha takrorlash (hook / repeat) tashkil etildi",
            "Yangi mavzu e'lon qilindi",
        ]),
        new("3-BO'LIM: DARSNING ASOSIY QISMI - Ta'lim jarayoni", "#8e44ad",
        [
            "Mavzu aniq va tushunarli tushuntirildi",
            "Yangi mavzu real hayot misoli, vizual yoki analogiya bilan bog'landi",
            "O'qituvchi faqat gapirib ketmadi - o'quvchilar faol ishtirok etdi",
            "Turli o'qitish usullari qo'llandi: juftlik, guruh, individual ish, debate",
            "No'ananaviy / innovatsion usullar qo'llandi",
            "O'quvchilarning savollari eshitildi va aniq, to'liq javob berildi",
            "Dars davomida formativ baholash bo'ldi: kuzatish, mini-test, og'zaki savol",
            "Vaqt oqilona boshqarildi - hech bir qism haddan oshib ketmadi",
        ]),
        new("4-BO'LIM: DIFFERENTSATSIYA VA INDIVIDUAL YONDASHUV", "#c0392b",
        [
            "Kuchli o'quvchilarga chuqurlashtiruvchi / ijodiy topshiriq berildi",
            "Past o'zlashtiruvchi o'quvchilarga qo'shimcha tushuntirish, vizual yoki yordam ko'rsatildi",
            "O'quvchilar mustaqil ishlashga yo'naltirildi",
        ]),
        new("5-BO'LIM: MUSTAHKAMLASH VA YAKUNLASH", "#16a085",
        [
            "Dars oxirida asosiy fikrlar umumlashtirildi (summary / xulosa)",
            "O'quvchilar nima o'rganganini o'zlari ifodaladi (exit ticket, savol-javob)",
            "Uy vazifasi aniq, hajmi maqbul, maqsadli va tushuntirib berildi",
            "Keyingi dars mavzusiga qisqacha yo'naltirish qilindi (cliffhanger)",
        ]),
        new("6-BO'LIM: SINF BOSHQARUVI VA O'QUV MUHITI", "#4b5563",
        [
            "Sinfda qulay, xavfsiz va hurmatli o'quv muhiti ta'minlandi",
            "O'qituvchi barcha o'quvchilarga teng va adolatli munosabatda bo'ldi",
            "Intizom muammolari tinch va pedagogik usulda hal qilindi",
            "O'quvchilar rag'batlantirilib borildi: maqtov, tabassum, ijobiy izoh",
        ]),
        new("7-BO'LIM: O'QITUVCHINING KASBIY MAHORAT VA QIYOFASI", "#5b4fc4",
        [
            "O'z fanini mukammal biladi - savolga aniq va to'g'ri javob bera oldi",
            "Metodik mahorat: dars qiziqarli, ilhomlantiruvchi va samarali tashkil etildi",
            "Doska va materiallardan to'g'ri, chiroyli va samarali foydalanildi",
            "Muloqot madaniyati: hurmatli, iliq tarzda gapiradi, ovozi aniq",
            "Kiyinish va tashqi ko'rinish maktab standartiga mos, o'quvchiga ibrat",
        ]),
        new("8-BO'LIM: TARBIYAVIY VA MILLIY-MA'NAVIY QIYMAT (Boshlang'ich sinflar uchun)", "#b03a5b",
        [
            "Darsda ma'naviy-axloqiy tarbiya elementlari mavjud",
            "Milliy g'ururni shakllantirish: Vatanga muhabbat, milliy qadriyatlar",
            "Ekologik tarbiya va tabiatga nisbatan mas'uliyat hissi",
            "Ta'lim-tarbiya birligi - bilim bilan bir qatorda shaxs sifatlari rivojlantirildi",
        ]),
    ];

    /// <summary>
    /// Inserts the catalogue when <c>lesson_observation_sections</c> is empty; otherwise does
    /// nothing. Order indexes start at 1, as in the old seed. Returns true when it seeded.
    /// </summary>
    public static async Task<bool> SeedAsync(IAppDbContext db, CancellationToken ct = default)
    {
        if (await db.LessonObservationSections.AnyAsync(ct)) return false;

        for (var s = 0; s < Sections.Count; s++)
        {
            var data = Sections[s];
            var section = new LessonObservationSection
            {
                Title = data.Title,
                Color = data.Color,
                OrderIndex = s + 1,
            };
            for (var c = 0; c < data.Criteria.Length; c++)
                section.Criteria.Add(new LessonObservationCriterion
                {
                    SectionId = section.Id,
                    Text = data.Criteria[c],
                    OrderIndex = c + 1,
                    IsActive = true,
                });
            db.LessonObservationSections.Add(section);
        }
        await db.SaveChangesAsync(ct);
        return true;
    }
}
