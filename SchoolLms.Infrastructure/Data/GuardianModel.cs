using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>
/// Vasiylar (SPEC §3.2) va Telegram Mini App bog'lanishining (SPEC §6 Faza 3)
/// EF konfiguratsiyasi.
///
/// <para>
/// <b>Nega alohida fayl?</b> <see cref="BillingModel"/> va <c>AnomalyModel</c> bilan
/// bir xil sabab: <see cref="AppDbContext.OnModelCreating"/> umumiy fayl va u
/// konflikt maydoniga aylanmasligi kerak. <see cref="AppDbContext"/> da bitta chaqiruv qoladi.
/// </para>
///
/// <para>
/// <b>Nega constraint'lar EF modelida?</b> Modeldagi constraint snapshot'ga tushadi,
/// ya'ni keyingi <c>--autogenerate</c> uni "ortiqcha" deb DROP qilmaydi. Xom SQL'da
/// faqat EF ifodalay olmaydigan narsa qoladi — <c>app_rw</c> uchun GRANT
/// (<c>Migrations/Sql/guardian_guards.sql</c>) va eski ma'lumotni ko'chirish
/// (<c>Migrations/Sql/guardians_backfill.sql</c>).
/// </para>
/// </summary>
internal static class GuardianModel
{
    public static void Apply(ModelBuilder b)
    {
        ConfigureGuardians(b);
        ConfigureStudentGuardians(b);
        ConfigureTelegramAccounts(b);
        ConfigureTelegramLinkCodes(b);
        ConfigureChatReads(b);
    }

    private static void ConfigureGuardians(ModelBuilder b)
    {
        b.Entity<Guardian>(e =>
        {
            e.HasKey(x => x.Id);

            // Telefonning solishtiruv kaliti BAZADA hisoblanadi — OXIRGI 9 RAQAM,
            // aynan `PhoneUtil.Key` dagidek ("+998 90 123 45 67" va "901234567"
            // bitta odam). Ilova uni yozmaydi, ya'ni u `phone` dan uzoqlasha
            // olmaydi va normallashtirish qoidasi ikkiga bo'linmaydi.
            // `right(s, 9)` satr qisqa bo'lsa uni butunligicha qaytaradi.
            e.Property(x => x.PhoneKey)
                .HasComputedColumnSql("right(regexp_replace(phone, '[^0-9]', '', 'g'), 9)", stored: true);

            // Bitta akkaunt — bitta vasiy. Ikki vasiy bitta akkauntga bog'lansa,
            // Mini App "kimning farzandlari" degan savolga ikki xil javob berardi.
            e.HasIndex(x => x.UserId).IsUnique().HasFilter("user_id is not null");

            // Bitta raqam — bitta vasiy. Aynan shu indeks "bir ota-ona, ikki farzand"
            // ni ishlatadi: ikkinchi farzand yangi vasiy yaratmaydi, mavjudini topadi.
            e.HasIndex(x => x.PhoneKey).IsUnique().HasFilter("phone_key <> ''");

            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.SetNull);

            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_guardians_full_name", "btrim(full_name) <> ''");
                t.HasCheckConstraint("ck_guardians_phone", "btrim(phone) <> ''");
            });
        });
    }

    private static void ConfigureStudentGuardians(ModelBuilder b)
    {
        b.Entity<StudentGuardian>(e =>
        {
            e.HasKey(x => new { x.StudentId, x.GuardianId });

            // O'quvchi o'chirilsa bog'lanish ham ketadi (bog'lanish tarix emas).
            e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Guardian>().WithMany().HasForeignKey(x => x.GuardianId)
                .OnDelete(DeleteBehavior.Cascade);

            // "Vasiyning farzandlari" so'rovi shu indeksdan yuradi (PK teskari tartibda).
            e.HasIndex(x => x.GuardianId);

            // Har o'quvchida ko'pi bilan bitta asosiy vasiy — ILOVA emas, BAZA kafolati.
            e.HasIndex(x => x.StudentId).IsUnique().HasFilter("is_primary")
                .HasDatabaseName("ux_student_guardians_one_primary");

            // students-parity.md §3.2 (S-8): ro'yxat KENGAYTIRILDI —
            // `father`, `mother`, `other` qo'shildi. Eski uchta qiymat joyida,
            // ya'ni birorta mavjud qator o'zgarmaydi va constraint faqat
            // ALMASHTIRILADI (migratsiyadagi yagona `drop` — u ma'lumotga emas,
            // check constraint'ga tegadi). Yonidagi `relation_note` "boshqa"
            // tanlovining tafsilotini yozadi.
            e.ToTable(t => t.HasCheckConstraint(
                "ck_student_guardians_relation",
                "relation in ('parent','father','mother','grandparent','trustee','other')"));
        });
    }

    private static void ConfigureTelegramAccounts(ModelBuilder b)
    {
        b.Entity<TelegramAccount>(e =>
        {
            e.HasKey(x => x.Id);

            // Ikki tomonlama unikal: bitta Telegram id bitta akkauntga, bitta akkaunt
            // bitta Telegram id'ga. Aks holda "kim kimning nomidan kirdi" savoli
            // ko'p javobli bo'lardi.
            e.HasIndex(x => x.TelegramUserId).IsUnique();
            e.HasIndex(x => x.UserId).IsUnique();

            // Akkaunt o'chsa bog'lanish ham ketadi — bog'lanish akkauntsiz ma'nosiz.
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.LinkedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            e.ToTable(t => t.HasCheckConstraint(
                "ck_telegram_accounts_user_id", "telegram_user_id > 0"));
        });
    }

    private static void ConfigureTelegramLinkCodes(ModelBuilder b)
    {
        b.Entity<TelegramLinkCode>(e =>
        {
            e.HasKey(x => x.Id);

            // Kod hash'i unikal — bir vaqtda ikkita bir xil kod chiqmaydi
            // (generator to'qnashuvda qayta uriniladi).
            e.HasIndex(x => x.CodeHash).IsUnique();

            // "Shu foydalanuvchining tirik kodi bormi" so'rovi uchun.
            e.HasIndex(x => new { x.UserId, x.ExpiresAt });

            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_telegram_link_codes_window", "expires_at > created_at");
                // Ishlatilgan kodda kim ishlatgani ham bo'lishi shart (audit izi to'liq).
                t.HasCheckConstraint(
                    "ck_telegram_link_codes_used",
                    "(used_at is null) = (used_by_telegram_user_id is null)");
            });
        });
    }

    private static void ConfigureChatReads(ModelBuilder b)
    {
        b.Entity<ChatRead>(e =>
        {
            e.HasKey(x => new { x.UserId, x.Channel });

            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
