using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using SchoolLms.Application.HrTasks;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Server.Controllers;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.HrTasks;

/// <summary>A Telegram sender that records instead of sending — tests never touch the network.</summary>
public sealed class FakeHrSender : IHrTelegramSender
{
    public ConcurrentQueue<(long Chat, string Text)> Messages { get; } = new();
    public ConcurrentQueue<(long Chat, string Name)> Voices { get; } = new();
    public bool IsConfigured => true;
    public string? BotUsername => "wk_test_bot";

    public Task<bool> SendHtmlAsync(long chatId, string html, CancellationToken ct = default)
    {
        Messages.Enqueue((chatId, html));
        return Task.FromResult(true);
    }

    public Task<bool> SendVoiceAsync(long chatId, byte[] bytes, string fileName, string contentType, string caption, CancellationToken ct = default)
    {
        Voices.Enqueue((chatId, fileName));
        return Task.FromResult(true);
    }

    public List<string> To(long chat) => [.. Messages.Where(m => m.Chat == chat).Select(m => m.Text)];
}

/// <summary>In-memory file store for service-level tests.</summary>
public sealed class MemoryHrFileStore : IHrFileStore
{
    private readonly ConcurrentDictionary<string, byte[]> _files = new();

    public Task<string> SaveAsync(string folder, byte[] content, string extension, DateTimeOffset now, CancellationToken ct = default)
    {
        var path = $"hr/{folder}/{Guid.NewGuid():N}{LocalHrFileStore.SafeExtension(extension)}";
        _files[path] = content;
        return Task.FromResult(path);
    }

    public string UrlFor(string relativePath) => "/uploads/" + relativePath;
    public Task<byte[]?> ReadAsync(string relativePath, CancellationToken ct = default) => Task.FromResult(_files.GetValueOrDefault(relativePath));
    public bool Delete(string relativePath) => _files.TryRemove(relativePath, out _);
}

/// <summary>Seeding and client helpers shared by the HR test classes.</summary>
public sealed class HrTasksKit(ApiFixture fixture)
{
    public const string Panel = "/api/admin/hr";
    public const string Me = "/api/hr/me";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static readonly TimeZoneInfo Tz = HrValues.Zone("Asia/Tashkent");

    private static int _phone = Random.Shared.Next(1_000_000, 8_000_000);
    private static long _telegram = 7_000_000_000 + Random.Shared.Next(1, 900_000_000);

    public static string NextPhone() => "+99899" + Interlocked.Increment(ref _phone).ToString("D7");

    public ApiFactory Api => fixture.Api;

    public sealed record Person(AppUser User, HrEmployee? Employee, HttpClient Client, long? Chat);

    /// <summary>A user (+ optional HR row, + optional Telegram link) and a client with its token.</summary>
    public async Task<Person> PersonAsync(
        string role = Roles.Staff, string? hrRole = HrRoles.Employee, string[]? permissions = null,
        bool active = true, bool telegram = false, string? name = null)
    {
        var (user, _) = await fixture.Api.SeedUserAsync(role, fullName: name, permissions: permissions);
        HrEmployee? employee = null;
        long? chat = null;
        await fixture.Api.WithDbAsync(async db =>
        {
            if (hrRole is not null)
            {
                employee = new HrEmployee
                {
                    UserId = user.Id, HrRole = hrRole, FullName = user.FullName, Phone = NextPhone(), IsActive = active,
                    CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
                };
                db.HrEmployees.Add(employee);
            }
            if (telegram)
            {
                chat = Interlocked.Increment(ref _telegram);
                db.TelegramAccounts.Add(new TelegramAccount { TelegramUserId = chat.Value, UserId = user.Id, DisplayName = user.FullName });
            }
            await db.SaveChangesAsync();
        });
        var client = fixture.Api.ClientWithToken(fixture.Api.TokenFor(role, user.Id, user.FullName, user.Email));
        return new Person(user, employee, client, chat);
    }

    public Task ReportsToAsync(string employeeId, string managerId) => fixture.Api.WithDbAsync(async db =>
    {
        db.HrEmployeeManagers.Add(new HrEmployeeManager { EmployeeId = employeeId, ManagerId = managerId });
        await db.SaveChangesAsync();
    });

    public async Task<HrTask> TaskAsync(
        string employeeId, DateTimeOffset dueAt, string status = HrTaskStatuses.Open, string source = HrTaskSources.Director,
        string title = "Test topshiriq", string kind = HrTaskKinds.Check, decimal? target = null,
        string policy = HrOverduePolicies.KeepOpen, DateTimeOffset? completedAt = null, string? comment = null)
    {
        var task = new HrTask
        {
            EmployeeId = employeeId, Title = title, Source = source, Kind = kind, TargetAmount = target,
            DueAt = dueAt.ToUniversalTime(), PeriodStart = HrValues.LocalDate(dueAt, Tz), Status = status,
            OverduePolicy = policy, CompletedAt = completedAt?.ToUniversalTime(), EmployeeComment = comment,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        await fixture.Api.WithDbAsync(async db => { db.HrTasks.Add(task); await db.SaveChangesAsync(); });
        return task;
    }

    /// <summary>Local Tashkent wall time → instant.</summary>
    public static DateTimeOffset At(int y, int mo, int d, int h = 12, int mi = 0) =>
        HrValues.At(new DateOnly(y, mo, d), new TimeOnly(h, mi), Tz);

    /// <summary>A service over the owner connection with fakes (service-level tests).</summary>
    public async Task WithServiceAsync(Func<HrTasksService, AppDbContext, Task> action, FakeHrSender? sender = null, DateTimeOffset? now = null)
    {
        await fixture.Api.WithDbAsync(async db =>
        {
            var service = new HrTasksService(db, sender ?? new FakeHrSender(), new MemoryHrFileStore(), NullLogger<HrTasksService>.Instance);
            if (now is { } n) service.Clock = () => n;
            await action(service, db);
        });
    }

    public static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(string.IsNullOrEmpty(text) ? "null" : text).RootElement.Clone();
    }

    public static async Task<string> CodeAsync(HttpResponseMessage response)
    {
        var body = await ReadAsync(response);
        return body.ValueKind == JsonValueKind.Object && body.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";
    }

    public static Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method is "POST" or "PUT" or "PATCH") request.Content = JsonContent.Create(body ?? new { });
        return client.SendAsync(request);
    }

    // ---------------------------------------------------------------------
    //  A derived host whose Telegram sender is a fake (the shared host is untouched)
    // ---------------------------------------------------------------------

    public static readonly FakeHrSender Sender = new();
    private static readonly Lock HostLock = new();
    private static WebApplicationFactory<AuthController>? _host;

    public WebApplicationFactory<AuthController> Host
    {
        get
        {
            lock (HostLock)
            {
                return _host ??= fixture.Api.WithWebHostBuilder(b => b.ConfigureServices(s =>
                {
                    s.RemoveAll<IHrTelegramSender>();
                    s.AddSingleton<IHrTelegramSender>(Sender);
                }));
            }
        }
    }

    public HttpClient HostClient(AppUser user)
    {
        var client = Host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", fixture.Api.TokenFor(user.Role, user.Id, user.FullName, user.Email));
        return client;
    }
}
