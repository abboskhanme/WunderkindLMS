/**
 * HR topshiriqlari — API shartnomasi (docs/modules/hr-tasks.md §6).
 *
 * WunderkindHR'ning `frontend/src/entities/hr/model.ts` faylidan MEXANIK ko'chirilgan:
 * maydon nomlari camelCase ga o'girilgan, qiymatlar (status, manba va h.k.) o'zgarmagan.
 * Backend DTO'lari aynan shu shaklda serializatsiya qilinadi — ikki tomon bir manbadan o'qiydi.
 * Eski izohlar joyida qoldi (ularda snake_case nomlar uchrasa — shu fayldagi camelCase haq).
 */
/**
 * Xodimlar boshqaruvi (`hr`) yo‘nalishining TypeScript tiplari.
 *
 * Manba — backend kontrakti: `backend/src/modules/hr/presentation/api/schemas.py`
 * (ustunlar `docs/hr/SPEC.md` §3, endpointlar §6, nomlar §13).
 *
 * Maydon nomlari backend JSON kalitlariga AYNAN mos — hammasi **snake_case**.
 * ⛔️ Qayta nomlash (camelCase) va alias YO‘Q: shundagina panel bilan backend
 * o‘rtasida kelishmovchilik xavfi yo‘qoladi. `Optional[...]` maydonlar bu
 * yerda `| null` ko‘rinishida.
 *
 * ⚠️ Sanoq turlarining qiymatlari backend `StrEnum` larining qiymatlari bilan
 * bir xil — ya’ni **kichik harfda** (`OPEN` emas, `'open'`).
 */
/** Keyset sahifa — eski `shared/types/api.ts` dagi `Page`, camelCase. */
export interface Page<T> {
  items: T[]
  nextCursor: string | null
  hasMore: boolean
  total?: number
}

// ── Sanoq turlari (`domain/enums.py`, SPEC §13) ───────────────────────────────

/** `TaskStatus` — topshiriqning holati. Oraliq bosqich ataylab yo‘q (SPEC §1). */
export type HrTaskStatus = 'open' | 'done' | 'returned' | 'missed' | 'cancelled'

/** `TaskKind` — belgilanadigan (`check`) yoki raqamli/KPI (`numeric`). */
export type HrTaskKind = 'check' | 'numeric'

/** `TaskSource` — topshiriqni kim yaratgan. */
export type HrTaskSource = 'director' | 'self' | 'template'

/** `Recurrence` — shablonning takroriylik davri. */
export type HrRecurrence = 'daily' | 'weekly' | 'monthly'

/** `TargetType` — shablon kimga tegishli: bitta xodimga yoki lavozimga. */
export type HrTargetType = 'employee' | 'position'

/** `OverduePolicy` — muddat o‘tganda topshiriq yopiladimi (SPEC §5.3). */
export type HrOverduePolicy = 'close' | 'keep_open'

/** `ProofKind` — isbotning turi. */
export type HrProofKind = 'photo' | 'file' | 'link' | 'text'

/**
 * `Branch` — xodim ishlaydigan bo'linma (SHARTNOMA §1.1).
 *
 * ⚠️ Ikkitasi QAT'IY, ro'yxat emas: egasining 02.09.2026 qarori. Xodimda
 * `null` bo'lishi mumkin — «belgilanmagan», u filtrsiz ro'yxatda ko'rinaveradi.
 */
export type HrBranch = 'center' | 'school'

export const HR_BRANCHES: readonly HrBranch[] = ['center', 'school'] as const

/**
 * `AttachmentKind` — RAHBAR topshiriqqa biriktirgan fayl turi.
 *
 * ⛔️ `HrProofKind` bilan chalkashtirilmasin: isbot XODIMniki, biriktirma
 * rahbarniki. Ikkalasi alohida jadvalda va alohida ro'yxatda ko'rsatiladi.
 */
export type HrAttachmentKind = 'voice' | 'file'

export const HR_ATTACHMENT_KINDS: readonly HrAttachmentKind[] = ['voice', 'file'] as const

/**
 * «Panelga kirish» darajasi — xodim formasidagi maydon (SPEC §3.2).
 *
 * ⚠️ LAVOZIM («Director») hech qanday huquq BERMAYDI, u shunchaki yorliq.
 * Panelga kirish faqat shu maydon orqali beriladi va `admin_users` jadvaliga
 * TELEFON RAQAMI bo'yicha yoziladi (backend: `hr/application/dto.py`).
 *
 * Qiymatlar `entities/admin-user` dagi `AdminRole` ning qismi — nomlar
 * `ROLE_LABEL` dan olinadi, ikkinchi lug'at yasalmaydi.
 *
 * ⛔️ `moderator` YO'Q: uning ruxsat to'plami bo'sh, ya'ni «Yo'q» dan farqi
 *    bo'lmasdi (backend `SELECTABLE_PANEL_ROLES` bilan bir xil).
 *
 * ⚠️ `manager` — o'ziga BO'YSUNUVCHILARNI (butun pastki shox) va ularning
 * topshiriqlarini ko'radi. Doira backendda majburlanadi (`HrScope`),
 * frontend qo'riqchi EMAS.
 * ⚠️ 03.09.2026 da doira BO'LINMA'dan bo'ysunish daraxtiga ko'chdi: manager
 * uchun bo'linma endi majburiy emas, rahbarlik esa «Rahbarlari» maydonidan
 * biriktiriladi. Rahbarsiz manager hech kimni ko'rmaydi (o'zidan boshqa).
 */
export type HrPanelRole = 'manager' | 'admin' | 'superadmin'

/** Formadagi tanlagich uchun tartib: kuchsizdan kuchliga. */
export const HR_PANEL_ROLES: readonly HrPanelRole[] = [
  'manager',
  'admin',
  'superadmin',
] as const

/** Filtr va formalar uchun tayyor ro‘yxatlar (tartib UI’da ham shu bo‘ladi). */
export const HR_TASK_STATUSES: readonly HrTaskStatus[] = [
  'open',
  'done',
  'returned',
  'missed',
  'cancelled',
] as const

export const HR_TASK_SOURCES: readonly HrTaskSource[] = ['director', 'self', 'template'] as const

export const HR_TASK_KINDS: readonly HrTaskKind[] = ['check', 'numeric'] as const

export const HR_RECURRENCES: readonly HrRecurrence[] = ['daily', 'weekly', 'monthly'] as const

export const HR_TARGET_TYPES: readonly HrTargetType[] = ['employee', 'position'] as const

export const HR_OVERDUE_POLICIES: readonly HrOverduePolicy[] = ['close', 'keep_open'] as const

/** Hafta kuni: 1 = dushanba … 7 = yakshanba (SPEC §3.3). */
export type HrWeekday = 1 | 2 | 3 | 4 | 5 | 6 | 7

export const HR_WEEKDAYS: readonly HrWeekday[] = [1, 2, 3, 4, 5, 6, 7] as const

// ── Chegaralar (bir marta shu yerda, SPEC §3.5) ───────────────────────────────

/** Bitta topshiriqqa nechta isbot biriktirsa bo‘ladi. */
export const HR_MAX_PROOFS = 5

/** Bitta isbot faylining eng katta hajmi (MB). */
export const HR_MAX_PROOF_MB = 10

/** Hajm chegarasining baytdagi ko‘rinishi — `formatBytes` uchun. */
export const HR_MAX_PROOF_BYTES = HR_MAX_PROOF_MB * 1024 * 1024

/**
 * ⛔️ TAQIQLANGAN MIME turlari — qolgan HAMMASI ruxsat etilgan
 * (backend: `dto.BLOCKED_PROOF_TYPES`).
 *
 * Isbot istalgan turdagi fayl bo'lishi mumkin: hujjat, jadval, arxiv, ovoz,
 * video. Faqat brauzerda IJRO etiladigan turlar rad etiladi — isbot fayllari
 * panel bilan bir xil domendan (`/media/`) beriladi, ya'ni HTML yoki SVG
 * ichidagi skript direktorning sessiyasini o'g'irlashi mumkin bo'lardi.
 */
export const HR_BLOCKED_PROOF_TYPES: readonly string[] = [
  'text/html',
  'application/xhtml+xml',
  'image/svg+xml',
  'text/xml',
  'application/xml',
  'text/javascript',
  'application/javascript',
  'application/x-javascript',
] as const

/**
 * `<input type="file" accept=...>` uchun qiymat.
 *
 * ⚠️ Bo'sh satr — ATAYLAB: `accept` cheklovi qo'yilsa telefon galereyasi
 * ba'zi mijozlarda hujjatlarni umuman ko'rsatmaydi. Turni backend tekshiradi.
 */
export const HR_PROOF_ACCEPT = ''

/** Fayl turi taqiqlanganmi (mijoz tomonda oldindan ogohlantirish uchun). */
export function hrIsBlockedProofType(contentType: string): boolean {
  return HR_BLOCKED_PROOF_TYPES.includes(contentType.split(';')[0]?.trim().toLowerCase() ?? '')
}

// ── Biriktirma (ovozli izoh) chegaralari ──────────────────────────────────────
// Backend manbasi: `application/dto.py` — MAX_ATTACHMENT_BYTES, MAX_VOICE_SECONDS.

/** Bitta biriktirmaning eng katta hajmi (MB). */
export const HR_MAX_ATTACHMENT_MB = 10

export const HR_MAX_ATTACHMENT_BYTES = HR_MAX_ATTACHMENT_MB * 1024 * 1024

/** Ovozli izohning eng uzun davomiyligi (soniya) — 5 daqiqa. */
export const HR_MAX_VOICE_SECONDS = 300

/**
 * ⛔️ Taqiqlangan turlar isbotnikiga BIR XIL (`BLOCKED_ATTACHMENT_TYPES =
 * BLOCKED_PROOF_TYPES`): fayllar bir xil domendan beriladi, ya'ni brauzerda
 * ijro etiladigan tur bo'lsa sessiya o'g'irlanishi mumkin.
 */
export const HR_BLOCKED_ATTACHMENT_TYPES = HR_BLOCKED_PROOF_TYPES

/**
 * Ovoz yuklash maydonining `accept` qiymati.
 *
 * ⚠️ Isbotdagi bo'sh `HR_PROOF_ACCEPT` dan farqli: bu yerda ATAYLAB `audio/*`
 * — rahbar ovozli izoh biriktiradi, galereyadan hujjat tanlash kerak emas.
 */
export const HR_VOICE_ACCEPT = 'audio/*'

// ── Ko'p yozuvli (batch) yaratish chegaralari ─────────────────────────────────
// Backend manbasi: `application/dto.py` — MAX_BATCH_*. Panel shu raqamlar
// bo'yicha «6 × 3 = 18 ta» xulosasini chiqaradi va yuborishdan oldin to'xtatadi.

export const HR_MAX_BATCH_EMPLOYEES = 100
export const HR_MAX_BATCH_ITEMS = 20
export const HR_MAX_BATCH_TOTAL = 500

// ── Eslatma sozlamasining chegaralari (`domain/enums.py`) ─────────────────────

/** Sukutdagi offsetlar: muddatdan 120 / 60 / 30 daqiqa oldin. */
export const HR_DEFAULT_REMINDER_OFFSETS: readonly number[] = [120, 60, 30] as const

export const HR_REMINDER_OFFSET_MIN = 5
/** 24 soat. */
export const HR_REMINDER_OFFSET_MAX = 1440
export const HR_MAX_REMINDER_OFFSETS = 5

/**
 * Jadvallar bir sahifada nechta qator so'raydi.
 *
 * ⚠️ Backend'ning `DEFAULT_LIMIT` i 50 (SPEC §13), lekin panel ATAYLAB 20 ta
 * so'raydi: ekranda ko'z yugurtirib o'qiladigan ro'yxat kerak, uzun varaq
 * emas. Ko'proq kerak bo'lsa «Keyingi» tugmasi bor.
 */
export const HR_DEFAULT_PAGE_SIZE = 20
export const HR_MAX_PAGE_SIZE = 200

// ── Umumiy yordamchi turlar ───────────────────────────────────────────────────

/**
 * Backend `numeric(12,2)` maydoni.
 *
 * ⚠️ pydantic `Decimal` ni JSON’ga ba’zan **matn** qilib chiqaradi
 * (`"147.00"`), shuning uchun hisob-kitobdan oldin `hrAmount()` ga beriladi.
 */
export type HrAmount = number | string

/** `HH:mm` (`time` ustuni) — Toshkent devor soati, mintaqasiz. */
export type HrTimeString = string

/** `yyyy-MM-dd` (`date` ustuni). */
export type HrDateString = string

// ── Lavozim (`hr.positions`, SPEC §3.1) ───────────────────────────────────────

export interface HrPosition {
  id: string
  /** «Marketolog», «Buxgalter» — unikal. */
  name: string
  isActive: boolean
  createdAt: string | null
  updatedAt: string | null
  /** Shu lavozimdagi faol xodimlar soni (backend qo‘shsa keladi). */
  employeesCount?: number
}

/**
 * `POST /hr/positions`.
 *
 * ⚠️ Lavozim yana PANELDAN boshqariladi (02.09.2026): `domain/positions.py`
 * dagi ro'yxat endi qat'iy emas, boshlang'ich seed. Chalkashlik qaytmasin —
 * lavozim faqat yorliq, panelga kirish huquqini `panel_role` beradi.
 */
export interface HrPositionCreatePayload {
  name: string
}

/** `PATCH /hr/positions/{id}` — berilmagan maydon o‘zgarmaydi. */
export interface HrPositionUpdatePayload {
  name?: string
  isActive?: boolean
}

// ── Xodim (`hr.employees`, SPEC §3.2) ─────────────────────────────────────────

/**
 * Xodim yozuvi.
 *
 * ⛔️ O‘chirish yo‘q — ishdan bo‘shatish `is_active = false` (tarix saqlanadi).
 */
export interface HrEmployee {
  id: string
  /** Bizning `users.id` — HR xodimi har doim LMS foydalanuvchisi (docs/modules/hr-tasks.md §2). */
  userId: string
  /** Bizdagi rol: `staff` | `teacher` | `admin` | `superadmin` | `cashier`. */
  role: string
  /** HR doirasi: `employee` — faqat o'zi; `manager` — o'zi + bo'ysunuvchilar shoxi; `director` — butun tashkilot. */
  hrRole: 'employee' | 'manager' | 'director'
  fullName: string
  /** Normal shakl: `+998901234567` (`formatPhone` bilan chiroylanadi). */
  phone: string
  positionId: string | null
  /**
   * Bo'linma: «Markaz» yoki «Maktab»; `null` — belgilanmagan.
   *
   * ⚠️ Ixtiyoriy (`?`) ATAYLAB: maydon 02.09.2026 da qo'shildi, eski javob
   * yoki keshlangan yozuvda umuman bo'lmasligi mumkin — sahifa yiqilmasin.
   */
  branch?: HrBranch | null
  /** Telegram’ning raqamli identifikatori; bog‘lanmagan bo‘lsa `null`. */
  telegramUserId: number | null
  /** Bir martalik deeplink kodi (`/start <invite_code>`). */
  inviteCode: string | null
  linkedAt: string | null
  isActive: boolean
  note: string | null
  createdAt: string | null
  updatedAt: string | null
  /**
   * Ro‘yxatda ko‘rsatish uchun backend qo‘shadigan nom (`join`).
   * Kelmasa — `useHrPositions()` ma’lumotnomasidan olinadi.
   */
  positionName?: string | null
  /** Ochiq (`open`/`returned`) topshiriqlar soni — backend qo‘shsa keladi. */
  openTasksCount?: number
  /**
   * FAOL panel hisobining roli; `null` — xodim panelga kirmaydi.
   *
   * ⚠️ Nofaol hisob ham `null` bo‘lib keladi: formadagi «Yo‘q» aynan shu
   * holatni bildiradi (hisob o‘chirilmaydi, faolsizlantiriladi).
   */
  panelRole?: HrPanelRole | null

  /**
   * BEVOSITA rahbarlarining `id` lari (`hr.employee_managers`).
   *
   * ⚠️ Bir xodimning bir nechta rahbari bo‘lishi mumkin — shuning uchun
   * massiv, bitta `manager_id` ustuni emas (REJA-2026-09-03 §1).
   * ⚠️ Ixtiyoriy: maydon 03.09.2026 da qo‘shildi, eski javobda yo‘q.
   * ⛔️ Rahbar biriktirish HUQUQ BERMAYDI: panelga kirish hamon `panel_role`
   *    dan. Rahbarlik faqat KO‘RISH DOIRASINI belgilaydi (`HrScope`).
   */
  managerIds?: string[]
  /** Ro‘yxatda ko‘rsatish uchun backend qo‘shadigan nomlar (`manager_ids` tartibida). */
  managerNames?: string[]
  /**
   * BEVOSITA bo‘ysunuvchilar soni (butun shox emas).
   *
   * ⚠️ Tranzitiv shox EMAS: daraxtda qator ochilganda nechta bola chiqishini
   * bildiradi. Doira esa backendda butun shox bo‘yicha hisoblanadi.
   */
  subordinateCount?: number
}

// ── Bo‘ysunish daraxti (`GET /hr/employees/tree`, REJA-2026-09-03 §4) ─────────

/**
 * Daraxtning bitta tuguni — xodim yozuvining o‘zi + bo‘ysunuvchilari.
 *
 * ⚠️ `children` ixtiyoriy: barg tugunda backend maydonni umuman bermasligi
 * mumkin. Ro‘yxatni `node.children ?? []` orqali oling.
 * ⛔️ Bir xodim bir nechta rahbarga bo‘ysunsa daraxtda BIR NECHTA joyda
 *    ko‘rinadi (nusxa) — bu daraxt, graf emas. `id` shu sababli daraxt
 *    bo‘ylab UNIKAL EMAS: React `key` uchun yo‘lni ham qo‘shing.
 */
export interface HrEmployeeNode extends HrEmployee {
  children?: HrEmployeeNode[]
}

/** `GET /hr/employees/tree` javobi. */
export interface HrEmployeeTree {
  /** Rahbari bor daraxtlarning ildizlari (o‘zi hech kimga bo‘ysunmaydiganlar). */
  roots: HrEmployeeNode[]
  /**
   * Rahbari biriktirilmaganlar — alohida guruh.
   *
   * ⚠️ Ular «ildiz» EMAS: ildiz — bo‘ysunuvchisi bor tepa rahbar, bu yerdagilar
   * esa daraxtga umuman ulanmagan xodimlar. Panel ularni «Biriktirilmagan»
   * sarlavhasi ostida ko‘rsatadi (REJA §5).
   */
  unassigned: HrEmployeeNode[]
}

/**
 * `GET /hr/employees/tree` filtrlari — xodimlar ro‘yxatiniki, sahifalashsiz.
 *
 * ⚠️ `limit`/`cursor` YO‘Q: daraxt kesilib qolsa bola ota-onasisiz qolardi,
 * shuning uchun endpoint butun shoxni birdan qaytaradi.
 */
export type HrEmployeeTreeFilters = Omit<HrEmployeeFilters, 'limit' | 'cursor' | 'withTotal'>

/**
 * `PUT /hr/employees/{id}/managers` tanasi.
 *
 * ⚠️ Ro‘yxat TO‘LIQ ALMASHTIRILADI (qo‘shish/olib tashlash emas): bo‘sh massiv
 * — «rahbari yo‘q», ya‘ni xodim «Biriktirilmagan» guruhiga tushadi.
 * ⛔️ Sikl (A → B → A) 409 `hr_manager_cycle` bilan rad etiladi.
 */
export interface HrManagersPayload {
  managerIds: string[]
}

/** `POST /hr/employees`. */
export interface HrEmployeeCreatePayload {
  /**
   * Make an existing LMS user an HR employee (docs/modules/hr-tasks.md §2).
   * Omitted — a new `staff` user is created from `fullName` + `phone` and the one-time
   * password comes back in `HrEmployeeCreateResult.credentials`.
   * (Added 2026-09-27 — admin panel port.)
   */
  userId?: string | null
  /** HR scope — directors only (hr-tasks.md §3). Omitted means `employee`. (Added 2026-09-27.) */
  hrRole?: HrEmployee['hrRole'] | null
  fullName: string
  phone: string
  positionId?: string | null
  note?: string | null
  /** `null` — panelga kirmaydi. */
  panelRole?: HrPanelRole | null
  /**
   * `null` — bo'linma belgilanmaydi.
   *
   * ⚠️ Bo'linma endi FAQAT YORLIQ (filtr va hisobot kesimi) — kim nimani
   * ko'rishiga ta'sir qilmaydi. Doira bo'ysunish daraxti bo'yicha.
   */
  branch?: HrBranch | null
  /**
   * Bevosita rahbarlari; berilmasa — rahbarsiz («Biriktirilmagan»).
   *
   * ⚠️ Ixtiyoriy: maydon 03.09.2026 da qo‘shildi. Backend hali eski bo‘lsa uni
   * shunchaki e‘tiborsiz qoldiradi, forma esa buzilmaydi.
   */
  managerIds?: string[]
}

/**
 * `PATCH /hr/employees/{id}` — berilmagan maydon o‘zgarmaydi.
 *
 * ⚠️ `panel_role` BERILMASA panel huquqiga tegilmaydi; `null` yuborilsa —
 * hisob faolsizlantiriladi.
 */
export interface HrEmployeeUpdatePayload {
  /** HR scope — directors only (hr-tasks.md §3). Omitted means unchanged. (Added 2026-09-27.) */
  hrRole?: HrEmployee['hrRole']
  fullName?: string
  phone?: string
  positionId?: string | null
  note?: string | null
  isActive?: boolean
  panelRole?: HrPanelRole | null
  /** `null` yuborilsa bo'linma TOZALANADI (backend: `clear_branch`). */
  branch?: HrBranch | null
  /**
   * Rahbarlar ro‘yxati TO‘LIQ almashtiriladi; bo‘sh massiv — rahbarsiz qoladi.
   *
   * ⚠️ BERILMASA rahbarlarga TEGILMAYDI (`panel_role` naqshi). Alohida
   * `PUT /hr/employees/{id}/managers` ham bor — forma tashqarisidan
   * (daraxtda sudrab ko‘chirish) o‘zgartirish uchun.
   */
  managerIds?: string[]
}

/**
 * `POST /hr/employees` response — the employee row plus, when a new `staff` user was
 * created, its one-time login data. No `credentials` when an existing user was picked.
 * (Added 2026-09-27 — hr-tasks.md §2.)
 */
export interface HrEmployeeCreateResult extends HrEmployee {
  credentials?: { login: string; password: string } | null
}

/**
 * `GET /api/admin/hr/users?q=` — candidates for "pick an existing user" (staff, teacher,
 * admin, superadmin, cashier). A filled `employeeId` means the user already is an HR
 * employee. (Added 2026-09-27 — hr-tasks.md §2.)
 */
export interface HrUserCandidate {
  id: string
  fullName: string
  phone: string | null
  role: string
  employeeId?: string | null
}

/**
 * `GET /api/admin/hr/access` — what the panel may show this account (director?, may manage?,
 * scope `all` | `subtree`, own HR row). (Added 2026-09-27 — mirrors the backend `HrAccessDto`.)
 */
export interface HrAccess {
  isDirector: boolean
  canManage: boolean
  scope: string
  employeeId: string | null
  hrRole: HrEmployee['hrRole'] | null
}

/** `POST /hr/employees/{id}/invite` javobi — taklif havolasi yangilanadi. */
export interface HrInvite {
  employeeId: string
  inviteCode: string
  /** `https://t.me/<bot>?start=<invite_code>`; bot ulanmagan bo‘lsa `null`. */
  inviteUrl: string | null
}

// ── Takroriy topshiriq shabloni (`hr.task_templates`, SPEC §3.3) ──────────────

/**
 * Shablon — bu **qoida**, topshiriqning o‘zi emas: undan har davr uchun
 * `HrTask` nusxasi yaratiladi (SPEC §5.2).
 */
export interface HrTaskTemplate {
  id: string
  title: string
  description: string | null

  targetType: HrTargetType
  targetEmployeeId: string | null
  targetPositionId: string | null

  recurrence: HrRecurrence
  /** `daily`/`weekly` uchun: 1 = dushanba … 7 = yakshanba. */
  weekdays: number[]
  /** `monthly` uchun: `[14]` yoki `[14, 28]` (1..31). */
  daysOfMonth: number[]
  /** `HH:mm` — shu kunning muddat soati; `null` bo‘lsa kun oxiri. */
  dueTime: HrTimeString | null

  kind: HrTaskKind
  /** `numeric` uchun majburiy (masalan 200). */
  targetAmount: HrAmount | null
  /** «lead», «dona», «so‘m». */
  unit: string | null
  proofRequired: boolean
  /** ⛔️ Faqat JAVOBDA: tanlov olib tashlangan, yangilarida doim `keep_open`. */
  overduePolicy: HrOverduePolicy

  isActive: boolean
  createdBy: string | null
  createdAt: string | null
  updatedAt: string | null

  /** Jadvalda ko‘rsatish uchun backend qo‘shadigan nomlar (`join`). */
  targetEmployeeName?: string | null
  targetPositionName?: string | null
}

/** `POST /hr/templates`. Invariantlar SPEC §3.3 da — backend ham tekshiradi. */
export interface HrTemplateCreatePayload {
  title: string
  description?: string | null
  targetType: HrTargetType
  targetEmployeeId?: string | null
  targetPositionId?: string | null
  recurrence: HrRecurrence
  weekdays?: number[]
  daysOfMonth?: number[]
  dueTime?: HrTimeString | null
  kind: HrTaskKind
  targetAmount?: HrAmount | null
  unit?: string | null
  proofRequired?: boolean
}

/** `PATCH /hr/templates/{id}` — berilmagan maydon o‘zgarmaydi. */
export type HrTemplateUpdatePayload = Partial<HrTemplateCreatePayload> & {
  isActive?: boolean
}

// ── Topshiriq (`hr.tasks`, SPEC §3.4) ─────────────────────────────────────────

/**
 * Topshiriq nusxasi.
 *
 * ⚠️ Shablon maydonlari yaratilganda **ko‘chirib olinadi** (snapshot): shablon
 * keyin o‘zgarsa ham tarixdagi topshiriq o‘zgarmaydi.
 */
export interface HrTask {
  id: string
  employeeId: string
  /** `null` — bir martalik topshiriq (shablondan emas). */
  templateId: string | null
  source: HrTaskSource

  title: string
  description: string | null

  kind: HrTaskKind
  targetAmount: HrAmount | null
  /** `numeric` uchun jami (`hr.task_progress` yig‘indisi). */
  currentAmount: HrAmount
  unit: string | null

  proofRequired: boolean
  /** ⛔️ Faqat JAVOBDA — izohi `HrTaskTemplate` da. */
  overduePolicy: HrOverduePolicy

  /** Davr boshi: kunlik — o‘sha kun, oylik — oy boshi. */
  periodStart: HrDateString
  dueAt: string
  status: HrTaskStatus
  /** `done` bo‘lganda `completed_at > due_at` bo‘lsa `true`. */
  isLate: boolean

  employeeComment: string | null
  directorComment: string | null
  completedAt: string | null
  returnedAt: string | null

  /**
   * Xodim topshiriqni «ko'rdim» deb tasdiqlagan vaqt.
   *
   * ⛔️ Bu HOLAT EMAS: `TaskStatus` ga yangi qiymat qo'shilmagan (SPEC §1).
   * Tasdiq — to'siq emas, xodim tasdiqlamasdan ham bajara oladi (bajarganda
   * backend `accepted_at` ni o'zi to'ldiradi).
   *
   * ⚠️ Ixtiyoriy: maydon 02.09.2026 da qo'shildi.
   */
  acceptedAt?: string | null
  /**
   * Tasdiq kutilyaptimi: FAQAT qo'lda berilgan (`source='director'`), hali
   * tasdiqlanmagan va hali ochiq topshiriqda `true` (backend hisoblaydi).
   */
  needsAcceptance?: boolean
  /**
   * Ro'yxatda ovoz borligi bilinsin — birinchi `voice` biriktirmaning manzili.
   * To'liq manzil uchun `hrAttachmentSrc()` ishlatiladi.
   */
  voiceUrl?: string | null

  createdBy: string | null
  createdAt: string | null
  updatedAt: string | null

  /** Jadvalda ko‘rsatish uchun backend qo‘shadigan nomlar (`join`). */
  employeeName?: string | null
  positionName?: string | null
  /** Ro‘yxatda 📎 belgisini chiqarish uchun. */
  proofCount?: number
}

/** Isbot (`hr.task_proofs`, SPEC §3.5). */
export interface HrTaskProof {
  id: string
  taskId: string
  kind: HrProofKind
  /** `hr/proofs/<yyyy>/<mm>/<uuid>.<ext>` — `hrProofUrl()` to‘liq manzil beradi. */
  filePath: string | null
  originalName: string | null
  contentType: string | null
  sizeBytes: number | null
  /** `kind='link'` uchun tashqi havola (yoki backend tayyorlagan `/media/...`). */
  url: string | null
  /** `kind='text'` uchun matn. */
  text: string | null
  createdAt: string | null
}

/**
 * Rahbar biriktirgan fayl (`hr.task_attachments`, odatda ovozli izoh).
 *
 * ⛔️ `HrTaskProof` bilan bir joyda ko'rsatilmaydi: isbot xodimniki (u nima
 * qilganini isbotlaydi), biriktirma rahbarniki (u nima so'raganini tushuntiradi).
 *
 * ⚠️ `POST /hr/attachments` javobida `task_id` bo'lmaydi — bu DRAFT: fayl
 * yuklandi, topshiriq esa hali yaratilmagan. Draft `id` si keyin
 * `HrTaskBatchItem.attachment_id` ga beriladi.
 */
export interface HrTaskAttachment {
  id: string
  kind: HrAttachmentKind
  /** `/media/hr/attachments/...` — `hrAttachmentSrc()` to'liq manzil beradi. */
  url: string
  contentType: string
  sizeBytes: number
  /** Ovozning davomiyligi; server o'lchay olmagan bo'lsa `null`. */
  durationSeconds: number | null
  /** `null` — draft (hali topshiriqqa bog'lanmagan). */
  taskId?: string | null
  originalName?: string | null
  createdAt?: string | null
}

/** KPI qadami (`hr.task_progress`, SPEC §3.6). */
export interface HrTaskProgress {
  id: string
  taskId: string
  /** Musbat yoki manfiy (tuzatish uchun). */
  amount: HrAmount
  note: string | null
  createdAt: string | null
}

/** `GET /hr/tasks/{id}` — isbotlar va KPI qadamlari bilan. */
export interface HrTaskDetail extends HrTask {
  proofs: HrTaskProof[]
  progress: HrTaskProgress[]
  /**
   * Rahbar biriktirgan fayllar (ovozli izoh).
   *
   * ⚠️ Ixtiyoriy: eski javobda maydon yo'q. Ro'yxatni `hrTaskAttachments()`
   * orqali oling — u hech qachon `undefined` qaytarmaydi.
   */
  attachments?: HrTaskAttachment[]
}

/** `POST /hr/tasks` — direktor bir martalik topshiriq beradi. */
export interface HrTaskCreatePayload {
  employeeId: string
  title: string
  description?: string | null
  /** UTC ISO (`toIsoFromTashkentInput` orqali tayyorlanadi). */
  dueAt: string
  kind?: HrTaskKind
  targetAmount?: HrAmount | null
  unit?: string | null
  proofRequired?: boolean
}

/** `PATCH /hr/tasks/{id}` — faqat sarlavha/tavsif/muddat (SPEC §6.1). */
export interface HrTaskUpdatePayload {
  title?: string
  description?: string | null
  dueAt?: string
}

/** `POST /hr/tasks/{id}/return` — qaytarish izohi xodimga darhol boradi. */
export interface HrTaskReturnPayload {
  comment: string
}

/** `POST /hr/tasks/{id}/cancel` — izoh ixtiyoriy. */
export interface HrTaskCancelPayload {
  comment?: string | null
}

// ── Ko'p yozuvli yaratish (`/hr/tasks/batch`, `/hr/templates/batch`) ──────────

/**
 * Batch'dagi BITTA topshiriq qatori (`CreateTaskItem`).
 *
 * ⚠️ `employee_id` YO'Q — kim olishini `employee_ids` belgilaydi: bitta qator
 * har bir xodimga nusxalanadi.
 * ⛔️ `proof_required` ATAYLAB yo'q: shartnomadagi `CreateTaskItem` da u yo'q,
 * ortiqcha maydon yuborilsa so'rov rad etilishi mumkin.
 */
export interface HrTaskBatchItem {
  title: string
  /** UTC ISO (`toIsoFromTashkentInput` orqali tayyorlanadi). */
  dueAt: string
  description?: string | null
  kind?: HrTaskKind
  targetAmount?: HrAmount | null
  unit?: string | null
  /**
   * Oldin `POST /hr/attachments` bilan yuklangan DRAFT biriktirmaning `id` si.
   *
   * ⚠️ Fayl BITTA marta yuklanadi va N ta topshiriqqa ulashiladi — 30 xodimga
   * bitta ovozli izoh yuborilsa ham server bitta fayl saqlaydi.
   */
  attachmentId?: string | null
}

/** `POST /hr/tasks/batch` tanasi (`CreateTaskBatchInput`). */
export interface HrTaskBatchPayload {
  employeeIds: string[]
  items: HrTaskBatchItem[]
}

/**
 * Batch'dagi BITTA shablon qatori (`CreateTemplateItem`).
 *
 * ⚠️ Nishon maydonlari (`target_*`) yo'q: T4 bo'yicha har xodimga ALOHIDA
 * shablon yaratiladi, ya'ni nishon `employee_ids` dan kelib chiqadi.
 */
export type HrTemplateBatchItem = Omit<
  HrTemplateCreatePayload,
  // camelCase (2026-09-27): the mechanical port left snake_case here, so Omit removed nothing.
  'targetType' | 'targetEmployeeId' | 'targetPositionId'
> & {
  attachmentId?: string | null
}

/** `POST /hr/templates/batch` tanasi (`CreateTemplateBatchInput`). */
export interface HrTemplateBatchPayload {
  employeeIds: string[]
  items: HrTemplateBatchItem[]
}

/** Yaratilmay o'tkazib yuborilgan xodim (`SkippedEmployeeDTO`). */
export interface HrBatchSkipped {
  employeeId: string
  /** Backend kodi, masalan `inactive` — matn `hrBatchSkipReason()` da. */
  reason: string
}

/**
 * `BatchResultDTO` — qisman muvaffaqiyat javobi.
 *
 * ⚠️ Nofaol xodim butun so'rovni yiqitmaydi: u `skipped` ga tushadi, qolgani
 * yaratiladi. Shuning uchun panel «N ta yaratildi, M ta o'tkazib yuborildi»
 * deb ko'rsatishi SHART.
 */
export interface HrBatchResult {
  created: number
  skipped: HrBatchSkipped[]
  /** `/hr/tasks/batch` yaratgan topshiriqlar. */
  taskIds?: string[]
  /** `/hr/templates/batch` yaratgan shablonlar. */
  templateIds?: string[]
}

// ── Sozlamalar (`hr.settings`, SPEC §3.7) ─────────────────────────────────────

export interface HrSettings {
  timezone: string
  /** Kechki yig‘ma xabar vaqti, `HH:mm`. */
  digestTime: HrTimeString
  /** Yig‘ma xabar yuboriladigan hafta kunlari (1..7). */
  digestWeekdays: number[]
  /** Direktor(lar)ning Telegram chat ID’lari. */
  morningListEnabled: boolean
  /** Ertalabki ro'yxat vaqti — sukut 07:30 (02.09.2026 da 09:00 dan ko'chirildi). */
  morningTime: HrTimeString
  generationTime: HrTimeString
  closeOverdueTime: HrTimeString

  /**
   * Muddatdan oldin eslatma yuborilsinmi.
   *
   * ⚠️ Uchala eslatma maydoni ham ixtiyoriy: ular 02.09.2026 da qo'shildi va
   * eski javobda bo'lmasligi mumkin. Formada `?? HR_DEFAULT_...` bilan
   * to'ldiriladi, PUT esa har doim to'liq yuboriladi.
   */
  reminderEnabled?: boolean
  /** Muddatdan necha daqiqa oldin — sukut `[120, 60, 30]`, kattadan kichikka. */
  reminderOffsets?: number[]
  /**
   * Muddati aniq belgilanmagan (mahalliy vaqt 23:59) topshiriqlarga ham
   * eslatma ketsinmi.
   *
   * ⚠️ Sukut `false`: aks holda har kecha 21:59/22:59/23:29 da uch xabar
   * ketardi va xodim bildirishnomalarni umuman o'chirib qo'yardi.
   */
  remindEndOfDay?: boolean

  updatedAt: string | null
}

/** `PUT /hr/settings` — to‘liq almashtiriladi (`updated_at` serverniki). */
export type HrSettingsPayload = Omit<HrSettings, 'updatedAt'>

// ── Hisobotlar (SPEC §6.1 `/hr/reports/...`) ──────────────────────────────────

/** Hisobot davri — ikkala chegara ham `yyyy-MM-dd`, ikkalasi ham kiradi. */
export interface HrReportRange {
  dateFrom: HrDateString
  dateTo: HrDateString
}

/**
 * Hisobot so'rovi: davr + ixtiyoriy bo'linma (`GET /hr/reports/*?branch=`).
 *
 * ⚠️ `HrReportRange` shu turga o'z-o'zidan mos keladi — mavjud chaqiruvlar
 * (`{date_from, date_to}`) o'zgarishsiz ishlayveradi.
 */
export interface HrReportFilters extends HrReportRange {
  branch?: HrBranch | null
}

/** `GET /hr/reports/summary` — davr bo‘yicha umumiy raqamlar. */
export interface HrSummaryReport {
  dateFrom: HrDateString
  dateTo: HrDateString
  totalTasks: number
  doneCount: number
  missedCount: number
  openCount: number
  returnedCount: number
  cancelledCount: number
  /** Kechikib bajarilganlar (`is_late`). */
  lateCount: number
  /** Muddati o‘tgan, lekin hali ochiq (`keep_open` siyosati). */
  overdueCount: number
  /** Xodim o‘ziga yaratganlari (`source='self'`). */
  selfCreatedCount: number
  employeesCount: number
  /** 0..100 — `formatPercent` shu ko‘rinishni kutadi. */
  donePercent: number
  /**
   * Xodim hali «ko'rdim» demagan topshiriqlar soni (`accepted_at IS NULL`,
   * `source='director'`, ochiq). Ixtiyoriy — eski javobda maydon yo'q.
   */
  unacceptedCount?: number
}

/** `GET /hr/reports/employees` jadvalining bitta qatori. */
export interface HrEmployeeReportRow {
  employeeId: string
  fullName: string
  positionName: string | null
  totalTasks: number
  doneCount: number
  missedCount: number
  openCount: number
  returnedCount: number
  lateCount: number
  /** 0..100. */
  donePercent: number
  /** KPI (`numeric`) topshiriqlarining yig‘indisi — bo‘lmasa `null`. */
  kpiTarget: HrAmount | null
  kpiCurrent: HrAmount | null
  kpiPercent: number | null
}

/** `GET /hr/reports/employees` javobi. */
export interface HrEmployeeReport {
  dateFrom: HrDateString
  dateTo: HrDateString
  items: HrEmployeeReportRow[]
}

// ── Filtrlar ──────────────────────────────────────────────────────────────────

/** `GET /hr/positions` — ma’lumotnoma ro‘yxati (odatda filtrsiz chaqiriladi). */
export interface HrPositionFilters {
  q?: string
  isActive?: boolean | null
  limit?: number
  cursor?: string | null
}

/** `GET /hr/employees` — SPEC §6.1: `?q=&position_id=&is_active=&linked=&branch=`. */
export interface HrEmployeeFilters {
  q?: string
  positionId?: string | null
  isActive?: boolean | null
  /** `true` — Telegram bog‘langanlar, `false` — hali bog‘lanmaganlar. */
  linked?: boolean | null
  /**
   * Bo'linma bo'yicha filtr.
   *
   * ⚠️ Bo'linmasi belgilanmagan xodimlar HECH QAYSI qiymatga tushmaydi —
   * ularni ko'rish uchun filtr bo'sh qoldiriladi.
   * ⛔️ Bu qo'riqchi EMAS: manager doirasi backendda majburlanadi.
   */
  branch?: HrBranch | null
  limit?: number
  cursor?: string | null
  withTotal?: boolean
}

/** `GET /hr/templates`. */
export interface HrTemplateFilters {
  q?: string
  isActive?: boolean | null
  targetType?: HrTargetType | null
  targetEmployeeId?: string | null
  targetPositionId?: string | null
  recurrence?: HrRecurrence | null
  /** Nishon xodimning bo'linmasi bo'yicha filtr. */
  branch?: HrBranch | null
  limit?: number
  cursor?: string | null
  withTotal?: boolean
}

/**
 * `GET /hr/tasks` — SPEC §6.1 filtrlari.
 *
 * ⚠️ `status` va `source` MASSIV: `?status=open&status=returned` ko‘rinishida
 * yuboriladi (`REPEAT_ARRAY_PARAMS`). `?page=` yo‘q — keyset (SPEC §12.5).
 */
export interface HrTaskFilters {
  /** `yyyy-MM-dd` — davr boshi (`period_start` bo‘yicha). */
  dateFrom?: HrDateString | null
  dateTo?: HrDateString | null
  employeeId?: string | null
  positionId?: string | null
  status?: HrTaskStatus[]
  source?: HrTaskSource[]
  /** Faqat muddati o‘tganlar (hisoblanadigan belgi, SPEC §5.3). */
  overdue?: boolean | null
  /** Xodimning bo'linmasi bo'yicha filtr. */
  branch?: HrBranch | null
  /**
   * Tasdiq holati: `false` — xodim hali «ko'rdim» demaganlari, `true` —
   * tasdiqlanganlari. `null`/berilmasa — hammasi.
   */
  accepted?: boolean | null
  q?: string
  /** `-due_at`, `due_at`, `-created_at` — oq ro‘yxat backendda. */
  sort?: string
  /**
   * `staff` — everyone in scope except the caller; `given` — only what the caller gave.
   * Omitted — everything in scope (the caller's own tasks included).
   */
  view?: HrTaskView | null
  limit?: number
  cursor?: string | null
  withTotal?: boolean
}

export type HrTaskView = 'staff' | 'given'

/**
 * `GET /hr/me/tasks` filtrlari — `HrTaskFilters` ning o‘zi, «kim» maydonlarisiz.
 *
 * ⚠️ `employee_id`/`position_id`/`branch` ATAYLAB yo‘q: endpoint HAR DOIM
 * so‘rovchining O‘Z topshiriqlarini beradi, boshqa xodimni so‘rab bo‘lmaydi.
 */
export type HrMyTaskFilters = Omit<HrTaskFilters, 'employeeId' | 'positionId' | 'branch'>

/**
 * `POST /hr/me/tasks/{id}/complete` tanasi — butunlay ixtiyoriy.
 *
 * ⚠️ Mini App’dagi `CompleteTaskInput` bilan bir xil shakl (`comment`,
 * `proof_ids`): bitta amal ikki joyda ikki xil so‘ralmasin.
 * ⚠️ `proof_required=true` bo‘lsa isbot BAZADAN sanaladi — bu yerdagi
 * `proof_ids` ga ishonilmaydi. Panelda isbot yuklash yo‘li hozircha YO‘Q
 * (Mini App’niki `initData` bilan imzolanadi), shuning uchun bunday
 * topshiriq panelda 409 (`proof_required`) berishi mumkin — sahifa xatoni
 * «Isbotni Mini App orqali yuklang» deb ko‘rsatishi kerak.
 */
export interface HrMyTaskCompletePayload {
  comment?: string | null
  proofIds?: string[]
}

// ── Ro‘yxat javoblari (keyset, `Page<T>`) ─────────────────────────────────────

export type HrPositionPage = Page<HrPosition>
export type HrEmployeePage = Page<HrEmployee>
export type HrTaskTemplatePage = Page<HrTaskTemplate>
export type HrTaskPage = Page<HrTask>


/* ======================================================================
 * Xodim (Mini App va "Mening topshiriqlarim") — eski `hrapp/types.ts` dan
 * mexanik ko'chirilgan. Endpointlar: `/api/hr/me/...` (hr-tasks.md §6.2).
 * ====================================================================== */

/**
 * Mini App API shartnomasi (SPEC §6.2 + §12.5).
 *
 * Yo'llar: `/api/v1/hr/app/...` — `APIRouter(prefix="/hr/app", tags=["hr-app"])`.
 * JSON butunlay **snake_case** (⛔️ camelCase yo'q).
 *
 * ⚠️ Enum qiymatlari: SPEC §13 «barchasi `StrEnum`, qiymatlar snake_case» —
 * ya'ni JSON'da `"open"`, `"numeric"`, `"keep_open"` ko'rinishida keladi,
 * a'zo nomlari (`OPEN`, `NUMERIC`) emas.
 *
 * ⚠️ `numeric(12,2)` ustunlar: pydantic v2 `Decimal` ni JSON rejimida SATR
 * qilib beradi (`"147.00"`). Shuning uchun `Decimalish` — satr ham, son ham
 * qabul qilinadi; o'qish `lib/format.ts` dagi `toNumber()` orqali.
 */

export type TaskKind = 'check' | 'numeric'
export type TaskStatus = 'open' | 'done' | 'returned' | 'missed' | 'cancelled'
export type TaskSource = 'director' | 'self' | 'template'
export type ProofKind = 'photo' | 'file' | 'link' | 'text'

/**
 * Rahbar topshiriqqa biriktirgan fayl turi (02.09.2026 shartnomasi §1.1).
 *
 * ⚠️ `ProofKind` EMAS: isbot — XODIMniki (bajarganini isbotlaydi),
 * biriktirma esa RAHBARniki (nima qilish kerakligini tushuntiradi). Bazada
 * ham ular alohida jadvalda (`task_proofs` / `task_attachments`).
 */
export type AttachmentKind = 'voice' | 'file'

/** Pul/miqdor maydonlari — satr yoki son, ikkalasi ham qabul qilinadi. */
export type Decimalish = string | number | null

/** `GET /hr/app/me` */
export interface Me {
  employeeId: string
  fullName: string
  /** Lavozim biriktirilmagan bo'lishi mumkin. */
  positionName: string | null
  /** Sozlamalardagi mintaqa (`Asia/Tashkent`). */
  timezone: string
  /** Server bo'yicha bugungi MAHALLIY sana, `YYYY-MM-DD`. */
  today: string
}

/** Bitta isbot (fayl yoki havola). */
export interface Proof {
  id: string
  kind: ProofKind
  /** `/media/hr/proofs/...` — `kind=text` bo'lsa `null`. */
  url: string | null
  originalName: string | null
}

/**
 * Rahbar biriktirgan fayl (hozircha — ovozli izoh).
 *
 * ⚠️ `url` doim to'ldirilgan (`/media/hr/attachments/...`): isbotdan farqli
 * o'laroq biriktirmaning «matn» ko'rinishi yo'q, ya'ni fayl har doim bor.
 */
export interface TaskAttachment {
  id: string
  kind: AttachmentKind
  url: string
  contentType: string
  sizeBytes: number
  /** Ovoz uzunligi soniyada. Server `ffprobe` ni topa olmasa — `null`. */
  durationSeconds: number | null
}

/** `GET /hr/app/tasks` ro'yxatidagi bitta topshiriq. */
export interface Task {
  id: string
  title: string
  description: string | null
  kind: TaskKind
  status: TaskStatus
  source: TaskSource
  /** ISO-8601, UTC (`2026-09-01T13:00:00Z`). Muddat qo'yilmagan bo'lsa `null`. */
  dueAt: string | null
  /** §5.3 — hisoblanadigan belgi (ustun EMAS): `due_at < hozir` va ochiq. */
  isOverdue: boolean
  /** `DONE` bo'lganda `completed_at > due_at` edimi. */
  isLate: boolean
  /**
   * @deprecated 01.09.2026 dan qoida o'zgardi: isbot BARCHA topshiriqlar
   * uchun majburiy va backendda bu maydon doim `true`. Interfeys unga
   * QARAMAYDI (aks holda eski yozuvlarda `false` bo'lsa xodim isbotsiz
   * yopib yuborardi). Shartnomada qolgani uchun tipda saqlanadi.
   */
  proofRequired?: boolean
  /** `kind=numeric` uchun maqsad; `check` da `null`. */
  targetAmount: Decimalish
  /** `kind=numeric` uchun jami; `check` da `null`. */
  currentAmount: Decimalish
  unit: string | null
  employeeComment: string | null
  /** Direktor qaytarganda yozgan izohi (`status=returned`). */
  directorComment: string | null
  completedAt: string | null
  proofs: Proof[]
  /**
   * Xodim topshiriqni «ko'rdim» deb tasdiqlagan payt (ISO-8601, UTC).
   *
   * ⚠️ Bu HOLAT emas: `TaskStatus` ga yangi qiymat qo'shilmagan (REJA 7-bosqich).
   * Bajarilganda backend uni avtomatik to'ldiradi — bajarilgan topshiriq
   * «tasdiqlanmagan» bo'lib qolmasin.
   */
  acceptedAt?: string | null
  /**
   * Hozir tasdiq kutilyaptimi — qarorni SERVER chiqaradi (`source=director`,
   * hali tasdiqlanmagan, hali ochiq).
   *
   * ⚠️ Ixtiyoriy: backend hali chiqarmagan bo'lsa `undefined` keladi, shuning
   * uchun tekshiruv HAR DOIM `=== true` (`undefined` — «tugma yo'q»).
   */
  needsAcceptance?: boolean
  /** Rahbar biriktirgan fayllar. Backend bermasa — `undefined`. */
  attachments?: TaskAttachment[]
  /**
   * Ro'yxatda ovoz borligi darrov bilinsin uchun qulaylik maydoni.
   *
   * ⚠️ `attachments` bilan IKKALASI ham bir xil faylni ko'rsatadi; ijrochi
   * avval `attachments` ni oladi (u yerda davomiylik bor), bo'lmasa shu
   * maydonga tushadi (`lib/attachments.ts` — `pickVoice()`).
   */
  voiceUrl?: string | null
}

/** `GET /hr/app/tasks?date=YYYY-MM-DD` */
export interface TaskList {
  /** Server tasdiqlagan sana (so'ralgani bilan bir xil bo'lishi kerak). */
  date: string
  items: Task[]
}

/** `POST /hr/app/proofs` javobi. */
export interface UploadedProof {
  id: string
  url: string | null
}

/** `GET /hr/app/summary` dagi bitta KPI satri. */
export interface SummaryKpi {
  taskId: string | null
  title: string
  currentAmount: Decimalish
  targetAmount: Decimalish
  unit: string | null
}

/** `GET /hr/app/summary` — joriy oy bo'yicha o'z natijasi. */
export interface Summary {
  /** `YYYY-MM-DD` — oy boshi va oxiri (mahalliy sana). */
  periodStart: string
  periodEnd: string
  /** Davrdagi barcha topshiriqlar (bekor qilinganlarsiz). */
  total: number
  done: number
  missed: number
  open: number
  /** Kechikib bajarilganlar soni. */
  late: number
  /**
   * Bajarilish foizi 0..100. `null` bo'lsa mijoz `done/total` dan hisoblaydi —
   * shuning uchun backend uni bermasa ham ekran buzilmaydi.
   */
  percent: number | null
  kpi: SummaryKpi[]
}

/** `POST /hr/app/tasks` so'rovi. */
export interface CreateTaskInput {
  title: string
  description: string | null
  /** ISO-8601, mintaqa siljishi bilan: `2026-09-02T23:59:00+05:00`. */
  dueAt: string
}

/** `POST /hr/app/tasks/{id}/complete` so'rovi. */
export interface CompleteTaskInput {
  comment: string | null
  proofIds: string[]
}

/** `POST /hr/app/tasks/{id}/progress` so'rovi. */
export interface AddProgressInput {
  /** ⚠️ SATR: `Decimal` aniqligi float orqali yo'qolmasin (`"12.50"`). */
  amount: string
  note: string | null
}
