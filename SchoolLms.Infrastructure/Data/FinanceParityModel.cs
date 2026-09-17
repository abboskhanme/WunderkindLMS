using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>
/// Moliya pariteti, A to'plami — EF konfiguratsiyasi
/// (<c>docs/modules/finance-parity.md</c> §3.1, migratsiya
/// <c>FinanceParityBatchA</c>): naqd chiqimning smenasi (A1),
/// kassadan pul topshirish (A2), o'quvchiga qaytarim (A3) va chiqim
/// hujjatlari (A4).
///
/// <para>
/// <b>Nega alohida fayl?</b> <see cref="BillingModel"/>,
/// <see cref="ParityModel"/> va <see cref="StudentsParityModel"/> bilan bir
/// xil sabab: <see cref="AppDbContext.OnModelCreating"/> konflikt maydoniga
/// aylanmasligi kerak. Bu to'lqinda M dan keyin ikkita moliya slice'i
/// (S2 — kassa, S3 — qaytarim) parallel yuradi.
/// </para>
///
/// <para>
/// <b>Nega <c>expenses.cash_shift_id</c> ham shu yerda, BillingModel'da
/// emas?</b> <see cref="StudentsParityModel.Apply"/> <c>students</c> ning
/// yangi ustunlarini qanday qo'shsa — shunday. O'zgarish qaysi hujjatdan
/// kelgan bo'lsa, o'sha hujjatning faylida turadi: keyin "bu constraint
/// qayerdan chiqdi?" degan savolga javob bitta <c>git log</c> bilan
/// topiladi.
/// </para>
///
/// <para>
/// <b>Nega check constraint'lar bu yerda, xom SQL'da emas?</b>
/// <see cref="BillingModel"/> dagi izoh bilan aynan bir xil: modeldagi
/// constraint snapshot'ga tushadi, ya'ni keyingi <c>--autogenerate</c> uni
/// "ortiqcha" deb DROP qilmaydi. Xom SQL'da faqat EF ifodalay olmaydigan
/// ikki narsa qoladi — qaytarim qulfi (trigger) va GRANT/REVOKE:
/// <c>Migrations/Sql/finance_parity_guards.sql</c>.
/// </para>
/// </summary>
internal static class FinanceParityModel
{
    public static void Apply(ModelBuilder b)
    {
        ConfigureExpenseCashShift(b);
        ConfigureCashHandovers(b);
        ConfigureStudentRefunds(b);
        ConfigureExpenseAttachments(b);
    }

    // =====================================================================
    //  A1 — naqd chiqim qaysi smenadan to'landi (F1.03)
    // =====================================================================

    private static void ConfigureExpenseCashShift(ModelBuilder b)
    {
        b.Entity<Expense>(e =>
        {
            // RESTRICT: chiqimi bor smena o'chirilmaydi. Smena — pul tarixi,
            // `payments.cash_shift_id` ham aynan shunday bog'langan.
            e.HasOne<CashShift>().WithMany().HasForeignKey(x => x.CashShiftId)
                .OnDelete(DeleteBehavior.Restrict);

            // "Shu smenaning chiqimlari" — kutilgan naqdni hisoblashdagi
            // asosiy so'rov (Z-hisobot va Kassa kuni).
            e.HasIndex(x => x.CashShiftId);

            // CHECK ATAYLAB YO'Q. "Naqd chiqimda smena bo'lsin" degan qoidani
            // bazada yozib bo'lmaydi: `expenses` da to'lov usuli ustuni umuman
            // yo'q (chiqim naqdmi yoki bankdanmi — buni hozircha xizmat
            // biladi). Qoida S2 da `ExpenseService` ichida yashaydi, va u
            // faqat YANGI qatorlarga tegadi — jonli bazadagi eski chiqimlar
            // (smenasiz) yaroqli bo'lib qolishi SHART.
        });
    }

    // =====================================================================
    //  A2 — kassadan pul topshirish (F1.04). FAQAT QO'SHILADI.
    // =====================================================================

    private static void ConfigureCashHandovers(ModelBuilder b)
    {
        b.Entity<CashHandover>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.Amount).HasPrecision(14, 2);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

            e.HasOne<CashShift>().WithMany().HasForeignKey(x => x.CashShiftId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);
            // Storno — o'ziga havola.
            e.HasOne<CashHandover>().WithMany().HasForeignKey(x => x.ReversalOf)
                .OnDelete(DeleteBehavior.Restrict);

            // "Shu smenadan qancha pul chiqdi" — kutilgan naqd shundan kamayadi.
            e.HasIndex(x => x.CashShiftId);

            // Bitta topshiriqni IKKI marta storno qilib bo'lmaydi — aks holda
            // kutilgan naqd ikki marta "qaytarilib" hisobot bo'yalardi.
            // §3.1 A2 aynan `unique` deb yozadi, ya'ni QISMAN indeks emas:
            // PostgreSQL'da NULL lar bir-biridan farqli, shuning uchun
            // to'liq unikal indeks ham storno bo'lmagan minglab qatorga
            // xalaqit bermaydi (`payments` dagi `where reversal_of is not
            // null` filtri faqat indeks hajmini tejaydi).
            e.HasIndex(x => x.ReversalOf).IsUnique();

            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_cash_handovers_amount", "amount > 0");
                // finance-parity §5 Q1 — ikkita manzil, boshqasi yo'q.
                t.HasCheckConstraint(
                    "ck_cash_handovers_destination", "destination in ('bank','safe')");
                t.HasCheckConstraint(
                    "ck_cash_handovers_reversal_not_self", "reversal_of is null or reversal_of <> id");
            });
        });
    }

    // =====================================================================
    //  A3 — o'quvchiga qaytarim (F1.05). MOLIYAVIY: qaror qulflanadi.
    // =====================================================================

    private static void ConfigureStudentRefunds(ModelBuilder b)
    {
        b.Entity<StudentRefund>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.Amount).HasPrecision(14, 2);
            e.Property(x => x.RequestedAt).HasDefaultValueSql("now()");

            // RESTRICT hamma joyda: qaytarim — pul tarixi, u o'quvchi,
            // foydalanuvchi yoki smena qatori bilan birga o'chib ketmasin.
            e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.RequestedBy)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ApprovedBy)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CashShift>().WithMany().HasForeignKey(x => x.CashShiftId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<StudentRefund>().WithMany().HasForeignKey(x => x.ReversalOf)
                .OnDelete(DeleteBehavior.Restrict);

            // O'quvchi kartochkasidagi "qaytarimlar" va qoldiq hisobi.
            e.HasIndex(x => x.StudentId);

            // §3.1 A3: `reversal_of uuid null unique`.
            e.HasIndex(x => x.ReversalOf).IsUnique();

            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_student_refunds_amount", "amount > 0");
                // `payments` bilan AYNAN bir xil ro'yxat (SPEC §8.1 Q13):
                // to'rtta yorliq, provayder integratsiyasi yo'q.
                t.HasCheckConstraint(
                    "ck_student_refunds_method", "method in ('cash','card','transfer','online')");
                // Sababsiz qaytarim — tekshirib bo'lmaydigan qaytarim.
                t.HasCheckConstraint("ck_student_refunds_reason", "btrim(reason) <> ''");
                // SPEC §4.5 — ikki qavatli nazorat. Ilova tekshiruvi chetlab
                // o'tilsa ham baza to'xtatadi.
                t.HasCheckConstraint(
                    "ck_student_refunds_approver_differs",
                    "approved_by is null or approved_by <> requested_by");
                // Naqd qaytarim ochiq smenasiz tasdiqlanmaydi: pul kassadan
                // chiqadi, ya'ni qaysidir smenaning kutilgan naqdini
                // kamaytirishi SHART. Busiz F1.03 ning aynan o'zi qaytarimda
                // takrorlanardi.
                t.HasCheckConstraint(
                    "ck_student_refunds_cash_shift",
                    "approved_by is null or method <> 'cash' or cash_shift_id is not null");
            });
        });
    }

    // =====================================================================
    //  A4 — chiqim hujjatlari (F1.08). SELECT + INSERT, boshqa hech narsa.
    // =====================================================================

    private static void ConfigureExpenseAttachments(ModelBuilder b)
    {
        b.Entity<ExpenseAttachment>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.UploadedAt).HasDefaultValueSql("now()");

            // RESTRICT: dalili bor chiqimni o'chirib bo'lmaydi. Chiqim
            // o'chirilsa hujjat "yetim" bo'lib qolardi va uni qaysi pulga
            // tegishli ekanini aniqlab bo'lmasdi.
            e.HasOne<Expense>().WithMany().HasForeignKey(x => x.ExpenseId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UploadedBy)
                .OnDelete(DeleteBehavior.Restrict);

            // "Shu chiqimning hujjatlari" — chiqim kartochkasidagi yagona so'rov.
            e.HasIndex(x => x.ExpenseId);

            // Nol baytli fayl — dalil emas, xato yuklash izi.
            e.ToTable(t => t.HasCheckConstraint(
                "ck_expense_attachments_size", "size_bytes > 0"));
        });
    }
}
