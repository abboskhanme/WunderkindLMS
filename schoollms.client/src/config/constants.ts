import type { ClassLanguage, FinanceDirection, Gender, MealType, MonthStatus } from '@/types'

export const genderLabels: Record<Gender, string> = {
  male: 'Erkak',
  female: 'Ayol',
}

export const genderOptions: { value: Gender; label: string }[] = [
  { value: 'male', label: 'Erkak' },
  { value: 'female', label: 'Ayol' },
]

/** O'qituvchi toifalari — maosh bir soat narxini belgilaydi (narx "Oylik hisoblash"da kiritiladi) */
export const teacherCategories: { value: string; label: string }[] = [
  { value: 'oliy', label: 'Oliy toifa' },
  { value: '1', label: '1-toifa' },
  { value: '2', label: '2-toifa' },
  { value: 'mutaxasis', label: 'Mutaxasis' },
]
export const teacherCategoryLabel = (c?: string): string =>
  teacherCategories.find((x) => x.value === c)?.label ?? '—'

/** 1-11 sinflar */
export const gradeOptions: number[] = Array.from({ length: 11 }, (_, i) => i + 1)

export const languageLabels: Record<ClassLanguage, string> = {
  uz: "O'zbek",
  ru: 'Rus',
}

export const languageOptions: { value: ClassLanguage; label: string }[] = [
  { value: 'uz', label: "O'zbek" },
  { value: 'ru', label: 'Rus' },
]

/** Hafta kunlari (Dushanba–Shanba) */
export const weekDays: string[] = [
  'Dushanba',
  'Seshanba',
  'Chorshanba',
  'Payshanba',
  'Juma',
  'Shanba',
]

/** Dars raqamlari (kuniga 10 tagacha dars) */
export const schedulePeriods: number[] = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]

/** Choraklar */
export const quarters: number[] = [1, 2, 3, 4]

/**
 * Xodimlar (barcha o'qituvchi + admin) umumiy guruh chati uchun maxsus kanal kaliti.
 * Backenddagi ChatService.StaffChannel bilan bir xil bo'lishi shart.
 */
export const STAFF_CHANNEL = '__xodimlar__'
/** Xodimlar kanalining ko'rsatiladigan nomi */
export const STAFF_CHANNEL_LABEL = 'Xodimlar'

/**
 * Admin-section keys a teacher can hold through a role (2026-10-12: teachers who are also dorm
 * commandants) — `TeacherPermissions.RoleKeys` on the server. Each also in its ":view" form.
 */
export const teacherRoleKeys = ['attendanceEvening', 'attendanceDorm', 'dormGirlsOnly', 'dormBoysOnly']

/**
 * A role a teacher can be given: at least one section key, every section key a teacher role key (full
 * or ":view"). Page grants ("/admin/…|edit") give no server access and are skipped.
 */
export const fitsTeacher = (permissions: string[]) => {
  const sections = permissions.filter((k) => !k.startsWith('/'))
  return sections.length > 0 && sections.every((k) => teacherRoleKeys.includes(k.replace(/:view$/, '')))
}

/** O'qituvchi web paneli bo'limlari (admin ruxsat beradi). Kalitlar backend bilan bir xil. */
export const teacherPermissions: { key: string; label: string }[] = [
  { key: 'journal', label: 'Jurnal' },
  { key: 'assignments', label: 'Topshiriqlar' },
  { key: 'schedule', label: 'Dars jadvali' },
  // 'messages' (guruh chati) retired 2026-10-11 — the chat lives only in the admin panel.
  // 'salary' retired 2026-09-28 — teachers never see their salary (TeacherPermissions.Salary).
]

/**
 * Xodim (role="staff") admin panelida ko'ra oladigan bo'limlar. Kalitlar nav (navigation.ts)
 * va route himoyasi (RequirePerm) bilan bir xil. Superadmin Boshqaruv → Rollar bo'limida rolga beradi.
 * (Filiallar bu ro'yxatda yo'q — u faqat superadmin uchun.)
 */
export const adminPermissions: { key: string; label: string }[] = [
  { key: 'dashboard', label: 'Bosh sahifa' },
  { key: 'leads', label: 'Lidlar' },
  { key: 'marketing', label: 'Sotuv va marketing' },
  { key: 'admission', label: 'Qabul' },
  { key: 'exams', label: 'Blok Test' },
  { key: 'seasonalMarks', label: 'Imtihonlar (mavsumiy baholash)' },
  { key: 'students', label: "O'quvchilar" },
  { key: 'teachers', label: "O'qituvchilar" },
  { key: 'attendance', label: "Davomat (ko'rish va analitika)" },
  // Belgilash alohida (2026-10-11): "kunduzgi davomat faqat hamshirada" — rahbarlar ko'radi, hamshira belgilaydi.
  { key: 'attendanceMark', label: 'Davomat belgilash (kunduzgi)' },
  { key: 'attendanceEvening', label: 'Kechki dars davomati' },
  { key: 'attendanceDorm', label: 'Yotoqxona davomati' },
  { key: 'schedule', label: 'Dars jadvali' },
  { key: 'classes', label: 'Sinflar' },
  { key: 'journal', label: 'Jurnal' },
  { key: 'messages', label: 'Xabarlar' },
  { key: 'app', label: 'Ilova' },
  { key: 'gradesReport', label: 'Baholar hisoboti' },
  { key: 'teacherReports', label: "O'qituvchilar hisoboti" },
  { key: 'contracts', label: 'Shartnomalar' },
  { key: 'finance', label: 'Moliya' },
  { key: 'academicYear', label: "Yangi o'quv yili" },
  { key: 'settings', label: 'Sozlamalar' },
  { key: 'staff', label: 'Xodimlar' },
  { key: 'feedback', label: 'Taklif va shikoyatlar' },
  { key: 'polls', label: "So'rovnomalar va NPS" },
  { key: 'kpi', label: 'Xodimlar KPI' },
  { key: 'gps', label: 'GPS (avtobus)' },
  { key: 'cameras', label: 'Kameralar' },
  { key: 'discipline', label: 'Intizomiy ball' },
  { key: 'lessonAnalysis', label: 'Dars tahlili' },
  { key: 'hrTasks', label: 'HR topshiriqlari' },
  { key: 'recruitment', label: 'Ishga qabul' },
]

/**
 * `adminPermissions` grouped the way the side menu is — for the role editor
 * (Boshqaruv → Rollar). Every key above appears in exactly one group.
 */
export const adminPermissionGroups: { label: string; keys: string[] }[] = [
  { label: 'Asosiy', keys: ['dashboard', 'leads', 'marketing', 'admission'] },
  { label: 'Moliya', keys: ['finance', 'contracts'] },
  { label: "O'quv bo'limi", keys: ['students', 'classes', 'teachers', 'journal', 'schedule'] },
  { label: 'Davomat', keys: ['attendance', 'attendanceMark', 'attendanceEvening', 'attendanceDorm'] },
  { label: 'Baholash', keys: ['exams', 'seasonalMarks', 'gradesReport', 'teacherReports', 'discipline'] },
  { label: 'Aloqa va ilova', keys: ['messages', 'app', 'feedback', 'polls'] },
  { label: 'Boshqaruv', keys: ['staff', 'gps', 'cameras', 'lessonAnalysis', 'kpi'] },
  { label: 'HR', keys: ['hrTasks', 'recruitment'] },
  { label: 'Sozlamalar', keys: ['settings', 'academicYear'] },
]

/** Oshxona — kunlik 3 mahal */
export const mealOrder: MealType[] = ['breakfast', 'lunch', 'dinner']

export const mealLabels: Record<MealType, string> = {
  breakfast: 'Nonushta',
  lunch: 'Tushlik',
  dinner: 'Kechki ovqat',
}

/* ---------- Moliya ---------- */

export const financeDirectionLabels: Record<FinanceDirection, string> = {
  income: 'Kirim',
  expense: 'Chiqim',
}

export interface CategoryOption {
  value: string
  label: string
}

/** Kirim toifalari */
export const incomeCategories: CategoryOption[] = [
  { value: 'tuition', label: "O'quvchi to'lovi" },
  { value: 'donation', label: 'Homiylik' },
  { value: 'rent_in', label: 'Ijaradan kirim' },
  { value: 'other', label: 'Boshqa kirim' },
]

/** Chiqim toifalari */
export const expenseCategories: CategoryOption[] = [
  { value: 'salary', label: 'Oylik maosh' },
  { value: 'utilities', label: 'Kommunal' },
  { value: 'supplies', label: 'Jihoz/materiallar' },
  { value: 'rent', label: 'Ijara' },
  { value: 'repair', label: "Ta'mirlash" },
  { value: 'other', label: 'Boshqa chiqim' },
]

export const categoriesByDirection: Record<FinanceDirection, CategoryOption[]> = {
  income: incomeCategories,
  expense: expenseCategories,
}

/** Toifa kodini o'qiladigan nomga aylantirish */
export function financeCategoryLabel(category: string): string {
  const all = [...incomeCategories, ...expenseCategories]
  return all.find((c) => c.value === category)?.label ?? category
}

/** Qisqa o'zbekcha oy nomlari (1-12) */
export const monthShortNames: string[] = [
  'Yan', 'Fev', 'Mar', 'Apr', 'May', 'Iyn',
  'Iyl', 'Avg', 'Sen', 'Okt', 'Noy', 'Dek',
]

/** "YYYY-MM" -> "May 2026" */
export function formatMonth(ym: string): string {
  const [y, m] = ym.split('-')
  return `${monthShortNames[Number(m) - 1] ?? m} ${y}`
}

export const monthStatusLabels: Record<MonthStatus, string> = {
  paid: "To'langan",
  partial: 'Qisman',
  unpaid: "To'lanmagan",
}
