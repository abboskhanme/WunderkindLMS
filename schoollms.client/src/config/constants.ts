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

/** O'qituvchi web paneli bo'limlari (admin ruxsat beradi). Kalitlar backend bilan bir xil. */
export const teacherPermissions: { key: string; label: string }[] = [
  { key: 'journal', label: 'Jurnal' },
  { key: 'assignments', label: 'Topshiriqlar' },
  { key: 'schedule', label: 'Dars jadvali' },
  { key: 'messages', label: 'Xabarlar (chat)' },
  { key: 'salary', label: 'Maosh' },
]

/**
 * Xodim (role="staff") admin panelida ko'ra oladigan bo'limlar. Kalitlar nav (navigation.ts)
 * va route himoyasi (RequirePerm) bilan bir xil. Superadmin "Xodimlar va rollar" bo'limida belgilaydi.
 * (Filiallar bu ro'yxatda yo'q — u faqat superadmin uchun.)
 */
export const adminPermissions: { key: string; label: string }[] = [
  { key: 'leads', label: 'Lidlar' },
  { key: 'students', label: "O'quvchilar" },
  { key: 'teachers', label: "O'qituvchilar" },
  { key: 'attendance', label: 'Davomat' },
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
  { key: 'gps', label: 'GPS (avtobus)' },
  { key: 'cameras', label: 'Kameralar' },
  { key: 'discipline', label: 'Intizomiy ball' },
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
