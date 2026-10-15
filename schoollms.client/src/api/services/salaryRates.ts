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
  /** O'rindosh ustoz: boshqalar o'rniga o'tilgan darslar minus o'z darsi boshqaga berilgani */
  substitutionLessons?: number
  /** Hisoblangan oylik maosh (davomatga moslangan + ustama + sertifikat/ustamalar + bonus − jarima, so'm) */
  monthlySalary: number
  /** Sertifikat va ustamalar — dars pulidan foiz (shu oy) */
  allowancePct?: number
  /** Sertifikat va ustamalar — qat'iy summa (shu oy; darssiz oyda 0) */
  allowanceFixed?: number
  /** Moliya → Bonus (shu oy) */
  adjustmentBonus?: number
  /** Moliya → Jarima (shu oy) */
  penalty?: number
}

/**
 * Jonli qayta hisob (narx kiritilayotganda): server bilan bir xil formula (TeacherPay).
 * Dars puli = (reja − kelmagan + o'rindosh) × narx; ustama va foizli sertifikat/ustamalar — dars pulidan;
 * keyin qat'iy summalar, bonus va jarima. Oy hech qachon 0 dan pastga tushmaydi.
 */
export function teacherMonthPay(row: TeacherPayrollRow, rate: number): number {
  const lessonPay = Math.max(0, row.monthlyLessons - row.missedLessons + (row.substitutionLessons ?? 0)) * rate
  const extras =
    (lessonPay * ((row.bonusPct || 0) + (row.allowancePct ?? 0))) / 100 +
    (row.allowanceFixed ?? 0) +
    (row.adjustmentBonus ?? 0) -
    (row.penalty ?? 0)
  return Math.max(0, lessonPay + extras)
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
  /** O'rindosh ustoz: covered for others minus own lessons covered by others (this month) */
  substitutionLessons?: number
  substitutions?: {
    id: string
    date: string
    period: number
    ownerName: string
    subjectName: string
    absentTeacherId: string
    absentTeacherName: string
    substituteTeacherName: string
  }[]
  /** Sertifikat va ustamalar shu oyda qo'shgan summa */
  allowanceAmount?: number
  allowances?: SalaryAllowanceLine[]
  /** Moliya → Bonus (shu oy) */
  adjustmentBonus?: number
  /** Moliya → Jarima (shu oy) */
  penalty?: number
  adjustments?: SalaryAdjustmentLine[]
}

/** Shu oy oylikka qo'shilgan bitta sertifikat / ustama */
export interface SalaryAllowanceLine {
  id: string
  source: 'certificate' | 'ustama'
  title: string
  amountKind: 'percent' | 'fixed'
  /** Sozlama: foiz yoki so'm */
  amount: number
  /** Shu oyda qo'shgan summa (so'm) */
  value: number
}

/** Shu oyning bitta bonusi yoki jarimasi (bekor qilinganlar yo'q) */
export interface SalaryAdjustmentLine {
  id: string
  kind: 'bonus' | 'penalty'
  reason: string
  amount: number
  comment: string | null
  createdAt: string
}

export async function getTeacherSalaryDetail(teacherId: string, month: string): Promise<TeacherSalaryDetail> {
  const { data } = await api.get<TeacherSalaryDetail>(`/admin/salary-rates/${teacherId}`, {
    params: { month },
  })
  return data
}
