import { api } from '../client'

/**
 * O'qituvchining sertifikatlari va ustamalari — oylikka to'g'ridan-to'g'ri qo'shiladi (mijoz, 2026-10-15).
 * Server: `TeacherAllowancesController`, hisob — `TeacherPay` (docs/modules/teacher-allowances.md).
 */
export type AllowanceSource = 'certificate' | 'ustama'
export type AllowanceKind = 'percent' | 'fixed'

export interface TeacherAllowance {
  id: string
  teacherId: string
  source: AllowanceSource
  title: string
  amountKind: AllowanceKind
  /** Foiz (dars pulidan) yoki so'm/oy */
  amount: number
  /** "yyyy-MM" — shu oydan */
  fromMonth: string
  /** "yyyy-MM" — shu oygacha; null — muddatsiz */
  toMonth: string | null
  level: string | null
  number: string | null
  /** "yyyy-MM-dd" */
  issuedOn: string | null
  /** "yyyy-MM-dd" — sertifikat muddati; o'tgan oydan keyin to'lanmaydi */
  expiresOn: string | null
  fileUrl: string | null
  note: string | null
  /** Shu oy oylikka qo'shilyaptimi */
  activeNow: boolean
  isExpired: boolean
  createdAt: string
}

/** Shu oyning maosh tarkibi (server `TeacherPayBreakdown`). */
export interface TeacherPayBreakdown {
  month: string
  plannedLessons: number
  missedLessons: number
  substitutionLessons: number
  rate: number
  lessonPay: number
  ustamaPct: number
  ustamaAmount: number
  allowancePct: number
  allowanceFixed: number
  allowanceAmount: number
  bonus: number
  penalty: number
  total: number
}

export interface TeacherAllowances {
  teacherId: string
  fullName: string
  /** Umumiy ustama foizi (Moliya → Ish haqi dagi «Ustama tayinlash») */
  bonusPct: number
  currentMonth: TeacherPayBreakdown
  items: TeacherAllowance[]
}

export interface TeacherAllowancePayload {
  source: AllowanceSource
  title: string
  amountKind: AllowanceKind
  amount: number
  fromMonth: string
  toMonth: string | null
  level: string | null
  number: string | null
  issuedOn: string | null
  expiresOn: string | null
  fileUrl: string | null
  note: string | null
}

const base = (teacherId: string) => `/admin/teachers/${teacherId}/allowances`

export async function getTeacherAllowances(teacherId: string): Promise<TeacherAllowances> {
  const { data } = await api.get<TeacherAllowances>(base(teacherId))
  return data
}

export async function createTeacherAllowance(teacherId: string, payload: TeacherAllowancePayload): Promise<TeacherAllowance> {
  const { data } = await api.post<TeacherAllowance>(base(teacherId), payload)
  return data
}

export async function updateTeacherAllowance(
  teacherId: string,
  id: string,
  payload: TeacherAllowancePayload,
): Promise<TeacherAllowance> {
  const { data } = await api.put<TeacherAllowance>(`${base(teacherId)}/${id}`, payload)
  return data
}

export async function deleteTeacherAllowance(teacherId: string, id: string): Promise<void> {
  await api.delete(`${base(teacherId)}/${id}`)
}

/** O'qituvchining umumiy ustama foizini saqlash (bor endpoint: `PUT /admin/salary-rates/{id}/bonus`). */
export { setTeacherBonus } from './salaryRates'
