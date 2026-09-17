// Tizimdagi barcha asosiy tiplar

/**
 * Tizim rollari.
 * - `superadmin` — tizim egasi: admin'ning hamma huquqlari + qulflangan amallarni (masalan,
 *   o'quv yili boshlangach guruhlashni) istalgan vaqtda o'zgartira oladi.
 * - `admin` — oddiy administrator. Qulflangan ma'lumotlarni o'zgartira olmaydi.
 */
export type Role =
  | 'superadmin'
  | 'admin'
  | 'teacher'
  | 'student'
  | 'parent'
  | 'staff'
  /**
   * Kassir (P1-04) — SPEC §3.1 bo'yicha birinchi darajali rol, `staff` +
   * "finance" ruxsati emas. To'lov qabul qiladi va o'z smenasini yopadi,
   * lekin storno qila olmaydi, chegirma bera olmaydi, chiqim yoza olmaydi
   * va boshqa kassirlarning hisobotini ko'ra olmaydi (SPEC §4.3).
   */
  | 'cashier'

export type Gender = 'male' | 'female'

export interface User {
  id: string
  fullName: string
  role: Role
  email?: string
  avatarUrl?: string
  /** O'qituvchi uchun ochiq bo'limlar (nav filtri); boshqa rollarda bo'lmaydi */
  permissions?: string[]
}

/** O'quvchi/o'qituvchiga biriktirilgan tizim akkaunti (login/parol) */
export interface Credentials {
  /** Tizimga kirish logini (email) */
  login: string
  /** Ochiq parol (admin topshirishi uchun) */
  password: string
  role: Role
}

/* ---------- Admin dashboard ---------- */

export interface AdminStats {
  studentsCount: number
  teachersCount: number
  /** Maktab o'rtacha bahosi (5 ballik tizim) */
  averageGrade: number
  /** Umumiy davomat foizi (0-100); o'tilgan dars bo'lmasa null */
  attendanceRate: number | null
  /** Sinfga biriktirilmagan faol o'quvchilar */
  unassignedCount: number
  classesCount: number
  archivedCount: number
  /** Avansi bor o'quvchilar — "haqdorlar" */
  creditCount: number
  debtorCount: number
  /** Hech bo'lmasa bitta to'lov qilganlar */
  paidAtLeastOnceCount: number
}

/** Bitta dars soati kesimidagi davomat — bosh sahifadagi jadval va diagramma */
export interface AttendanceByPeriod {
  period: number
  expected: number
  present: number
  absent: number
  /** Davomati umuman belgilanmaganlar — ATAYLAB alohida, "keldi" ga qo'shilmaydi */
  unchecked: number
}

/** Dars qoldirayotgan o'quvchi — bosh sahifadagi ro'yxat */
export interface AbsentStudent {
  studentId: string
  fullName: string
  className: string
  /** Oxirgi 30 kunda SABABSIZ qoldirgan kunlari */
  missedDays: number
  lastSeen: string | null
}

export interface ClassPerformance {
  classId: string
  /** Masalan: "9-A" */
  className: string
  /** O'rtacha baho (5 ballik) */
  averageGrade: number
  /** Davomat foizi (0-100); o'tilgan dars bo'lmasa null */
  attendanceRate: number | null
}

export interface TopClass {
  id: string
  name: string
  studentsCount: number
  averageGrade: number
}

export interface AdminDashboard {
  stats: AdminStats
  classPerformance: ClassPerformance[]
  /** O'rtacha baho bo'yicha eng yuqori sinflar */
  topClasses: TopClass[]
  /** Bugungi davomat — dars soatlari kesimida */
  attendanceByPeriod: AttendanceByPeriod[]
  /** Oxirgi 30 kunda eng ko'p sababsiz qoldirganlar */
  absentStudents: AbsentStudent[]
}

/* ---------- Lidlar (maktabga qiziqqanlar) ---------- */

export type StageColor =
  | 'slate'
  | 'blue'
  | 'emerald'
  | 'amber'
  | 'violet'
  | 'rose'
  | 'cyan'
  | 'orange'

/** Kanban ustuni (lid bosqichi) */
export interface Stage {
  id: string
  title: string
  color: StageColor
}

export interface Lead {
  id: string
  /** Familiya Ism Sharif */
  fullName: string
  gender: Gender
  birthDate: string
  /** Ota-onasi FISH */
  parentFullName: string
  /** Ota-onasi telefon raqami */
  parentPhone: string
  /** Nechinchi sinfga kelmoqchi (1-11) */
  targetGrade: number
  note?: string
  /** Tegishli ustun (Stage) id'si */
  stage: string
}

/* ---------- O'quvchilar ---------- */

export interface Student {
  id: string
  /** Familiya Ism Sharif — parts'dan join qilinadi (saqlash + qidiruv uchun) */
  fullName: string
  /** Familiya (alohida) */
  lastName?: string
  /** Ism (alohida) */
  firstName?: string
  /** Otasining ismi / sharifi (alohida) */
  middleName?: string
  birthDate: string
  /** Metrika (tug'ilganlik haqida guvohnoma) rasm/skani manzili */
  birthCertificateUrl?: string | null
  address: string
  gender: Gender
  /** Ota-onasi FISH — parts'dan join */
  parentFullName: string
  /** Ota-ona familiyasi */
  parentLastName?: string
  /** Ota-ona ismi */
  parentFirstName?: string
  /** Ota-ona otasining ismi / sharifi */
  parentMiddleName?: string
  /** Ota-onasi telefon raqami */
  parentPhone: string
  /** Ota-ona passport rasm/skani manzili */
  parentPassportUrl?: string | null
  /** Joylashuv kengligi (mobil ilovadan GPS) */
  latitude?: number | null
  /** Joylashuv uzunligi */
  longitude?: number | null
  /** Joylashuv manzili (reverse geocode) */
  locationAddress?: string | null
  /** Joylashuv oxirgi yangilangan vaqti (ISO) */
  locationUpdatedAt?: string | null
  /** Arxivlanganmi (o'quvchi maktabdan ketgan/chiqarilgan) */
  isArchived?: boolean
  /** Arxivga olingan sana (ISO) */
  archivedAt?: string | null
  /** Arxivga olish sababi */
  archiveReason?: string | null
  /** Biriktirilgan sinf, masalan "9-A" */
  className: string
  /** Maktabga kelgan (qabul) sanasi (ISO) — oylik to'lov shu oydan boshlanadi */
  enrollmentDate: string
  /**
   * Balans (so'm): manfiy = qarzdor, 0 = qarzsiz, musbat = avans.
   *
   * HISOBLANADI (`invoices` − `payment_allocations`), o'quvchi qatorida
   * saqlanmaydi — P1-21. `undefined` = "bu ro'yxatda pul ko'rsatilmaydi"
   * (jurnal, davomat, reyting), 0 emas.
   *
   * Chegirma bu tipda YO'Q: u endi `discounts` jadvalida, direktor tasdig'i
   * bilan — "Moliya → Chegirmalar" ekrani (SPEC §8.1 Q5).
   */
  balance?: number
  /**
   * Sinf ichidagi guruh: 0 = guruhsiz (yoki butun sinfga), 1 = 1-guruh, 2 = 2-guruh.
   * Bo'lingan darslarda (ScheduleLesson.subGroup != 0) faqat shu guruhdagi o'quvchi qatnashadi.
   * Faqat o'quv yili boshida (jurnal yozuvi yo'q paytda) o'zgartirilishi mumkin.
   * Belgilanmagan = 0.
   */
  subGroup?: number
}

/** Sinfdagi guruh tayinlash (admin "Guruhlar" oynasi uchun) */
export interface ClassGroups {
  classId: string
  className: string
  /** O'quv yili boshlangan (jurnalda yozuv bor) — oddiy admin o'zgartira olmaydi */
  locked: boolean
  lockReason: string | null
  /** Joriy foydalanuvchi tahrirlay oladimi: !locked YOKI superadmin */
  canEdit: boolean
  ungroupedCount: number
  group1Count: number
  group2Count: number
  students: { id: string; fullName: string; subGroup: number }[]
}

/* ---------- Fanlar ---------- */

export interface Subject {
  id: string
  name: string
  /**
   * "Guruhlarga bo'linadi" (students-parity.md §2.5, G-9). Faqat shunday fanga
   * o'quv guruhi (`/admin/groups`) ochiladi. Eski javoblarda maydon bo'lmasligi
   * mumkin, shuning uchun ixtiyoriy.
   */
  isGroupable?: boolean
}

/* ---------- Sinflar ---------- */

export type ClassLanguage = 'uz' | 'ru'

export interface SchoolClass {
  id: string
  /** Sinf nomi, masalan "3-A" */
  name: string
  /** Sinf darajasi (1-11), masalan 3 */
  grade: number
  /** O'zbek yoki rus sinfligi */
  language: ClassLanguage
  /** Oylik to'lov (so'm) */
  monthlyFee: number
  /** Xona raqami */
  room?: string
  /** Sinf arxivlangan (arxivlanganda o'quvchilari ham arxivlanadi) */
  isArchived?: boolean
  /** Arxivga olingan sana (ISO) */
  archivedAt?: string | null
}

/* ---------- Dars jadvali ---------- */

export interface ScheduleLesson {
  /** Hafta kuni: 0=Dushanba ... 5=Shanba */
  day: number
  /** Dars raqami: 1-10 */
  period: number
  subjectId: string
  /** O'qituvchi id'si (bo'sh bo'lishi mumkin) */
  teacherId: string
  /** Bo'linish: 0 = butun sinf (default), 1 = 1-guruh, 2 = 2-guruh. Belgilanmagan = 0. */
  subGroup?: number
}

/**
 * Dars jadvalining EGASI — sinf yoki o'quv guruhi
 * (`docs/modules/students-parity.md` §2.1.4).
 *
 * Guruh darsi ham o'sha `classId` ustunida saqlanadi, faqat `ownerKind`
 * bilan belgilanadi — shuning uchun jadval ekranlari ikkalasi uchun ham
 * bitta yo'l va bitta komponent bilan ishlaydi.
 */
export type LessonOwnerKind = 'class' | 'group'

/** Sinf yoki o'quv guruhi uchun nomli dars jadvali varianti */
export interface ScheduleTemplate {
  id: string
  /** Eganing id'si: sinf id'si yoki guruh id'si (`ownerKind` qaysi ekanini aytadi) */
  classId: string
  name: string
  lessons: ScheduleLesson[]
  /** Egasi sinfmi yoki guruhmi. Eski javoblarda bo'lmasligi mumkin → 'class'. */
  ownerKind?: LessonOwnerKind
}

/** Chorak ichidagi haftaga jadval biriktirish */
export interface WeekAssignment {
  /** Hafta raqami (1-based) */
  week: number
  /** Tegishli jadval (template) id'si yoki null */
  templateId: string | null
}

/* ---------- Oshxona ---------- */

export type MealType = 'breakfast' | 'lunch' | 'dinner'

export interface Dish {
  id: string
  name: string
  /** Tarkibi */
  ingredients: string
  /** Rasm (data URL yoki yuklangan manzil) */
  imageUrl?: string
}

export interface DayMenu {
  date: string
  meals: Record<MealType, Dish[]>
}

/* ---------- Jurnal ---------- */

/** Jurnal ustuni: bitta dars (sana + dars raqami + guruh). Bo'lingan darsda har guruh o'z ustunini oladi. */
export interface JournalColumn {
  date: string
  /** Dars raqami (1-10) */
  period: number
  /** Bo'linish: 0 = butun sinf, 1 = 1-guruh, 2 = 2-guruh. Belgilanmagan = 0. */
  subGroup?: number
}

export interface JournalEntry {
  studentId: string
  /** Dars sanasi (ISO) */
  date: string
  /** Dars raqami (1-10) — bir kunda bir necha dars bo'lsa farqlash uchun */
  period: number
  /** Baho (1-5), agar kelgan va baholangan bo'lsa */
  grade?: number
  /** Davomat sababi id'si, agar kelmagan bo'lsa */
  reasonId?: string
  /** Uyga vazifa: 0 = belgilanmagan, 1 = qildi, 2 = qilmadi */
  homework?: number
  /** Xulq: 0 = belgilanmagan, 1 = yaxshi, 2 = yomon */
  behavior?: number
  /** Shu darsni o'zlashtirish foizi (0-100); null/undefined = belgilanmagan */
  mastery?: number | null
}

/** Dars ma'lumoti (sana + dars raqami + guruh bo'yicha): mavzu, uyga vazifa, o'tildi */
export interface JournalTopic {
  date: string
  period: number
  topic: string
  homework?: string
  /** Dars o'tildimi (ptichka) */
  conducted: boolean
  /** Bo'linish: 0 = butun sinf, 1 = 1-guruh, 2 = 2-guruh. Belgilanmagan = 0. */
  subGroup?: number
}

/** O'quvchining fan+chorak bo'yicha chorak bahosi va tavsiyasi */
export interface QuarterGradeRow {
  studentId: string
  /** O'qituvchi qo'ygan rasmiy chorak bahosi (yo'q bo'lsa undefined) */
  grade?: number
  /** Kunlik baholar o'rtachasidan tavsiya etilgan baho (yo'q bo'lsa undefined) */
  recommended?: number
}

/* ---------- Sozlamalar ---------- */

export interface QuarterPeriod {
  /** Chorak raqami 1-4 */
  quarter: number
  startDate: string
  endDate: string
  /** O'qituvchilarga shu chorak bahosini kiritish ochiqmi (admin boshqaradi) */
  gradesOpen: boolean
}

export interface LessonTime {
  /** Dars raqami 1-10 */
  period: number
  /** "HH:MM" */
  startTime: string
  endTime: string
}

/** Davomat sababi (kelmaganlik turi) */
export interface AbsenceReason {
  id: string
  name: string
  /** Jurnal katagida ko'rsatiladigan qisqa belgi */
  short: string
  /** "Kech keldi" turi — yo'qlik emas (davomatga ta'sir qilmaydi), baho qo'ysa bo'ladi */
  isLate: boolean
}

export interface SchoolSettings {
  quarters: QuarterPeriod[]
  lessonTimes: LessonTime[]
  absenceReasons: AbsenceReason[]
}

/* ---------- Moliya ---------- */

export type FinanceDirection = 'income' | 'expense'

export interface FinanceTransaction {
  id: string
  /** Sana (ISO) */
  date: string
  direction: FinanceDirection
  /** Toifa: tuition, salary, utilities, supplies, rent, donation, other ... */
  category: string
  /** Summa (musbat; yo'nalish belgini aniqlaydi) */
  amount: number
  note?: string
  /** O'quvchi to'lovi bo'lsa — tegishli o'quvchi id'si */
  studentId?: string
  /** Backend qaytaradigan o'quvchi nomi (qulaylik uchun) */
  studentName?: string
  /** O'qituvchi maoshi bo'lsa — tegishli o'qituvchi id'si */
  teacherId?: string
  /** Backend qaytaradigan o'qituvchi nomi */
  teacherName?: string
  /** Oylik to'lov bo'lsa — qaysi oy uchun ("YYYY-MM") */
  month?: string
}

export interface CategoryAmount {
  category: string
  amount: number
}

export interface FinanceSummary {
  totalIncome: number
  totalExpense: number
  /** Sof = kirim - chiqim */
  net: number
  tuitionIncome: number
  otherIncome: number
  incomeByCategory: CategoryAmount[]
  expenseByCategory: CategoryAmount[]
  /** O'quvchilar jami qarzi (manfiy balanslar yig'indisi, musbat son) */
  studentDebt: number
  /** O'quvchilar jami avansi (musbat balanslar yig'indisi) */
  studentAdvance: number
  transactionsCount: number
}

export interface FinanceMonthly {
  /** "YYYY-MM" */
  month: string
  income: number
  expense: number
}

/* ---------- O'quvchi to'lov tarixi (ledger) ---------- */

export type MonthStatus = 'paid' | 'partial' | 'unpaid'

export interface MonthLedger {
  /** "YYYY-MM" */
  month: string
  /** Shu oyga hisoblangan TO'LIQ summa (sinf oylik narxi — chegirmasiz) */
  charged: number
  /** Shu oy uchun berilgan chegirma summasi */
  discount: number
  /** Qoplangan (haqiqiy naqd) summa — chegirma kirmaydi */
  paid: number
  /** Qolgan qarz = charged − discount − paid */
  remaining: number
  status: MonthStatus
}

export interface LedgerPayment {
  date: string
  amount: number
  note?: string
  /** Qaysi oy uchun to'langani ("YYYY-MM"), agar biriktirilgan bo'lsa */
  month?: string
  /** Bu qatorning O'ZI storno (bekor qiluvchi yozuv) */
  isReversal?: boolean
  /** Bu to'lov keyinchalik storno qilingan */
  reversed?: boolean
}

export interface StudentLedger {
  student: Student
  balance: number
  /** Hozirgi effektiv oylik to'lov (sinf narxi − chegirma) */
  monthlyFee: number
  /** Jami hisoblangan (to'liq narx — chegirmasiz) */
  totalCharged: number
  /** Jami berilgan chegirma (so'm) */
  totalDiscount: number
  /** Jami haqiqiy naqd to'langan summa (chegirma kirmaydi) */
  totalPaid: number
  months: MonthLedger[]
  payments: LedgerPayment[]
}

/* ---------- O'zgarishlar tarixi (audit) ---------- */

export type AuditAction = 'create' | 'update' | 'delete'

export interface AuditLog {
  id: string
  /** FinanceTransaction | TeacherSalary | ClassFee */
  entityType: string
  entityId: string
  action: AuditAction
  /** ISO "yyyy-MM-ddTHH:mm:ss" */
  timestamp: string
  /** O'zgartirgan foydalanuvchi nomi (yoki "Tizim") */
  actorName?: string
  /** O'qiladigan o'zbekcha izoh */
  summary: string
  /** O'zgarishdan oldingi holat (JSON satr) — create uchun yo'q */
  before?: string
  /** O'zgarishdan keyingi holat (JSON satr) — delete uchun yo'q */
  after?: string
  studentId?: string
  teacherId?: string
}

/* ---------- O'qituvchilar ---------- */

export interface Teacher {
  id: string
  /** Familiya Ism Sharif */
  fullName: string
  birthDate: string
  address: string
  gender: Gender
  /** Telefon raqami — Telegram bot orqali shartnoma olish uchun ro'yxatdan o'tishda moslashtiriladi */
  phone?: string
  /** O'qituvchining rasmi (profil surati) URL'i */
  photoUrl?: string | null
  /** Sinf rahbari bo'lsa — biriktirilgan sinf nomi; aks holda bo'sh */
  homeroomClass: string
  /** Dars beradigan fanlar (Subject id'lari) */
  subjectIds: string[]
  /** Oylik ish haqi (so'm) — endi jadval + toifa narxidan AVTOMATIK hisoblanadi (faqat ko'rsatish) */
  salary: number
  /** O'qituvchi toifasi: "oliy" | "1" | "2" | "mutaxasis" (bo'sh = belgilanmagan). Soat narxini belgilaydi. */
  category?: string
  /** Oylik qaysi oydan hisoblansin ("YYYY-MM"); bo'sh = hisobot davri boshidan (eski maydon) */
  salaryStartMonth: string
  /** Maosh qaysi KUNdan hisoblansin ("YYYY-MM-DD"); oy o'rtasida kelsa birinchi oy qisman */
  salaryStartDate?: string
  /** O'qituvchi web panelida ochiq bo'limlar (admin belgilaydi) */
  permissions: string[]
  /** Arxivlanganmi (ishdan ketgan/to'xtatilgan) */
  isArchived?: boolean
  /** Arxivga olingan sana (ISO) */
  archivedAt?: string | null
  /** Arxivga olish sababi */
  archiveReason?: string | null
}

/* ---------- O'qituvchi faollik hisoboti ---------- */

/** Bitta o'qituvchi qatori (umumiy ko'rinish). Status: active | low | none */
export interface TeacherReportRow {
  teacherId: string
  fullName: string
  isArchived: boolean
  /** Reja — jadvaldan kelib chiqib bugungacha bo'lishi kerak bo'lgan darslar */
  expected: number
  /** O'tilgan (jurnal "o'tildi" belgilari) */
  conducted: number
  /** Bajarilish foizi (conducted/expected); reja yo'q bo'lsa null */
  donePct: number | null
  /** Qo'yilgan baholar soni */
  grades: number
  /** O'tilgan darslarning necha %ida mavzu yozilgan */
  topicPct: number | null
  /** O'tilgan darslarning necha %ida uy vazifa berilgan */
  homeworkPct: number | null
  /** Oxirgi faollik sanasi (ISO) yoki null */
  lastActivity: string | null
  status: 'active' | 'low' | 'none'
}

/** Sinf/fan kesimida bitta qator (batafsil hisobot) */
export interface TeacherReportBreakdown {
  className: string
  subjectName: string
  subGroup: number
  expected: number
  conducted: number
  donePct: number | null
  grades: number
  topicPct: number | null
  homeworkPct: number | null
}

/** Bitta o'qituvchining batafsil hisoboti */
export interface TeacherReportDetail extends TeacherReportRow {
  rows: TeacherReportBreakdown[]
}

/* ---------- Shartnomalar ---------- */

/** target: 'parent' | 'staff' */
export interface ContractTemplate {
  id: string
  target: 'parent' | 'staff'
  name: string
  fileUrl: string
  fileName: string
  uploadedAt: string
}

/** Ota-ona oluvchi (telefon bo'yicha guruhlangan) */
export interface ParentRecipient {
  key: string
  parentName: string
  phone: string
  children: string[]
  /** Telegramda ro'yxatdan o'tganmi */
  registered: boolean
  /** Oxirgi yuborilgan shartnoma raqami */
  lastNumber: number | null
}

/** Xodim oluvchi */
export interface StaffRecipient {
  teacherId: string
  fullName: string
  phone: string
  registered: boolean
  lastNumber: number | null
}

/** Bitta oluvchiga yuborish natijasi */
export interface SendResult {
  recipientKey: string
  ok: boolean
  number: number | null
  message: string
}

/* ---------- Boshqaruv ---------- */

/** Filial (branch) */
export interface Branch {
  id: string
  name: string
  address: string
  latitude: number
  longitude: number
  radiusMeters: number
  createdAt: string
}

/** Xodim (o'qituvchi bo'lmagan ishchi) */
export interface Staff {
  id: string
  fullName: string
  /** Lavozim yorlig'i (Kassir/Administrator/...) */
  position: string
  /** Tizim logini */
  login: string
  /** Ochiq admin bo'limlari (adminPermissions kalitlari) */
  permissions: string[]
}

/** Taklif yoki shikoyat (ota-ona ilovasidan) */
export interface Feedback {
  id: string
  studentName: string
  parentName: string
  className: string
  /** suggestion | complaint */
  type: 'suggestion' | 'complaint'
  text: string
  createdAt: string
  /** new | resolved */
  status: 'new' | 'resolved'
  /** parent | teacher — yuboruvchi roli */
  senderRole: 'parent' | 'teacher'
  /** Yuboruvchining ismi (ota-ona yoki o'qituvchi) */
  senderName: string
  /** Biriktirilgan rasm ("/uploads/...") yoki null */
  imageUrl: string | null
}

/* ---------- O'qituvchi maoshi ---------- */

export interface SalaryHistory {
  teacherId: string
  fullName: string
  salary: number
  totalPaid: number
  payments: LedgerPayment[]
}

/** Oy bo'yicha maosh holati */
export interface MonthSalary {
  /** "YYYY-MM" */
  month: string
  /** Shu oy uchun belgilangan oylik */
  expected: number
  /** Shu oyda berilgan */
  paid: number
  /** Qoldiq (belgilangan − berilgan) */
  remaining: number
  status: MonthStatus
}

/** O'qituvchi maoshi bo'yicha batafsil hisob (davr bo'yicha) */
export interface SalaryLedger {
  teacherId: string
  fullName: string
  salary: number
  /** Jami hisoblangan (oylik × davr oylari) */
  totalExpected: number
  /** Jami berilgan */
  totalPaid: number
  /** Umumiy qoldiq */
  remaining: number
  months: MonthSalary[]
  payments: LedgerPayment[]
}

export interface SalaryReportRow {
  teacherId: string
  teacherName: string
  /** Belgilangan oylik */
  salary: number
  /** Davr ichida berilgan jami */
  totalPaid: number
  paymentsCount: number
  /** Davrdagi oylar soni */
  months: number
  /** Kerakli (oylik × davr oylari) */
  expected: number
  /** Qoldiq (kerakli − berilgan); manfiy = ortiqcha berilgan */
  remaining: number
}

/** O'quvchilar bo'yicha moliya hisoboti qatori (joriy holat) */
export interface StudentFinanceRow {
  studentId: string
  fullName: string
  className: string
  /** Jami hisoblangan (TO'LIQ oylik to'lovlar yig'indisi — chegirmasiz) */
  charged: number
  /** Jami berilgan chegirma (so'm) */
  discount: number
  /** Jami HAQIQIY naqd to'lov (chegirma kirmaydi — to'langan summa o'zgarmaydi) */
  paid: number
  /** Qoldiq qarz (balansdan) */
  debt: number
  /** Ortiqcha to'langan (avans, balansdan) */
  advance: number
  /** Chegirma foizi qoidasi (0..100). 0 — chegirma yo'q. */
  discountPct: number
  /** Chegirma aniq summa qoidasi. 0 — chegirma yo'q. */
  discountAmount: number
}

/* ---------- Xabarlar (chat + e'lon + telegram) ---------- */

/** Sinf guruh chatidagi bitta xabar */
export interface ChatMessage {
  id: string
  /** Qaysi sinf chati (sinf nomi) */
  className: string
  senderUserId: string
  senderName: string
  /** admin | teacher | student */
  senderRole: Role
  text: string
  /** ISO 8601 vaqt */
  createdAt: string
}

/** Admin "Xabarlar" bo'limidagi sinf kartasi */
export interface MessageClass {
  name: string
  grade: number
  studentCount: number
  /** Telegramda ro'yxatdan o'tgan (e'lon oluvchi) ota-onalar soni */
  parentCount: number
  /** Oxirgi chat xabari vaqti (ISO) yoki null */
  lastMessageAt: string | null
}

/** Telegram bot orqali yuborilgan e'lon */
export interface Broadcast {
  id: string
  className: string
  text: string
  senderName: string
  createdAt: string
  /** Yuborilganda ro'yxatda bo'lgan ota-onalar (chatlar) soni */
  recipientCount: number
  /** Muvaffaqiyatli yetkazilganlar soni */
  sentCount: number
}

/** Telegramda ro'yxatdan o'tgan ota-ona */
export interface TelegramParent {
  studentId: string
  studentName: string
  /** O'quvchi sinfi */
  className: string
  /** O'quvchi balansi (manfiy = qarz) — qarzdorlar filtri uchun */
  balance: number
  parentName: string
  phone: string
  chatId: string
  createdAt: string
}

/* ---------- O'quvchilarni baholash ---------- */

/** Baholash turi (admin xohlagancha qo'shadi: nom + ixtiyoriy izoh) */
export interface EvaluationType {
  id: string
  name: string
  description: string
}

/** Bitta davomat sababidan o'quvchida necha marta bo'lgani (jurnal belgilaridan) */
export interface AttendanceReasonCount {
  reasonId: string
  name: string
  short: string
  isLate: boolean
  count: number
}

/** Baholash jadvalidagi bitta o'quvchi qatori */
export interface EvaluationRow {
  studentId: string
  fullName: string
  className: string
  /** O'tilgan darslar soni (guruhga mos) */
  conducted: number
  /** Qatnashgan darslar = o'tilgan − davomatsizlik (kech keldi mustasno) */
  attended: number
  /** Davomat sabablari taqsimoti (har sababdan necha marta) */
  reasons: AttendanceReasonCount[]
  /** Baholash turi id → baho (1-5) */
  grades: Record<string, number>
  /** Baholar o'rtachasi (0 = baho yo'q) */
  avgGrade: number
}

/** Baholash jadvali: oylar katalogi, tanlangan oy/hafta, ustun turlari + o'quvchi qatorlari */
export interface EvaluationBoard {
  /** Mavjud oylar ("YYYY-MM"), yangidan eskiga */
  months: string[]
  /** Tanlangan (joriy) oy "YYYY-MM" */
  month: string
  /** Tanlangan hafta (0 = butun oy, 1..5) */
  week: number
  types: EvaluationType[]
  rows: EvaluationRow[]
  /** Tanlangan fan id'si ("" yoki "all" = fanlar o'rtachasi, faqat ko'rish). */
  subjectId?: string
  /** Mavjud fanlar (admin board fan selektori uchun). */
  subjects?: { id: string; name: string }[]
}

/* ---------- Intizomiy ball ---------- */

/** Intizomiy ball sababi (nomi + ball, musbat=rag'bat / manfiy=jazo) */
export interface DisciplineReason {
  id: string
  name: string
  points: number
  /** "other" — mustaqil intizomiy sabab; "attendance" — davomat sababi (jurnalda ishlatiladi) */
  kind: 'other' | 'attendance'
  /**
   * Shu sabab bilan ball qo'yilganda ota-onaga Telegram xabari ketadimi (§6.3).
   * Sukut — false; davomat sabablarida har doim false.
   */
  notifyParent: boolean
  /** Sabab izohi — qachon qo'yiladi, nimani anglatadi. */
  description: string | null
  /** false = yangi ball qo'yishda tanlanmaydi; eski yozuvlar joyida qoladi. */
  isActive: boolean
}

/** Ballar nazorati qatori: o'quvchi, sinf, plus, minus, qoldi (100 + plus − minus) */
export interface DisciplineScoreRow {
  studentId: string
  fullName: string
  className: string
  plus: number
  minus: number
  remaining: number
}

/** Bitta intizomiy ball yozuvi (tarix) */
export interface DisciplinePoint {
  id: string
  studentId: string
  reasonName: string
  points: number
  note: string
  createdAt: string
  createdBy: string
  /** "manual" — qo'lda (o'chirsa bo'ladi), "attendance" — jurnal davomati (faqat ko'rish) */
  source: 'manual' | 'attendance'
  /**
   * Shu ball haqida ota-onaga HAQIQATAN yuborilgan Telegram xabarlari soni (§6.3).
   * Faqat yangi ball qo'yilganda ma'noli; tarixda har doim 0.
   */
  notifiedParents?: number
}

/** Maktab bo'ylab "Harakatlar" lentasidagi bitta qator (o'quvchi va sinf bilan) */
export interface DisciplineFeedRow {
  id: string
  studentId: string
  fullName: string
  className: string
  /** Yozuv paytidagi NUSXA — sabab keyin qayta nomlansa ham shu nom qoladi */
  reasonName: string
  points: number
  note: string
  createdAt: string
  createdBy: string
  /** "manual" — qo'lda kiritilgan, "attendance" — jurnal davomatidan */
  source: 'manual' | 'attendance'
}

/** "Harakatlar" lentasi: bitta sahifa + butun filtrlangan to'plamning jamlamasi */
export interface DisciplineFeed {
  items: DisciplineFeedRow[]
  total: number
  page: number
  pageSize: number
  plusCount: number
  minusCount: number
  pointsSum: number
  /** Filtr ro'yxatlari — filtrga bog'liq emas, butun bazadan */
  authors: string[]
  classNames: string[]
}

/** "Harakatlar" lentasining filtrlari (hammasi ixtiyoriy) */
export interface DisciplineFeedFilters {
  /** YYYY-MM-DD, ikkalasi ham davrga KIRADI */
  from?: string
  to?: string
  className?: string
  reasonId?: string
  author?: string
  sign?: 'positive' | 'negative'
  source?: 'manual' | 'attendance'
  search?: string
  page?: number
  pageSize?: number
}

/** Bayram/dam olish kuni (butun maktab) — bu sanada dars bo'lmaydi */
export interface Holiday {
  /** Sana "YYYY-MM-DD" */
  date: string
  /** Nomi (ixtiyoriy, masalan "Navro'z") */
  name: string
}

/** Telegram bot holati (admin UI uchun) */
export interface TelegramStatus {
  configured: boolean
  botUsername: string
}

/** Push uchun tanlanadigan oluvchi */
export interface PushRecipient {
  /** Akkaunt id (UserId) */
  userId: string
  name: string
  /** "Ota-ona" yoki "O'qituvchi" */
  group: string
  /** Qo'shimcha (ota-ona uchun sinf) */
  detail: string
  /** Qurilma ulanganmi (push haqiqatan yetadimi) */
  hasDevice: boolean
}

/** Yuborilgan push bildirishnoma (tarix) */
export interface PushMessage {
  id: string
  audience: string
  title: string
  body: string
  senderName: string
  createdAt: string
  recipientCount: number
  sentCount: number
}

/* ---------- Topshiriqlar (qo'shimcha) ---------- */

/** Topshiriq formati */
export type AssignmentFormat = 'written' | 'file' | 'test' | 'video'

/** Topshiriqqa biriktirilgan material (yuklangan fayl yoki havola) */
export interface AssignmentMaterial {
  id: string
  name: string
  url: string
  size: number
  contentType: string
}

/** Test savoli (format=test) */
export interface TestQuestion {
  id: string
  text: string
  options: string[]
  correctIndex: number
  order: number
}

/** Topshiriq natijasi — bitta o'quvchining holati (bajardi/bajarmadi + ball + yuborgan javobi) */
export interface SubmissionRow {
  studentId: string
  studentName: string
  className: string
  completed: boolean
  submittedAt: string | null
  /** Qo'yilgan/avto-hisoblangan ball (yo'q bo'lsa null) */
  score: number | null
  /** Yozma javob matni (format=written) */
  answerText: string | null
  /** Yuklangan fayl manzili (format=file/video) */
  fileUrl: string | null
}

/** Topshiriq bo'yicha natijalar (kim bajardi/bajarmadi) */
export interface AssignmentResult {
  assignmentId: string
  title: string
  /** Topshiriq formati — ballni ko'rsatish va javob turini bilish uchun */
  format: AssignmentFormat
  /** Maksimal ball */
  maxScore: number
  total: number
  completedCount: number
  rows: SubmissionRow[]
}

/** Topshiriqlar bali (admin) — ustun: bitta topshiriq */
export interface AssignmentScoreColumn {
  assignmentId: string
  title: string
  subjectName: string
  format: AssignmentFormat
  maxScore: number
  dueDate: string | null
}
/** Bitta katak — o'quvchining shu topshiriqdagi holati/bali */
export interface AssignmentScoreCell {
  assignmentId: string
  completed: boolean
  score: number | null
}
/** Bitta qator — o'quvchi va barcha topshiriqlardagi ballari */
export interface AssignmentScoreRow {
  studentId: string
  fullName: string
  className: string
  cells: AssignmentScoreCell[]
  totalScore: number
  totalMax: number
  gradedCount: number
}
/** Sinf bo'yicha topshiriqlar ball jadvali */
export interface AssignmentScoreboard {
  classId: string
  className: string
  assignments: AssignmentScoreColumn[]
  students: AssignmentScoreRow[]
}

/** Topshiriq/test (boy model) */
export interface Assignment {
  id: string
  createdByUserId: string
  subjectId: string
  subjectName: string
  title: string
  description: string
  format: AssignmentFormat
  /** Beriladigan sinflar (id'lar) */
  classIds: string[]
  /** Sinf nomlari (ko'rsatish uchun) */
  classNames: string[]
  /** Boshlash vaqti (ISO) yoki null */
  startDate: string | null
  /** Tugash/muddat vaqti (ISO) yoki null */
  dueDate: string | null
  lateAccept: boolean
  latePenaltyPct: number
  maxScore: number
  autoGrade: boolean
  createdAt: string
  materials: AssignmentMaterial[]
  questions: TestQuestion[]
}

/** Topshiriq turi (Sozlamalarda boshqariladi) */
export interface AssignmentType {
  id: string
  name: string
}

/** Ota-onalar bo'limidagi farzand (qisqacha) */
export interface ParentChild {
  studentId: string
  fullName: string
  className: string
  firstLoginAt: string | null
  lastLoginAt: string | null
  /** Oxirgi faol qurilma nomi */
  deviceName?: string
  platform?: string
  /** Push provayder app_id */
  appId?: string
}

/** Ota-onalar ro'yxati qatori (telefon bo'yicha guruhlangan) */
export interface ParentRow {
  fullName: string
  phone: string
  childrenCount: number
  isActivated: boolean
  activatedAt: string | null
  lastSeenAt: string | null
  children: ParentChild[]
  /** Oxirgi faol qurilma (farzandlar bo'yicha) */
  deviceName?: string
  platform?: string
}

/** Ilova → O'qituvchilar qatori (o'qituvchi ilova faolligi + qurilma) */
export interface TeacherAppRow {
  teacherId: string
  fullName: string
  phone: string
  isActivated: boolean
  activatedAt: string | null
  lastSeenAt: string | null
  deviceName: string
  platform: string
  appId: string
}

/** Admin xarita sahifasi uchun — joylashuvi bor bitta o'quvchi qatori */
export interface StudentLocationRow {
  studentId: string
  fullName: string
  className: string
  latitude: number
  longitude: number
  address?: string | null
  updatedAt?: string | null
}

/** O'qituvchi dars beradigan sinf (o'qituvchi paneli uchun) */
export interface TeacherClass {
  classId: string
  className: string
  grade: number
  isHomeroom: boolean
  /** Shu sinfda o'qituvchi dars beradigan fanlar */
  subjects: Subject[]
}

/** Portal umumiy konteksti (choraklar, dars vaqtlari, davomat sabablari + joriy chorak/hafta) */
export interface PortalMeta {
  quarters: QuarterPeriod[]
  lessonTimes: LessonTime[]
  absenceReasons: AbsenceReason[]
  currentQuarter: number
  currentWeek: number
}

/** O'qituvchi jadvalidagi bitta dars */
export interface TeacherLesson {
  day: number
  period: number
  startTime?: string | null
  endTime?: string | null
  /** Eganing id'si — sinf yoki o'quv guruhi (`ownerKind` ga qarang) */
  classId: string
  /** Eganing nomi — sinf nomi yoki guruh nomi */
  className: string
  subjectId: string
  subjectName: string
  /** Bo'linish: 0 = butun sinf, 1 = 1-guruh, 2 = 2-guruh. Belgilanmagan = 0. */
  subGroup?: number
  /** Dars sinfnikimi yoki o'quv guruhinikimi. Eski javoblarda yo'q → 'class'. */
  ownerKind?: LessonOwnerKind
}

/* ─── LMS (Ta'lim) ─────────────────────────────────────────── */

export type LmsUnlockMode = 'all' | 'sequential' | 'batch'

export interface LmsSubject {
  id: string
  classId: string
  className: string
  title: string
  description: string
  unlockMode: LmsUnlockMode
  batchSize: number
  topicsCount: number
  createdAt: string
}

export interface LmsMaterial {
  id: string
  name: string
  url: string
  size: number
  contentType: string
}

export interface LmsModule {
  id: string
  subjectId: string
  title: string
  description: string
  order: number
  topicsCount: number
}

export interface LmsTopic {
  id: string
  moduleId: string
  title: string
  description: string
  videoUrl?: string | null
  textContent?: string | null
  order: number
  materials: LmsMaterial[]
  completedCount: number
}

export interface LmsTopicBrief {
  id: string
  title: string
  order: number
}

export interface LmsStudentProgress {
  studentId: string
  fullName: string
  completedTopicIds: string[]
  completedCount: number
  totalCount: number
}

export interface LmsProgressReport {
  topics: LmsTopicBrief[]
  students: LmsStudentProgress[]
}

/* ---------- Bildirishnomalar (topbar qo'ng'irog'i) ---------- */

/** Bildirishnoma turi — ikonka va rangni belgilaydi. */
export type NotificationKind = 'suggestion' | 'complaint' | 'pickup' | 'chat' | 'birthday'

export interface NotificationItem {
  id: string
  kind: NotificationKind
  title: string
  text: string
  /** ISO sana-vaqt. */
  createdAt: string
  /** Bosilganda ochiladigan admin sahifasi yo'li. */
  link: string
  /** Oxirgi "o'qildi" belgisidan keyin paydo bo'lganmi. */
  isNew: boolean
}

export interface NotificationList {
  items: NotificationItem[]
  unreadCount: number
}

/* =========================================================================
   Moliya (billing) — MUZLATILGAN SHARTNOMA. Vazifa: P1-06.
   =========================================================================

   Backend manbasi: SchoolLms.Application/Dtos/BillingDtos.cs
   (namespace `SchoolLms.Application.Dtos.Billing`). Har bir interfeys u
   yerdagi DTO bilan BIR XIL nomda va bir xil maydonlarda.

   BU BLOK P1-06 DA MUZLAYDI. Faza 1.F (P1-16…P1-19) to'rtta sahifa daraxti
   shu tiplarga tayanib PARALLEL yoziladi. Maydon nomini o'zgartirish —
   to'rtta agentning ishini buzish. Yangi IXTIYORIY maydon qo'shish xavfsiz.

   PULNI FRONTEND HISOBLAMAYDI.
   JavaScript'da `number` — float64. `0.1 + 0.2 !== 0.3`, va so'mning
   tiyinlari shu yerda yo'qoladi. Shuning uchun server hisoblangan
   qiymatlarni TAYYOR beradi: `payable`, `paid`, `remaining`, `variance`,
   `debt`, `unallocated`. UI ularni faqat KO'RSATADI. Agar yig'indi kerak
   bo'lsa — backend'dan so'rang, `reduce` qilmang.

   Eski `FinanceTransaction` / `StudentLedger` tiplari (yuqorida) hali
   tirik: eski moliya sahifasi P1-21 gacha ishlaydi. Ular bilan bu yerdagi
   tiplar ARALASHTIRILMAYDI.
   ========================================================================= */

/** To'lov toifasi kodi. Beshtasi migratsiyada seed qilingan (SPEC §3.7). */
export type FeeCategoryCode = 'tuition' | 'bus' | 'dormitory' | 'meals' | 'other'

/**
 * To'lov usuli — FAQAT YORLIQ (mijoz javobi, SPEC §8.1 Q13).
 * Hech qanday provayder integratsiyasi yo'q: kassir to'lovchi nima bilan
 * to'laganini belgilaydi, tizim Payme/Click/Uzum/terminalga murojaat
 * QILMAYDI. Smena yopilishida faqat `cash` sanaladi.
 */
export type PaymentMethod = 'cash' | 'card' | 'transfer' | 'online'

export type InvoiceStatus = 'open' | 'partial' | 'paid' | 'void'

/**
 * Chegirma holati. Mijoz javobi (SPEC §8.1 Q5): CHEGARA YO'Q — har qanday
 * chegirma direktor tasdig'ini talab qiladi. `pending` chegirma qarzga
 * TA'SIR QILMAYDI.
 */
export type DiscountStatus = 'pending' | 'approved' | 'rejected'

export type CashShiftStatus = 'open' | 'closed'

export type LedgerDirection = 'debit' | 'credit'

export type LedgerRefType = 'payment' | 'invoice' | 'expense' | 'salary' | 'reversal'

/* ---------- Ma'lumotnoma: toifalar ---------- */

export interface FeeCategory {
  id: string
  code: FeeCategoryCode | string
  name: string
  isActive: boolean
}

/* ---------- Obunalar ---------- */

export interface StudentSubscription {
  id: string
  studentId: string
  studentName: string
  categoryId: string
  categoryCode: FeeCategoryCode | string
  categoryName: string
  /** Oylik summa (so'm) */
  monthlyAmount: number
  /** Avtobus yo'nalishi, yotoqxona xonasi va h.k. */
  detail?: string
  /** ISO sana "YYYY-MM-DD" */
  startsOn: string
  endsOn?: string
  /** Bugungi kunga faolmi (server hisoblaydi) */
  isActive: boolean
  createdByName: string
  /** ISO sana-vaqt (ofset bilan) */
  createdAt: string
}

/* ---------- Chegirmalar ---------- */

export interface Discount {
  id: string
  studentId: string
  studentName: string
  /** null = barcha toifalarga */
  categoryId?: string
  categoryCode?: string
  categoryName?: string
  /** Foiz (0..100) — avval shu ayriladi */
  percent: number
  /** Aniq summa (so'm) — foizdan keyin ayriladi */
  amount: number
  reason: string
  startsOn: string
  endsOn?: string
  status: DiscountStatus
  createdByName: string
  approvedByName?: string
  decidedAt?: string
  createdAt: string
}

/* ---------- Hisob-fakturalar ---------- */

export interface Invoice {
  id: string
  studentId: string
  studentName: string
  categoryId: string
  categoryCode: FeeCategoryCode | string
  categoryName: string
  /** Oyning birinchi kuni, "YYYY-MM-01" */
  periodMonth: string
  /** To'liq summa, chegirmasiz */
  amount: number
  /** Qo'llangan (TASDIQLANGAN) chegirma */
  discount: number
  /** To'lash kerak = amount − discount (SERVER hisoblaydi) */
  payable: number
  /** Taqsimlangan (to'langan) qism */
  paid: number
  /** Qoldiq = payable − paid */
  remaining: number
  dueOn: string
  status: InvoiceStatus
  /** `overdue_after_day` sozlamasi bo'yicha muddati o'tganmi */
  isOverdue: boolean
  createdAt: string
}

/** Oylik hisoblashni ishga tushirish natijasi */
export interface AccrualResult {
  periodMonth: string
  created: number
  skipped: number
  total: number
}

/** O'quvchining moliyaviy kartochkasi */
export interface StudentBilling {
  studentId: string
  studentName: string
  className: string
  /** Jami qarz (musbat son). 0 = qarzsiz */
  debt: number
  /** Taqsimlanmagan avans */
  credit: number
  subscriptions: StudentSubscription[]
  invoices: Invoice[]
  payments: Payment[]
}

/* ---------- Kassa smenasi ---------- */

export interface CashShift {
  id: string
  cashierId: string
  cashierName: string
  openedAt: string
  closedAt?: string
  openingFloat: number
  /** Ledger'dan hisoblangan. Yopilmaguncha undefined */
  expectedCash?: number
  /** Kassir qo'lda sanagan. Yopilmaguncha undefined */
  countedCash?: number
  /** countedCash − expectedCash. BAZA hisoblaydi, tahrirlab bo'lmaydi */
  variance?: number
  status: CashShiftStatus
  closedByName?: string
  paymentsCount: number
  /** Faqat `cash` — smenada sanaladigan qism */
  cashTotal: number
  /** card + transfer + online — bankka tushadi */
  nonCashTotal: number
}

export interface ZReportMethodRow {
  method: PaymentMethod
  count: number
  amount: number
}

export interface ZReportCategoryRow {
  categoryId: string
  categoryCode: string
  categoryName: string
  amount: number
}

/** Smena yakuni (SPEC §4.6) */
export interface ZReport {
  shift: CashShift
  byMethod: ZReportMethodRow[]
  byCategory: ZReportCategoryRow[]
  /** Birinchi chek raqami; smena bo'sh bo'lsa undefined */
  receiptFrom?: number
  receiptTo?: number
  reversalsCount: number
  /**
   * Javondan CHIQQAN naqd (F1.03, F1.04). Ikkalasi ham musbat = kassadan
   * chiqdi; storno allaqachon ayirilgan.
   *
   * Nega kerak: naqd tushum bilan `expectedCash` orasidagi farqni AYNAN shu
   * ikki qator tushuntiradi. Invariant:
   * `openingFloat + (cash usuli) − cashExpensesTotal − cashHandoversTotal
   *  === expectedCash`.
   */
  cashExpensesTotal: number
  cashExpensesCount: number
  cashHandoversTotal: number
  cashHandoversCount: number
}

/* ---------- To'lovlar ---------- */

export interface PaymentAllocation {
  id: string
  invoiceId: string
  categoryId: string
  categoryCode: string
  categoryName: string
  periodMonth: string
  amount: number
}

/**
 * Kassaga tushgan to'lov. O'ZGARMAS: tahrirlash/o'chirish API'si yo'q va
 * bo'lmaydi (SPEC §4.1 — baza darajasida ham taqiqlangan). Tuzatish faqat
 * storno orqali.
 */
export interface Payment {
  id: string
  /** Chek raqami — smena ichida uzluksiz */
  receiptNo: number
  studentId: string
  studentName: string
  amount: number
  method: PaymentMethod
  cashShiftId: string
  cashierId: string
  cashierName: string
  note?: string
  receivedAt: string
  /** Bu qator storno bo'lsa — qaysi to'lovni bekor qilgani */
  reversalOf?: string
  /** Bu to'lov storno qilingan bo'lsa — storno qatori id'si */
  reversedBy?: string
  /** Taqsimlanmagan qoldiq (avans) = amount − Σ allocations */
  unallocated: number
  allocations: PaymentAllocation[]
}

/** Kassir ekranidagi taqsimot taklifi (FIFO — eng eski qarzdan) */
export interface AllocationSuggestion {
  invoiceId: string
  categoryId: string
  categoryCode: string
  categoryName: string
  periodMonth: string
  remaining: number
  suggested: number
}

/* ---------- Chiqimlar ---------- */

export interface Expense {
  id: string
  onDate: string
  category: string
  amount: number
  note?: string
  createdByName: string
  /** Tasdiqlovchi yaratuvchidan BOSHQA shaxs bo'lishi shart (SPEC §4.5) */
  approvedByName?: string
  createdAt: string
  /**
   * Server hisoblagan holat: `pending` (tasdiq kutmoqda) · `posted` (jurnalga
   * tushgan) · `reversed` (storno). Ixtiyoriy — mock ma'lumotda yo'q bo'lishi
   * mumkin, lekin real serverda HAR DOIM keladi va holatning yagona haqiqiy
   * manbai shu (`ExpenseService.ExpenseStatus`).
   */
  status?: 'pending' | 'posted' | 'reversed'
  /** Pul qayerdan chiqdi: `cash` yoki `bank`. Tasdiq kutayotganda null. */
  settlementAccount?: string | null
  /** Jurnalga qaysi buxgalteriya sanasi bilan tushgani. */
  postedOn?: string | null
}

/* ---------- Ledger ---------- */

export interface LedgerEntry {
  id: number
  entryDate: string
  /** cash | bank | receivable | revenue:tuition | expense:salary ... */
  account: string
  direction: LedgerDirection
  amount: number
  refType: LedgerRefType
  refId?: string
  memo?: string
  createdByName: string
  createdAt: string
  reversalOf?: number
}

/** Hisob bo'yicha qoldiq — P&L va Cash Flow shundan quriladi */
export interface AccountBalance {
  account: string
  debit: number
  credit: number
  balance: number
}

/* ---------- Hisobotlar ---------- */

export interface DebtorCategoryRow {
  categoryCode: string
  categoryName: string
  debt: number
}

export interface DebtorRow {
  studentId: string
  fullName: string
  className: string
  parentPhone: string
  debt: number
  /** Eng eski to'lanmagan oy */
  oldestUnpaidMonth?: string
  /** Necha kun kechikkan. 0 = kechikmagan */
  daysOverdue: number
  byCategory: DebtorCategoryRow[]
}

export interface BillingMonthly {
  periodMonth: string
  /** Shu oyga hisoblangan (chegirmadan keyin) */
  accrued: number
  /** Shu oyda haqiqatan tushgan pul */
  collected: number
  /** Yig'ilish darajasi %; accrued = 0 bo'lsa null */
  collectionRate: number | null
}

/* ---------- Moliya sozlamalari (SPEC §8.1 Q6) ---------- */

/**
 * To'lov muddati QAT'IY RAQAM EMAS — admin UI'dan o'zgartiriladi.
 * Ikkalasi ham 1..28 oralig'ida (28 — har oyda mavjud eng katta kun).
 */
export interface BillingSettings {
  paymentDueDay: number
  overdueAfterDay: number
  updatedAt: string
  updatedByName?: string
}
