import { api } from '../client'

/** Bitta o'qituvchining jadval bo'yicha hisoblangan oyligi */
export interface TeacherPayrollRow {
  id: string
  fullName: string
  /** "oliy" | "1" | "2" | "mutaxasis" | "" */
  category: string
  /** Jadvaldagi haftalik darslar soni */
  weeklyLessons: number
  /** Oylik darslar soni (haftalik × WeeksPerMonth) */
  monthlyLessons: number
  /** Kelmagan (absent) kunlardagi darslar soni — chegiriladi */
  missedLessons: number
  /** Ustama foizi (%) — 0 = yo'q */
  bonusPct: number
  /** Hisoblangan oylik maosh (davomatga moslangan + ustama, so'm) */
  monthlySalary: number
}

export interface SalaryRates {
  oliy: number
  t1: number
  t2: number
  mutaxasis: number
  /** Oyiga o'rtacha hafta soni (oylik darslarni hisoblash uchun, odatda 4) */
  weeksPerMonth: number
  /** Tanlangan oy ("yyyy-MM") */
  month: string
  teachers: TeacherPayrollRow[]
}

export async function getSalaryRates(month?: string): Promise<SalaryRates> {
  const { data } = await api.get<SalaryRates>('/admin/salary-rates', { params: { month } })
  return data
}

export async function saveSalaryRates(rates: {
  oliy: number
  t1: number
  t2: number
  mutaxasis: number
}): Promise<void> {
  await api.put('/admin/salary-rates', rates)
}

/** O'qituvchining ustama foizini saqlash (0 = ustama yo'q). */
export async function setTeacherBonus(teacherId: string, bonusPct: number): Promise<void> {
  await api.put(`/admin/salary-rates/${teacherId}/bonus`, { bonusPct })
}

/** Bir nechta tanlangan o'qituvchiga bir vaqtda ustama foizini tayinlash. */
export async function setBonusBulk(teacherIds: string[], bonusPct: number): Promise<void> {
  await api.put('/admin/salary-rates/bonus', { teacherIds, bonusPct })
}

/** Bitta kelmagan kun */
export interface AbsentDay {
  /** "yyyy-MM-dd" */
  date: string
  /** O'sha kun chegirilgan darslar soni */
  lessons: number
  note: string
}

/** O'qituvchining tanlangan oydagi maosh tafsiloti */
export interface TeacherSalaryDetail {
  teacherId: string
  fullName: string
  category: string
  month: string
  /** Ishga kirgan sana ("YYYY-MM-DD"), bo'lmasa bo'sh */
  startDate: string
  /** Tanlangan oy ishga kirgan oy (qisman) ekanmi */
  partialMonth: boolean
  hourlyRate: number
  weeklyLessons: number
  monthlyLessons: number
  plannedSalary: number
  missedLessons: number
  deduction: number
  /** Davomatga moslangan, ustamasiz */
  baseSalary: number
  bonusPct: number
  bonusAmount: number
  /** Jami (ustama bilan) */
  netSalary: number
  paid: number
  remaining: number
  absentDays: AbsentDay[]
}

export async function getTeacherSalaryDetail(teacherId: string, month: string): Promise<TeacherSalaryDetail> {
  const { data } = await api.get<TeacherSalaryDetail>(`/admin/salary-rates/${teacherId}`, {
    params: { month },
  })
  return data
}
