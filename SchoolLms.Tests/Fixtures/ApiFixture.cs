namespace SchoolLms.Tests.Fixtures;

/// <summary>
/// Butun test yurishi uchun BIR MARTA: Postgres konteyneri + migratsiya qo'llangan baza +
/// ko'tarilgan ilova. Test klasslari <c>[Collection(SchoolLmsCollection.Name)]</c> orqali
/// shu bitta nusxani bo'lishadi.
///
/// <para>
/// Nega bitta umumiy baza? Konteyner ko'tarish ~3 s, migratsiya ~2 s, ilova ~1 s. Har test
/// uchun qaytarish 90 soniyalik byudjetni yeb qo'yardi. Izolyatsiya BOSHQACHA ta'minlanadi:
/// har test o'z ma'lumotini o'zi yaratadi (takrorlanmas login/id bilan) va boshqa testning
/// qatorlariga tayanmaydi. Haqiqatan toza baza kerak bo'lsa —
/// <c>Postgres.CreateDatabaseAsync()</c> chaqiriladi, u shablondan ~100 ms da nusxa oladi.
/// </para>
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public PostgresFixture Postgres { get; } = new();

    /// <summary>Ilova ulangan umumiy baza.</summary>
    public TestDatabase Database { get; private set; } = default!;

    public ApiFactory Api { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        await Postgres.InitializeAsync();
        Database = await Postgres.CreateDatabaseAsync("app");
        Api = new ApiFactory(Database);
        // Host'ni shu yerda ko'taramiz — ko'tarilish vaqti birinchi testning hisobiga
        // tushmasin va ishga tushmay qolsa xato fixture'da, aniq joyda chiqsin.
        _ = Api.Services;
    }

    public async Task DisposeAsync()
    {
        if (Api is not null) await Api.DisposeAsync();
        await Postgres.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class SchoolLmsCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "schoollms";
}
