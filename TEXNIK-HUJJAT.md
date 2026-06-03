# Texnik hujjat — Auth, Xavfsizlik, Rollar (Admin/User/Owner) va Multi‑tenant

Bu hujjat SchoolLms tizimidagi **autentifikatsiya, avtorizatsiya, xavfsizlik va ko'p‑maktablilik (multi‑tenant)** arxitekturasini, boshqa bir saytni shu asosda qurish uchun, amalga oshirish darajasida tavsiflaydi. Texnologiyaga bog'liq joylar .NET/EF Core misolida, lekin g'oyalar har qanday stekka ko'chiriladi.

> Yondashuv qisqacha: **bitta umumiy DB**, har qatorda `TenantId`; **global query filter** avtomatik izolyatsiya; **subdomen = maktab**, **asosiy domen = Control Plane (owner)**; **JWT** (12 soat) + har so'rovda **revocation** tekshiruvi; **PBKDF2** parol; **rol + ruxsat (modul) + obuna** matritsasi.

---

## 1. Umumiy arxitektura

**Stek:** .NET 8 (ASP.NET Core), EF Core 8 (SQL Server), JWT Bearer, SignalR; frontend React 19 + TypeScript + Vite + Tailwind.

**Qatlamlar (Clean Architecture), bog'liqlik ichkariga:**

```
Server  →  Infrastructure  →  Application  →  Domain
```

| Qatlam | Mas'uliyat | Asosiy fayllar |
|---|---|---|
| **Domain** | Sof entity'lar, rol/ruxsat konstantalari, soat | `Entities.cs`, `Roles.cs`, `AdminModules.cs`, `AppClock`, `Platform/{Tenant,PlatformOwner}.cs` |
| **Application** | DTO, servis mantiqi, `IAppDbContext` abstraksiyasi | `Dtos/`, `Services/`, `Hubs/`, `Abstractions/IAppDbContext.cs` |
| **Infrastructure** | DB konteksti, auth (hash/jwt/account), migratsiyalar, tenancy | `Data/AppDbContext.cs`, `Auth/`, `Tenancy/`, `Migrations/` |
| **Server** | Controllerlar, pipeline, middleware | `Controllers/`, `Program.cs`, `Tenancy/TenantResolutionMiddleware.cs` |

**Muhim qoidalar:**
- `Application` `Infrastructure`'ga REF qilmaydi; servislar konkret `AppDbContext` o'rniga **`IAppDbContext`** ga bog'lanadi (DI: `AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>())`).
- Vaqt har doim **`AppClock.Now`** (Asia/Tashkent, UTC+5) orqali — `DateTime.Now/UtcNow` ishlatilmaydi (timestamp'lar mahalliy "wall clock" sifatida saqlanadi).

---

## 2. Multi‑tenant model (eng muhim qism)

### 2.1 Shared‑DB + shadow `TenantId`
Barcha maktablar bitta bazada. Har bir "maktab entity"siga **shadow ustun** `TenantId` (string, 64, default `""`) qo'shiladi, indekslanadi va **global query filter** bilan avtomatik filtrlanadi. Platform entity'lari (`PlatformOwner`, `Tenant`) **filtrlanmaydi**.

`AppDbContext.OnModelCreating` (har entity uchun):
```csharp
private void ApplyTenantFilter<TEntity>(ModelBuilder b) where TEntity : class
{
    b.Entity<TEntity>().Property<string>("TenantId").HasMaxLength(64).IsRequired().HasDefaultValue("");
    b.Entity<TEntity>().HasIndex("TenantId");
    // _tenant — scoped ITenantContext, query bajarilganda o'qiladi (joriy maktab)
    b.Entity<TEntity>().HasQueryFilter(e => EF.Property<string>(e, "TenantId") == _tenant.TenantId);
}
```

Hamma entity bo'ylab qo'llash:
```csharp
foreach (var et in b.Model.GetEntityTypes().ToList())
{
    var clr = et.ClrType;
    if (IsPlatformEntity(clr) || et.IsOwned() || et.BaseType != null) continue;
    apply.MakeGenericMethod(clr).Invoke(this, [b]);
}
```

### 2.2 `SaveChanges` — TenantId yozish va chegarani himoya qilish
```csharp
private void TagTenant()
{
    var tid = _tenant.TenantId;
    foreach (var entry in ChangeTracker.Entries())
    {
        if (IsPlatformEntity(entry.Entity.GetType())) continue;
        if (entry.Metadata.FindProperty("TenantId") is null) continue;
        switch (entry.State)
        {
            case EntityState.Added:
                var cur = (string?)entry.Property("TenantId").CurrentValue;
                if (string.IsNullOrEmpty(cur) && !string.IsNullOrEmpty(tid))
                    entry.Property("TenantId").CurrentValue = tid;   // yangi qatorga joriy tenant
                break;
            case EntityState.Modified:
            case EntityState.Deleted:
                var owner = (string?)entry.Property("TenantId").CurrentValue;
                if (!string.IsNullOrEmpty(tid) && owner != tid)
                    throw new InvalidOperationException("Tenant chegarasi buzildi"); // cross-tenant yozishni bloklaydi
                break;
        }
    }
}
```

### 2.3 `ITenantContext` (scoped)
```csharp
public interface ITenantContext { bool IsPlatform { get; } string? Slug { get; } string? TenantId { get; }
    void SetTenant(string tenantId, string slug); void SetPlatform(); }
```
Har so'rovda middleware to'ldiradi. Fon xizmatlari (so'rov yo'q) uchun `ITenantDbRunner.ForEachActiveTenantAsync(...)` har aktiv tenant uchun alohida scope ochib `SetTenant` qiladi (aks holda query filter hamma narsani yashiradi).

### 2.4 Tenant aniqlash (middleware)
Tartib: `UseAuthentication()` → **TenantResolutionMiddleware** → `UseAuthorization()`.

- **Kirgan foydalanuvchi:** tokendagi `tenant` claim ASOSIY (ishonchli) manba. So'rovdagi subdomen/`X-Tenant` boshqa bo'lsa → **403** (token boshqa maktabniki).
- **Anonim (login):** `X-Tenant` sarlavhasi (ustun) yoki Host subdomeni.
- Bo'sh / `www`,`app`,`admin` / asosiy domen → **Control Plane** (`SetPlatform`).
- Slug topilmasa → 404; **Suspended** → 403; **obuna muddatidan tashqari** → 403; aks holda `SetTenant(id, slug)` + **modul litsenziyasi** tekshiruvi.

`TenantStore` slug→`TenantInfo(Id, Slug, Status, SubscriptionStartsAt, SubscriptionEndsAt, EnabledModules)` ni **30 soniya** keshlaydi.

> Boshqa stekka ko'chirish: "tenant context"ni request-scoped qiling; ORM'da global filter yo'q bo'lsa, har repodan `WHERE tenant_id = @current` ni majburlovchi bazaviy repо/queryni markazlashtiring; yozishda tenant tekshiruvini bitta `SaveChanges` ekvivalentiga joylashtiring.

---

## 3. Control Plane (Owner / loyiha boshlig'i)

Asosiy domen = Control Plane. Maktab rollaridan **butunlay alohida**: o'z tokeni (`scope=platform`), o'z auth oqimi, hech bir maktab DB satriga tegmaydi.

### 3.1 Entity'lar (filtrlanmaydi)
```csharp
class PlatformOwner { string Id; string FullName; string Email; string PasswordHash; string? LastLoginAt; }

class Tenant {
    string Id; string Name; string Slug;          // Slug = subdomen, unikal
    string Status;                                 // provisioning | active | suspended
    string SuperAdminEmail; DateTime CreatedAt;
    // --- Obuna (owner boshqaradi) ---
    List<string> EnabledModules;                   // bo'sh = CHEKLOVSIZ (hamma bo'lim)
    DateTime? SubscriptionStartsAt;                // null = chegarasiz (kun boshi)
    DateTime? SubscriptionEndsAt;                  // null = muddatsiz (kun oxiri, inclusive)
    decimal SubscriptionPrice;                     // bitta umumiy narx
}
```

### 3.2 Yangi maktab ochish (provisioning)
`ProvisioningService.CreateAsync(name, slug, superAdminEmail, superAdminPassword, fullName, modules, startsAt, endsAt, price)`:
1. Slug normalizatsiya + validatsiya (a‑z,0‑9,'-'; 2‑32), unikal.
2. Email **butun baza bo'ylab unikal** (login maktab kodisiz ishlaydi).
3. Parol ≥ 8.
4. `Tenant` + superadmin `AppUser` (Role=`superadmin`) + `SchoolMeta` yaratiladi; platform kontekstida `TenantId` **qo'lda** o'rnatiladi (SaveChanges bo'sh ko'rib o'zgartirmaydi).

### 3.3 Obuna enforcement
- Muddat: `now < start` → 403 "hali boshlanmagan"; `now > end` → 403 "muddati tugagan" (vaqt = `AppClock.Now`).
- Modul: `EnabledModules` bo'sh = cheklovsiz; aks holda ochilmagan bo'lim API'si 403 — lekin **faqat izolyatsiyalangan (leaf) admin yo'llari** (`/api/admin/finance|contracts|discipline|teacher-reports|leads|lead-stages|feedback|academic-year|staff`). O'quvchilar/sinflar/jadval kabi **ulashilgan** yo'llar backend'da bloklanMAYDI (boshqa bo'limlarga ham kerak) — ular faqat frontend nav'da yashiriladi.

### 3.4 Owner API (qisqa)
```
POST /api/platform/auth/login            → { token, owner }
GET  /api/platform/auth/me
PUT  /api/platform/auth/account
GET  /api/platform/tenants               (ro'yxat)
POST /api/platform/tenants               (yaratish: modules, subscriptionStartsAt/EndsAt, price)
PATCH/api/platform/tenants/{id}          (status: active|suspended)
PUT  /api/platform/tenants/{id}          (nom/superadmin email/parol)
PUT  /api/platform/tenants/{id}/subscription   (modules, narx, start/end sanalar)
GET  /api/platform/tenants/modules       (bo'limlar katalogi)
GET  /api/platform/tenants/{id}/stats
```
Hammasi `[Authorize(Roles="platformowner")]`.

---

## 4. Autentifikatsiya

### 4.1 Foydalanuvchi (AppUser)
```csharp
class AppUser {
    string Id; string FullName;
    string Role;                 // admin | superadmin | teacher | student | staff | (parent)
    string Email;                // = LOGIN (username), butun baza bo'ylab unikal
    string PasswordHash;         // PBKDF2
    string? InitialPassword;     // admin yaratgan ochiq parol — FAQAT 1‑login'gacha (keyin null)
    string? FirstLoginAt; string? LastLoginAt;
    string Position;             // staff lavozimi (yorliq)
    List<string> Permissions;    // FAQAT staff uchun (admin bo'limlari)
}
```

### 4.2 Login (maktab) — `POST /api/auth/login`
1. Login (Email) **butun baza bo'ylab unikal** ⇒ maktab kodi shart emas; server foydalanuvchini va uning maktabini topadi.
   - Platform kontekstida: `Users.IgnoreQueryFilters().Where(Email==..)`; aks holda filtrlangan.
2. Parolni `PasswordHasher.Verify` bilan tekshirib mosini ajratamiz.
3. **0 mos** → 401 (urinish IP bilan log'lanadi — brute‑force kuzatuvi). **>1 mos** (kamdan‑kam: bir login bir nechta maktabda) → 409 + maktablar ro'yxati (X‑Tenant bilan qayta login).
4. Foydalanuvchining tenantini aniqlab `SetTenant`; **arxiv tekshiruvi** (`IsBlockedAsync`) → arxivlangan o'qituvchi/o'quvchi 401.
5. Login kuzatuvi: `FirstLoginAt` (bir marta), `LastLoginAt` (har safar); `InitialPassword=null`.
6. JWT yaratiladi (`tenant` claim = slug) va `UserDto`(perms + modules) qaytadi.

### 4.3 JWT tuzilishi
- Algoritm: **HMAC‑SHA256**, kalit `Jwt:Key` (prod'da `Jwt__Key` env SHART; dev'da tasodifiy vaqtinchalik). Issuer/Audience = `SchoolLms`. Muddat **12 soat**.
- Maktab tokeni claim'lari: `sub`, `NameIdentifier` (=userId), `Name`, `Email`, `Role`, **`tenant`** (slug).
- Platform tokeni: `Role=platformowner`, **`scope=platform`** (AppUser EMAS).
- Validatsiya: issuer/audience/lifetime/signing key. SignalR uchun token query‑string `access_token` dan ham olinadi (`/hubs/chat`).

### 4.4 Akkaunt yaratish (`AccountFactory`)
- **Login (username)** FISHdan tuziladi: familiya+ism lotinlashtirilib (kirill→lotin map), belgisiz qo'shiladi (`voxidjonovabduxalil`), band bo'lsa raqam (`...2`). Unikallik **butun baza** (`IgnoreQueryFilters`) + hali saqlanmagan (`Local`) bo'yicha.
- **Parol**: 8 belgili tasodifiy, chalkashtirmaydigan alfavit (`0/O`,`1/l/I` yo'q) — `RandomNumberGenerator`.
- Ochiq parol `InitialPassword` da saqlanadi (superadmin ko'rishi/eksport uchun), birinchi login'da yoki parol o'zgarsa `null`.

### 4.5 Parol hashlash (`PasswordHasher`) — **PBKDF2**
```
Format: {iterations}.{saltBase64}.{hashBase64}
Algoritm: PBKDF2-SHA256, salt=16B, key=32B, iterations=100_000
Verify: CryptographicOperations.FixedTimeEquals (timing-safe)
```
> Boshqa saytda: bcrypt/scrypt/argon2 ham bo'ladi; muhimi — sho'r (salt) + ko'p iteratsiya + timing‑safe solishtirish. Parolni HECH QACHON ochiq saqlamang (InitialPassword — ataylab, 1 martalik ko'rsatish uchun; ishlab chiqarishda bu funksiyani olib tashlash mumkin).

---

## 5. Avtorizatsiya — Rollar va Ruxsatlar (Admin/User/Owner)

### 5.1 Rollar
| Rol | Izoh |
|---|---|
| `superadmin` | Maktab egasi — admin huquqlari + o'quv yili boshida muzlatilgan amallarni o'zgartirish |
| `admin` | Oddiy administrator — barcha admin bo'limlari (muzlatilganlardan tashqari) |
| `teacher` | O'qituvchi — `Teacher.Permissions` dagi bo'limlar |
| `student` | O'quvchi — `Student.UserId` orqali bog'langan akkaunt |
| `staff` | O'qituvchi bo'lmagan xodim — admin panel, faqat `AppUser.Permissions` bo'limlari |
| `parent` | Ota‑ona — login = **telefon**, `Student.ParentPhone` orqali farzandga bog'lanadi (UserId emas!) |
| `platformowner` | Control Plane egasi — maktab DB'siga tegmaydi |

### 5.2 Ruxsat matritsasi (3 qatlam)
Bo'lim ko'rinishi = **rol** ∧ **xodim ruxsati** ∧ **tenant moduli**:

1. **Rol** — route'larda `[Authorize(Roles=...)]`.
2. **Xodim ruxsati** — `staff` uchun `AppUser.Permissions`, `teacher` uchun `Teacher.Permissions`; `admin/superadmin` uchun `null` (hammasi). Login javobida `UserDto.Permissions`.
3. **Tenant moduli (litsenziya)** — `Tenant.EnabledModules` (bo'sh = cheklovsiz). Login/`me` javobida `UserDto.Modules` (null = cheklovsiz) → SPA nav shu bo'yicha yashiradi (admin/superadmin uchun ham).

Bo'lim kalitlari yagona manba — **`AdminModules`** (18 ta: leads, students, teachers, attendance, schedule, classes, journal, messages, app, gradesReport, teacherReports, contracts, finance, academicYear, settings, staff, feedback, discipline). Frontend `adminPermissions` va nav `perm` kalitlari shularga MOS.

Frontend nav filtri (g'oya):
```ts
const canSee = (x) =>
  (!x.roles || x.roles.includes(role)) &&            // rol
  (!x.perm || !user.permissions || user.permissions.includes(x.perm)) &&  // xodim ruxsati
  (!x.perm || !user.modules || user.modules.includes(x.perm))             // tenant moduli
```

---

## 6. Token bekor qilish (Revocation)

JWT stateless va 12 soat amal qiladi. Arxivlangan/o'chirilgan akkaunt eski tokeni bilan kira olmasligi uchun **har so'rovda** holat tekshiriladi (`JwtBearerEvents.OnTokenValidated`):

```csharp
OnTokenValidated = async ctx => {
    var p = ctx.Principal;
    var userId = p?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? p?.FindFirst("sub")?.Value;
    if (p is null || string.IsNullOrEmpty(userId)) return;
    var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
    bool blocked;
    if (p.IsInRole("teacher"))
        blocked = !await db.Teachers.IgnoreQueryFilters().AnyAsync(t => t.UserId==userId && !t.IsArchived);
    else if (p.IsInRole("student"))
        blocked = !await db.Students.IgnoreQueryFilters().AnyAsync(s => s.UserId==userId && !s.IsArchived);
    else if (p.IsInRole("staff") || p.IsInRole("admin") || p.IsInRole("superadmin"))
        blocked = !await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id==userId);  // o'chirilgan = bloklangan
    else blocked = false;  // parent (telefon orqali) / platformowner — tegilmaydi
    if (blocked) ctx.Fail("Akkaunt arxivlangan yoki o'chirilgan");
};
```

Qoidalar:
- **O'qituvchi/o'quvchi arxivlanganda** `IsArchived=true`, AppUser saqlanadi ⇒ shu tekshiruv kerak.
- **Xodim o'chirilganda** AppUser DB'dan o'chadi ⇒ "user mavjudmi" tekshiruvi yetarli.
- **Parent** `UserId` orqali bog'lanmaydi (telefon) ⇒ uni bloklash mantig'iga kiritmaymiz (xato bloklamaslik uchun).
- Login'da ham `IsBlockedAsync` bilan bloklash (arxivlangan qayta login qila olmasin).
- `IgnoreQueryFilters` — tenant kontekstidan mustaqil; GUID id'lar global unikal.

> Eslatma: bu har so'rovda 1 ta indeksli so'rov. Katta yuk uchun qisqa (30‑60s) keshlash mumkin (lekin revocation kechikadi). Muqobil — `SecurityStamp`/token versiyasi: AppUser'da versiya, tokenга claim, arxivlash/parol o'zgarganda versiyani oshirish.

---

## 7. Xavfsizlik choralari (OWASP / Zero‑Trust)

| Soha | Chora |
|---|---|
| **Parol** | PBKDF2‑SHA256 (100k), salt, timing‑safe verify; ochiq parol saqlanmaydi |
| **JWT kaliti** | `Jwt__Key` env (prod SHART); dev'da tasodifiy (restartda sessiya tushadi) |
| **Login brute‑force** | Rate‑limit: IP bo'yicha 1 daqiqada 10 urinish → 429; muvaffaqiyatsiz urinish IP bilan log |
| **Login unikalligi (TOCTOU)** | `AppUser.Email` ga **unique indeks** (parallel ro'yxatdan o'tish poygasini DB bloklaydi) |
| **Multi‑tenant izolyatsiya** | Global query filter + `SaveChanges` cross‑tenant yozishni bloklaydi; tokendagi `tenant` ASOSIY; mos kelmasa 403 |
| **IDOR** | Portal so'rovlarida student/owner token'dan olinadi (`?studentId` faqat admin uchun) |
| **Fayl yuklash** | Allowlist kengaytma (rasm/hujjat/media), **20 MB** limit; `.svg`/`.html` RAD (saqlangan XSS); fayl nomi `Guid` + kengaytma |
| **Xavfsizlik sarlavhalari** | `X-Content-Type-Options:nosniff`, `X-Frame-Options:DENY`, `Referrer-Policy:no-referrer`, prod'da **CSP** (`default-src 'self'`, `script-src 'self'`, `frame-ancestors 'none'`, `object-src 'none'`...), **HSTS** |
| **Reverse‑proxy** | Prod'da `ForwardedHeaders` (X‑Forwarded‑For/Proto) — real IP rate‑limit uchun; konteyner porti internetga ochilmaydi (faqat tunnel) |
| **Input validatsiya** | Pul (to'lov/maosh/tranzaksiya) `Amount > 0`; pul maydonlari `decimal(18,2)` |
| **SQL injection** | EF Core parametrlangan so'rovlar (raw SQL yo'q) |
| **Audit** | `AuditService` — yaratish/o'zgartirish/o'chirish tarixini yozadi (kim, qachon, eski/yangi snapshot) |

CSP (prod) namunasi:
```
default-src 'self'; img-src 'self' data: blob: https:; style-src 'self' 'unsafe-inline';
script-src 'self'; connect-src 'self' ws: wss:; font-src 'self' data:;
frame-ancestors 'none'; object-src 'none'; base-uri 'self'
```

---

## 8. Frontend (SPA) integratsiyasi

- **Token saqlash:** maktab `localStorage['token']`, Control Plane alohida `localStorage['platform_token']` (aralashmaydi).
- **So'rov interceptori:** har so'rovga `Authorization: Bearer <token>` + subdomen bo'lsa `X-Tenant: <slug>`.
- **Javob interceptori (401):** token tugagan/bekor → token+user tozalanadi va `/login`ga yo'naltiriladi (login so'rovining o'zidagi 401 mustasno).
- **Sessiyani tiklash:** sahifa ochilganda `GET /api/auth/me` orqali tekshiriladi; muvaffaqiyatsiz → logout. `me` javobi `permissions` va `modules` ni yangilaydi (rol/obuna o'zgarsa nav yangilanadi).
- **Rol bo'yicha bosh sahifa:** `homeByRole` (admin→/admin, teacher→/teacher, ...).

---

## 9. Boshqa sayt uchun amalga oshirish — bosqichma‑bosqich checklist

1. **Domen modeli:** `User`(role, email=login, passwordHash), tenant entity (`Tenant`), platform egasi (`Owner`). Har "tenant entity"ga `tenant_id`.
2. **Tenant konteksti:** request‑scoped `TenantContext`; subdomen/header'dan aniqlovchi middleware (auth'dan keyin); tokendagi tenant'ni ASOSIY qiling.
3. **Izolyatsiya:** ORM global filter yoki markaziy repo `WHERE tenant_id=@current`; yozishda cross‑tenant bloklash.
4. **Parol:** PBKDF2/argon2 + salt + timing‑safe; ochiq saqlamang.
5. **Login:** global‑unikal username (yoki email); ko'p maktab holati uchun "maktab tanlash" oqimi; muvaffaqiyatsiz urinishni log + rate‑limit.
6. **JWT:** qisqa muddat (12s), claim'larda `userId, role, tenant`; kalit env'da; platform uchun alohida `scope`.
7. **Rollar/ruxsat:** rol + bo'lim (modul) kalitlari yagona katalog; backend route guard + frontend nav filtri; modul/obuna tenant darajasida.
8. **Revocation:** har so'rovda akkaunt holatini tekshir (arxiv/o'chirilgan) yoki token‑versiya (`SecurityStamp`).
9. **Owner/Control Plane:** tenant ochish (provisioning), obuna (modullar + sana oralig'i + narx), muddat/suspend → 403.
10. **Xavfsizlik:** unique email indeks, fayl allowlist+limit, xavfsizlik sarlavhalari + CSP + HSTS, input validatsiya, audit log.

---

## 10. Asosiy API xaritasi (qisqa)

```
# Maktab auth
POST /api/auth/login            → { token, user{ id, fullName, role, email, permissions, modules } }
GET  /api/auth/me
PUT  /api/auth/account          (login/parol o'zgartirish)

# Control Plane (owner)
POST /api/platform/auth/login
GET/POST/PUT/PATCH /api/platform/tenants[...]   (3‑bo'limga qarang)

# Misol himoyalangan resurs
[Authorize(Roles="admin,superadmin,staff")] /api/admin/...
[Authorize(Roles="teacher")]               /api/teacher/...
[Authorize(Roles="student,parent")]        /api/student/...
```

Barcha `/api/*` so'rovlari: JWT validatsiya → revocation tekshiruvi → tenant aniqlash (mos kelmasa/suspended/muddat/modul → 403) → controller (avtomatik tenant‑filtrlangan ma'lumot).
