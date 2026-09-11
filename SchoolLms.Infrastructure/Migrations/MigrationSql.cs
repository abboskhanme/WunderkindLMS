using System.Reflection;

namespace SchoolLms.Infrastructure.Migrations;

/// <summary>
/// Migratsiya ichidan xom SQL faylini o'qish (<c>Migrations/Sql/*.sql</c>).
///
/// <para>
/// <b>Nega alohida fayl, C# satri emas?</b> Trigger va GRANT bloki 100+ qator SQL.
/// Uni C# string ichiga solish uni o'qib bo'lmaydigan qiladi: sintaksis bo'yash yo'q,
/// qo'shtirnoqlarni ekranlash kerak, <c>git diff</c> bitta ulkan qator bo'lib chiqadi.
/// Moliyaviy himoyani KO'RIB tekshirish mumkin bo'lishi kerak.
/// </para>
///
/// <para>
/// <b>Nega embedded resource, diskdagi fayl emas?</b> Migratsiya prod konteynerida
/// ishga tushadi. U yerda faqat chop etilgan (<c>publish</c>) natija bor — manba
/// papkasi yo'q. Embedded resource assembly ichida keladi, ya'ni har doim topiladi.
/// Fayl yo'q bo'lsa <see cref="InvalidOperationException"/> — migratsiya JIM
/// qolmasdan, baland yiqiladi (yarim qo'llangan moliyaviy himoyadan ko'ra yaxshiroq).
/// </para>
/// </summary>
internal static class MigrationSql
{
    private static readonly Assembly Owner = typeof(MigrationSql).Assembly;

    /// <summary><paramref name="fileName"/> — masalan <c>billing_guards.sql</c>.</summary>
    public static string Read(string fileName)
    {
        // csproj: <EmbeddedResource Include="Migrations\Sql\*.sql" /> — resurs nomi
        // "SchoolLms.Infrastructure.Migrations.Sql.<fayl>" ko'rinishida bo'ladi.
        var resource = $"{typeof(MigrationSql).Namespace}.Sql.{fileName}";

        using var stream = Owner.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException(
                $"Migratsiya SQL fayli topilmadi: '{resource}'. "
                + "SchoolLms.Infrastructure.csproj dagi <EmbeddedResource Include=\"Migrations\\Sql\\*.sql\" /> "
                + "qatorini tekshiring. Mavjud resurslar: "
                + string.Join(", ", Owner.GetManifestResourceNames()));

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
