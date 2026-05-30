// Tizimdagi barcha asosiy tiplar

/**
 * Tizim rollari.
 * - `superadmin` — tizim egasi: admin'ning hamma huquqlari + qulflangan amallarni (masalan,
 *   o'quv yili boshlangach guruhlashni) istalgan vaqtda o'zgartira oladi.
 * - `admin` — oddiy administrator. Qulflangan ma'lumotlarni o'zgartira olmaydi.
 */
export type Role = 'superadmin' | 'admin' | 'teacher' | 'student' | 'parent' | 'staff'

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
  /** Balans (so'm): manfiy = qarzdor, 0 = qarzsiz, musbat = avans */
  balance: number
  /** Chegirma — foiz (0..100). Avval olib tashlanadi, keyin discountAmount ayriladi. */
  discountPct: number
  /** Chegirma — aniq summa (so'm). Foizdan keyin ayriladi. */
  discountAmount: number
  /** Chegirma izohi/sababi (masalan "Aka-uka chegirmasi"). */
  discountNote: string
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

/** Sinf uchun nomli dars jadvali varianti */
export interface ScheduleTemplate {
  id: string
  classId: string
  name: string
  lessons: ScheduleLesson[]
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
  /** Sinf rahbari bo'lsa — biriktirilgan sinf nomi; aks holda bo'sh */
  homeroomClass: string
  /** Dars beradigan fanlar (Subject id'lari) */
  subjectIds: string[]
  /** Oylik ish haqi (so'm) */
  salary: number
  /** Oylik qaysi oydan hisoblansin ("YYYY-MM"); bo'sh = hisobot davri boshidan */
  salaryStartMonth: string
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
  parentName: string
  phone: string
  chatId: string
  createdAt: string
}

/** Telegram bot holati (admin UI uchun) */
export interface TelegramStatus {
  configured: boolean
  botUsername: string
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
  classId: string
  className: string
  subjectId: string
  subjectName: string
  /** Bo'linish: 0 = butun sinf, 1 = 1-guruh, 2 = 2-guruh. Belgilanmagan = 0. */
  subGroup?: number
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

export interface LmsTopic {
  id: string
  subjectId: string
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
